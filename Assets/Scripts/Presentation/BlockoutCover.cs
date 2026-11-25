using UnityEngine;

namespace TacticsGame.Presentation
{
    /// <summary>
    /// Tags a part of a map blockout (a ProBuilder shape, or any mesh) as purely cover: it blocks sight and walking (cut out
    /// of the navmesh) but nobody can stand or climb on it. Everything in a blockout without one is climbable ground. It
    /// covers the object it is on and everything under it. Single faces can be made cover instead by giving them a material
    /// whose name starts with "Cover" (or the MapLoader's Cover Material). See MapBuilderWindow's Convert Blockout.
    /// </summary>
    public class BlockoutCover : MonoBehaviour { }
}
