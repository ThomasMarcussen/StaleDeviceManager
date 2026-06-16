using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using Microsoft.Win32;
using StaleDeviceManager.Models;
using StaleDeviceManager.Services;

namespace StaleDeviceManager;

public partial class MainWindow : Window
{
    private const string AllOs = "(All OS)";

    private readonly ActiveDirectoryService _ad = new();
    private readonly EntraService _entra = new();
    private readonly ObservableCollection<StaleDevice> _devices = new();
    private ICollectionView _view = null!;
    private string _osFilter = AllOs;
    private bool _suppressFilterEvents;

    public MainWindow()
    {
        InitializeComponent();
        _view = CollectionViewSource.GetDefaultView(_devices);
        _view.Filter = o => _osFilter == AllOs || ((StaleDevice)o).OsFamily == _osFilter;
        Grid.ItemsSource = _view;
        OsFilter.Items.Add(AllOs);
        OsFilter.SelectedIndex = 0;
        Log("Ready. Set the inactive threshold, choose sources, then Scan.");
    }

    // ---- logging -------------------------------------------------------
    private void Log(string message)
    {
        Dispatcher.Invoke(() =>
        {
            LogBox.AppendText($"{DateTime.Now:HH:mm:ss}  {message}{Environment.NewLine}");
            LogBox.ScrollToEnd();
        });
    }

    private int InactiveDays =>
        int.TryParse(DaysBox.Text, out var d) && d > 0 ? d : 90;

    /// <summary>
    /// Pushes the current AD credential fields onto the AD service. Blank user =
    /// fall back to the current Windows user. Called before every AD operation so
    /// edits to the fields take effect without re-scanning.
    /// </summary>
    private void ApplyAdCredentials()
    {
        var user = AdUser.Text?.Trim();
        var server = AdServer.Text?.Trim();
        _ad.Credentials = string.IsNullOrWhiteSpace(user) && string.IsNullOrWhiteSpace(server)
            ? null
            : new AdCredentials(server, user, AdPass.Password);
    }

    private void SetBusy(bool busy)
    {
        BtnScan.IsEnabled = !busy;
        BtnConnectEntra.IsEnabled = !busy;
        BtnDisable.IsEnabled = !busy;
        BtnDelete.IsEnabled = !busy;
        Cursor = busy ? System.Windows.Input.Cursors.Wait : null;
    }

    // ---- Entra connect -------------------------------------------------
    private async void BtnConnectEntra_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SetBusy(true);
            Log("Entra: opening sign-in…");
            await _entra.ConnectAsync(Log, EntraUpn.Text);
            EntraStatus.Text = $"Entra: {_entra.SignedInUser}";
            EntraStatus.Foreground = System.Windows.Media.Brushes.Green;
        }
        catch (Exception ex)
        {
            Log($"Entra connect FAILED: {ex.Message}");
            MessageBox.Show(ex.Message, "Entra sign-in failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { SetBusy(false); }
    }

    // ---- Scan ----------------------------------------------------------
    private async void BtnScan_Click(object sender, RoutedEventArgs e)
    {
        if (!ChkAd.IsChecked.GetValueOrDefault() && !ChkEntra.IsChecked.GetValueOrDefault()
            && !ChkIntune.IsChecked.GetValueOrDefault())
        {
            MessageBox.Show("Select at least one source (AD, Entra ID and/or Intune).", "Nothing to scan",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var days = InactiveDays;
        var excludeServers = ChkExcludeServers.IsChecked.GetValueOrDefault();
        var doAd = ChkAd.IsChecked.GetValueOrDefault();
        var doEntra = ChkEntra.IsChecked.GetValueOrDefault();
        var doIntune = ChkIntune.IsChecked.GetValueOrDefault();

        // Entra ID and Intune both use the Graph sign-in.
        if ((doEntra || doIntune) && !_entra.IsConnected)
        {
            var r = MessageBox.Show("Entra ID / Intune is selected but you are not signed in. Connect now?",
                "Connect to Microsoft Graph", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r == MessageBoxResult.Yes)
            {
                await BtnConnectEntraInline();
                if (!_entra.IsConnected) return;
            }
            else { doEntra = false; doIntune = false; }
        }

        try
        {
            SetBusy(true);
            _devices.Clear();
            Log($"Scanning (threshold {days} days, exclude servers: {excludeServers})…");

            if (doAd)
            {
                ApplyAdCredentials();
                var adResults = await Task.Run(() => _ad.Scan(days, excludeServers, null, Log));
                foreach (var d in adResults) _devices.Add(d);
            }

            if (doEntra)
            {
                var entraResults = await _entra.ScanAsync(days, excludeServers, Log);
                foreach (var d in entraResults) _devices.Add(d);
            }

            if (doIntune)
            {
                var intuneResults = await _entra.ScanIntuneAsync(days, excludeServers, Log);
                foreach (var d in intuneResults) _devices.Add(d);
            }

            PopulateOsFilter();
            Log($"Scan complete. {_devices.Count} stale device(s) listed.");
            var srcList = new[] { doAd ? "AD" : null, doEntra ? "Entra" : null, doIntune ? "Intune" : null }
                .Where(s => s != null);
            AuditLog.Info("SCAN",
                $"sources=[{string.Join("+", srcList)}] threshold={days}d excludeServers={excludeServers} found={_devices.Count}",
                _entra.SignedInUser);
            UpdateSelectionInfo();
        }
        catch (Exception ex)
        {
            Log($"Scan FAILED: {ex.Message}");
            MessageBox.Show(ex.Message, "Scan failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { SetBusy(false); }
    }

    /// <summary>Rebuilds the OS filter dropdown from the current scan results.</summary>
    private void PopulateOsFilter()
    {
        _suppressFilterEvents = true;
        var current = _osFilter;
        OsFilter.Items.Clear();
        OsFilter.Items.Add(AllOs);
        foreach (var os in _devices.Select(d => d.OsFamily)
                                   .Where(s => !string.IsNullOrWhiteSpace(s))
                                   .Distinct().OrderBy(s => s))
            OsFilter.Items.Add(os);

        OsFilter.SelectedItem = OsFilter.Items.Contains(current) ? current : AllOs;
        _osFilter = (string)OsFilter.SelectedItem;
        _suppressFilterEvents = false;
        _view.Refresh();
    }

    private async Task BtnConnectEntraInline()
    {
        try
        {
            Log("Entra: opening sign-in…");
            await _entra.ConnectAsync(Log, EntraUpn.Text);
            EntraStatus.Text = $"Entra: {_entra.SignedInUser}";
            EntraStatus.Foreground = System.Windows.Media.Brushes.Green;
        }
        catch (Exception ex)
        {
            Log($"Entra connect FAILED: {ex.Message}");
        }
    }

    // ---- selection helpers --------------------------------------------
    private IEnumerable<StaleDevice> VisibleDevices => _view.Cast<StaleDevice>();

    private void BtnSelectAll_Click(object sender, RoutedEventArgs e) => SetSelectionOnVisible(true);
    private void BtnSelectNone_Click(object sender, RoutedEventArgs e) => SetSelectionOnVisible(false);

    private void SetSelectionOnVisible(bool value)
    {
        foreach (var d in VisibleDevices.ToList()) d.Selected = value;
        UpdateSelectionInfo();
    }

    // Header tick: select / clear all currently shown rows.
    private void HeaderSelectAll_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressFilterEvents) return;
        SetSelectionOnVisible(HeaderSelectAll.IsChecked == true);
    }

    private void UpdateSelectionInfo()
    {
        var shown = VisibleDevices.ToList();
        var sel = shown.Count(d => d.Selected);
        SelectionInfo.Text = $"{sel} selected of {shown.Count} shown";
        FilterInfo.Text = _osFilter == AllOs
            ? $"{_devices.Count} total"
            : $"showing {shown.Count} of {_devices.Count}";

        // Reflect aggregate state in the header tick without re-triggering selection.
        _suppressFilterEvents = true;
        HeaderSelectAll.IsChecked = shown.Count > 0 && sel == shown.Count ? true
                                  : sel == 0 ? false
                                  : (bool?)null;
        _suppressFilterEvents = false;
    }

    // ---- filter / export / audit --------------------------------------
    private void OsFilter_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_suppressFilterEvents || OsFilter.SelectedItem == null) return;
        _osFilter = (string)OsFilter.SelectedItem;
        _view.Refresh();
        UpdateSelectionInfo();
    }

    private void BtnExportCsv_Click(object sender, RoutedEventArgs e)
    {
        var items = VisibleDevices.ToList();
        if (items.Count == 0) { MessageBox.Show("Nothing to export.", "Export CSV"); return; }

        var dlg = new SaveFileDialog
        {
            Title = "Export scan results",
            Filter = "CSV files (*.csv)|*.csv",
            FileName = $"StaleDevices_{DateTime.Now:yyyyMMdd-HHmmss}.csv"
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            CsvExport.Write(items, dlg.FileName);
            Log($"Exported {items.Count} row(s) to {dlg.FileName}");
            AuditLog.Info("EXPORT", $"scan results rows={items.Count} file={dlg.FileName}", _entra.SignedInUser);
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Export failed", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void BtnOpenAudit_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!File.Exists(AuditLog.FilePath))
            {
                MessageBox.Show($"No audit log yet.\n\nIt will be created at:\n{AuditLog.FilePath}", "Audit log");
                return;
            }
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(AuditLog.FilePath) { UseShellExecute = true });
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Audit log", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    /// <summary>After a disable/delete batch, offer to export the result set.</summary>
    private void OfferResultExport(string action, List<StaleDevice> results)
    {
        if (results.Count == 0) return;
        var r = MessageBox.Show($"Export the {action} results ({results.Count} device(s)) to CSV?",
            $"Export {action} results", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (r != MessageBoxResult.Yes) return;

        var dlg = new SaveFileDialog
        {
            Title = $"Export {action} results",
            Filter = "CSV files (*.csv)|*.csv",
            FileName = $"{action}_results_{DateTime.Now:yyyyMMdd-HHmmss}.csv"
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            CsvExport.Write(results, dlg.FileName);
            Log($"Exported {action} results to {dlg.FileName}");
            AuditLog.Info("EXPORT", $"{action} results rows={results.Count} file={dlg.FileName}", _entra.SignedInUser);
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Export failed", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    // ---- Disable -------------------------------------------------------
    private async void BtnDisable_Click(object sender, RoutedEventArgs e)
    {
        var targets = _devices.Where(d => d.Selected && d.Enabled).ToList();

        // Intune managed devices have no disable operation - flag any that were selected.
        var skippedIntune = _devices.Count(d => d.Selected && d.Source == DeviceSource.Intune);
        if (skippedIntune > 0)
            Log($"Note: {skippedIntune} selected Intune device(s) skipped - Disable is not applicable to MDM records (use Delete).");

        if (targets.Count == 0)
        {
            MessageBox.Show("No selected, currently-enabled devices to disable.\n\n" +
                "(Intune devices cannot be disabled - use Delete for those.)", "Nothing to do",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirm = MessageBox.Show(
            $"Disable {targets.Count} device(s)?\n\n" +
            $"AD: {targets.Count(t => t.Source == DeviceSource.AD)}   " +
            $"Entra: {targets.Count(t => t.Source == DeviceSource.Entra)}",
            "Confirm disable", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            SetBusy(true);
            ApplyAdCredentials();
            int ok = 0, fail = 0;
            foreach (var d in targets)
            {
                try
                {
                    if (d.Source == DeviceSource.AD)
                        await Task.Run(() => _ad.Disable(d));
                    else
                        await _entra.DisableAsync(d);

                    d.Status = $"Disabled {DateTime.Now:HH:mm:ss}";
                    Log($"[DISABLED] {d.SourceLabel}: {d.Name}");
                    AuditLog.Write("DISABLE", d.SourceLabel, d.Name, d.Id, "OK", ActorFor(d));
                    ok++;
                }
                catch (Exception ex)
                {
                    d.Status = "Disable FAILED";
                    Log($"[FAILED]  {d.SourceLabel}: {d.Name} :: {ex.Message}");
                    AuditLog.Write("DISABLE", d.SourceLabel, d.Name, d.Id, "FAILED: " + ex.Message, ActorFor(d));
                    fail++;
                }
            }
            Log($"Disable complete. OK: {ok}, failed: {fail}.");
            UpdateSelectionInfo();
            OfferResultExport("Disable", targets);
        }
        finally { SetBusy(false); }
    }

    /// <summary>The acting identity recorded in the audit log for a given device.</summary>
    private string ActorFor(StaleDevice d) =>
        d.Source == DeviceSource.AD
            ? (_ad.Credentials is { HasUser: true } ? _ad.Credentials.Username! : Environment.UserName)
            : (_entra.SignedInUser ?? Environment.UserName);

    // ---- Delete --------------------------------------------------------
    private async void BtnDelete_Click(object sender, RoutedEventArgs e)
    {
        var targets = _devices.Where(d => d.Selected).ToList();
        if (targets.Count == 0)
        {
            MessageBox.Show("No devices selected.", "Nothing to do",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var stillEnabled = targets.Count(t => t.Enabled);
        var prompt =
            $"You are about to PERMANENTLY DELETE {targets.Count} device(s).\n\n" +
            $"AD: {targets.Count(t => t.Source == DeviceSource.AD)}   " +
            $"Entra: {targets.Count(t => t.Source == DeviceSource.Entra)}   " +
            $"Intune: {targets.Count(t => t.Source == DeviceSource.Intune)}\n" +
            (stillEnabled > 0 ? $"\nWARNING: {stillEnabled} of these are still ENABLED (not disabled first).\n" : "") +
            "\nThis cannot be easily undone. Type DELETE to confirm.";

        var typed = ConfirmDialog.Prompt(this, "Confirm delete", prompt, "DELETE");
        if (!typed) { Log("Delete aborted by operator."); return; }

        try
        {
            SetBusy(true);
            ApplyAdCredentials();
            int ok = 0, fail = 0;
            foreach (var d in targets)
            {
                try
                {
                    switch (d.Source)
                    {
                        case DeviceSource.AD:
                            await Task.Run(() => _ad.Delete(d));
                            break;
                        case DeviceSource.Entra:
                            await _entra.DeleteAsync(d);
                            break;
                        case DeviceSource.Intune:
                            await _entra.DeleteIntuneAsync(d);
                            break;
                    }

                    d.Status = $"Deleted {DateTime.Now:HH:mm:ss}";
                    d.Selected = false;   // prevent a second Delete click from re-targeting it
                    Log($"[DELETED] {d.SourceLabel}: {d.Name}");
                    AuditLog.Write("DELETE", d.SourceLabel, d.Name, d.Id, "OK", ActorFor(d));
                    ok++;
                }
                catch (Exception ex)
                {
                    d.Status = "Delete FAILED";
                    Log($"[FAILED]  {d.SourceLabel}: {d.Name} :: {ex.Message}");
                    AuditLog.Write("DELETE", d.SourceLabel, d.Name, d.Id, "FAILED: " + ex.Message, ActorFor(d));
                    fail++;
                }
            }
            Log($"Delete complete. OK: {ok}, failed: {fail}.");
            UpdateSelectionInfo();
            OfferResultExport("Delete", targets);
        }
        finally { SetBusy(false); }
    }

    // ---- About ---------------------------------------------------------
    private void BtnAbout_Click(object sender, RoutedEventArgs e)
    {
        new AboutWindow { Owner = this }.ShowDialog();
    }
}
