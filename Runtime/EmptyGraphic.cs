using UnityEngine;
using UnityEngine.UI;

namespace SiPVLib.Extras.Components
{
    /// <summary>
    /// A <see cref="Graphic"/> that participates in raycasting but emits no geometry, so it costs
    /// nothing to draw.
    /// </summary>
    /// <remarks>
    /// Use this instead of a transparent <see cref="Image"/> for click-blockers, popup dimmers'
    /// input catchers, drag zones and full-screen "tap anywhere to continue" areas. A transparent
    /// Image still allocates a quad, adds a batch and pays fill-rate on every pixel it covers; this
    /// component overrides <see cref="OnPopulateMesh"/> to produce zero vertices, so the canvas
    /// batcher skips it entirely while <see cref="Graphic.raycastTarget"/> keeps working normally.
    /// </remarks>
    [AddComponentMenu("UI/Empty Graphic (SiPVLib)")]
    public class EmptyGraphic : Graphic
    {
        /// <summary>
        /// Emits no geometry. The vertex helper is cleared and left empty.
        /// </summary>
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
        }

#if UNITY_EDITOR
        protected override void Reset()
        {
            base.Reset();

            // An empty graphic exists to catch input; make that the default.
            raycastTarget = true;
        }
#endif
    }
}
