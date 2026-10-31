using UnityEngine;

namespace TacticsGame.Presentation
{
    /// <summary>
    /// Marks a box under the MapLoader's "Map" root as a map piece, so the Map Builder can save it back into a
    /// MapTemplate. The box's transform is the piece: position (its centre), scale (its size) and Y rotation.
    /// </summary>
    public class MapPieceTag : MonoBehaviour
    {
        public MapPieceKind Kind;
    }
}
