using UnityEngine;
using UnityEngine.UI;

namespace SiPVLib.Extras.Components
{
    /// <summary>
    /// Drives a <see cref="RectTransform"/> to a fixed aspect ratio inside its parent, with two
    /// modes Unity's built-in <see cref="AspectRatioFitter"/> does not offer directly: taking the
    /// ratio from an assigned sprite/texture, and swapping fit/envelope behaviour per screen
    /// orientation.
    /// </summary>
    /// <remarks>
    /// The common use is a full-screen background that must cover the screen on both a 16:9 tablet
    /// and a 20:9 phone without letterboxing or distortion: envelope in portrait, fit in landscape,
    /// ratio read from the sprite so art swaps do not need Inspector edits.
    /// </remarks>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    [AddComponentMenu("Layout/Aspect Ratio Fitter Plus (SiPVLib)")]
    public class AspectRatioFitterPlus : MonoBehaviour
    {
        /// <summary>How the rect is sized against its parent.</summary>
        public enum FitMode
        {
            /// <summary>Fit fully inside the parent, possibly leaving empty space (letterbox).</summary>
            Fit,

            /// <summary>Cover the parent completely, possibly overflowing (crop).</summary>
            Envelope
        }

        [Header("Ratio")]
        [SerializeField]
        [Tooltip("Width / height ratio to enforce when no source graphic is assigned.")]
        private float _aspectRatio = 1.7777778f;

        [SerializeField]
        [Tooltip("Optional Graphic (Image/RawImage) whose sprite or texture provides the ratio instead.")]
        private MaskableGraphic _ratioSource;

        [Header("Mode")]
        [SerializeField]
        [Tooltip("Mode used when the screen is landscape (width >= height).")]
        private FitMode _landscapeMode = FitMode.Envelope;

        [SerializeField]
        [Tooltip("Mode used when the screen is portrait (height > width).")]
        private FitMode _portraitMode = FitMode.Envelope;

        [Header("Refresh")]
        [SerializeField]
        [Tooltip("Re-evaluate every frame. Disable if you call Refresh() yourself.")]
        private bool _refreshEveryFrame = true;

        private RectTransform _rectTransform;
        private Vector2 _lastParentSize = Vector2.zero;
        private float _lastRatio;

        /// <summary>Gets the aspect ratio currently being enforced.</summary>
        public float CurrentAspectRatio => ResolveRatio();

        private RectTransform RectTransform
        {
            get
            {
                if (_rectTransform == null)
                {
                    _rectTransform = transform as RectTransform;
                }

                return _rectTransform;
            }
        }

        private RectTransform ParentRect => RectTransform != null ? RectTransform.parent as RectTransform : null;

        /// <summary>
        /// Unity OnEnable method. Applies the ratio immediately.
        /// </summary>
        protected virtual void OnEnable()
        {
            Refresh(true);
        }

        /// <summary>
        /// Unity Update method. Re-applies when the parent size or source ratio changed.
        /// </summary>
        protected virtual void Update()
        {
            if (!_refreshEveryFrame) return;

            Refresh(false);
        }

        /// <summary>
        /// Unity OnRectTransformDimensionsChange method. Fires when the parent canvas is resized.
        /// </summary>
        protected virtual void OnRectTransformDimensionsChange()
        {
            if (!isActiveAndEnabled) return;

            Refresh(false);
        }

#if UNITY_EDITOR
        /// <summary>
        /// Unity OnValidate method. Re-applies when settings are edited in the Inspector.
        /// </summary>
        protected virtual void OnValidate()
        {
            _aspectRatio = Mathf.Max(0.0001f, _aspectRatio);

            if (!isActiveAndEnabled) return;

            Refresh(true);
        }
#endif

        /// <summary>
        /// Re-applies the aspect ratio, skipping the work when nothing relevant changed.
        /// </summary>
        /// <param name="force">Apply even if the parent size and ratio are unchanged.</param>
        public void Refresh(bool force = false)
        {
            var parent = ParentRect;
            if (parent == null) return;

            var parentSize = parent.rect.size;
            if (parentSize.x <= 0f || parentSize.y <= 0f) return;

            var ratio = ResolveRatio();
            if (ratio <= 0f) return;

            if (!force &&
                parentSize == _lastParentSize &&
                Mathf.Approximately(ratio, _lastRatio))
            {
                return;
            }

            Apply(parentSize, ratio);

            _lastParentSize = parentSize;
            _lastRatio = ratio;
        }

        private void Apply(Vector2 parentSize, float ratio)
        {
            var rect = RectTransform;
            if (rect == null) return;

            var mode = Screen.height > Screen.width ? _portraitMode : _landscapeMode;
            var parentRatio = parentSize.x / parentSize.y;

            // Fit: constrain by the tighter axis. Envelope: constrain by the looser one.
            var matchWidth = mode == FitMode.Fit ? parentRatio > ratio : parentRatio < ratio;

            var size = matchWidth
                ? new Vector2(parentSize.y * ratio, parentSize.y)
                : new Vector2(parentSize.x, parentSize.x / ratio);

            // The size is fully driven here, so the rect must not also be driven by anchors.
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = size;
        }

        private float ResolveRatio()
        {
            if (_ratioSource != null)
            {
                var mainTexture = _ratioSource.mainTexture;
                if (mainTexture != null && mainTexture.height > 0)
                {
                    return (float)mainTexture.width / mainTexture.height;
                }
            }

            return _aspectRatio;
        }
    }
}
