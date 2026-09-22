<p style="text-align:center;">
  <img src=".github/media/waytype-banner.png" alt="WayType" style="max-width:800px; width:100%; height:auto;" />
</p>

[![Tests](https://github.com/bitbound/WayType/actions/workflows/test.yml/badge.svg)](https://github.com/bitbound/WayType/actions/workflows/test.yml)
[![NuGet](https://img.shields.io/nuget/v/waytype)](https://www.nuget.org/packages/waytype)
[![License: GPL-3.0](https://img.shields.io/badge/license-GPL--3.0-blue)](LICENSE)

WayType is a small desktop utility for Linux Wayland sessions. It records the microphone, sends the audio to an OpenAI-compatible speech endpoint, and types the result back as keystrokes.

> Unashamedly vibe-coded.  Inspired by [Handy](https://github.com/cjpais/Handy).

## Features

- A global hotkey that works while another app has focus.
- Tap the hotkey to start and stop, or hold it down to talk.
- Transcription through any endpoint that speaks the OpenAI `/v1/audio/transcriptions` API.
- An optional second pass through a text model to clean up the transcript before it gets typed.
- Text is injected through the XDG Desktop Portal.
- A history of past dictations with optional recording playback.
- Update checks against GitHub Releases, with an in-place install when you accept one.
- Light and dark themes that follow the system color scheme.

## Quick Start

### Install as a .NET tool

Requires the [.NET 10 runtime](https://dotnet.microsoft.com/download/dotnet/10.0).

```
dotnet tool install --global waytype
waytype
```

To update or remove it later:

```
dotnet tool update --global waytype
dotnet tool uninstall --global waytype
```

### Download from GitHub Releases

Grab `waytype-x64` from the [latest release](https://github.com/bitbound/WayType/releases/latest). It is a self-contained single file, so no .NET install is needed.

```
mkdir -p ~/.local/bin
curl -L -o ~/.local/bin/waytype \
  https://github.com/bitbound/WayType/releases/latest/download/waytype-x64
chmod +x ~/.local/bin/waytype
waytype
```

Make sure `~/.local/bin` is on your `PATH`.

Once WayType is running, open **Settings**, fill in the sections below, then press your hotkey and start talking.

## Requirements

- A Linux session running Wayland.
- PipeWire, or PulseAudio, for microphone capture.
- `xdg-desktop-portal` with a backend that implements the **GlobalShortcuts** and **RemoteDesktop** interfaces. KDE Plasma and GNOME both provide these.

WayType uses the portals for both halves of the job. GlobalShortcuts registers the dictation hotkey, and RemoteDesktop types the transcript into the focused window. It does not read input devices directly and does not need root.

## Setup

### Grant text input permission

**Settings → Text input permission → Grant permission.** The portal shows a dialog that asks you to allow remote desktop input. WayType saves the restore token, so you are not asked again on every launch. If the permission is revoked later, click **Re-check** and grant it again.

### Choose a speech endpoint

**Settings → Speech to text.** Any OpenAI-compatible service works. Set the endpoint, the API key, and the model id. The endpoint defaults to `https://api.openai.com/v1`, so you can point it at a local server instead. Click the refresh button next to the model field to pull the list from `/v1/models`.

### Pick a hotkey

**Settings → Dictation → Set shortcut.** Press the combination you want. The default is `Ctrl+Alt+Space`. The trigger mode decides the behavior.

- **Tap** starts dictation on one press and stops it on the next.
- **Press** records only while the key is held down.

### Tune recording

Also under **Settings → Dictation**.

| Setting | What it does |
| --- | --- |
| Microphone | Capture device. Refresh rescans what the audio server exposes. |
| Max recording seconds | Hard stop for a single take. |
| Silence threshold | Below this level the take ends early. Raise it if background noise keeps a recording alive. |
| Typing delay | Pause between injected keys. Raise it if an app drops characters, since very low values can type nothing at all. |

### Optional post-processing

**Settings → Post-processing** sends the raw transcript through a chat model before typing it. This helps with punctuation, capitalization, and filler words. It takes its own endpoint, key, and model, so you can run a small local model for cleanup and keep the speech endpoint somewhere else.

## Files and Logs

| Path | Contents |
| --- | --- |
| `~/.config/waytype/settings.json` | Settings |
| `~/.config/waytype/prompts.json` | Post-processing prompts |
| `~/.config/waytype/remotedesktop-restore-token` | Portal grant token, owner-readable only |
| `~/.local/share/waytype/history.json` | Dictation history |
| `~/.local/share/waytype/audio/` | Saved recordings |

If something misbehaves, turn on **Settings → Diagnostics → Write debug logs**, reproduce the problem, then use **Open log file**.

## Building from Source

Requires the .NET 10 SDK.

```
dotnet build WayType.slnx
dotnet run --project WayType/WayType.csproj
```

To produce the same self-contained binary that the release workflow ships:

```
dotnet publish WayType/WayType.csproj --configuration Release --runtime linux-x64
```

Run the tests with:

```
dotnet run --project tests/WayType.Tests/WayType.Tests.csproj
```

## License

WayType is licensed under the [GPL-3.0](LICENSE).
