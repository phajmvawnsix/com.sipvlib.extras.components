using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace SiPVLib.Extras.Components
{
    /// <summary>
    /// How <see cref="TMPEffect"/> spreads its gradient across the text.
    /// </summary>
    public enum TMPGradientMode
    {
        /// <summary>No gradient; the text keeps its own colour.</summary>
        None,

        /// <summary>Sampled left-to-right across the full width of the text.</summary>
        Horizontal,

        /// <summary>Sampled bottom-to-top across the full height of the text.</summary>
        Vertical,

        /// <summary>
        /// Two gradients sampled left-to-right — one along the top edge, one along the bottom — with
        /// the two blended vertically, giving all four corners independent colours.
        /// </summary>
        FourCorner
    }

    /// <summary>
    /// Inspector-editable outline, drop shadow and colour gradient for any <see cref="TMP_Text"/>.
    /// </summary>
    /// <remarks>
    /// Creates no assets and never touches the font asset's own material. At runtime, identically
    /// styled text objects share one material instance keyed off the same original font asset material
    /// — one style, one batch, same as a hand-authored material preset, but generated on demand instead
    /// of created by hand per colour combination. In the Editor outside Play mode each object gets its
    /// own private material instead, mutated in place rather than reassigned, so dragging a slider stays
    /// smooth (reassigning the shared material on every value would repaint and drop the drag). The
    /// gradient is written into the generated mesh's vertex colours, so it costs no material and no draw
    /// call at all, in the Editor or at runtime.
    ///
    /// Outline and shadow are computed per pixel from the glyph's signed distance field, so they are
    /// evenly thick around every character at every size — unlike uGUI's <c>Outline</c>/<c>Shadow</c>,
    /// which duplicate the whole mesh per offset.
    ///
    /// The gradient spans the whole text block, not each character: the colour at a vertex comes from
    /// its position within the bounds of all visible glyphs, so a word reads as one continuous ramp
    /// rather than the same ramp repeated per letter (which is what TextMeshPro's own Vertex Color
    /// Gradient does).
    ///
    /// Limits, both from TextMeshPro itself:
    /// <list type="bullet">
    /// <item>Outline and shadow can only grow as far as the padding baked into the font asset's
    /// atlas. Thick outlines need the font asset regenerated with more padding, or they flatten off.</item>
    /// <item>Rich text <c>&lt;sprite&gt;</c> icons render from a sprite atlas that has no distance
    /// field, so SDF outlining cannot apply to them. Bake the outline into the sprite atlas instead.</item>
    /// </list>
    /// </remarks>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(TMP_Text))]
    [AddComponentMenu("UI/TMP Effect (SiPVLib)")]
    public class TMPEffect : MonoBehaviour
    {
        private const float MinGradientExtent = 0.0001f;

        [Header("Outline")]
        [SerializeField]
        [Tooltip("Draw an outline around the glyphs.")]
        private bool _outlineEnabled = true;

        [SerializeField]
        [Tooltip("Outline colour.")]
        private Color _outlineColor = Color.black;

        [SerializeField]
        [Range(0f, 1f)]
        [Tooltip("Outline thickness, as a fraction of the font asset's atlas padding. Values near 1 " +
                 "need a font asset generated with generous padding.")]
        private float _outlineWidth = 0.2f;

        [SerializeField]
        [Range(0f, 1f)]
        [Tooltip("Softens the outer edge of the outline. 0 is crisp.")]
        private float _outlineSoftness;

        [Header("Shadow")]
        [SerializeField]
        [Tooltip("Draw a drop shadow behind the glyphs.")]
        private bool _shadowEnabled;

        [SerializeField]
        [Tooltip("Shadow colour. Alpha controls its strength.")]
        private Color _shadowColor = new(0f, 0f, 0f, 0.5f);

        [SerializeField]
        [Tooltip("Shadow offset, as a fraction of the font asset's atlas padding. X right/left, Y " +
                 "up/down — same as Unity's built-in Shadow component's Effect Distance.")]
        private Vector2 _shadowOffset = new(0.15f, -0.15f);

        [SerializeField]
        [Range(0f, 1f)]
        [Tooltip("Blurs the shadow. 0 is a hard offset copy, higher values a soft glow.")]
        private float _shadowSoftness = 0.25f;

        [Header("Gradient")]
        [SerializeField]
        [Tooltip("Spread across the whole text block, not per character. None leaves the text's own " +
                 "colour alone.")]
        private TMPGradientMode _gradientMode = TMPGradientMode.None;

        [SerializeField]
        [Tooltip("Sampled left (0) to right (1). Used by Horizontal, and as the top edge in FourCorner.")]
        private Gradient _topGradient = LinearGradient(Color.white, Color.white);

        [SerializeField]
        [Tooltip("Sampled left (0) to right (1). Only used by FourCorner, as the bottom edge.")]
        private Gradient _bottomGradient = LinearGradient(Color.white, Color.white);

        [SerializeField]
        [Tooltip("Sampled bottom (0) to top (1). Only used by Vertical.")]
        private Gradient _verticalGradient = LinearGradient(Color.white, Color.white);

        // Keyed by the instance ID of the *original* font asset material, so styling never depends on
        // whatever this component (or another instance of it) last assigned — only on the source and
        // the requested look. Shared across every TMPEffect in the game: two labels with the same font
        // and the same outline/shadow settings resolve to the same Material and batch together.
        private static readonly Dictionary<Style, Material> SharedMaterials = new();

        private TMP_Text _text;
        private readonly List<TMP_SubMeshUI> _subMeshesUI = new();
        private readonly List<TMP_SubMesh> _subMeshes = new();
        private bool _isApplyingGradient;

        // Editor-only, per-object materials used while editing outside Play mode so a slider drag
        // mutates floats in place instead of swapping the shared material (which repaints and drops
        // the drag). Keyed by base material so main text and any fallback sub-meshes each get their own.
        private Dictionary<Material, Material> _ownMaterials;

        // Face Dilate is derived rather than exposed: a thick outline eats into the letterform, and
        // growing the face by the outline width keeps the glyph's original weight readable
        // without another slider to keep in sync.
        private float FaceDilate => _outlineEnabled ? _outlineWidth : 0f;

        /// <summary>Gets or sets whether the outline is drawn.</summary>
        public bool OutlineEnabled
        {
            get => _outlineEnabled;
            set => Set(ref _outlineEnabled, value);
        }

        /// <summary>Gets or sets the outline colour.</summary>
        public Color OutlineColor
        {
            get => _outlineColor;
            set => Set(ref _outlineColor, value);
        }

        /// <summary>Gets or sets the outline thickness, as a fraction of atlas padding (0-1).</summary>
        public float OutlineWidth
        {
            get => _outlineWidth;
            set => Set(ref _outlineWidth, Mathf.Clamp01(value));
        }

        /// <summary>Gets or sets whether the drop shadow is drawn.</summary>
        public bool ShadowEnabled
        {
            get => _shadowEnabled;
            set => Set(ref _shadowEnabled, value);
        }

        /// <summary>Gets or sets the shadow colour.</summary>
        public Color ShadowColor
        {
            get => _shadowColor;
            set => Set(ref _shadowColor, value);
        }

        /// <summary>
        /// Gets or sets the shadow offset, as a fraction of atlas padding. Values whose magnitude
        /// pushes past the padding the glyph quad was expanded by will clip.
        /// </summary>
        public Vector2 ShadowOffset
        {
            get => _shadowOffset;
            set => Set(ref _shadowOffset, value);
        }

        /// <summary>Gets or sets how the gradient is spread across the text.</summary>
        public TMPGradientMode GradientMode
        {
            get => _gradientMode;
            set => Set(ref _gradientMode, value);
        }

        /// <summary>
        /// Sets outline colour and thickness in one call, rebuilding only once.
        /// </summary>
        public void SetOutline(Color color, float width)
        {
            _outlineEnabled = true;
            _outlineColor = color;
            _outlineWidth = Mathf.Clamp01(width);
            Refresh();
        }

        /// <summary>
        /// Sets shadow colour and offset in one call, rebuilding only once.
        /// </summary>
        public void SetShadow(Color color, Vector2 offset)
        {
            _shadowEnabled = true;
            _shadowColor = color;
            _shadowOffset = offset;
            Refresh();
        }

        /// <summary>
        /// Ramps <paramref name="gradient"/> left-to-right across the whole text.
        /// </summary>
        public void SetHorizontalGradient(Gradient gradient)
        {
            _gradientMode = TMPGradientMode.Horizontal;
            _topGradient = gradient;
            Refresh();
        }

        /// <summary>
        /// Ramps <paramref name="gradient"/> bottom-to-top across the whole text.
        /// </summary>
        public void SetVerticalGradient(Gradient gradient)
        {
            _gradientMode = TMPGradientMode.Vertical;
            _verticalGradient = gradient;
            Refresh();
        }

        /// <summary>
        /// Ramps <paramref name="top"/> along the top edge and <paramref name="bottom"/> along the
        /// bottom, blending vertically between them.
        /// </summary>
        public void SetFourCornerGradient(Gradient top, Gradient bottom)
        {
            _gradientMode = TMPGradientMode.FourCorner;
            _topGradient = top;
            _bottomGradient = bottom;
            Refresh();
        }

        /// <summary>
        /// Re-applies every setting. Call this after changing several properties by hand.
        /// </summary>
        public void Refresh()
        {
            if (_text == null) _text = GetComponent<TMP_Text>();
            if (_text == null) return;

            ApplyMaterial();

            // Outline and shadow both grow the glyph, so the quads have to be rebuilt at the new
            // padding before the gradient can read final vertex positions.
            _text.UpdateMeshPadding();
            _text.ForceMeshUpdate();

            ApplyGradient();
        }

        /// <summary>
        /// Unity OnEnable method. Applies the effect and subscribes to text changes so the gradient
        /// survives every mesh regeneration.
        /// </summary>
        protected virtual void OnEnable()
        {
            _text = GetComponent<TMP_Text>();
            TMPro_EventManager.TEXT_CHANGED_EVENT.Add(OnTextChanged);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
#endif
            Refresh();
        }

#if UNITY_EDITOR
        // Covers the "Enter Play Mode Options: reload disabled" case, where OnEnable does not re-run
        // on entering Play mode: without this, the object would keep rendering with its edit-time
        // private material instead of switching to the shared, batchable one.
        private void OnPlayModeStateChanged(UnityEditor.PlayModeStateChange change)
        {
            if (change != UnityEditor.PlayModeStateChange.EnteredPlayMode) return;

            DestroyOwnMaterials();
            if (isActiveAndEnabled) Refresh();
        }
#endif

        /// <summary>
        /// Unity OnDisable method. Unsubscribes from text changes and restores the text's own colours.
        /// </summary>
        protected virtual void OnDisable()
        {
            TMPro_EventManager.TEXT_CHANGED_EVENT.Remove(OnTextChanged);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
#endif

            // The vertex colours this component wrote live in the generated mesh, so they have to be
            // regenerated away rather than reset field by field.
            if (_text != null) _text.ForceMeshUpdate();

            DestroyOwnMaterials();
        }

        private void DestroyOwnMaterials()
        {
            if (_ownMaterials == null) return;

            foreach (var material in _ownMaterials.Values)
            {
                if (material == null) continue;

#if UNITY_EDITOR
                DestroyImmediate(material);
#else
                Destroy(material);
#endif
            }

            _ownMaterials.Clear();
        }

#if UNITY_EDITOR
        /// <summary>
        /// Unity OnValidate method. Gives live feedback while the effect is edited in the Inspector.
        /// </summary>
        protected virtual void OnValidate()
        {
            if (!isActiveAndEnabled) return;

            // Nothing here allocates beyond the very first call (which creates this object's private
            // edit-time material), so this is safe to run on every frame of a slider drag — which is
            // what keeps the sliders draggable rather than click-only.
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this != null && isActiveAndEnabled) Refresh();
            };
        }
#endif

        private void OnTextChanged(UnityEngine.Object changed)
        {
            if (!ReferenceEquals(changed, _text)) return;

            // TextMeshPro rewrites vertex colours from scratch every time it regenerates the mesh, so
            // the gradient has to be re-applied on top of each regeneration.
            ApplyGradient();
        }

        private void ApplyMaterial()
        {
            var mainBase = _text.font != null ? _text.font.material : _text.fontSharedMaterial;
            if (mainBase != null) Assign(_text.fontSharedMaterial, mainBase, m => _text.fontSharedMaterial = m);

            // Fallback fonts render through sub-mesh objects, each with its own atlas and original
            // material, so each is resolved and cached independently.
            GetComponentsInChildren(true, _subMeshesUI);
            for (var i = 0; i < _subMeshesUI.Count; i++)
            {
                var subMesh = _subMeshesUI[i];
                if (subMesh.spriteAsset != null) continue;

                var baseMaterial = subMesh.fontAsset != null ? subMesh.fontAsset.material : subMesh.sharedMaterial;
                if (baseMaterial == null) continue;

                var capturedSubMesh = subMesh;
                Assign(subMesh.sharedMaterial, baseMaterial, m => capturedSubMesh.sharedMaterial = m);
            }

            GetComponentsInChildren(true, _subMeshes);
            for (var i = 0; i < _subMeshes.Count; i++)
            {
                var subMesh = _subMeshes[i];
                if (subMesh.spriteAsset != null) continue;

                var baseMaterial = subMesh.fontAsset != null ? subMesh.fontAsset.material : subMesh.sharedMaterial;
                if (baseMaterial == null) continue;

                var capturedSubMesh = subMesh;
                Assign(subMesh.sharedMaterial, baseMaterial, m => capturedSubMesh.sharedMaterial = m);
            }
        }

        // Sprite sub-meshes sample a plain sprite atlas with no distance field, so the SDF outline and
        // underlay properties have nothing to threshold against — they are skipped by callers above.
        //
        // Outside Play mode, `current` (if it's already this component's own material) is mutated in
        // place: no reassignment, so no repaint, so a slider drag stays a drag. At runtime it resolves
        // (or creates) the shared cached material for the current style and reassigns only if the
        // reference actually changed, restoring cross-object batching.
        private void Assign(Material current, Material baseMaterial, Action<Material> assign)
        {
            if (!Application.isPlaying)
            {
                _ownMaterials ??= new Dictionary<Material, Material>();
                if (!_ownMaterials.TryGetValue(baseMaterial, out var own) || own == null)
                {
                    own = new Material(baseMaterial) { hideFlags = HideFlags.DontSave };
                    own.shaderKeywords = baseMaterial.shaderKeywords;
                    _ownMaterials[baseMaterial] = own;
                }

                Write(own);
                if (!ReferenceEquals(current, own)) assign(own);
                return;
            }

            var style = new Style(baseMaterial.GetInstanceID(), _outlineEnabled, _outlineColor, _outlineWidth,
                _outlineSoftness, _shadowEnabled, _shadowColor, _shadowOffset, _shadowSoftness);

            if (!SharedMaterials.TryGetValue(style, out var shared) || shared == null)
            {
                shared = new Material(baseMaterial) { hideFlags = HideFlags.DontSave };
                shared.shaderKeywords = baseMaterial.shaderKeywords;
                Write(shared);
                SharedMaterials[style] = shared;
            }

            if (!ReferenceEquals(current, shared)) assign(shared);
        }

        private void Write(Material material)
        {
            if (_outlineEnabled && _outlineWidth > 0f)
            {
                material.EnableKeyword(ShaderUtilities.Keyword_Outline);
                material.SetColor(ShaderUtilities.ID_OutlineColor, _outlineColor);
                material.SetFloat(ShaderUtilities.ID_OutlineWidth, _outlineWidth);
                material.SetFloat(ShaderUtilities.ID_OutlineSoftness, _outlineSoftness);
            }
            else
            {
                material.DisableKeyword(ShaderUtilities.Keyword_Outline);
                material.SetFloat(ShaderUtilities.ID_OutlineWidth, 0f);
            }

            material.SetFloat(ShaderUtilities.ID_FaceDilate, FaceDilate);

            if (_shadowEnabled)
            {
                // TMP's underlay silhouette is cut from the face SDF alone (TMP_SDF_SSD.cginc), so by
                // default the shadow is glyph-shaped and ignores the outline sitting on top of it — an
                // outlined glyph would cast a thinner shadow than what is actually drawn. Growing
                // Underlay Dilate to match the dilated face plus the outline folds the outline into
                // the shadow's silhouette.
                var underlayDilate = _outlineEnabled ? FaceDilate + _outlineWidth : 0f;

                material.EnableKeyword(ShaderUtilities.Keyword_Underlay);
                material.SetColor(ShaderUtilities.ID_UnderlayColor, _shadowColor);
                material.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, _shadowOffset.x);
                material.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, _shadowOffset.y);
                material.SetFloat(ShaderUtilities.ID_UnderlayDilate, Mathf.Clamp(underlayDilate, -1f, 1f));
                material.SetFloat(ShaderUtilities.ID_UnderlaySoftness, _shadowSoftness);
            }
            else
            {
                material.DisableKeyword(ShaderUtilities.Keyword_Underlay);
            }
        }

        // The gradient spans the whole text block: every vertex is coloured by where it sits inside the
        // bounds of all visible glyphs. TMP's own colorGradient instead applies the same four corners
        // to every character, which repeats the ramp per letter.
        private void ApplyGradient()
        {
            if (_isApplyingGradient || _text == null) return;
            if (_gradientMode == TMPGradientMode.None) return;

            // TMP's per-character gradient would fight this one for the same vertex colours.
            if (_text.enableVertexGradient) _text.enableVertexGradient = false;

            var textInfo = _text.textInfo;
            if (textInfo == null || textInfo.characterCount == 0) return;

            if (!TryGetVisibleBounds(textInfo, out var min, out var max)) return;

            var size = max - min;

            for (var i = 0; i < textInfo.characterCount; i++)
            {
                var character = textInfo.characterInfo[i];
                if (!character.isVisible) continue;

                var meshInfo = textInfo.meshInfo[character.materialReferenceIndex];
                var vertexIndex = character.vertexIndex;

                for (var corner = 0; corner < 4; corner++)
                {
                    var index = vertexIndex + corner;
                    var position = meshInfo.vertices[index];

                    var tx = size.x > MinGradientExtent ? (position.x - min.x) / size.x : 0f;
                    var ty = size.y > MinGradientExtent ? (position.y - min.y) / size.y : 0f;

                    // Multiplied rather than replaced so the text's own colour, alpha fades and
                    // <alpha>/<color> rich text tags still apply on top of the gradient.
                    Color existing = meshInfo.colors32[index];
                    meshInfo.colors32[index] = Evaluate(tx, ty) * existing;
                }
            }

            _isApplyingGradient = true;
            _text.UpdateVertexData(TMP_VertexDataUpdateFlags.Colors32);
            _isApplyingGradient = false;
        }

        private static bool TryGetVisibleBounds(TMP_TextInfo textInfo, out Vector2 min, out Vector2 max)
        {
            min = new Vector2(float.MaxValue, float.MaxValue);
            max = new Vector2(float.MinValue, float.MinValue);

            var found = false;

            for (var i = 0; i < textInfo.characterCount; i++)
            {
                var character = textInfo.characterInfo[i];
                if (!character.isVisible) continue;

                var vertices = textInfo.meshInfo[character.materialReferenceIndex].vertices;

                for (var corner = 0; corner < 4; corner++)
                {
                    var position = vertices[character.vertexIndex + corner];

                    min.x = Mathf.Min(min.x, position.x);
                    min.y = Mathf.Min(min.y, position.y);
                    max.x = Mathf.Max(max.x, position.x);
                    max.y = Mathf.Max(max.y, position.y);
                }

                found = true;
            }

            return found;
        }

        private Color Evaluate(float tx, float ty)
        {
            return _gradientMode switch
            {
                TMPGradientMode.Horizontal => _topGradient.Evaluate(tx),
                TMPGradientMode.Vertical => _verticalGradient.Evaluate(ty),
                TMPGradientMode.FourCorner => Color.Lerp(_bottomGradient.Evaluate(tx),
                    _topGradient.Evaluate(tx), ty),
                _ => Color.white
            };
        }

        private static Gradient LinearGradient(Color left, Color right)
        {
            return new Gradient
            {
                colorKeys = new[] { new GradientColorKey(left, 0f), new GradientColorKey(right, 1f) },
                alphaKeys = new[] { new GradientAlphaKey(left.a, 0f), new GradientAlphaKey(right.a, 1f) }
            };
        }

        private void Set<T>(ref T field, T value)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return;

            field = value;
            Refresh();
        }

        // Domain reload doesn't always clear static state (e.g. with Enter Play Mode Options'
        // "reload disabled"), and materials from a previous session are meaningless in a new one.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ClearSharedMaterials() => SharedMaterials.Clear();

        // Full-value key rather than a combined hash: a hash collision here would silently hand one
        // style's material to a completely different colour combination.
        private readonly struct Style : IEquatable<Style>
        {
            private readonly int _baseMaterialId;
            private readonly bool _outlineEnabled;
            private readonly Color _outlineColor;
            private readonly float _outlineWidth;
            private readonly float _outlineSoftness;
            private readonly bool _shadowEnabled;
            private readonly Color _shadowColor;
            private readonly Vector2 _shadowOffset;
            private readonly float _shadowSoftness;

            public Style(int baseMaterialId, bool outlineEnabled, Color outlineColor, float outlineWidth,
                float outlineSoftness, bool shadowEnabled, Color shadowColor, Vector2 shadowOffset,
                float shadowSoftness)
            {
                _baseMaterialId = baseMaterialId;
                _outlineEnabled = outlineEnabled;
                _outlineColor = outlineColor;
                _outlineWidth = outlineWidth;
                _outlineSoftness = outlineSoftness;
                _shadowEnabled = shadowEnabled;
                _shadowColor = shadowColor;
                _shadowOffset = shadowOffset;
                _shadowSoftness = shadowSoftness;
            }

            public bool Equals(Style other) =>
                _baseMaterialId == other._baseMaterialId &&
                _outlineEnabled == other._outlineEnabled &&
                _outlineColor.Equals(other._outlineColor) &&
                _outlineWidth.Equals(other._outlineWidth) &&
                _outlineSoftness.Equals(other._outlineSoftness) &&
                _shadowEnabled == other._shadowEnabled &&
                _shadowColor.Equals(other._shadowColor) &&
                _shadowOffset.Equals(other._shadowOffset) &&
                _shadowSoftness.Equals(other._shadowSoftness);

            public override bool Equals(object obj) => obj is Style other && Equals(other);

            public override int GetHashCode()
            {
                var hash = new HashCode();
                hash.Add(_baseMaterialId);
                hash.Add(_outlineEnabled);
                hash.Add(_outlineColor);
                hash.Add(_outlineWidth);
                hash.Add(_outlineSoftness);
                hash.Add(_shadowEnabled);
                hash.Add(_shadowColor);
                hash.Add(_shadowOffset);
                hash.Add(_shadowSoftness);
                return hash.ToHashCode();
            }
        }
    }
}
