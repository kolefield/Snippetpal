# Building Snippetpal

Snippetpal is a native C# / WPF app on .NET 10, with Windows Forms used for its tray icon. It has no third-party runtime NuGet dependencies. Builds target Windows x64 and include the .NET runtime.

## Local build

Install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) and run PowerShell from the repository root:

```powershell
.\scripts\build.ps1 -Test -Installer
```

The installer build downloads the pinned portable NSIS compiler into the ignored `.tools` folder, verifies its SHA-256, and does not install it system-wide. The script also recognizes a development SDK at `%LOCALAPPDATA%\SnippetpalBuildTools\dotnet`; use `-Dotnet 'C:\path\dotnet.exe'` to select another SDK.

Outputs are in `artifacts/release/`: setup EXE, standalone EXE, portable ZIP, third-party notices ZIP, and `SHA256SUMS.txt`. Versioned publish directories keep a running app from being overwritten. The NSIS installer is per-user, registers a Start menu shortcut and an uninstall entry, checks for a locked app before replacing files, and leaves the user's library intact. The earlier WiX MSI source is retained in `installer/Package.wxs` for reference; it is not used by the release workflow.

Default tests use isolated data, show no window, and do not access the system clipboard. `-TestClipboard` additionally overwrites the real clipboard with synthetic content; use it only when intended. Offscreen previews render the real WPF controls with sample data:

```powershell
dotnet .\src\Snippetpal\bin\Release\net10.0-windows\Snippetpal.dll --render-preview --data-dir .\artifacts\previews
```

Offscreen previews cannot verify native acrylic or actual mouse input. The initial release has automated persistence, reordering, folder watching, installer lifecycle, and layout checks; manual drag/drop, multi-monitor/DPI behavior, and actual Snipping Tool workflows still warrant testing.

## Publishing a release

1. Update `<Version>` in `src/Snippetpal/Snippetpal.csproj` and commit the change to `main`.
2. Tag that commit with the matching version, for example `git tag v0.1.4`, and push the tag with `git push origin v0.1.4`.
3. The GitHub Actions workflow builds and tests on Windows, then creates a GitHub release with downloadable files under **Assets**. Failed builds or installer tests block publication.

The same workflow checks pushes and pull requests without publishing releases. Release jobs receive write permission; build jobs have read-only repository access. Installer smoke tests run only on disposable GitHub-hosted runners, not on a developer's personal account. Current releases are unsigned previews; no signing certificates, license server, or automatic updater are configured.

## Data and capture behavior

- Regular app data lives in `%LOCALAPPDATA%\Snippetpal`; `--data-dir PATH` creates an isolated library. Windows DPAPI encrypts text and metadata, with an atomic recovery backup. Cached images are ordinary PNG files.
- A Windows clipboard listener collects new text and images while running. Supported exclusion flags are respected; existing Win+V history is not imported. Images cannot reliably be attributed to the Snipping Tool alone.
- Capture watchers monitor the Screenshots known folder and `Videos\Screen Recordings`, with configurable paths and a 30-second fallback scan. Only saved recordings are imported. Removal preserves original files; cleared/dismissed captures are excluded from reimport.
- History is bounded and deduplicated. Pausing history excludes captures created during the paused period when recording resumes.

See [third-party notices](../THIRD-PARTY-NOTICES.md) for bundled component details.
