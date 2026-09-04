# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

Cocoa Recorder (GitHub repository `darkdarkcocoa/cocoa-recorder`) is a single-window WPF desktop app for Windows that records the system audio output (loopback) and saves it as WAV or MP3, wrapped in a pixel-art UI. Target framework is `net9.0-windows`; the project does not build on non-Windows platforms.

The look is **Moon Studio**: a deep-indigo night sky with a pixel moon, a pixel cat sitting on it, and exactly three signal colors — mint for active, amber for waiting, pink for recording.

## Commands

```powershell
dotnet run                       # debug run from source
dotnet build -c Release          # Release build → bin/Release/net9.0-windows/CocoaRecorder.exe
dotnet build                     # Debug build  → bin/Debug/net9.0-windows/CocoaRecorder.exe

# Self-contained single-file artifact (the README's "CocoaRecorder.exe" download).
# Publishes to ./publish/CocoaRecorder.exe (~76 MB; bundles the .NET 9 runtime).
dotnet publish -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true -o publish
```

There is no test project — no `dotnet test` to run.

## Architecture

Seven source files plus one XAML resource dictionary. Don't go looking for view-models, services, or DI — there are none.

- **`AudioRecorder.cs`** — capture/encoding engine. Uses `WasapiLoopbackCapture` to grab the render endpoint, writes a temp WAV via `WaveFileWriter`, then on stop either `File.Copy`s to the final path (WAV) or transcodes through `LameMP3FileWriter` (MP3). Surfaces completion through the `RecordingFinished` event; the UI never touches NAudio types directly except `MMDevice` for the device picker. `CapturedBytes` and `SampleRate` exist only so the hero can show a running size and the endpoint's rate. The only NuGet dependencies for the whole app are `NAudio` and `NAudio.Lame` — don't add more without a strong reason.
- **`CountdownChime.cs`** — the countdown meow. Decodes the five `Sounds/meow-N.mp3` resources to PCM once at startup (`Mp3FileReader`) and plays one clip per countdown tick through a fresh `WaveOutEvent`; `ClipFor` maps remaining/total seconds to a clip so the closing ticks 3→2→1 always play meow-1→2→3 and meow-4/5 alternate earlier. Nothing plays at 0 — the app records the speakers, so the start of a take must stay clean. If a resource or the MP3 codec is missing it stays silent (`IsAvailable == false`) and the toggles disable themselves.
- **`OverlayWindow.xaml` / `OverlayWindow.xaml.cs`** — mini mode. A cat-face button in the title bar hides the main window and leaves a small always-on-top, draggable card (position and the mode itself persist in `settings.json` as `OverlayLeft`/`OverlayTop`/`MiniMode`; closing the app in mini mode reopens it there). The card is a thin view: its buttons call `MainWindow.Mini*` methods and `MainWindow` pushes state back through `SyncState`/`ShowCountdown`/`SetElapsed`/`SetCountdown` — all recording logic stays in `MainWindow`, which keeps running hidden. While mini mode is open a global hotkey (first free of F3, Ctrl+Alt+R, Ctrl+Shift+R, Ctrl+Alt+Shift+R; the registered one is shown on the card) starts/stops recording from any window. The card art lives in `Art/`: `overlay-frame.png` is a cat-eared 1536×1024 pixel frame generated with Codex image-2 and shown at exactly ¼ scale (the seven cross-stars were erased from it and are re-drawn in code so each can twinkle on its own clock), and `cocoa-{idle,count,vibe,sleep}.png` are pose sprites sliced from one generated sheet — recording swaps in the headphone-wearing vibe pose, nods it on a loop, and floats pixel notes (`Mascot.DrawNote`).
- **`Mascot.cs`** — Cocoa, a 22 × 24 pixel cat. `MascotMood` (Idle / Countdown / Recording / Paused) picks a pose (sit / alert / sleep / vibe — headphones on, eyes closed) and a collar color; `DrawFace` draws the small toggle/title-bar face and `DrawNote` a 4×4 pixel note.
- **`NightSky.cs`** — the hero backdrop: a 31 × 31 pixel crescent moon with craters, plus 130 dust stars and 11 cross stars from a seeded LCG (`SkySeed`), each twinkling on its own clock. Same seed always gives the same sky.
- **`MainWindow.xaml` / `MainWindow.xaml.cs`** — every interactive feature lives here: four nav tabs (record / library / settings / help; help is a full-window four-step tour, reachable with F1, where clicking a step changes the cat's pose and collar), the hero (status, 118-px timer, transport, 56-bar waveform, full-bleed countdown overlay), 18-cell input meter, recordings library scan/rename/drag-export (clicking a row opens it and pops out play / reveal-in-folder / delete), custom window chrome (drag, double-click maximize, grip resize). The private `Transport` enum is the single source of truth — one state decides the hero color, both transport buttons, the cat's pose, the status dot and which controls lock. Idle motion (sky twinkle, moon breathing, the cat's drift and tail sway, the status dot) runs as WPF animations; the two `DispatcherTimer`s (UI 50ms, waveform 60ms) drive only what depends on live audio — the timer text, the waveform, the input meter, and the cat's vertical lift while recording. New installs save to `Music\Cocoa Recorder\CocoaRecorder_yyyyMMdd_HHmmss.{wav|mp3}`; existing `Music\Reccoo` libraries remain in place.
- **`App.xaml`** — the design system. The night palette (night/hero/panel/line grounds, cream/lilac/mute text, mint/amber/pink signals, moon and cat colors), brushes, and the flat 2-px-border button/toggle/combo/scrollbar styles are defined here as a single `ResourceDictionary`. Anything that affects look-and-feel almost certainly belongs in this file, not in `MainWindow.xaml`.

Nothing in the hero is an image asset — the cat, the moon and every star are drawn cell by cell into `Canvas` elements from `Mascot.cs` and `NightSky.cs`.

## Conventions

- The app is bilingual: UI strings, mascot speech, and many comments are Korean. Keep that voice when editing user-facing text.
- Fonts are embedded as explicit resources in the csproj. `NeoDunggeunmo` handles display text in both languages — its 15-px grid keeps strokes even at the sizes this app uses, and it covers all 11,172 Hangul syllables plus Latin. `SUIT` handles dense Korean/English UI copy, and `Pixelify Sans` remains dedicated to the large timer.
- Section labels in the settings rail (SOURCE, FORMAT, INPUT LEVEL, COUNTDOWN, SAVE TO) stay English on purpose — they are part of the visual style. Everything a sentence (tabs, status, buttons, help, mascot lines) goes through `Localization.cs`.
- `design/` holds Claude-generated design mocks and the imported Claude Design source, and is gitignored; treat it as scratch, not source.
- `Nullable` and `ImplicitUsings` are both enabled — match that style (no `using System;` clutter, honor nullable annotations).
- When searching with Glob/Grep, ignore `obj/**/*_wpftmp.*.cs` and `obj/**/*.g.cs` — these are WPF/MSBuild-generated intermediates, not real source.
