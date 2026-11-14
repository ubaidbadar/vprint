# Architecture: VPrint 0.2

- Tauri 2 (Rust) provides native Windows integration and IPC commands.
- React 19/Vite renders the desktop interface and accessible dialogs. TanStack Virtual renders only the visible history rows; background polling is skipped while the window is hidden. Equivalent snapshots are ignored.
- Direct Win32 spooler APIs: EnumPrintersW, AddPrinterW, OpenPrinterW, GetPrinterW, DeletePrinter, ClosePrinter. Printer names are validated. Removal checks both prefix and driver.
- Direct Windows Event Log APIs: EvtOpenChannelConfig, EvtGetChannelConfigProperty, EvtSetChannelConfigProperty, EvtSaveChannelConfig, EvtQuery, EvtNext, EvtRender, EvtClose. Event 307 records job metadata.
- SQLite stores metadata at %LOCALAPPDATA%\VPrint\jobs.sqlite3; event identifiers are unique and inserts are idempotent. Existing WPF history remains compatible.
- ShellExecuteExW requests Windows UAC elevation when needed. Elevated helper arguments are restricted to VPrint printer creation/removal and PrintService logging. No PowerShell, arbitrary script execution or non-VPrint printer deletion.
- Cargo unit tests cover event XML, labels and helper argument encoding; Playwright tests cover basic UI behavior and compact desktop layout.

### Boundaries

Microsoft Print to PDF prompts for an output filename; current VPrint captures metadata only. Silent document capture requires Microsoft Print Support App v4, including an MSIX manifest and Windows 11 24H2+ integration, not just a portable EXE.

The old WPF code is retained as a temporary migration reference until the Rust installer passes its workflow and Windows manual QA.

Relevant documentation:
- https://learn.microsoft.com/en-us/windows/win32/printdocs/addprinter
- https://learn.microsoft.com/en-us/windows-hardware/drivers/devapps/print-support-app-v4-design-guide
- https://learn.microsoft.com/en-us/windows-hardware/drivers/devapps/msix-manifest-specification-print-support-virtual-printer
- https://tauri.app/distribute/windows-installer/

### Manual QA required

Verify printer creation/removal, UAC cancel/allow behavior, printing from Chrome/Word/Notepad, event 307 recording, database persistence, high-DPI UI, protected print modes, multi-user sessions, and many concurrent jobs. CI passing is not equivalent to production certification.
