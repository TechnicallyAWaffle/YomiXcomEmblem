using UnityEngine;

namespace TacticsGame.Presentation
{
    /// <summary>
    /// An objective spawn point on a map being edited: the Map Builder adds these, MapLoader.Capture saves them into
    /// MapTemplate.ObjectiveSpawns (in Index order), and the mission builder stands the objectives it picks on them.
    /// </summary>
    public class MapObjectiveMarker : MonoBehaviour
    {
        public int Index;

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.8f, 0.3f, 0.9f);
            Vector3 c = transform.position + Vector3.up * 1f;
            Gizmos.DrawLine(c + Vector3.up * 0.8f, c + Vector3.right * 0.6f);
            Gizmos.DrawLine(c + Vector3.right * 0.6f, c - Vector3.up * 0.8f);
            Gizmos.DrawLine(c - Vector3.up * 0.8f, c - Vector3.right * 0.6f);
            Gizmos.DrawLine(c - Vector3.right * 0.6f, c + Vector3.up * 0.8f);
#if UNITY_EDITOR
            UnityEditor.Handles.color = Gizmos.color;
            UnityEditor.Handles.DrawWireDisc(transform.position + Vector3.up * 0.02f, Vector3.up, 0.6f);
            UnityEditor.Handles.Label(transform.position + Vector3.up * 2.2f, "Objective spawn " + (Index + 1));
#endif
        }
    }
}
