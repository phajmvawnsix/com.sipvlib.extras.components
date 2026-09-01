using SiPVLib.Debugging;
using UnityEngine;
using UnityEngine.UI;

namespace SiPVLib.Extras.Components
{
    /// <summary>
    /// Flattens everything the camera renders behind this component — world geometry and any UI
    /// drawn below it in the hierarchy — into a single runtime <see cref="RenderTexture"/>, then
    /// displays that texture through one <see cref="RawImage"/>.
    /// </summary>
    /// <remarks>
    /// This trades N background layers for 1 full-screen blit. On fill-rate-bound mobile GPUs that
    /// is usually the single biggest win available for a deep UI stack: a popup sitting on top of a
    /// dimmer, a scrolling list, a HUD and a 3D scene pays for all of them every frame, while a
    /// captured background pays for one opaque-ish quad.
    ///
    /// Capture is explicit: call <see cref="Capture"/> when the background becomes static (e.g. from
    /// a UI view's Show callback) and <see cref="Release"/> when it starts moving again. Nothing is
    /// captured automatically, so this component never costs anything while idle.
    ///
    /// While captured, the sibling objects listed in <see cref="_hideWhileCaptured"/> are disabled,
    /// since their pixels now live inside the texture — that disabling is where the overdraw saving
    /// actually comes from.
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RawImage))]
    [AddComponentMenu("Rendering/Background Capture (SiPVLib)")]
    public class BackgroundCapture : MonoBehaviour
    {
        [Header("Source")]
        [SerializeField]
        [Tooltip("Camera whose output is captured. Defaults to Camera.main.")]
        private Camera _sourceCamera;

        [Header("Target")]
        [SerializeField]
        [Tooltip("RawImage the captured texture is assigned to. Defaults to the RawImage on this GameObject.")]
        private RawImage _target;

        [Header("Capture Settings")]
        [SerializeField]
        [Range(0.1f, 1f)]
        [Tooltip("Resolution of the capture relative to the screen. 0.5 halves both axes (quarter the pixels).")]
        private float _resolutionScale = 1f;

        [SerializeField]
        [Tooltip("Depth buffer bits for the capture. 24 is needed when the source camera renders 3D geometry; 0 is enough for pure UI.")]
        private int _depthBits = 24;

        [Header("Alpha Cutoff")]
        [SerializeField]
        [Tooltip("Discard captured pixels whose alpha is below this threshold, so fully transparent background areas do not draw. 0 disables cutoff.")]
        [Range(0f, 1f)]
        private float _alphaCutoff;

        [Header("Hide While Captured")]
        [SerializeField]
        [Tooltip("Objects disabled while the capture is live — normally the background content now baked into the texture.")]
        private GameObject[] _hideWhileCaptured;

        private static readonly int CutoffId = Shader.PropertyToID("_Cutoff");

        private RenderTexture _renderTexture;
        private Material _cutoffMaterial;
        private bool _isCaptured;

        /// <summary>Gets whether a capture is currently live and being displayed.</summary>
        public bool IsCaptured => _isCaptured;

        /// <summary>Gets the RenderTexture holding the current capture, or null when not captured.</summary>
        public RenderTexture CapturedTexture => _renderTexture;

        /// <summary>Gets the <see cref="RawImage"/> the capture is displayed through.</summary>
        public RawImage Target
        {
            get
            {
                if (_target == null)
                {
                    _target = GetComponent<RawImage>();
                }

                return _target;
            }
        }

        private Camera SourceCamera
        {
            get
            {
                if (_sourceCamera == null)
                {
                    _sourceCamera = Camera.main;
                }

                return _sourceCamera;
            }
        }

        /// <summary>
        /// Unity OnDisable method. Releases the capture so the RenderTexture is never kept alive by
        /// a hidden object.
        /// </summary>
        protected virtual void OnDisable()
        {
            Release();
        }

        /// <summary>
        /// Unity OnDestroy method. Releases the capture and destroys the cutoff material.
        /// </summary>
        protected virtual void OnDestroy()
        {
            Release();

            if (_cutoffMaterial != null)
            {
                Destroy(_cutoffMaterial);
                _cutoffMaterial = null;
            }
        }

        /// <summary>
        /// Renders the source camera into a fresh <see cref="RenderTexture"/>, shows it on the
        /// target <see cref="RawImage"/>, and disables the objects in
        /// <see cref="_hideWhileCaptured"/>. Re-capturing while already captured refreshes the
        /// texture.
        /// </summary>
        /// <returns>True if the capture succeeded.</returns>
        public bool Capture()
        {
            var camera = SourceCamera;
            if (camera == null)
            {
                CustomLog.LogWarning($"[BackgroundCapture] No source camera on '{name}'. Nothing captured.");
                return false;
            }

            var target = Target;
            if (target == null)
            {
                CustomLog.LogWarning($"[BackgroundCapture] No RawImage to display the capture on '{name}'.");
                return false;
            }

            var width = Mathf.Max(1, Mathf.RoundToInt(Screen.width * _resolutionScale));
            var height = Mathf.Max(1, Mathf.RoundToInt(Screen.height * _resolutionScale));

            // Reuse the existing texture when the resolution is unchanged (orientation flips, etc.).
            if (_renderTexture != null && (_renderTexture.width != width || _renderTexture.height != height))
            {
                ReleaseTexture();
            }

            if (_renderTexture == null)
            {
                _renderTexture = new RenderTexture(width, height, _depthBits, RenderTextureFormat.ARGB32)
                {
                    name = $"BackgroundCapture_{name}",
                    filterMode = FilterMode.Bilinear
                };
                _renderTexture.Create();
            }

            // Render one frame of the camera into the texture, then restore its normal output.
            var previousTarget = camera.targetTexture;
            camera.targetTexture = _renderTexture;
            camera.Render();
            camera.targetTexture = previousTarget;

            target.texture = _renderTexture;
            target.material = GetCutoffMaterial();
            target.enabled = true;

            SetHiddenObjectsActive(false);

            _isCaptured = true;
            return true;
        }

        /// <summary>
        /// Drops the capture, restores the objects hidden by <see cref="Capture"/>, and frees the
        /// <see cref="RenderTexture"/>.
        /// </summary>
        public void Release()
        {
            if (!_isCaptured && _renderTexture == null) return;

            SetHiddenObjectsActive(true);

            var target = Target;
            if (target != null)
            {
                target.texture = null;
                target.enabled = false;
            }

            ReleaseTexture();
            _isCaptured = false;
        }

        /// <summary>
        /// Sets the alpha cutoff applied to the captured texture and pushes it to the material.
        /// </summary>
        /// <param name="cutoff">Alpha below which captured pixels are discarded. 0 disables cutoff.</param>
        public void SetAlphaCutoff(float cutoff)
        {
            _alphaCutoff = Mathf.Clamp01(cutoff);

            if (_cutoffMaterial != null)
            {
                _cutoffMaterial.SetFloat(CutoffId, _alphaCutoff);
            }
        }

        /// <summary>
        /// Builds (once) the cutout material used to discard transparent captured pixels, or returns
        /// null when cutoff is disabled so the RawImage uses its default material.
        /// </summary>
        private Material GetCutoffMaterial()
        {
            if (_alphaCutoff <= 0f) return null;

            if (_cutoffMaterial == null)
            {
                // Cutout keeps the quad out of the transparent queue, which is what actually saves
                // fill rate — an alpha-blended full-screen quad would defeat the purpose.
                var shader = Shader.Find("Unlit/Transparent Cutout");
                if (shader == null)
                {
                    CustomLog.LogWarning("[BackgroundCapture] 'Unlit/Transparent Cutout' shader not found. " +
                                         "Add it to Always Included Shaders in Graphics settings to use alpha cutoff.");
                    return null;
                }

                _cutoffMaterial = new Material(shader) { name = "BackgroundCapture_Cutoff" };
            }

            _cutoffMaterial.SetFloat(CutoffId, _alphaCutoff);
            return _cutoffMaterial;
        }

        private void SetHiddenObjectsActive(bool active)
        {
            if (_hideWhileCaptured == null) return;

            foreach (var go in _hideWhileCaptured)
            {
                if (go == null) continue;

                go.SetActive(active);
            }
        }

        private void ReleaseTexture()
        {
            if (_renderTexture == null) return;

            _renderTexture.Release();
            Destroy(_renderTexture);
            _renderTexture = null;
        }
    }
}
