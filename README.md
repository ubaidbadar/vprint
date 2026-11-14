# VPrint

VPrint is a Windows 11 virtual-printer workspace built with **Rust, Tauri 2 and React**.

## Features (v0.2)

- Native Windows printer management using Win32 spooler APIs (no PowerShell).
- Multiple named VPrint printer queues, such as VPrint - Kitchen and VPrint - Counter.
- PrintService Event Log job history, SQLite persistence, per-printer filtering, search and job details.
- Virtualized React job rows, accessible dialogs and a compact Windows-native layout.
- In-app enabling of print event logging through Windows Event Log APIs.
- Automated Windows EXE and NSIS installer builds, Rust unit tests, and Playwright desktop UI tests.

### Download

Get the Windows EXE and installer from a successful run of the [Rust Windows workflow](https://github.com/ubaidbadar/vprint/actions/workflows/rust-windows.yml). Look for the VPrint-Rust-Windows artifact.

### Build

Windows 11 x64, MSVC Rust toolchain, Visual Studio C++ build tools, Node.js 24 and WebView2 are required for local development. Run the following from app/:

    npm install
    npm run tauri dev

For a release build:

    npm run build
    cargo test --manifest-path src-tauri/Cargo.toml
    npm run tauri build -- --bundles nsis

## Current limitations

This milestone still uses the inbox Microsoft Print to PDF driver and PORTPROMPT: port. **Windows still prompts for a Save As filename**. The dashboard stores successful print-job metadata, **not document contents**; automatic PDF capture and previews are not yet implemented.

The Rust app does not launch PowerShell. However **Windows still requires administrator access to add or remove traditional local printer queues**. When necessary, VPrint requests Windows UAC authorization using a hidden short-lived helper. That secure OS prompt cannot be eliminated simply by changing languages. A future PSA v4/MSIX implementation can provide driverless virtual printers without legacy third-party printer drivers; it is not part of v0.2.

The installer is currently unsigned, and this native printing integration requires real Windows 11 device tests beyond CI.

Local history: %LOCALAPPDATA%\VPrint\jobs.sqlite3 (compatible with the earlier version). No external service or telemetry.

See docs/ARCHITECTURE.md for implementation details.

MIT licensed.
