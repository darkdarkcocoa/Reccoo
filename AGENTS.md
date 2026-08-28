# Repository Guidelines

## Project Structure & Module Organization

Cocoa Recorder (GitHub repository `darkdarkcocoa/cocoa-recorder`) is a Windows-only, single-window WPF application targeting `net9.0-windows`. The project intentionally uses a flat structure:

- `MainWindow.xaml` and `MainWindow.xaml.cs` contain the UI and interaction logic.
- `AudioRecorder.cs` handles loopback capture and WAV/MP3 output through NAudio.
- `Mascot.cs` draws the pixel-art mascot; extend `MascotMood` for new expressions.
- `Localization.cs` stores all Korean and English UI text.
- `App.xaml` is the shared design system for colors, brushes, and control styles.
- `Fonts/` contains embedded fonts; `assets/` contains README screenshots.
- `.github/workflows/` defines Windows CI and tag-based releases.

Treat `bin/`, `obj/`, `publish/`, and ignored `design/` content as generated or scratch output.

## Build, Test, and Development Commands

Run commands from the repository root on Windows with the .NET 9 SDK:

```powershell
dotnet restore
dotnet run
dotnet build -c Release /warnaserror
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish
```

`dotnet run` launches a debug build. The Release build matches CI and treats warnings as errors. The publish command creates the distributable `publish/CocoaRecorder.exe`.

## Coding Style & Naming Conventions

Use four-space indentation in C# and conventional .NET naming: `PascalCase` for types, methods, properties, and XAML resource keys; `camelCase` for locals and parameters; `_camelCase` for private fields. Nullable reference types and implicit usings are enabled—honor annotations and avoid redundant imports. Keep the current simple architecture; do not introduce view-model, dependency-injection, or service layers without prior discussion. Reuse resources from `App.xaml` instead of hard-coding UI colors or styles. Add every user-facing string in both languages in `Localization.cs`, preserving the friendly tone.

## Testing Guidelines

There is currently no test project or coverage threshold. Before submitting, run the warning-as-error Release build and manually verify recording, pause/stop, WAV and MP3 saving, playback, device selection, library actions, and both KOR/EN modes. If tests are introduced, place them in a separate `CocoaRecorder.Tests` project and name files `<Subject>Tests.cs`.

## Commit & Pull Request Guidelines

Recent commits use short, imperative summaries, optionally prefixed by an area such as `docs:`. Keep each commit focused. Use branches such as `feature/short-description`. PRs should explain the change and validation performed, link relevant issues, and include screenshots for UI changes. Ensure `dotnet build -c Release /warnaserror` passes. Discuss large UI or architectural changes—and any new NuGet dependency—in an issue first.
