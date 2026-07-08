# HUMAN_TODO — mobile-arcade

## 1. Android APK builds (optional — needs the Unity Android module)

The Unity **Android Build Support** module (+ OpenJDK, Android SDK & NDK) is **not installed** on
this machine, so the games target **WebGL** (live on Pages) and **Windows** — the spec's stated
fallback. To also produce Android APKs:

1. **Unity Hub → Installs → 6000.4.9f1 → ⚙ → Add Modules → Android Build Support**
   (tick *Android SDK & NDK Tools* and *OpenJDK*). This is a multi-GB download.
2. Add a `BuildAndroid` method to each game's `Assets/Editor/BuildTool.cs` mirroring
   `BuildWebGL` (target `BuildTarget.Android`, `locationPathName = "Builds/<Game>.apk"`), then:
   ```
   Unity -batchmode -nographics -projectPath <game> -buildTarget Android -executeMethod BuildTool.BuildAndroid -logFile -
   ```
   (Run it detached and poll the log — Android builds, like WebGL, can exceed a 10-minute cap.)

The WebGL builds are the primary deliverable and are already live; Android is a nice-to-have.

## 2. Final on-device touch check

The WebGL links open in mobile browsers — do the real one-thumb feel check on an actual phone
(portrait). Everything is verified working in desktop Chromium (canvas fills the viewport, touch
maps to mouse), but on-device feel is the human check.

## 3. Build note (for future rebuilds)

Unity WebGL builds here can take **longer than 10 minutes on a cold cache** and the shell tool
kills synchronous commands at 10 min. Launch builds **detached** (`Start-Process` without `-Wait`)
and poll the log for `=== BUILD WebGL: Succeeded`. The warm incremental cache makes rebuilds
~1–2 min. After each build, copy `Builds/WebGL/Build/` into `web/<game>/` and keep the custom
responsive `web/<game>/index.html` (Unity overwrites its own `index.html` on every build).
