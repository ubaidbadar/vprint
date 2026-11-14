# VPrint technical architecture

VPrint 0.1 is a .NET 10 WPF app with a small view model, a Windows printer manager, a Win32 event-log adapter, and a local SQLite store. It is a printer-queue and job-metadata manager, not a content capture service.

The built-in Microsoft Print to PDF driver is reused to create named queues. These appear in Windows print dialogs. Windows itself prompts the user to save PDF output. Each successful print writes a PrintService Operational event 307, when logging is enabled. The event includes the printer queue name in Param5, document name in Param2 when logging policies allow it, user in Param3, pages in Param8, and total bytes in Param7. VPrint parses these events, filters managed queues, deduplicates them and stores only metadata.

The desktop dashboard uses a virtualized WPF DataGrid and a filtered collection view. Printer enumeration is requested on startup, after queue changes or on manual refresh; it is not executed on each UI change.

Security:
- All printer creation and removal needs explicit administrator elevation via Windows UAC.
- Managed printers carry a fixed name prefix, and removal re-checks the printer driver.
- Only product-generated PowerShell is run, with quoted user labels.
- No telemetry, accounts or remote services.

## Later: true automatic content capture

A Windows Print Support App v4 (PSA v4), packaged as MSIX and registered through the windows.printSupportVirtualPrinterWorkflow extension, is required for the planned driverless automatic document capture. This needs Windows 11 24H2+, an OS-activated workflow task, actual PDL handling and conversion, and end-to-end Windows validation. It is separate from the portable .exe produced by this first release. Do not call this app a silent PDF recorder until the driverless engine is implemented and tested.

Relevant Microsoft docs:
- https://learn.microsoft.com/en-us/windows-hardware/drivers/devapps/print-support-app-v4-design-guide
- https://learn.microsoft.com/en-us/windows-hardware/drivers/devapps/msix-manifest-specification-print-support-virtual-printer
- https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview
