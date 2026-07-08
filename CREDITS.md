# Credits

All code, art, and audio in **mobile-arcade** are **original and generated procedurally** —
there are no imported third-party assets, so nothing here is derived from someone else's IP.

- **Art** — every sprite is drawn at runtime as a `Texture2D` in C# (circles, triangles,
  squares with soft edges). No image files ship in the games.
- **Audio** — sound effects are synthesised in code (sine tones + decaying noise bursts via
  `AudioClip.Create`). No audio files ship in the games.
- **Fonts / UI** — HUD text uses Unity's built-in IMGUI font; no custom font assets.
- **Engine** — [Unity](https://unity.com) 6 (6000.4.9f1), WebGL build target. Unity itself is
  © Unity Technologies and used under its standard license; the Unity WebGL loader/runtime files
  under each `web/<game>/Build/` are produced by the engine.

No CC0 or other third-party art/audio was needed, so none is bundled. If any is added later, it
will be listed here with its source and license.

Built by **@arcsymer**.
