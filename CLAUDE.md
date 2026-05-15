# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

Reccoo is a single-window WPF desktop app for Windows that records the system audio output (loopback) and saves it as WAV or MP3, wrapped in a pixel-art UI. Target framework is `net9.0-windows`; the project does not build on non-Windows platforms.

## Commands

```powershell
dotnet run                       # debug run from source
dotnet build -c Release          # Release build → bin/Release/net9.0-windows/Reccoo.exe
dotnet build                     # Debug build  → bin/Debug/net9.0-windows/Reccoo.exe

# Self-contained single-file artifact (the README's "Reccoo.exe" download).
# Publishes to ./publish/Reccoo.exe (~76 MB; bundles the .NET 9 runtime).
dotnet publish -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true -o publish
```

There is no test project — no `dotnet test` to run.

## Architecture

The entire app is three source files plus one XAML resource dictionary. Don't go looking for view-models, services, or DI — there are none.

- **`AudioRecorder.cs`** — capture/encoding engine. Uses `WasapiLoopbackCapture` to grab the render endpoint, writes a temp WAV via `WaveFileWriter`, then on stop either `File.Copy`s to the final path (WAV) or transcodes through `LameMP3FileWriter` (MP3). Surfaces completion through the `RecordingFinished` event; the UI never touches NAudio types directly except `MMDevice` for the device picker. The only NuGet dependencies for the whole app are `NAudio` and `NAudio.Lame` — don't add more without a strong reason.
- **`MainWindow.xaml` / `MainWindow.xaml.cs`** — every interactive feature lives here: transport (record/stop/pause), 56-bar live waveform, 18-cell input meter, mascot sprite drawing, recordings library scan/play/delete, custom window chrome (drag, double-click maximize, grip resize). Animations are driven by three `DispatcherTimer`s (UI 50ms, waveform 60ms, blink 500ms). Recordings save to `Documents\Music\Reccoo\Reccoo_yyyyMMdd_HHmmss.{wav|mp3}`.
- **`App.xaml`** — the design system. All pixel-art colors (cream/coral/mint/lilac/gold/ink palette), brushes, and the chunky button/combo/scrollbar styles with 4-px offset shadows are defined here as a single `ResourceDictionary`. Anything that affects look-and-feel almost certainly belongs in this file, not in `MainWindow.xaml`.

The mascot ("Recco") is not an image asset — it's drawn pixel-by-pixel from `MainWindow.xaml.cs` into a `Canvas`, switching expressions based on a `MascotMood` enum.

## Conventions

- The app is bilingual: UI strings, mascot speech, and many comments are Korean. Keep that voice when editing user-facing text.
- Fonts are embedded via `<Resource Include="Fonts\*.ttf">` in the csproj. WPF font fallback is relied on (`Pixelify Sans → DotGothic16 → Consolas`) so Korean/CJK glyphs render in the pixel style without a separate code path.
- `design/` holds Claude-generated design mocks and is gitignored; treat it as scratch, not source.
- `Nullable` and `ImplicitUsings` are both enabled — match that style (no `using System;` clutter, honor nullable annotations).
- When searching with Glob/Grep, ignore `obj/**/*_wpftmp.*.cs` and `obj/**/*.g.cs` — these are WPF/MSBuild-generated intermediates, not real source.
