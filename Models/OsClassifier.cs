using System.Text.RegularExpressions;

namespace StaleDeviceManager.Models;

/// <summary>
/// Collapses the many raw OperatingSystem strings the different sources report
/// into a single OS "family" so the UI can filter by e.g. "Windows 10" without
/// caring about edition or minor build.
///
/// The sources don't agree on format:
///   AD            -> "Windows 10 Enterprise", "Windows Server 2019 Standard"
///   Entra/Intune  -> "Windows 10.0.19045.4046"  (operatingSystem + version)
///
/// Critical wrinkle: in the version-number style Windows 11 still reports a
/// "10.0" marketing major; only the build number tells 10 from 11
/// (build >= 22000 == Windows 11). Matching on the "10" alone misfiles every
/// Win11 device as Win10, so we parse the build.
/// </summary>
public static class OsClassifier
{
    public const string Unknown = "(Unknown)";

    private static readonly Regex VersionStyle = new(@"\b10\.0\.(\d+)", RegexOptions.Compiled);
    private static readonly Regex ServerYear   = new(@"Server\s*(\d{4}(?:\s*R2)?)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex NamedClient   = new(@"Windows\s+(11|10|8\.1|8|7|Vista|XP)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex NonWindows    = new(@"^([A-Za-z/ ]+?)\s*[\d.]", RegexOptions.Compiled);

    /// <summary>Maps a raw OS string to its family (e.g. "Windows 10", "Windows Server 2019", "iOS").</summary>
    public static string Family(string? os)
    {
        if (string.IsNullOrWhiteSpace(os)) return Unknown;
        os = os.Trim();

        // Windows Server first: "Server" can co-occur with a "10.0" build.
        if (os.Contains("Server", StringComparison.OrdinalIgnoreCase))
        {
            var y = ServerYear.Match(os);
            return y.Success ? $"Windows Server {y.Groups[1].Value.Replace("  ", " ")}" : "Windows Server";
        }

        if (os.StartsWith("Windows", StringComparison.OrdinalIgnoreCase))
        {
            // Version-number style ("Windows 10.0.22631.4317"): build decides 10 vs 11.
            var v = VersionStyle.Match(os);
            if (v.Success && int.TryParse(v.Groups[1].Value, out var build))
                return build >= 22000 ? "Windows 11" : "Windows 10";

            // Named style ("Windows 11 Enterprise", "Windows 10 Pro").
            var n = NamedClient.Match(os);
            if (n.Success) return $"Windows {n.Groups[1].Value}";

            return "Windows (other)";
        }

        // Non-Windows (iOS / iPadOS / macOS / Android / Linux): drop trailing version.
        var f = NonWindows.Match(os);
        return f.Success ? f.Groups[1].Value.Trim() : os;
    }
}
