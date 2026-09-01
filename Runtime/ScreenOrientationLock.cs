using UnityEngine;

namespace SiPVLib.Extras.Components
{
    /// <summary>
    /// Locks <see cref="Screen.orientation"/> while this component is enabled and restores the
    /// previous orientation settings when it is disabled.
    /// </summary>
    /// <remarks>
    /// Unity's orientation settings are global and per-application, so a screen that must be
    /// portrait-only (a shop, a leaderboard) inside an otherwise rotatable game normally leaks its
    /// setting to whatever opens next. Putting this component on that screen's root makes the lock
    /// scoped to the screen's lifetime instead.
    /// </remarks>
    [DisallowMultipleComponent]
    [AddComponentMenu("Layout/Screen Orientation Lock (SiPVLib)")]
    public class ScreenOrientationLock : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Orientation forced while this component is enabled.")]
        private ScreenOrientation _orientation = ScreenOrientation.Portrait;

        [SerializeField]
        [Tooltip("Restore the previous orientation settings when this component is disabled.")]
        private bool _restoreOnDisable = true;

        private ScreenOrientation _previousOrientation;
        private bool _previousPortrait;
        private bool _previousPortraitUpsideDown;
        private bool _previousLandscapeLeft;
        private bool _previousLandscapeRight;
        private bool _hasPrevious;

        /// <summary>Gets the orientation this component forces while enabled.</summary>
        public ScreenOrientation Orientation => _orientation;

        /// <summary>
        /// Unity OnEnable method. Stores the current orientation settings and applies the lock.
        /// </summary>
        protected virtual void OnEnable()
        {
            StorePrevious();
            Apply(_orientation);
        }

        /// <summary>
        /// Unity OnDisable method. Restores the orientation settings captured on enable.
        /// </summary>
        protected virtual void OnDisable()
        {
            if (!_restoreOnDisable || !_hasPrevious) return;

            Screen.autorotateToPortrait = _previousPortrait;
            Screen.autorotateToPortraitUpsideDown = _previousPortraitUpsideDown;
            Screen.autorotateToLandscapeLeft = _previousLandscapeLeft;
            Screen.autorotateToLandscapeRight = _previousLandscapeRight;
            Screen.orientation = _previousOrientation;

            _hasPrevious = false;
        }

        /// <summary>
        /// Changes the locked orientation and applies it immediately.
        /// </summary>
        /// <param name="orientation">Orientation to force.</param>
        public void SetOrientation(ScreenOrientation orientation)
        {
            _orientation = orientation;

            if (!isActiveAndEnabled) return;

            Apply(orientation);
        }

        private void StorePrevious()
        {
            if (_hasPrevious) return;

            _previousOrientation = Screen.orientation;
            _previousPortrait = Screen.autorotateToPortrait;
            _previousPortraitUpsideDown = Screen.autorotateToPortraitUpsideDown;
            _previousLandscapeLeft = Screen.autorotateToLandscapeLeft;
            _previousLandscapeRight = Screen.autorotateToLandscapeRight;
            _hasPrevious = true;
        }

        private static void Apply(ScreenOrientation orientation)
        {
            if (orientation == ScreenOrientation.AutoRotation)
            {
                Screen.orientation = ScreenOrientation.AutoRotation;
                return;
            }

            // Autorotation must be narrowed first, otherwise the device can rotate back out of the
            // orientation we just set.
            Screen.autorotateToPortrait = orientation == ScreenOrientation.Portrait;
            Screen.autorotateToPortraitUpsideDown = orientation == ScreenOrientation.PortraitUpsideDown;
            Screen.autorotateToLandscapeLeft = orientation == ScreenOrientation.LandscapeLeft;
            Screen.autorotateToLandscapeRight = orientation == ScreenOrientation.LandscapeRight;

            Screen.orientation = orientation;
        }
    }
}
