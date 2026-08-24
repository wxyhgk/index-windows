# Index.Windows

`Index.Windows` contains Windows-specific infrastructure that does not require WinUI. Keeping
these adapters outside the executable project lets both the app and unit tests consume the same
compiled implementation without triggering Windows App SDK packaging targets.

Current adapters:

- `WindowsCaptureImagePreparer` (`System.Drawing` crop and PNG validation)
- `WindowsBrowserSourceMetadataResolver` (local browser history metadata)
- `WindowsSourceApplicationResolver` (Win32 window/process attribution)

WinUI, HWND lifecycle, clipboard windows, and other desktop-side effects remain in the executable
project's `Platform/` directory until their contracts and lifecycle boundaries are ready.
