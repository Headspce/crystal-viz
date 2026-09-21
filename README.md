# CrystalViz

A calm, always-on-tree visualization toy for Android: a gnarled tree grows
through stages in a grassy field under a painted anime sky, with traveling
wind gusts rippling through the grass and wildflowers.

Built with Unity 6 (URP) — CI builds the Android APK on every push to the
prototype branch. Installable APKs ship via
[GitHub Releases](https://github.com/Headspce/crystal-viz/releases).

## Credits & attribution

- **Anime sky** — fully procedural (the `CrystalViz/AnimeSkybox` shader:
  painted gradient, cel-shaded cumulus, cirrus wisps). A Sketchfab panorama
  (*"FREE - SkyBox Anime Sky"* by **Paul** (@paul_paul_paul),
  [CC-BY 4.0](http://creativecommons.org/licenses/by/4.0/)) was tried in
  v1.0.20, but Sketchfab's publicly served texture carries baked-in stripe
  artifacts (viewer-pipeline anti-theft degradation; the clean file needs a
  logged-in download), so it was removed again in v1.0.21 and the sky
  returned to the procedural look.

See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) for the full list of
third-party assets and their licenses.
