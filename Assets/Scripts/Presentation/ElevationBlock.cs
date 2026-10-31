using UnityEngine;
using TacticsGame.Core;

namespace TacticsGame.Presentation
{
    /// <summary>
    /// Marks a piece of level geometry as raised ground units can stand on (a ledge, ridge or crate stack). BattleContext
    /// turns every one in the scene into a Core.TerrainBlock: its collider's bounds in XZ, the top of the bounds as its
    /// height. Keep it an unrotated box. Leave it out of the navmesh bake (no NavMeshModifier): the navmesh covers the
    /// base ground only, and the simulation adds the height (walks jump onto it, shoves can knock units off it).
    /// Its collider also blocks sight like any other Environment geometry.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class ElevationBlock : MonoBehaviour
    {
        public TerrainBlock ToBlock()
        {
            Bounds b = GetComponent<Collider>().bounds;
            return new TerrainBlock { Id = name, MinX = b.min.x, MinZ = b.min.z, MaxX = b.max.x, MaxZ = b.max.z, Height = b.max.y };
        }
    }
}
