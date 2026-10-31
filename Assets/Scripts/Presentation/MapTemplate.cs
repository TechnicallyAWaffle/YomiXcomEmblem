using System.Collections.Generic;
using UnityEngine;
using TacticsGame.Core;

namespace TacticsGame.Presentation
{
    public enum MapPieceKind
    {
        Cover,       // blocks sight and walking (a low wall, a pillar): cut out of the navmesh
        Elevation,   // raised ground units stand on (see ElevationBlock); kept unrotated
    }

    /// <summary>One box of level geometry. Position is the centre of its base, so a second tier sits on a first by
    /// setting Position.y to the first one's height.</summary>
    [System.Serializable]
    public class MapPiece
    {
        public string Name;
        public MapPieceKind Kind;
        public Vector3 Position;
        public Vector3 Size = Vector3.one;
        [Tooltip("Degrees around Y. Cover only: elevation is always axis-aligned.")]
        public float Yaw;
    }

    /// <summary>
    /// A saved battle map: the ground's size, its cover and raised ground, and where each team starts. Built into the
    /// scene by MapLoader (in the editor by the Map Builder window, at runtime when the pre-battle screen picks it).
    /// Create one with Tools > Combat Test > Map Builder, or Assets > Create > Tactics > Map Template.
    /// </summary>
    [CreateAssetMenu(menuName = "Tactics/Map Template", fileName = "NewMap")]
    public class MapTemplate : ScriptableObject
    {
        public string DisplayName = "New Map";
        [TextArea] public string Description;
        [Tooltip("Ground plane size in meters (X by Z), centred on the origin.")]
        public Vector2 GroundSize = new Vector2(40f, 50f);
        public List<MapPiece> Pieces = new List<MapPiece>();
        [Tooltip("Squad start points, in squad order. Extra squad members without one keep their scene position.")]
        public List<Vector3> PlayerSpawns = new List<Vector3>();
        [Tooltip("Enemy start points, in order. Enemies past the last one are lined up in rows behind the first.")]
        public List<Vector3> EnemySpawns = new List<Vector3>();

        public List<Vector3> Spawns(Team team) { return team == Team.Player ? PlayerSpawns : EnemySpawns; }

        public string Title { get { return string.IsNullOrEmpty(DisplayName) ? name : DisplayName; } }
    }
}
