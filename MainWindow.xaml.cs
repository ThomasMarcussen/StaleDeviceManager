using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using StaleDeviceManager.Models;
using StaleDeviceManager.Services;

namespace StaleDeviceManager;

public partial class MainWindow : Window
{
    private readonly ActiveDirectoryService _ad = new();
    private readonly EntraService _entra = new();
    private readonly ObservableCollection<StaleDevice> _devices = new();

    public MainWindow()
    {
        InitializeComponent();
        Grid.ItemsSource = _devices;
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
            await _entra.ConnectAsync(Log);
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

            Log($"Scan complete. {_devices.Count} stale device(s) listed.");
            UpdateSelectionInfo();
        }
        catch (Exception ex)
        {
            Log($"Scan FAILED: {ex.Message}");
            MessageBox.Show(ex.Message, "Scan failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { SetBusy(false); }
    }

    private async Task BtnConnectEntraInline()
    {
        try
        {
            Log("Entra: opening sign-in…");
            await _entra.ConnectAsync(Log);
            EntraStatus.Text = $"Entra: {_entra.SignedInUser}";
            EntraStatus.Foreground = System.Windows.Media.Brushes.Green;
        }
        catch (Exception ex)
        {
            Log($"Entra connect FAILED: {ex.Message}");
        }
    }

    // ---- selection helpers --------------------------------------------
    private void BtnSelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var d in _devices) d.Selected = true;
        UpdateSelectionInfo();
    }

    private void BtnSelectNone_Click(object sender, RoutedEventArgs e)
    {
        foreach (var d in _devices) d.Selected = false;
        UpdateSelectionInfo();
    }

    private void UpdateSelectionInfo()
    {
        var sel = _devices.Count(d => d.Selected);
        SelectionInfo.Text = $"{sel} selected of {_devices.Count}";
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
                    ok++;
                }
                catch (Exception ex)
                {
                    d.Status = "Disable FAILED";
                    Log($"[FAILED]  {d.SourceLabel}: {d.Name} :: {ex.Message}");
                    fail++;
                }
            }
            Log($"Disable complete. OK: {ok}, failed: {fail}.");
        }
        finally { SetBusy(false); }
    }

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
                    ok++;
                }
                catch (Exception ex)
                {
                    d.Status = "Delete FAILED";
                    Log($"[FAILED]  {d.SourceLabel}: {d.Name} :: {ex.Message}");
                    fail++;
                }
            }
            Log($"Delete complete. OK: {ok}, failed: {fail}.");
        }
        finally { SetBusy(false); }
    }

    // ---- About ---------------------------------------------------------
    private void BtnAbout_Click(object sender, RoutedEventArgs e)
    {
        new AboutWindow { Owner = this }.ShowDialog();
    }
}
