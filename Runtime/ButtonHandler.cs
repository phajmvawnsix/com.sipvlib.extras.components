using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using SiPVLib.Sound;
using SiPVLib.Sound.Configs;
using SiPVLib.Vibrate;
using SiPVLib.Vibrate.Haptics;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SiPVLib.Extras.Components
{
    /// <summary>
    /// One component covering what a game button almost always needs: a single tap, a press-and-hold
    /// that repeats on an interval, and the SFX + haptic feedback for both — played through
    /// <see cref="SoundManager"/> and <see cref="VibrateManager"/>.
    /// </summary>
    /// <remarks>
    /// Tap and hold are mutually exclusive: once the hold threshold is passed the press is a hold,
    /// and releasing it does not fire <see cref="OnTap"/>. This is the behaviour increment/decrement
    /// buttons need (tap once to step, hold to repeat) and avoids the double-trigger you get when
    /// both fire.
    ///
    /// Works with or without a <see cref="Button"/>. If one is present its <c>interactable</c> state
    /// is honoured and its transitions still run; the handler drives the callbacks itself either way,
    /// so it also works on any raycast target (a <see cref="Graphic"/>, or a collider under a
    /// PhysicsRaycaster).
    /// </remarks>
    [DisallowMultipleComponent]
    [AddComponentMenu("UI/Button Handler (SiPVLib)")]
    public class ButtonHandler : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        /// <summary>Default SFX id played on tap.</summary>
        public const string DefaultTapSfxId = "sfx.button.tap";

        /// <summary>Default SFX id played on each hold repeat.</summary>
        public const string DefaultHoldSfxId = "sfx.button.onhold";

        [Header("Tap")]
        [SerializeField]
        [Tooltip("Invoked when the button is released before the hold threshold.")]
        private UnityEvent _onTap = new();

        [Header("Hold")]
        [SerializeField]
        [Tooltip("Enable press-and-hold repeat.")]
        private bool _holdEnabled;

        [SerializeField]
        [Min(0f)]
        [Tooltip("Seconds the button must be held before the first OnHold fires.")]
        private float _holdThreshold = 0.5f;

        [SerializeField]
        [Min(0.01f)]
        [Tooltip("Seconds between repeated OnHold invocations while held.")]
        private float _holdInterval = 0.1f;

        [SerializeField]
        [Tooltip("Invoked once per interval while the button is held past the threshold.")]
        private UnityEvent _onHold = new();

        [Header("Sfx")]
        [SerializeField]
        [Tooltip("Play SFX through SoundManager.")]
        private bool _sfxEnabled = true;

        [SerializeField]
        [ConfigSound(SoundType.Sfx)]
        [Tooltip("Sound id played on tap.")]
        private string _tapSfxId = DefaultTapSfxId;

        [SerializeField]
        [ConfigSound(SoundType.Sfx)]
        [Tooltip("Sound id played on each hold repeat.")]
        private string _holdSfxId = DefaultHoldSfxId;

        [Header("Vibrate")]
        [SerializeField]
        [Tooltip("Play haptics through VibrateManager.")]
        private bool _vibrateEnabled = true;

        [SerializeField]
        [Tooltip("Haptic preset played on tap.")]
        private HapticType _tapHaptic = HapticType.LightImpact;

        [SerializeField]
        [Tooltip("Haptic preset played on each hold repeat.")]
        private HapticType _holdHaptic = HapticType.LightImpact;

        private Button _button;

        // MonoSingleton.Instance falls back to FindFirstObjectByType when its static cache is empty,
        // so the managers are cached here — a hold repeat would otherwise scan the scene 1/interval.
        private SoundManager _soundManager;
        private VibrateManager _vibrateManager;

        private bool _isPressed;
        private bool _isHolding;
        private UniTask _holdTask;
        private CancellationTokenSource _holdCts;

        /// <summary>Raised when the button is tapped (released before the hold threshold).</summary>
        public event Action Tapped;

        /// <summary>Raised on every hold repeat while the button is held past the threshold.</summary>
        public event Action Held;

        /// <summary>Gets the inspector-assigned tap event.</summary>
        public UnityEvent OnTap => _onTap;

        /// <summary>Gets the inspector-assigned hold event.</summary>
        public UnityEvent OnHold => _onHold;

        /// <summary>Gets whether the button is currently held past the hold threshold.</summary>
        public bool IsHolding => _isHolding;

        /// <summary>Gets or sets whether press-and-hold repeat is enabled.</summary>
        public bool HoldEnabled
        {
            get => _holdEnabled;
            set => _holdEnabled = value;
        }

        /// <summary>
        /// Unity Awake method. Caches the optional <see cref="Button"/> on this GameObject.
        /// </summary>
        protected virtual void Awake()
        {
            _button = GetComponent<Button>();
        }

        /// <summary>
        /// Unity OnDisable method. Cancels any press in progress so a disabled button never keeps
        /// repeating.
        /// </summary>
        protected virtual void OnDisable()
        {
            CancelPress();
        }

        /// <summary>
        /// Unity OnDestroy method. Cleans up the hold CancellationTokenSource.
        /// </summary>
        protected virtual void OnDestroy()
        {
            _holdCts?.Dispose();
        }

        /// <summary>
        /// Unity EventSystems pointer-down handler. Starts a press and begins hold detection.
        /// </summary>
        public virtual void OnPointerDown(PointerEventData eventData)
        {
            if (!IsInteractable()) return;

            _isPressed = true;
            _isHolding = false;

            if (_holdEnabled)
            {
                _holdCts?.Dispose();
                _holdCts = new CancellationTokenSource();
                _holdTask = RunHoldAsync(_holdCts.Token);
            }
        }

        /// <summary>
        /// Unity EventSystems pointer-up handler. Fires the tap unless the press became a hold.
        /// </summary>
        public virtual void OnPointerUp(PointerEventData eventData)
        {
            if (!_isPressed) return;

            var wasHolding = _isHolding;
            CancelPress();

            if (wasHolding) return;
            if (!IsInteractable()) return;

            FireTap();
        }

        /// <summary>
        /// Unity EventSystems pointer-exit handler. Cancels the press when the pointer leaves, so
        /// dragging off the button neither taps nor keeps repeating.
        /// </summary>
        public virtual void OnPointerExit(PointerEventData eventData)
        {
            CancelPress();
        }

        /// <summary>
        /// Async hold detection and repeat loop. Waits for the hold threshold, then fires OnHold
        /// at intervals until cancelled.
        /// </summary>
        private async UniTask RunHoldAsync(CancellationToken ct)
        {
            try
            {
                await UniTask.Delay((int)(_holdThreshold * 1000f), cancellationToken: ct);

                _isHolding = true;
                FireHold();

                while (!ct.IsCancellationRequested)
                {
                    await UniTask.Delay((int)(_holdInterval * 1000f), cancellationToken: ct);
                    if (!_isPressed) break;
                    FireHold();
                }
            }
            catch (OperationCanceledException)
            {
                // Normal cancellation on release.
            }
        }

        /// <summary>
        /// Fires the tap callbacks and feedback as if the button had been tapped.
        /// </summary>
        public void FireTap()
        {
            PlaySfx(_tapSfxId);
            PlayHaptic(_tapHaptic);

            _onTap?.Invoke();
            Tapped?.Invoke();
        }

        /// <summary>
        /// Fires one hold repeat's callbacks and feedback.
        /// </summary>
        public void FireHold()
        {
            PlaySfx(_holdSfxId);
            PlayHaptic(_holdHaptic);

            _onHold?.Invoke();
            Held?.Invoke();
        }

        /// <summary>
        /// Aborts the press in progress without firing tap or hold. Cancels the hold task if running.
        /// </summary>
        public void CancelPress()
        {
            _isPressed = false;
            _isHolding = false;
            _holdCts?.Cancel();
        }

        private bool IsInteractable()
        {
            return _button == null || _button.interactable;
        }

        private void PlaySfx(string soundId)
        {
            if (!_sfxEnabled || string.IsNullOrEmpty(soundId)) return;

            if (_soundManager == null)
            {
                _soundManager = SoundManager.Instance;
            }

            if (_soundManager == null || !_soundManager.IsInitialized) return;

            _soundManager.PlaySfx(soundId);
        }

        private void PlayHaptic(HapticType haptic)
        {
            if (!_vibrateEnabled || haptic == HapticType.None) return;

            if (_vibrateManager == null)
            {
                _vibrateManager = VibrateManager.Instance;
            }

            if (_vibrateManager == null || !_vibrateManager.IsInitialized) return;

            _vibrateManager.Play(haptic);
        }
    }
}
