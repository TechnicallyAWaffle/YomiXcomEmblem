using UnityEngine;
using TacticsGame.Core;

namespace TacticsGame.Presentation
{
    /// <summary>
    /// A team's start point on a map (under the MapLoader's "Map" root). Order matters: the Nth squad member or enemy
    /// starts at the Nth spawn of its team, counted in Index order. Draws as a coloured disc with its number.
    /// </summary>
    public class MapSpawnPoint : MonoBehaviour
    {
        public Team Team;
        public int Index;

        private void OnDrawGizmos()
        {
            Gizmos.color = Team == Team.Player ? new Color(0.3f, 0.6f, 1f, 0.9f) : new Color(1f, 0.35f, 0.3f, 0.9f);
            Gizmos.DrawWireSphere(transform.position + Vector3.up * 0.9f, 0.45f);
            Gizmos.DrawLine(transform.position, transform.position + Vector3.up * 1.8f);
#if UNITY_EDITOR
            UnityEditor.Handles.color = Gizmos.color;
            UnityEditor.Handles.DrawWireDisc(transform.position + Vector3.up * 0.02f, Vector3.up, 0.45f);
            UnityEditor.Handles.Label(transform.position + Vector3.up * 2.1f, (Team == Team.Player ? "P" : "E") + (Index + 1));
#endif
        }
    }
}
