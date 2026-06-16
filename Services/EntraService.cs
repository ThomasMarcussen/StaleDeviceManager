using Azure.Identity;
using StaleDeviceManager.Models;
using Microsoft.Graph;
using Microsoft.Graph.Models;

namespace StaleDeviceManager.Services;

/// <summary>
/// Entra ID (Microsoft Graph) device operations. Uses interactive browser
/// sign-in against Microsoft's well-known "Microsoft Graph Command Line Tools"
/// public client, so no custom app registration is required.
/// </summary>
public class EntraService
{
    // Microsoft Graph Command Line Tools - first-party public client.
    private const string GraphCliClientId = "14d82eec-204b-4c2f-b7e8-296a70dab67e";

    private static readonly string[] Scopes =
    {
        "Device.ReadWrite.All",
        "Directory.ReadWrite.All",
        "DeviceManagementManagedDevices.ReadWrite.All"
    };

    private GraphServiceClient? _graph;

    public bool IsConnected => _graph != null;
    public string? SignedInUser { get; private set; }

    /// <summary>
    /// Interactive browser sign-in. Throws on failure. Pass <paramref name="loginHint"/>
    /// (a UPN) to pre-target the alternate admin account in the browser prompt.
    /// No token cache is persisted, so each connect is an explicit sign-in - this
    /// lets you authenticate as an account other than the logged-on Windows user.
    /// </summary>
    public async Task ConnectAsync(Action<string> log, string? loginHint = null)
    {
        var options = new InteractiveBrowserCredentialOptions
        {
            ClientId = GraphCliClientId,
            RedirectUri = new Uri("http://localhost")
        };
        if (!string.IsNullOrWhiteSpace(loginHint))
            options.LoginHint = loginHint.Trim();

        var credential = new InteractiveBrowserCredential(options);
        _graph = new GraphServiceClient(credential, Scopes);

        // Force a token acquisition + identify the signed-in user.
        var me = await _graph.Me.GetAsync(rc =>
            rc.QueryParameters.Select = new[] { "userPrincipalName", "displayName" });
        SignedInUser = me?.UserPrincipalName ?? me?.DisplayName ?? "(unknown)";
        log($"Entra: connected as {SignedInUser}.");
    }

    /// <summary>
    /// Returns Entra devices whose approximateLastSignInDateTime is older than the
    /// cutoff (or null/never), created before the cutoff. Optionally excludes servers.
    /// </summary>
    public async Task<List<StaleDevice>> ScanAsync(int inactiveDays, bool excludeServers, Action<string> log)
    {
        if (_graph == null) throw new InvalidOperationException("Not connected to Entra ID.");

        var cutoff = DateTimeOffset.UtcNow.AddDays(-inactiveDays);
        log($"Entra: querying devices (cutoff {cutoff:yyyy-MM-dd})...");

        var devices = new List<Device>();
        var page = await _graph.Devices.GetAsync(rc =>
        {
            rc.QueryParameters.Top = 999;
            rc.QueryParameters.Select = new[]
            {
                "id", "displayName", "accountEnabled", "approximateLastSignInDateTime",
                "operatingSystem", "operatingSystemVersion", "deviceId", "registrationDateTime"
            };
            // Registered owner = best available "primary user" for an Entra device.
            rc.QueryParameters.Expand = new[] { "registeredOwners($select=userPrincipalName,displayName)" };
        });

        if (page?.Value != null)
        {
            var iterator = Microsoft.Graph.PageIterator<Device, DeviceCollectionResponse>
                .CreatePageIterator(_graph, page, d => { devices.Add(d); return true; });
            await iterator.IterateAsync();
        }

        var results = new List<StaleDevice>();
        foreach (var d in devices)
        {
            var last = d.ApproximateLastSignInDateTime;
            var created = d.RegistrationDateTime;
            bool isServer = d.OperatingSystem?.Contains("Server", StringComparison.OrdinalIgnoreCase) ?? false;

            bool lastStale = !last.HasValue || last.Value < cutoff;
            // Safe direction: only "old enough" when we KNOW it was registered
            // before the cutoff. Unknown age must not count as stale.
            bool oldEnough = created.HasValue && created.Value < cutoff;

            if (excludeServers && isServer) continue;
            if (!(lastStale && oldEnough)) continue;

            var owner = d.RegisteredOwners?.OfType<User>().FirstOrDefault();

            results.Add(new StaleDevice
            {
                Source = DeviceSource.Entra,
                Id = d.Id ?? "",
                Name = d.DisplayName ?? "(no name)",
                OperatingSystem = $"{d.OperatingSystem} {d.OperatingSystemVersion}".Trim(),
                LastUser = owner?.UserPrincipalName ?? owner?.DisplayName ?? "",
                LastActivity = last?.UtcDateTime,
                PasswordLastSet = null,
                WhenCreated = created?.UtcDateTime,
                Enabled = d.AccountEnabled ?? false,
                Reason = last.HasValue
                    ? $"LastSignIn {last:yyyy-MM-dd}"
                    : "LastSignIn never"
            });
        }

        log($"Entra: scanned {devices.Count} device(s), {results.Count} stale.");
        return results;
    }

    public async Task DisableAsync(StaleDevice device)
    {
        if (_graph == null) throw new InvalidOperationException("Not connected to Entra ID.");
        GuardId(device);
        await _graph.Devices[device.Id].PatchAsync(new Device { AccountEnabled = false });
        device.Enabled = false;
    }

    public async Task DeleteAsync(StaleDevice device)
    {
        if (_graph == null) throw new InvalidOperationException("Not connected to Entra ID.");
        GuardId(device);
        await _graph.Devices[device.Id].DeleteAsync();
    }

    /// <summary>Guard against acting on a blank object id (would form an invalid /devices/ request).</summary>
    private static void GuardId(StaleDevice device)
    {
        if (string.IsNullOrWhiteSpace(device.Id))
            throw new InvalidOperationException($"Refusing to act on '{device.Name}': missing directory object id.");
    }

    // ---- Intune (managed devices) -------------------------------------
    // Intune managed devices have no "disable" concept; the cleanup action is
    // delete (remove the stale managed-device record).

    /// <summary>
    /// Returns Intune managed devices whose lastSyncDateTime is older than the
    /// cutoff (or never synced), enrolled before the cutoff. Optionally excludes servers.
    /// </summary>
    public async Task<List<StaleDevice>> ScanIntuneAsync(int inactiveDays, bool excludeServers, Action<string> log)
    {
        if (_graph == null) throw new InvalidOperationException("Not connected to Entra ID.");

        var cutoff = DateTimeOffset.UtcNow.AddDays(-inactiveDays);
        log($"Intune: querying managed devices (cutoff {cutoff:yyyy-MM-dd})...");

        var devices = new List<ManagedDevice>();
        var page = await _graph.DeviceManagement.ManagedDevices.GetAsync(rc =>
        {
            rc.QueryParameters.Top = 1000;
            rc.QueryParameters.Select = new[]
            {
                "id", "deviceName", "lastSyncDateTime", "operatingSystem",
                "osVersion", "enrolledDateTime", "managedDeviceOwnerType", "complianceState",
                "userPrincipalName", "userDisplayName"
            };
        });

        if (page?.Value != null)
        {
            var iterator = Microsoft.Graph.PageIterator<ManagedDevice, ManagedDeviceCollectionResponse>
                .CreatePageIterator(_graph, page, d => { devices.Add(d); return true; });
            await iterator.IterateAsync();
        }

        var results = new List<StaleDevice>();
        foreach (var d in devices)
        {
            var last = d.LastSyncDateTime;
            var enrolled = d.EnrolledDateTime;
            bool isServer = d.OperatingSystem?.Contains("Server", StringComparison.OrdinalIgnoreCase) ?? false;

            bool lastStale = !last.HasValue || last.Value < cutoff;
            // Safe direction: only "old enough" when we KNOW it was enrolled
            // before the cutoff. Unknown age must not count as stale.
            bool oldEnough = enrolled.HasValue && enrolled.Value < cutoff;

            if (excludeServers && isServer) continue;
            if (!(lastStale && oldEnough)) continue;

            results.Add(new StaleDevice
            {
                Source = DeviceSource.Intune,
                Id = d.Id ?? "",
                Name = d.DeviceName ?? "(no name)",
                OperatingSystem = $"{d.OperatingSystem} {d.OsVersion}".Trim(),
                LastUser = d.UserPrincipalName ?? d.UserDisplayName ?? "",
                LastActivity = last?.UtcDateTime,
                PasswordLastSet = null,
                WhenCreated = enrolled?.UtcDateTime,
                // No enable/disable for managed devices; mark false so the
                // Disable action skips them and only Delete applies.
                Enabled = false,
                Reason = last.HasValue
                    ? $"LastSync {last:yyyy-MM-dd}"
                    : "LastSync never"
            });
        }

        log($"Intune: scanned {devices.Count} managed device(s), {results.Count} stale.");
        return results;
    }

    /// <summary>Removes the stale managed-device record from Intune.</summary>
    public async Task DeleteIntuneAsync(StaleDevice device)
    {
        if (_graph == null) throw new InvalidOperationException("Not connected to Entra ID.");
        GuardId(device);
        await _graph.DeviceManagement.ManagedDevices[device.Id].DeleteAsync();
    }
}
