# Changelog

All notable changes to this project are documented here.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.2.0] - 2026-06-16

### Added
- **Alternate credentials.** Optional credentials panel so cleanup can run under
  an account other than the logged-on Windows user:
  - On-prem AD: username, password, and an optional domain/DC (supports a
    different domain). Blank = current Windows user.
  - Entra ID / Intune: a "Sign in as (UPN)" hint that pre-targets the admin
    account in the interactive browser sign-in (MFA completed in the browser).

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
