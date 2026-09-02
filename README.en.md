# Keisai (罫彩)

[日本語](README.md) | English

Keisai is a simple, ruled-notebook-style sticky note for Windows. It opens directly into an editable note and provides rich-text formatting, ten paper backgrounds, and monitor-configuration-aware window placement. It is a free Windows application for both personal and commercial use.

- Official download page: https://ytec.cloudfree.jp/forge/en/projects/keisai/
- Supported systems: Windows 10 and Windows 11, 64-bit
- Distribution: Microsoft Store package (published after certification) and portable ZIP
- Portable installation: None; extract the ZIP and run `Keisai.exe`
- Network communication: None
- Source code: https://github.com/ytec-forge-commits/ytec-sticky-note
- Current release: [1.6.0 Preview (self-signed)](https://ytec.cloudfree.jp/forge/en/projects/keisai/)

## Features

- Bold, italic, underline, strikethrough, center alignment, and bulleted lists
- Multiple pages in one window, with independent content, rich formatting, and background per page
- Undo/redo, current-page search, clear character formatting, and paste as plain text
- Every font installed on the PC, with frequently used fonts starred and listed first
- Five font sizes and ten text colors
- Ten backgrounds: Lemon, Sakura, Mint, Sky, Ivory, Lavender, Peach, Aqua, Gray, and Mocha
- Fixed line spacing that naturally aligns text baselines with the horizontal ruling
- An editable area that starts to the right of the red margin line
- Toolbar controls that follow the font, size, and color at the caret or selected text
- Natural wrapping for bulleted items and `Shift+Enter` for a line break within an item
- Automatic saving of text, formatting, background, window position, and window size
- Separate window placement profiles for different monitor configurations
- Portable storage in a `data` directory beside the executable
- A `.bak` copy of the previously saved note
- A dedicated application icon and system-tray operation without a taskbar button

## System tray behavior

At startup, Keisai displays the note at its saved position and places its icon in the Windows notification area instead of the taskbar. Minimizing the window or clicking the close button hides the note without terminating the application. Double-click the tray icon to show it again. To exit completely, right-click the tray icon and select **Exit**.

## Portable data and window placement

The portable build stores all pages, rich-text formatting, and page-specific backgrounds in `data/sticky-note.json` beside the executable. The Microsoft Store build uses the app-specific Windows LocalState directory instead of the read-only package installation directory. Both channels use the same data format, but their storage locations remain separate and are not copied automatically.

The JSON file is not encrypted. Every page stores a Base64-encoded XAML package, RTF compatibility data, and searchable plain text. Single-page data created by version 1.5.4 or earlier is migrated into the first page without losing its formatting or background.

Updates are written through a temporary file, and the previous save is preserved as `.bak` before replacement. The first migration from the version 1 format also creates `sticky-note.json.v1.bak`, which is not overwritten by later saves.

Window position and size are stored separately in `data/window-state.json`. Keisai creates a monitor-configuration identifier from monitor count, arrangement, resolution, work area, and display scaling, and retains up to twelve placement profiles. This allows, for example, a three-monitor home setup and a two-monitor workplace setup to restore different positions. The previous placement data is preserved as `window-state.backup.json`.

When an external monitor sleeps, disconnects, or reconnects, Keisai pauses placement saving until the monitor layout is stable. Temporary Windows-initiated moves or resizes do not overwrite the saved profile. Once the layout stabilizes, Keisai restores the position and size stored for that configuration.

When carrying Keisai on a USB drive or Google Drive, move the entire application folder rather than only the executable.

## Starting with Windows

The Microsoft Store build uses the Windows package StartupTask. For the portable build, Keisai verifies a Y-TEC-signed runtime manifest and the SHA-256 of every EXE, DLL, runtime JSON, and DAT file before copying the self-contained application to `%LOCALAPPDATA%\Y-TEC\StickyNote\app`. It then registers that local Keisai executable directly with Windows Run. The former `YTEC-Sticky-Note-Startup.exe`, which triggered an antivirus heuristic, is not included in the 1.6.0 distribution.

For safety, a saved state file is limited to 64 MiB and 1,000 pages. If oversized or damaged data is detected, Keisai leaves the original file untouched and disables editing and saving while it displays a warning.

Keisai changes Windows startup settings only when the user explicitly enables **Start with Windows**. At that point it verifies the application signature and SHA-256 values before creating the local cache and Run entry. Normal startup does not rewrite a correct registration. Disabling the option is also performed only after an explicit user action.

At Windows sign-in, Keisai runs only the verified local copy; it does not execute application code from Google Drive. The canonical note and placement files remain in the original portable `data` directory on Google Drive.

Keisai does not treat the presence of a Google Drive process as proof that the drive is ready. It waits until the original storage directory and any existing note and placement files are readable and writable for three continuous seconds. The wait is limited to ten minutes; if readiness is not reached, that startup attempt is silently skipped.

After installing a legitimate update, turn **Start with Windows** off and on once from the new version to refresh the signed local cache. The portable data location and format do not change.

If the application folder is moved, enable **Start with Windows** again from the new location. On managed workplace PCs, follow the administrator's and security product's policies. Even this explicit registration may be detected by some endpoint-security configurations.

When version 1.6.0 detects the retired helper-based registration, it asks whether to replace it with the local Keisai executable or remove it. Windows Run settings are changed only after the user answers that migration prompt.

For the safest update from an older version, turn off **Start with Windows** in the old version before replacing the application folder, then start version 1.6.0 manually and enable it again. If the files were already replaced, manually start a verified copy of version 1.6.0 and finish the migration prompt before the next Windows restart.

## Development

Requirements:

- Windows
- .NET 10 SDK

Python with ReportLab is required only to regenerate the PDF manual, and Python with Pillow is required only to regenerate the icon. They are not required for a normal application build or test run.

```powershell
dotnet build src/YtecStickyNote/YtecStickyNote.csproj -c Release
dotnet run --project tests/YtecStickyNote.Tests/YtecStickyNote.Tests.csproj -c Release
dotnet run --project tests/YtecStickyNote.VisualTest/YtecStickyNote.VisualTest.csproj -c Release -- 520 620 artifacts/visual-test/520x620.png
```

To open a test instance without touching real user data:

```powershell
dotnet run --project src/YtecStickyNote/YtecStickyNote.csproj -c Release -- --test-mode
```

## Packaging

```powershell
powershell -ExecutionPolicy Bypass -File scripts/package.ps1
powershell -ExecutionPolicy Bypass -File scripts/package-self-signed-direct.ps1
```

The first command creates an unsigned development candidate. The second creates the self-signed portable ZIP published on Forge and GitHub, a public-only CER, the Japanese manual, and `SHA256SUMS.txt`. Personal `data` is never included. A self-signed signature helps detect tampering but is not CA-backed identity verification and does not remove Windows or SmartScreen warnings. Keisai never installs the certificate into the user's trust store.

The Store MSIX must be built with the Identity Name and Publisher supplied by Partner Center; these values are never guessed.

```powershell
powershell -ExecutionPolicy Bypass -File scripts/package-msix.ps1 `
  -PackageIdentityName '<Partner Center Identity Name>' `
  -Publisher '<Partner Center Publisher>' `
  -CreateUpload
```

The application package is self-contained, so the destination PC does not require a separate .NET runtime or Visual C++ Redistributable installation. Keep the application folder together when carrying Keisai. For compatibility with older startup registrations, the ZIP also contains `YTEC-Sticky-Note.exe`, which launches the same application; new users should run `Keisai.exe`.

## Out of scope

Keisai intentionally does not include multiple notes, cloud synchronization, authentication, encryption, printing, PDF export, image attachments, or sharing features.

## Code signing policy

Microsoft signs the Store package after certification. Until the SignPath Foundation application is resolved, direct Forge and GitHub downloads use a Y-TEC self-signed signature plus SHA-256. See [CODE_SIGNING_POLICY.md](CODE_SIGNING_POLICY.md) for the limitations of self-signing, private-key protection, and future provider migration.

## License and credits

Keisai source code is available under the [Apache License 2.0](LICENSE.txt). See [NOTICE](NOTICE) for attribution and copyright notices, [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) for third-party software, and [PRIVACY.md](PRIVACY.md) for data handling.

The application icon is original artwork generated and refined specifically for this project. Keisai uses no external UI library or external visual asset. Microsoft .NET and WPF provide the application runtime and UI framework.
