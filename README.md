# com.sipvlib.extras.components

Part of [SiPVLib]. Drop-in MonoBehaviour components that are used in nearly every project — a few for
convenience, most for cutting rendering cost on mobile.

## Install

Add to your project's `Packages/manifest.json`:

```json
"com.sipvlib.extras.components": "https://github.com/phajmvawnsix/com.sipvlib.extras.components.git"
```

Depends on `com.sipvlib.debugging`, `com.sipvlib.sound`, `com.sipvlib.vibrate` and `com.unity.ugui`
(which ships TextMeshPro).

## Components

### TMPEffect — `UI/TMP Effect (SiPVLib)`

Outline, drop shadow and colour gradient for any `TMP_Text`, editable from the Inspector: outline
width, shadow offset (a Vector2, same as Unity's Shadow component's Effect Distance), colour, and a
gradient built from Unity's Gradient Editor. No duplicated mesh, no extra draw calls, and no font asset
or material preset to create by hand.

Outline and shadow are computed per pixel from the glyph's signed distance field — the same data TMP
already uses to render the glyph — so the outline is evenly thick around every character at every size.
uGUI's built-in `Outline`/`Shadow` instead duplicate the entire mesh once per offset (4–8× the vertices,
extra batches, chunky corners); this does neither.

```csharp
effect.SetOutline(Color.black, width: 0.25f);
effect.SetShadow(new Color(0f, 0f, 0f, 0.5f), offset: new Vector2(0.15f, -0.15f));

effect.OutlineColor = Color.red;            // each setter rebuilds only if the value changed
effect.ShadowOffset = new Vector2(0.2f, -0.1f); // same field as Unity's Shadow Effect Distance
effect.Refresh();                           // after setting several fields by hand
```

**It creates no assets and never touches the font asset's own material.** At runtime, every label with
the same font asset and the same outline/shadow settings resolves to one shared `Material` instance,
generated on demand and cached by style — twenty labels sharing a look share one material and one
batch, same as a hand-authored material preset, but without creating one per colour combination by
hand. A second style adds one more cached material, not one per label. There is nothing to configure:
the Inspector shows outline, shadow and gradient, and no material fields at all.

In the Editor, outside Play mode, each object instead gets its own private material that's mutated in
place as you edit rather than reassigned — reassigning the shared material on every value would repaint
the Inspector and drop a slider drag mid-motion. This only applies while editing; entering Play mode (or
running a build) switches every object over to the shared, batchable material for its current style.

`Face Dilate` is not exposed. It is set to half the outline width automatically, so a thick outline
does not visibly thin the letterforms.

The shadow's silhouette automatically grows by the outline's width when both are enabled — TextMeshPro
computes the shadow from the glyph's face shape alone, so without this an outlined glyph would cast a
shadow thinner than what's actually drawn.

#### Gradient

The gradient spans **the whole text block, not each character.** A vertex's colour comes from where it
sits inside the bounds of every visible glyph, so a word reads as one continuous ramp instead of the
same ramp repeated per letter — which is what TextMeshPro's own Vertex Color Gradient does. Enabling a
mode here turns TMP's `enableVertexGradient` off so the two don't fight.

`Gradient Mode`, edited with Unity's own Gradient Editor (`UnityEngine.Gradient` fields):

- **Horizontal** — `Top Gradient` sampled left (0) to right (1) across the full text width.
- **Vertical** — `Vertical Gradient` sampled bottom (0) to top (1) across the full text height.
- **FourCorner** — `Top Gradient` along the top edge and `Bottom Gradient` along the bottom, each
  sampled left-to-right, blended vertically between them.

```csharp
effect.SetHorizontalGradient(myGradient);
effect.SetFourCornerGradient(topGradient, bottomGradient);
```

It is written into the generated mesh's vertex colours, not into a material: it costs no material, no
draw call and no batch. Colours are multiplied into whatever TMP already generated, so `text.color`,
alpha fades and `<color>`/`<alpha>` rich text tags still apply on top. It tints the glyph face only,
never the outline colour.

Two limits, both from TextMeshPro itself:

- **Outline and shadow can only grow as far as the font asset's atlas padding.** Wide outlines flatten
  off unless the font asset is regenerated with more padding (which enlarges the atlas).
- **Rich text `<sprite>` icons cannot be outlined this way.** They render from a sprite atlas that has
  no distance field, so there is nothing to threshold — the component leaves sprite sub-meshes alone.
  Bake the outline into the sprite atlas at import instead; that costs nothing at runtime. (The
  gradient is a separate mechanism — TMP's own `TMP_SpriteAsset` tint setting controls whether sprite
  icons pick up vertex colour at all.)

Fallback-font sub-meshes *are* handled: the component follows TMP's text-changed event and applies the
style to sub-meshes as they appear.

### ButtonHandler — `UI/Button Handler (SiPVLib)`

Single tap plus press-and-hold repeat, with SFX and haptic feedback for both, played through
`SoundManager` and `VibrateManager`.

Tap and hold are mutually exclusive: once the hold threshold is passed the press is a hold, and
releasing it does not fire `OnTap`. That's what increment/decrement buttons need (tap to step, hold
to repeat) without double-triggering.

Works with or without a `Button`. If one is present its `interactable` state is honoured; otherwise
any raycast target works.

Hold detection uses `UniTask` so there is no `Update()` loop — only active presses consume resources.
Hold task is cancelled on pointer-up/exit and cleaned on destroy.

```csharp
buttonHandler.OnTap.AddListener(Step);      // UnityEvent, inspector-assignable
buttonHandler.Tapped += Step;               // C# event
buttonHandler.Held   += Step;               // fires every Hold Interval while held

buttonHandler.HoldEnabled = true;
buttonHandler.CancelPress();                // abort without firing, cancels hold task
buttonHandler.IsHolding;                    // true once threshold is passed
```

Defaults: tap SFX `sfx.button.tap`, hold SFX `sfx.button.onhold`, both haptics `LightImpact`, hold
threshold 0.5s, hold interval 0.1s. SFX ids use the `[ConfigSound]` picker. Feedback is skipped
silently when `SoundManager`/`VibrateManager` isn't initialized, so buttons work in isolated scenes.

### EmptyGraphic — `UI/Empty Graphic (SiPVLib)`

A `Graphic` that raycasts but draws nothing. Use it instead of a transparent `Image` for
click-blockers, popup dimmer input catchers, drag zones and "tap anywhere" areas.

A transparent `Image` still allocates a quad, adds a batch and pays fill rate on every pixel it
covers. `EmptyGraphic` produces zero vertices, so the canvas batcher skips it while `raycastTarget`
keeps working.

```csharp
// No API — add the component and set Raycast Target.
```

### SafeArea — `Layout/Safe Area (SiPVLib)`

Drives a `RectTransform` to match `Screen.safeArea`, re-applying on resolution/orientation/safe-area
changes (including the Editor's Device Simulator). Per-axis conform toggles and per-edge ignore
toggles for designs that should bleed to one edge.

```csharp
safeArea.ForceRefresh();          // re-apply now
var applied = safeArea.AppliedSafeArea;
```

> Moved here from `com.sipvlib.utilities` in 1.0.0. The namespace changed from `SiPVLib.Utilities` to
> `SiPVLib.Extras.Components`.

### BackgroundCapture — `Rendering/Background Capture (SiPVLib)`

Flattens everything the camera renders behind it — world geometry plus any UI below it in the
hierarchy — into one runtime `RenderTexture`, shows that through a `RawImage`, and disables the
now-redundant background objects.

This trades N background layers for 1 full-screen blit, which on fill-rate-bound mobile GPUs is
usually the biggest single win available for a deep UI stack. Capture is explicit, so the component
costs nothing while idle.

```csharp
// When a popup opens over a static background:
backgroundCapture.Capture();

// When the background needs to animate again:
backgroundCapture.Release();

backgroundCapture.SetAlphaCutoff(0.01f);   // discard fully transparent captured pixels
```

Set `Resolution Scale` below 1 to capture at reduced resolution. Alpha cutoff uses the
`Unlit/Transparent Cutout` shader — add it to **Always Included Shaders** if you enable cutoff.

### SortingLayerSetter — `Rendering/Sorting Layer Setter (SiPVLib)`

Applies a sorting layer + order to any `Renderer` (sprites, **particles**, meshes) or `Canvas` on the
object, optionally including children. Unity hides these fields for ParticleSystem renderers and
nested canvases, so mixing particles with sprites or UI normally needs a script.

```csharp
sortingLayerSetter.SetSorting("UI", 100);
```

### AspectRatioFitterPlus — `Layout/Aspect Ratio Fitter Plus (SiPVLib)`

Aspect-ratio fitting with two things Unity's built-in `AspectRatioFitter` lacks: the ratio can come
from an assigned sprite/texture, and Fit/Envelope can differ per screen orientation. The usual case
is a full-screen background that must cover both a 16:9 tablet and a 20:9 phone without letterboxing
or distortion.

```csharp
fitter.Refresh(force: true);
var ratio = fitter.CurrentAspectRatio;
```

### ScreenOrientationLock — `Layout/Screen Orientation Lock (SiPVLib)`

Locks `Screen.orientation` while enabled and restores the previous settings on disable, so a
portrait-only screen inside a rotatable game doesn't leak its orientation to whatever opens next.

```csharp
orientationLock.SetOrientation(ScreenOrientation.Portrait);
```
