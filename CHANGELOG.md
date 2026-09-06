# Changelog

## [1.0.1] - 2026-09-03

Lower minimum Unity Editor version to 2022.3 LTS (was 6000.3) and add a `repository`
field to `package.json`, both required for OpenUPM registry submission.

Pin `com.unity.ugui` to 1.0.0 (was 2.0.0): uGUI 2.0.0 ships only with Unity 6.x — Unity 2022.3 ships
uGUI 1.0.0, and Package Manager can't install a built-in package version the Editor doesn't provide.

## [1.0.0] - 2026-09-01

Initial release.

- `ButtonHandler` — single tap plus press-and-hold repeat (threshold + interval), with SFX
  (`sfx.button.tap` / `sfx.button.onhold`) and `LightImpact` haptics played through `SoundManager`
  and `VibrateManager`. Hold suppresses the tap. Hold detection via `UniTask` with no `Update()`
  loop — only active presses allocate, task cleaned on pointer-up/exit/destroy.
- `TMPEffect` — Inspector-editable outline, drop shadow (offset/colour) and colour gradient
  (Horizontal/Vertical/FourCorner, via Unity's Gradient Editor) for any `TMP_Text`, driven by
  TextMeshPro's own SDF shader properties and per-vertex colouring. No duplicated mesh and no extra
  draw calls. Creates no assets and never modifies the font asset or its material: at runtime,
  identically styled labels share one on-demand, cached material per font asset (batches like a
  hand-authored preset); in the Editor outside Play mode each object gets its own private material,
  mutated in place so slider drags stay smooth. The gradient is written into the generated mesh's
  vertex colours (no material, no draw call) and spans the whole text block rather than repeating per
  character. Face Dilate is derived automatically as half the outline width. Sprite sub-meshes are left
  untouched (no distance field to threshold).
- `EmptyGraphic` — raycast target with zero draw cost, replacing transparent `Image` blockers.
- `SafeArea` — moved here from `com.sipvlib.utilities` 1.0.2. Namespace changed from
  `SiPVLib.Utilities` to `SiPVLib.Extras.Components`; behaviour unchanged.
- `BackgroundCapture` — captures the render behind it into a runtime `RenderTexture` with optional
  alpha cutoff, displays it via a `RawImage`, and disables the baked-in background objects to cut
  overdraw. Explicit `Capture()`/`Release()`.
- `SortingLayerSetter` — sorting layer/order for renderers (including particles) and nested canvases.
- `AspectRatioFitterPlus` — aspect fitting with sprite-derived ratio and per-orientation
  Fit/Envelope modes.
- `ScreenOrientationLock` — scoped orientation lock that restores previous settings on disable.
