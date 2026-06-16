using System.IO;
using System.Text;
using StaleDeviceManager.Models;

namespace StaleDeviceManager.Services;

/// <summary>
/// Append-only audit log written to %ProgramData%\Stale Device Manager\audit.log.
/// Every scan, disable, and delete is recorded with a timestamp, the operator,
/// the device, and the result. Best-effort: failures to write are swallowed so
/// they never block an operation.
/// </summary>
public static class AuditLog
{
    private static readonly object _lock = new();

    public static string Directory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                     "Stale Device Manager");

    public static string FilePath => Path.Combine(Directory, "audit.log");

    private const string Header = "Timestamp\tWindowsUser\tActor\tAction\tSource\tDevice\tDeviceId\tResult";

    public static void Write(string action, string source, string device, string deviceId,
                             string result, string? actor = null)
    {
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            var line = string.Join("\t",
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                Environment.UserName,
                Clean(actor ?? Environment.UserName),
                Clean(action), Clean(source), Clean(device), Clean(deviceId), Clean(result));

            lock (_lock)
            {
                if (!File.Exists(FilePath))
                    File.AppendAllText(FilePath, Header + Environment.NewLine, Encoding.UTF8);
                File.AppendAllText(FilePath, line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch { /* auditing must never break an operation */ }
    }

    /// <summary>Convenience: log a free-form informational entry (e.g. a scan summary).</summary>
    public static void Info(string action, string detail, string? actor = null) =>
        Write(action, "", detail, "", "info", actor);

    private static string Clean(string? s) => (s ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
}

/// <summary>CSV export of device lists (scan results, disable/delete batch results).</summary>
public static class CsvExport
{
    public static void Write(IEnumerable<StaleDevice> devices, string path)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Source,Name,Enabled,LastUser,LastActivity,PasswordLastSet,Created,Reason,OperatingSystem,Status");
        foreach (var d in devices)
        {
            sb.AppendLine(string.Join(",",
                F(d.SourceLabel),
                F(d.Name),
                d.Enabled ? "Yes" : "No",
                F(d.LastUser),
                F(d.LastActivity?.ToString("yyyy-MM-dd")),
                F(d.PasswordLastSet?.ToString("yyyy-MM-dd")),
                F(d.WhenCreated?.ToString("yyyy-MM-dd")),
                F(d.Reason),
                F(d.OperatingSystem),
                F(d.Status)));
        }
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true)); // BOM so Excel detects UTF-8
    }

    private static string F(string? value)
    {
        value ??= "";
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        return value;
    }
}
