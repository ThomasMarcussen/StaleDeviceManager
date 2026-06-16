# Changelog

All notable changes to this project are documented here.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.3.0] - 2026-06-16

### Added
- **OS filter** on the scanned grid - a dropdown of the operating systems present
  in the results filters the view live.
- **Last user** column, populated where the platform exposes it: Intune primary
  user (`userPrincipalName`) and Entra registered owner. AD computer objects do
  not store a last-logged-on user, so it stays blank there.
- **Export to CSV** of the (filtered) scan results.
- **Result export** offered after each Disable and Delete batch.
- **Audit log** - append-only `audit.log` under
  `%ProgramData%\Stale Device Manager\`, recording every scan, disable, and
  delete with timestamp, operator, device, and result. An "Open audit log"
  button opens it.
- The column header tick now selects / clears all shown rows.

## [1.2.0] - 2026-06-16

### Added
- **Alternate credentials.** Optional credentials panel so cleanup can run under
  an account other than the logged-on Windows user:
  - On-prem AD: username, password, and an optional domain/DC (supports a
    different domain). Blank = current Windows user.
  - Entra ID / Intune: a "Sign in as (UPN)" hint that pre-targets the admin
    account in the interactive browser sign-in (MFA completed in the browser).
- **Signed MSI installer** (WiX). Installs per-machine to
  `C:\Program Files\Stale Device Manager` with Start menu and desktop shortcuts,
  an Add/Remove Programs entry, and major-upgrade support.

### Changed
- The Entra/Intune sign-in no longer persists a token cache, so each connect is
  an explicit sign-in - you can authenticate as an account other than the
  logged-on user.

## [1.1.0] - 2026-06-16

### Added
- **Intune** as a third device source: scans managed devices by `lastSyncDateTime`,
  with delete support (Intune has no disable state, so Disable skips Intune rows).
- **About** dialog with author/contact details and clickable links.
- Application icon (multi-resolution), embedded in the executable.

### Changed
- Renamed the application to **Stale Device Manager**.
- Entra ID and Intune now share a single Microsoft Graph sign-in.

### Fixed
- **Safety:** hard guard against acting on objects with a missing/invalid
  identifier - prevents an empty AD distinguishedName from ever binding to the
  domain root. AD deletes now also verify the bound object is a computer.
- **Staleness accuracy:** Entra/Intune no longer flag devices whose creation or
  enrolment date is unknown; a device must be provably older than the threshold.
- Delete confirmation now reports the Intune count, and deleted rows are
  deselected so they cannot be re-targeted by a second action.

## [1.0.0] - 2026-06-16

### Added
- Initial release: WPF desktop tool to scan, disable, and delete stale device
  records across on-premises Active Directory and Entra ID.
- Multi-signal staleness detection (password age, last logon/sign-in, creation date).
- Typed `DELETE` confirmation, live activity log, and self-contained single-file build.
