# Keisai (罫彩)

[日本語](README.md) | English

Keisai is a simple, ruled-notebook-style sticky note for Windows. It opens directly into an editable note and provides rich-text formatting, ten paper backgrounds, and monitor-configuration-aware window placement. It is a free Windows application for both personal and commercial use.

- Official download page: https://ytec.cloudfree.jp/forge/en/projects/keisai/
- Supported systems: Windows 10 and Windows 11, 64-bit
- Installation: None; extract the ZIP and run `Keisai.exe`
- Network communication: None
- Source code: https://github.com/ytec-forge-commits/ytec-sticky-note
- Current release: [1.5.4 Beta (unsigned)](https://ytec.cloudfree.jp/forge/en/projects/keisai/)

## Features

- Bold, italic, underline, strikethrough, center alignment, and bulleted lists
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

The note, rich-text formatting, and selected background are stored in `data/sticky-note.json` beside the executable. The JSON file is not encrypted. Formatted content is stored as a Base64-encoded XAML package, with RTF compatibility data and searchable plain text. Keisai can still load older RTF-only data and falls back to RTF if the newer format cannot be read.

Updates are written through a temporary file, and the previous save is preserved as `.bak` before replacement. The first migration from the version 1 format also creates `sticky-note.json.v1.bak`, which is not overwritten by later saves.

Window position and size are stored separately in `data/window-state.json`. Keisai creates a monitor-configuration identifier from monitor count, arrangement, resolution, work area, and display scaling, and retains up to twelve placement profiles. This allows, for example, a three-monitor home setup and a two-monitor workplace setup to restore different positions. The previous placement data is preserved as `window-state.backup.json`.

When an external monitor sleeps, disconnects, or reconnects, Keisai pauses placement saving until the monitor layout is stable. Temporary Windows-initiated moves or resizes do not overwrite the saved profile. Once the layout stabilizes, Keisai restores the position and size stored for that configuration.

When carrying Keisai on a USB drive or Google Drive, move the entire application folder rather than only the executable.

## Starting with Windows

Keisai changes Windows startup settings only when the user explicitly enables **Start with Windows** in the application. Normal application startup only reads the existing registration and does not rewrite it. Disabling the option is also performed only in response to an explicit user action.

For portable installations on Google Drive, Keisai follows the same local-waiter design as Koyomado. A small helper under `%LOCALAPPDATA%\Y-TEC\StickyNote` starts before the application. It exits without launching a duplicate if Keisai is already running, and it does not treat the presence of a Google Drive process as proof that the drive is ready.

The helper waits for the application files and both note and placement data to become readable, and for the destination folder to become writable and stable. It then starts Keisai. The wait is limited to ten minutes; if readiness is not reached, that startup attempt is silently skipped.

Starting with version 1.5.4, the helper statically links the MSVC CRT. It can therefore run by itself under `%LOCALAPPDATA%` on Windows systems without the Visual C++ Redistributable, and no companion runtime DLL needs to be copied.

If the application folder is moved, enable **Start with Windows** again from the new location. On managed workplace PCs, follow the administrator's and security product's policies. Even this explicit registration may be detected by some endpoint-security configurations.

## Development

Requirements:

- Windows
- .NET 10 SDK
- Rust toolchain for the startup helper

Python with ReportLab is required only to regenerate the PDF manual, and Python with Pillow is required only to regenerate the icon. They are not required for a normal application build or test run.

```powershell
dotnet build src/YtecStickyNote/YtecStickyNote.csproj -c Release
dotnet run --project tests/YtecStickyNote.Tests/YtecStickyNote.Tests.csproj -c Release
cargo test --manifest-path src/YtecStickyNote.Startup/Cargo.toml --release --locked
./scripts/check-startup-dependencies.ps1 -ExecutablePath src/YtecStickyNote.Startup/target/release/YTEC-Sticky-Note-Startup.exe
dotnet run --project tests/YtecStickyNote.VisualTest/YtecStickyNote.VisualTest.csproj -c Release -- 520 620 artifacts/visual-test/520x620.png
```

To open a test instance without touching real user data:

```powershell
dotnet run --project src/YtecStickyNote/YtecStickyNote.csproj -c Release -- --test-mode
```

## Packaging

```powershell
powershell -ExecutionPolicy Bypass -File scripts/package.ps1
```

The packaging script creates `artifacts/Keisai-win-x64/`, `artifacts/Keisai-1.5.4-win-x64.zip`, and a matching `.sha256.txt` file. The public ZIP also includes `output/pdf/罫彩_操作説明書.pdf`. Existing user data under a distribution folder is preserved, and the ZIP never includes personal data from `data`.

The application package is self-contained, and the startup helper statically links the MSVC CRT, so the destination PC does not require a separate .NET runtime or Visual C++ Redistributable installation. Keep the application folder together when carrying Keisai. For compatibility with older startup registrations, the ZIP also contains `YTEC-Sticky-Note.exe`, which launches the same application; new users should run `Keisai.exe`.

## Out of scope

Keisai intentionally does not include multiple notes, cloud synchronization, authentication, encryption, printing, PDF export, image attachments, or sharing features.

## Code signing policy

Free code signing provided by [SignPath.io](https://about.signpath.io/), certificate by [SignPath Foundation](https://signpath.org/).

The application process, review requirements, privacy policy, and GitHub Actions release procedure are documented in [CODE_SIGNING_POLICY.md](CODE_SIGNING_POLICY.md). Until Keisai is accepted by SignPath Foundation or when the signing service is unavailable, distributed builds are explicitly labeled as unsigned and accompanied by SHA-256 checksums.

## License and credits

Keisai source code is available under the [Apache License 2.0](LICENSE.txt). See [NOTICE](NOTICE) for attribution and copyright notices, [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) for third-party software, and [PRIVACY.md](PRIVACY.md) for data handling.

The application icon is original artwork generated and refined specifically for this project. Keisai uses no external UI library or external visual asset. Microsoft .NET and WPF provide the application runtime and UI framework.
