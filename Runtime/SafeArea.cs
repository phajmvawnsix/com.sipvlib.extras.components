using Alchemy.Inspector;
using SiPVLib.Debugging;
using UnityEngine;

namespace SiPVLib.Extras.Components
{
    /// <summary>
    /// Drives a <see cref="RectTransform"/> so it always matches Unity's <see cref="Screen.safeArea"/>.
    /// The anchors, pivot, offsets and size of the target are fully owned by this component and are
    /// re-applied whenever the screen resolution, orientation or safe area changes (including in the
    /// Editor's Device Simulator, thanks to <see cref="ExecuteAlways"/>).
    /// </summary>
    /// <remarks>
    /// The normalized anchors produced here are relative to the parent <see cref="RectTransform"/>,
    /// so the target should be a direct child of a full-screen Canvas (the standard Unity setup).
    /// Manual edits to the target's anchors/offsets are overwritten on the next refresh.
    /// </remarks>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    [AddComponentMenu("Layout/Safe Area (SiPVLib)")]
    public class SafeArea : MonoBehaviour
    {
        [Header("Target")]
        [SerializeField]
        [Tooltip("RectTransform driven by this component. Defaults to the RectTransform on this GameObject.")]
        private RectTransform _target;

        [Header("Conform Axes")]
        [SerializeField]
        [Tooltip("Apply the safe area insets on the horizontal axis (left/right edges).")]
        private bool _conformX = true;

        [SerializeField]
        [Tooltip("Apply the safe area insets on the vertical axis (top/bottom edges).")]
        private bool _conformY = true;

        [Header("Ignored Edges")]
        [SerializeField]
        [Tooltip("Ignore the left inset and keep the target extended to the screen edge.")]
        private bool _ignoreLeft;

        [SerializeField]
        [Tooltip("Ignore the right inset and keep the target extended to the screen edge.")]
        private bool _ignoreRight;

        [SerializeField]
        [Tooltip("Ignore the bottom inset and keep the target extended to the screen edge.")]
        private bool _ignoreBottom;

        [SerializeField]
        [Tooltip("Ignore the top inset and keep the target extended to the screen edge.")]
        private bool _ignoreTop;

        [Header("Refresh")]
        [SerializeField]
        [Tooltip("Poll the screen state every frame. Disable only if you call ForceRefresh() yourself.")]
        private bool _refreshEveryFrame = true;

        private Rect _lastSafeArea = Rect.zero;
        private Vector2Int _lastScreenSize = Vector2Int.zero;
        private ScreenOrientation _lastOrientation = ScreenOrientation.AutoRotation;
        private bool _applied;

        /// <summary>
        /// Gets the safe area rect (in pixels) that was last applied to the target.
        /// </summary>
        [ShowInInspector, ReadOnly, FoldoutGroup("Debug")]
        public Rect AppliedSafeArea => _lastSafeArea;

        /// <summary>
        /// Gets the <see cref="RectTransform"/> controlled by this component.
        /// </summary>
        public RectTransform Target
        {
            get
            {
                if (_target == null)
                {
                    _target = transform as RectTransform;
                }

                return _target;
            }
        }

        /// <summary>
        /// Unity OnEnable method. Applies the safe area immediately.
        /// </summary>
        protected virtual void OnEnable()
        {
            ForceRefresh();
        }

        /// <summary>
        /// Unity Update method. Re-applies the safe area when the screen state changed.
        /// </summary>
        protected virtual void Update()
        {
            if (!_refreshEveryFrame) return;

            Refresh();
        }

        /// <summary>
        /// Unity RectTransformDimensionsChange method.
        /// Fires when the canvas is resized, which is the earliest reliable hook on desktop/editor.
        /// </summary>
        protected virtual void OnRectTransformDimensionsChange()
        {
            if (!isActiveAndEnabled) return;

            Refresh();
        }

#if UNITY_EDITOR
        /// <summary>
        /// Unity OnValidate method. Re-applies the safe area when settings are edited in the Inspector.
        /// </summary>
        protected virtual void OnValidate()
        {
            if (_target == null)
            {
                _target = transform as RectTransform;
            }

            _applied = false;
        }
#endif

        /// <summary>
        /// Applies the safe area only if the screen resolution, orientation or safe area changed
        /// since the last call.
        /// </summary>
        public void Refresh()
        {
            var safeArea = Screen.safeArea;
            var screenSize = new Vector2Int(Screen.width, Screen.height);
            var orientation = Screen.orientation;

            if (_applied &&
                safeArea == _lastSafeArea &&
                screenSize == _lastScreenSize &&
                orientation == _lastOrientation)
            {
                return;
            }

            Apply(safeArea, screenSize, orientation);
        }

        /// <summary>
        /// Applies the safe area unconditionally, ignoring the cached screen state.
        /// </summary>
        public void ForceRefresh()
        {
            _applied = false;
            Apply(Screen.safeArea, new Vector2Int(Screen.width, Screen.height), Screen.orientation);
        }

        /// <summary>
        /// Writes the normalized safe area anchors onto the target and resets its offsets and pivot,
        /// so the target exactly covers the safe area of its full-screen parent.
        /// </summary>
        /// <param name="safeArea">Safe area rect in pixels, as reported by <see cref="Screen.safeArea"/>.</param>
        /// <param name="screenSize">Current screen size in pixels.</param>
        /// <param name="orientation">Current screen orientation.</param>
        private void Apply(Rect safeArea, Vector2Int screenSize, ScreenOrientation orientation)
        {
            var target = Target;

            if (target == null)
            {
                CustomLog.LogWarning($"[SafeArea] No RectTransform to control on '{name}'. Component disabled.");
                enabled = false;
                return;
            }

            // Screen size can be reported as 0 during the very first frames or while minimized.
            if (screenSize.x <= 0 || screenSize.y <= 0) return;

            // Some platforms report an empty/degenerate safe area before the window is ready.
            if (safeArea.width <= 0f || safeArea.height <= 0f) return;

            var min = safeArea.position;
            var max = safeArea.position + safeArea.size;

            if (!_conformX || _ignoreLeft) min.x = 0f;
            if (!_conformX || _ignoreRight) max.x = screenSize.x;
            if (!_conformY || _ignoreBottom) min.y = 0f;
            if (!_conformY || _ignoreTop) max.y = screenSize.y;

            min.x /= screenSize.x;
            min.y /= screenSize.y;
            max.x /= screenSize.x;
            max.y /= screenSize.y;

            // Guard against inverted/out-of-range values coming from odd platform reports.
            min.x = Mathf.Clamp01(min.x);
            min.y = Mathf.Clamp01(min.y);
            max.x = Mathf.Clamp(max.x, min.x, 1f);
            max.y = Mathf.Clamp(max.y, min.y, 1f);

            target.anchorMin = min;
            target.anchorMax = max;

            // The rect is fully driven by the anchors: no pivot offset, no manual size or position.
            target.pivot = new Vector2(0.5f, 0.5f);
            target.offsetMin = Vector2.zero;
            target.offsetMax = Vector2.zero;
            target.anchoredPosition3D = Vector3.zero;
            target.localRotation = Quaternion.identity;
            target.localScale = Vector3.one;

            _lastSafeArea = safeArea;
            _lastScreenSize = screenSize;
            _lastOrientation = orientation;
            _applied = true;
        }
    }
}
