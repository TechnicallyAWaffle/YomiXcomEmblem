using UnityEngine;
using TacticsGame.Core;

namespace TacticsGame.Presentation
{
    /// <summary>
    /// Static level geometry only (walls, rocks, buildings): a Physics.Linecast against the environment layers.
    /// Units, their sizes, and deployables are handled by Core.SimVisibility with math against simulation data,
    /// because the sim moves units as data and their GameObjects lag behind during playback.
    /// </summary>
    public class PhysicsStaticOcclusion : IStaticOcclusion
    {
        private readonly LayerMask environmentMask;

        public PhysicsStaticOcclusion(LayerMask environmentMask) { this.environmentMask = environmentMask; }

        public bool IsBlocked(Vector3 from, Vector3 to) { return Physics.Linecast(from, to, environmentMask); }
    }
}
