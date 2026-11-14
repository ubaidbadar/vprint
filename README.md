# VPrint

VPrint is a Windows 11 desktop application for managing multiple named virtual PDF printer queues and viewing successful print-job metadata separately for each queue.

**Milestone 0.1: Windows build in validation.** Automatic PDF capture and previews are not yet implemented. The current queues use the built-in Microsoft Print to PDF driver, so Windows still prompts for an output filename.

## Implemented

- Create and remove printers such as VPrint - Kitchen, VPrint - Cashier and VPrint - Receipt.
- See printers in ordinary Windows app print dialogs.
- Record successful print-job metadata (event ID 307) per printer.
- Search by document name, printer and Windows user, with virtualized table rows.
- Store history privately at %LOCALAPPDATA%/VPrint/jobs.sqlite3.
- Record jobs after the app is closed when Windows print logging is enabled; import recent Windows events at next launch.
- Build a portable self-contained VPrint.exe through GitHub Actions.

## Build

Requirements: Windows 11 x64, Microsoft Print to PDF feature enabled, .NET 10 SDK if building from source.

Run in PowerShell from the repository directory:

    ./scripts/build.ps1
    ./artifacts/win-x64/VPrint.exe

Or download the EXE artifact from a successful Actions run:
https://github.com/ubaidbadar/vprint/actions

Windows administrator permission is needed to create/remove queues and enable PrintService logging. Normal dashboard browsing does not need elevation.

## Try printing

1. Open VPrint and select Enable history if prompted.
2. Add a printer such as Kitchen.
3. From Word, Chrome or another Windows program, select VPrint - Kitchen and print.
4. Save the PDF when Windows opens its Save As dialog.
5. Inspect the completed job details in VPrint.

Document names may appear as "Print Document" if the Windows printer job-name event logging policy is disabled. Failed or cancelled jobs are not reported as successful print events.

## Important boundaries

This is a functional first milestone, not a fully verified print-driver product:
- It does not silently save PDFs or include document previews.
- The standalone EXE is not a full Windows installer or an MSIX printer package.
- Future driverless document capture will use Windows Print Support App v4 (24H2+), packaged through MSIX.
- The app and queue behavior still require end-to-end testing on Windows 11. GitHub Actions compilation and smoke checks alone do not prove printing works in every application.

More detail: docs/ARCHITECTURE.md. Logo source: assets/vprint-logo.svg.

## License

MIT.
