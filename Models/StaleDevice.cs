using System.ComponentModel;

namespace StaleDeviceManager.Models;

public enum DeviceSource
{
    AD,
    Entra,
    Intune
}

/// <summary>
/// A single stale device candidate from either on-prem AD or Entra ID.
/// Implements INotifyPropertyChanged so the grid checkbox (Selected) and
/// post-action state (Enabled / status) update live.
/// </summary>
public class StaleDevice : INotifyPropertyChanged
{
    private bool _selected;
    private bool _enabled;
    private string _status = "";

    /// <summary>AD or Entra.</summary>
    public DeviceSource Source { get; init; }

    public string SourceLabel => Source switch
    {
        DeviceSource.AD => "On-prem AD",
        DeviceSource.Entra => "Entra ID",
        DeviceSource.Intune => "Intune (MDM)",
        _ => Source.ToString()
    };

    /// <summary>Native identifier: distinguishedName (AD) or directory object id (Entra).</summary>
    public string Id { get; init; } = "";

    public string Name { get; init; } = "";

    public string OperatingSystem { get; init; } = "";

    /// <summary>
    /// OperatingSystem collapsed to an OS family (e.g. "Windows 10", "Windows 11",
    /// "Windows Server 2019") for grouping/filtering regardless of edition or build.
    /// </summary>
    public string OsFamily => OsClassifier.Family(OperatingSystem);

    /// <summary>
    /// Last/primary user where the platform exposes it: Intune userPrincipalName,
    /// Entra registered owner. Blank for AD (computer objects don't store this).
    /// </summary>
    public string LastUser { get; init; } = "";

    /// <summary>Last meaningful activity: lastLogonTimestamp (AD) or approximateLastSignInDateTime (Entra).</summary>
    public DateTime? LastActivity { get; init; }

    /// <summary>AD only: machine-account password last set. Null for Entra.</summary>
    public DateTime? PasswordLastSet { get; init; }

    public DateTime? WhenCreated { get; init; }

    /// <summary>Human-readable explanation of why this was flagged stale.</summary>
    public string Reason { get; init; } = "";

    public bool Selected
    {
        get => _selected;
        set { _selected = value; OnPropertyChanged(nameof(Selected)); }
    }

    public bool Enabled
    {
        get => _enabled;
        set { _enabled = value; OnPropertyChanged(nameof(Enabled)); }
    }

    /// <summary>Result of the last action taken against this object (Disabled/Deleted/Failed...).</summary>
    public string Status
    {
        get => _status;
        set { _status = value; OnPropertyChanged(nameof(Status)); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
