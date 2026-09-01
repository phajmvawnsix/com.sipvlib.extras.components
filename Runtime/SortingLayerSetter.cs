using SiPVLib.Debugging;
using UnityEngine;

namespace SiPVLib.Extras.Components
{
    /// <summary>
    /// Applies a sorting layer and order to whatever renders on this GameObject — a
    /// <see cref="Renderer"/> (sprites, particles, meshes) or a <see cref="Canvas"/>.
    /// </summary>
    /// <remarks>
    /// Unity exposes sorting layer/order in the Inspector for SpriteRenderer but hides it for
    /// ParticleSystem's renderer and for nested canvases unless override sorting is on, so mixing
    /// particles with sprites or UI normally needs a script. This is that script, in one place.
    /// </remarks>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("Rendering/Sorting Layer Setter (SiPVLib)")]
    public class SortingLayerSetter : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Sorting layer name. Must exist in Project Settings > Tags and Layers.")]
        private string _sortingLayerName = "Default";

        [SerializeField]
        [Tooltip("Order within the sorting layer. Higher draws on top.")]
        private int _sortingOrder;

        [SerializeField]
        [Tooltip("Also apply to renderers on child objects.")]
        private bool _includeChildren;

        /// <summary>Gets the sorting layer name applied by this component.</summary>
        public string SortingLayerName => _sortingLayerName;

        /// <summary>Gets the sorting order applied by this component.</summary>
        public int SortingOrder => _sortingOrder;

        /// <summary>
        /// Unity OnEnable method. Applies the sorting settings.
        /// </summary>
        protected virtual void OnEnable()
        {
            Apply();
        }

#if UNITY_EDITOR
        /// <summary>
        /// Unity OnValidate method. Re-applies when the settings are edited in the Inspector.
        /// </summary>
        protected virtual void OnValidate()
        {
            if (!isActiveAndEnabled) return;

            Apply();
        }
#endif

        /// <summary>
        /// Sets the sorting layer and order, then re-applies them immediately.
        /// </summary>
        /// <param name="layerName">Sorting layer name. Must exist in Tags and Layers.</param>
        /// <param name="order">Order within the layer.</param>
        public void SetSorting(string layerName, int order)
        {
            _sortingLayerName = layerName;
            _sortingOrder = order;
            Apply();
        }

        /// <summary>
        /// Pushes the current sorting layer/order onto the renderers and canvases in scope.
        /// </summary>
        public void Apply()
        {
            if (!IsKnownSortingLayer(_sortingLayerName))
            {
                CustomLog.LogWarning($"[SortingLayerSetter] Sorting layer '{_sortingLayerName}' does not exist. " +
                                     $"Add it in Project Settings > Tags and Layers.");
                return;
            }

            if (_includeChildren)
            {
                foreach (var renderer in GetComponentsInChildren<Renderer>(true))
                {
                    ApplyTo(renderer);
                }

                foreach (var canvas in GetComponentsInChildren<Canvas>(true))
                {
                    ApplyTo(canvas);
                }

                return;
            }

            if (TryGetComponent<Renderer>(out var ownRenderer))
            {
                ApplyTo(ownRenderer);
            }

            if (TryGetComponent<Canvas>(out var ownCanvas))
            {
                ApplyTo(ownCanvas);
            }
        }

        private void ApplyTo(Renderer target)
        {
            if (target == null) return;

            target.sortingLayerName = _sortingLayerName;
            target.sortingOrder = _sortingOrder;
        }

        private void ApplyTo(Canvas target)
        {
            if (target == null) return;

            // Sorting on a nested canvas is ignored unless it overrides its parent's sorting.
            target.overrideSorting = true;
            target.sortingLayerName = _sortingLayerName;
            target.sortingOrder = _sortingOrder;
        }

        private static bool IsKnownSortingLayer(string layerName)
        {
            foreach (var layer in SortingLayer.layers)
            {
                if (layer.name == layerName) return true;
            }

            return false;
        }
    }
}
