# Stale Device Manager

A Windows desktop (WPF) tool to find, disable, and delete stale device records
across **on-premises Active Directory**, **Entra ID**, and **Intune** from a
single UI.

![Platform](https://img.shields.io/badge/platform-Windows-blue)
![.NET](https://img.shields.io/badge/.NET-9.0-512BD4)
![License](https://img.shields.io/badge/license-MIT-green)

Stale device objects pile up as machines are reimaged, retired, or replaced
without cleanup. They inflate reporting, weaken security posture, and clutter
the directory. Stale Device Manager gives you one controlled, auditable workflow
to clear them out across all three platforms.

## The flow
**Scan → review & select → Disable → (wait) → Delete**

1. **Scan** - lists stale devices from the selected source(s) into the grid.
2. **Review & select** - tick the devices you want to act on. Columns show
   last activity, password-last-set (AD), creation date, and the reason flagged.
3. **Disable selected** - disables the ticked, still-enabled devices.
4. **Delete selected** - permanently removes the ticked devices. Requires typing
   `DELETE` to confirm; warns if any are still enabled.

## Sources and staleness rules
- **AD**: machine-account password older than the threshold AND last logon older
  than the threshold (or never) AND created before the threshold.
- **Entra ID**: `approximateLastSignInDateTime` older than the threshold (or never)
  AND registered before the threshold.
- **Intune**: `lastSyncDateTime` older than the threshold (or never synced) AND
  enrolled before the threshold.
- Threshold defaults to **90 days** (editable in the UI). "Exclude servers" skips
  server operating systems.

A device is only ever flagged when its age is *known* to be older than the
threshold - devices with unknown creation/enrolment dates are never flagged.

> **Note on Intune + Disable:** Intune managed devices have no "disable" state.
> The Disable action skips Intune rows (with a note in the log); use **Delete** to
> remove a stale managed-device record.

## Filtering, export and auditing
- **Filter** the scanned grid by operating system with the OS dropdown.
- The column header tick selects/clears all shown rows.
- **Last user** is shown where the platform exposes it: Intune primary user and
  Entra registered owner. AD computer objects do not record a last-logged-on user.
- **Export CSV** writes the current (filtered) view to a file.
- After each **Disable** or **Delete** batch you are offered a CSV of the results.
- Every scan, disable and delete is written to an append-only **audit log** at
  `%ProgramData%\Stale Device Manager\audit.log` (timestamp, operator, device,
  result). Use the **Open audit log** button to view it.

## Authentication
- **AD**: uses the current Windows account of whoever runs the tool. That account
  needs rights to read/disable/delete computer objects in the target OUs.
- **Entra ID + Intune**: click **Connect Entra…** for an interactive browser
  sign-in (MFA supported). Uses Microsoft's first-party Graph CLI public client,
  so **no app registration is required**. The signed-in admin needs:
  - `Device.ReadWrite.All`
  - `Directory.ReadWrite.All`
  - `DeviceManagementManagedDevices.ReadWrite.All` (Intune)

  e.g. Cloud Device Administrator + Intune Administrator, or Global Administrator.
  Consent is requested on first sign-in.

### Alternate credentials
Cleanup is often run under a dedicated admin account that differs from the
logged-on user. Expand the **Credentials (optional)** panel to provide them:

- **On-prem AD** - username (`DOMAIN\user` or `user@domain`), password, and an
  optional **Domain/DC** (e.g. `corp.local` or `dc01.corp.local`) when the
  account belongs to a different domain. Leave blank to use the current Windows user.
- **Entra ID / Intune** - a **Sign in as (UPN)** hint that pre-fills the target
  admin account in the browser sign-in. You still complete authentication
  (including MFA) in the browser, so any account type is supported.

## Download
From the [Releases](../../releases) page (both are signed):

- **`StaleDeviceManager-<version>.msi`** - installer. Installs to
  `C:\Program Files\Stale Device Manager` and adds Start menu and desktop
  shortcuts. Per-machine, so it prompts for elevation.
- **`StaleDeviceManager.exe`** - portable, self-contained single file. Just run it.

No .NET runtime is needed on the target machine. Run on a domain-joined host with
internet access for the Entra/Intune sign-in.

## Building from source
Requires the [.NET 9 SDK](https://dotnet.microsoft.com/download).

```
dotnet publish StaleDeviceManager.csproj -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist
```

The icon can be regenerated with `Build-Icon.ps1`.

## Safety
- Delete requires a typed `DELETE` confirmation and warns about still-enabled items.
- Nothing is pre-selected after a scan - you must explicitly tick each device.
- Hard guards refuse to act on objects with a missing/invalid identifier, and AD
  deletes verify the bound object is actually a computer first.
- Recommended workflow: disable first (AD/Entra), wait 2-4 weeks, then delete.
- Recovery: deleted AD objects need AD Recycle Bin / authoritative restore;
  deleted Entra devices restore from the Entra recycle bin for 30 days;
  deleted Intune records re-appear only on device re-enrolment.

> **Use at your own risk.** This tool performs irreversible directory changes.
> Always run a scan-only pass first, review the results, and test against a
> single known-dead device before any batch operation.

## Tech
C# / .NET 9 WPF. AD via `System.DirectoryServices`; Entra ID and Intune via the
Microsoft Graph SDK with interactive browser authentication.

## Contributing
Issues and pull requests are welcome.

## License
[MIT](LICENSE) © Thomas Marcussen

---
By [Thomas Marcussen](https://thomasmarcussen.com) · Thomas@ThomasMarcussen.com
