# CrystalViz

A calm, always-on-tree visualization toy for Android: a gnarled tree grows
through stages in a grassy field under a painted anime sky, with traveling
wind gusts rippling through the grass and wildflowers.

Built with Unity 6 (URP) — CI builds the Android APK on every push to the
prototype branch. Installable APKs ship via
[GitHub Releases](https://github.com/Headspce/crystal-viz/releases).

## Credits & attribution

- **Anime sky** — panorama from *"FREE - SkyBox Anime Sky"* by **Paul**
  (@paul_paul_paul), [CC-BY 4.0](http://creativecommons.org/licenses/by/4.0/),
  https://sketchfab.com/3d-models/free-skybox-anime-sky-56a60c1d1e8b44eabff138374f996d8f —
  the clean original texture via authenticated download (2026-09-21),
  downscaled to 4096x2048 and sampled equirectangular on the sky dome with
  an ultra-slow drift. (v1.0.20 shipped Sketchfab's publicly served copy,
  which carries baked-in stripe artifacts; v1.0.21 used the procedural
  `CrystalViz/AnimeSkybox` shader until the clean file was in hand.)

See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) for the full list of
third-party assets and their licenses.
