using System.DirectoryServices;
using StaleDeviceManager.Models;

namespace StaleDeviceManager.Services;

/// <summary>
/// Optional alternate credentials / target for AD. When Username is blank the
/// current Windows user of the running process is used (default behaviour).
/// Server is an optional domain or domain-controller name (e.g. "corp.local"
/// or "dc01.corp.local") - useful when the alternate account is in another domain.
/// </summary>
public sealed record AdCredentials(string? Server, string? Username, string? Password)
{
    public bool HasUser => !string.IsNullOrWhiteSpace(Username);
}

/// <summary>
/// On-premises Active Directory operations via System.DirectoryServices.
/// Uses the current Windows credentials of the running user unless
/// <see cref="Credentials"/> is set with an alternate account.
/// </summary>
public class ActiveDirectoryService
{
    private const int UF_ACCOUNTDISABLE = 0x2;

    /// <summary>Optional alternate credentials / target. Null = current Windows user.</summary>
    public AdCredentials? Credentials { get; set; }

    /// <summary>
    /// Builds a DirectoryEntry for a RootDSE bind or a distinguishedName, applying
    /// the optional server prefix and alternate credentials when present.
    /// </summary>
    private DirectoryEntry MakeEntry(string pathSuffix)
    {
        var server = Credentials?.Server;
        var prefix = string.IsNullOrWhiteSpace(server) ? "LDAP://" : $"LDAP://{server}/";
        var path = prefix + pathSuffix;

        if (Credentials is { HasUser: true })
            return new DirectoryEntry(path, Credentials.Username, Credentials.Password, AuthenticationTypes.Secure);
        return new DirectoryEntry(path);
    }

    /// <summary>
    /// Scans computer accounts and returns those that are stale: machine-account
    /// password older than the cutoff AND last logon older than cutoff (or never),
    /// AND created before the cutoff. Optionally excludes server OS.
    /// </summary>
    public List<StaleDevice> Scan(int inactiveDays, bool excludeServers, string? searchBase, Action<string> log)
    {
        var cutoff = DateTime.UtcNow.AddDays(-inactiveDays);
        var results = new List<StaleDevice>();

        string baseDn;
        if (!string.IsNullOrWhiteSpace(searchBase))
        {
            baseDn = searchBase;
        }
        else
        {
            using var rootDse = MakeEntry("RootDSE");
            baseDn = rootDse.Properties["defaultNamingContext"].Value?.ToString() ?? "";
        }

        var who = Credentials is { HasUser: true } ? Credentials.Username : "current Windows user";
        log($"AD: searching {baseDn} as {who} (cutoff {cutoff:yyyy-MM-dd})");

        using var root = MakeEntry(baseDn);
        using var searcher = new DirectorySearcher(root)
        {
            Filter = "(objectCategory=computer)",
            PageSize = 1000,
            SizeLimit = 0
        };
        searcher.PropertiesToLoad.AddRange(new[]
        {
            "name", "distinguishedName", "pwdLastSet", "lastLogonTimestamp",
            "whenCreated", "operatingSystem", "userAccountControl"
        });

        int total = 0;
        using var found = searcher.FindAll();
        foreach (SearchResult r in found)
        {
            total++;
            var name = GetString(r, "name");
            var dn = GetString(r, "distinguishedName");
            var os = GetString(r, "operatingSystem");
            var uac = (int)GetLong(r, "userAccountControl");
            var enabled = (uac & UF_ACCOUNTDISABLE) == 0;

            var pwdLastSet = FromFileTime(GetLong(r, "pwdLastSet"));
            var lastLogon = FromFileTime(GetLong(r, "lastLogonTimestamp"));
            DateTime? whenCreated = r.Properties["whenCreated"].Count > 0
                ? (DateTime)r.Properties["whenCreated"][0]!
                : null;

            bool pwdStale = pwdLastSet.HasValue && pwdLastSet.Value.ToUniversalTime() < cutoff;
            bool logonStale = !lastLogon.HasValue || lastLogon.Value.ToUniversalTime() < cutoff;
            bool oldEnough = whenCreated.HasValue && whenCreated.Value.ToUniversalTime() < cutoff;
            bool isServer = os?.Contains("Server", StringComparison.OrdinalIgnoreCase) ?? false;

            if (excludeServers && isServer) continue;
            if (!(pwdStale && logonStale && oldEnough)) continue;

            var reasons = new List<string>();
            if (pwdStale) reasons.Add($"PwdLastSet {pwdLastSet:yyyy-MM-dd}");
            reasons.Add(lastLogon.HasValue ? $"LastLogon {lastLogon:yyyy-MM-dd}" : "LastLogon never");

            results.Add(new StaleDevice
            {
                Source = DeviceSource.AD,
                Id = dn,
                Name = name,
                OperatingSystem = os,
                LastActivity = lastLogon,
                PasswordLastSet = pwdLastSet,
                WhenCreated = whenCreated,
                Enabled = enabled,
                Reason = string.Join("; ", reasons)
            });
        }

        log($"AD: scanned {total} computer object(s), {results.Count} stale.");
        return results;
    }

    /// <summary>Disables the account, stamps a dated description.</summary>
    public void Disable(StaleDevice device)
    {
        GuardDn(device);
        using var de = MakeEntry(device.Id);
        int uac = (int)(de.Properties["userAccountControl"].Value ?? 0);
        de.Properties["userAccountControl"].Value = uac | UF_ACCOUNTDISABLE;
        de.Properties["description"].Value =
            $"Disabled as stale on {DateTime.Now:yyyy-MM-dd} by Stale Device Manager.";
        de.CommitChanges();
        device.Enabled = false;
    }

    /// <summary>Deletes the computer object (and any leaf children).</summary>
    public void Delete(StaleDevice device)
    {
        GuardDn(device);
        using var de = MakeEntry(device.Id);

        // Final safety: never act on anything that is not a computer object.
        var classes = de.Properties["objectClass"];
        bool isComputer = false;
        foreach (var c in classes) if (string.Equals(c?.ToString(), "computer", StringComparison.OrdinalIgnoreCase)) isComputer = true;
        if (!isComputer)
            throw new InvalidOperationException($"Refusing to delete '{device.Name}': bound object is not a computer.");

        de.DeleteTree();
        de.CommitChanges();
    }

    /// <summary>
    /// Hard guard against acting on an empty/invalid DN. An empty Id would make
    /// "LDAP://" bind to the domain root - DeleteTree there would be catastrophic.
    /// A real computer DN always contains a domain component (DC=).
    /// </summary>
    private static void GuardDn(StaleDevice device)
    {
        if (string.IsNullOrWhiteSpace(device.Id) ||
            !device.Id.Contains("DC=", StringComparison.OrdinalIgnoreCase) ||
            !device.Id.Contains("CN=", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Refusing to act on '{device.Name}': missing or invalid distinguishedName ('{device.Id}').");
        }
    }

    private static string GetString(SearchResult r, string prop) =>
        r.Properties[prop].Count > 0 ? r.Properties[prop][0]?.ToString() ?? "" : "";

    private static long GetLong(SearchResult r, string prop)
    {
        if (r.Properties[prop].Count == 0) return 0;
        var val = r.Properties[prop][0];
        return val switch
        {
            long l => l,
            int i => i,
            _ => long.TryParse(val?.ToString(), out var p) ? p : 0
        };
    }

    private static DateTime? FromFileTime(long fileTime)
    {
        // 0 = never set; 0x7FFFFFFFFFFFFFFF = "never expires" sentinel.
        if (fileTime <= 0 || fileTime == long.MaxValue) return null;
        try { return DateTime.FromFileTimeUtc(fileTime); }
        catch { return null; }
    }
}
