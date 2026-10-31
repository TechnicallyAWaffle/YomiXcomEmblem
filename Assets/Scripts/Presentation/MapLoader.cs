using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;
using TacticsGame.Core;

namespace TacticsGame.Presentation
{
    /// <summary>
    /// Builds a MapTemplate into the scene under a "Map" child: cover boxes (Environment layer, cut out of the navmesh),
    /// raised ground (Environment layer, an ElevationBlock each, left out of the navmesh) and spawn points, and sizes the
    /// ground plane. Load also re-bakes the navmesh and fits the camera bounds, so a map can be swapped at runtime (the
    /// pre-battle screen does). Capture goes the other way, for the Map Builder: whatever is under "Map" now, as a template.
    /// </summary>
    public class MapLoader : MonoBehaviour
    {
        public const string RootName = "Map";

        [Tooltip("The map built in the scene right now.")]
        public MapTemplate Current;
        [Tooltip("Ground plane (Unity's 10 x 10 m Plane), scaled to the map's GroundSize.")]
        public Transform Ground;
        public NavMeshSurface Surface;
        public Material CoverMaterial, ElevationMaterial;
        public string EnvironmentLayer = "Environment";

        public Transform Root
        {
            get
            {
                var t = transform.Find(RootName);
                if (t != null) return t;
                t = new GameObject(RootName).transform;
                t.SetParent(transform, false);
                return t;
            }
        }

        /// <summary>Build the map (replacing whatever map is there) and re-bake the navmesh. For play mode.</summary>
        public void Load(MapTemplate map)
        {
            Build(map);
            if (Surface != null) Surface.BuildNavMesh();
            Physics.SyncTransforms();
        }

        /// <summary>Replace the scene's map pieces with this template's. Works in edit mode too (no navmesh bake).</summary>
        public Transform Build(MapTemplate map)
        {
            Clear();
            Current = map;
            var root = Root;
            root.position = Vector3.zero; root.rotation = Quaternion.identity; root.localScale = Vector3.one;
            if (map == null) return root;

            int layer = LayerMask.NameToLayer(EnvironmentLayer);
            foreach (var piece in map.Pieces) CreatePiece(root, piece, layer);
            for (int i = 0; i < map.PlayerSpawns.Count; i++) CreateSpawn(root, Team.Player, i, map.PlayerSpawns[i]);
            for (int i = 0; i < map.EnemySpawns.Count; i++) CreateSpawn(root, Team.Enemy, i, map.EnemySpawns[i]);

            if (Ground != null)
            {
                Ground.position = Vector3.zero;
                Ground.localScale = new Vector3(map.GroundSize.x / 10f, 1f, map.GroundSize.y / 10f);
            }
            var rig = FindFirstObjectByType<TacticsCamera>();
            if (rig != null)
            {
                rig.BoundsMax = map.GroundSize / 2f;
                rig.BoundsMin = -rig.BoundsMax;
            }
            return root;
        }

        /// <summary>Remove the map pieces and spawns (not the ground).</summary>
        public void Clear()
        {
            var old = transform.Find(RootName);
            if (old == null) return;
            if (Application.isPlaying)
            {
                // Out of physics, the navmesh sources and FindObjectsByType right away; destroyed at the end of the frame.
                old.gameObject.SetActive(false);
                old.name = RootName + " (old)";
                Destroy(old.gameObject);
            }
            else DestroyImmediate(old.gameObject);
        }

        public GameObject CreatePiece(Transform root, MapPiece piece, int layer = -1)
        {
            if (layer < 0) layer = LayerMask.NameToLayer(EnvironmentLayer);
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = string.IsNullOrEmpty(piece.Name) ? piece.Kind.ToString() : piece.Name;
            if (layer >= 0) go.layer = layer;
            go.transform.SetParent(root, false);
            bool elevation = piece.Kind == MapPieceKind.Elevation;
            go.transform.position = piece.Position + Vector3.up * (piece.Size.y / 2f);
            go.transform.rotation = Quaternion.Euler(0f, elevation ? 0f : piece.Yaw, 0f);
            go.transform.localScale = piece.Size;
            var mat = elevation ? ElevationMaterial : CoverMaterial;
            if (mat != null) go.GetComponent<Renderer>().sharedMaterial = mat;
            go.AddComponent<MapPieceTag>().Kind = piece.Kind;
            if (elevation) go.AddComponent<ElevationBlock>();
            else
            {
                // The surface only collects objects marked with a modifier: cover cuts holes in the navmesh. Raised
                // ground has none, so the navmesh runs on underneath it and the sim adds the height (see ElevationBlock).
                var modifier = go.AddComponent<NavMeshModifier>();
                modifier.overrideArea = true;
                modifier.area = NavMesh.GetAreaFromName("Not Walkable");
            }
            return go;
        }

        public GameObject CreateSpawn(Transform root, Team team, int index, Vector3 position)
        {
            var go = new GameObject((team == Team.Player ? "PlayerSpawn_" : "EnemySpawn_") + (index + 1));
            go.transform.SetParent(root, false);
            go.transform.position = position;
            var spawn = go.AddComponent<MapSpawnPoint>();
            spawn.Team = team;
            spawn.Index = index;
            return go;
        }

        /// <summary>
        /// Write the pieces and spawns under "Map" into 'into' (its name, description and ground size are kept). Cover
        /// keeps its Y rotation; raised ground is saved axis-aligned. Positions are snapped to 'snap' meters on the ground
        /// (0 = as is).
        /// </summary>
        public void Capture(MapTemplate into, float snap = 0f)
        {
            into.Pieces.Clear();
            into.PlayerSpawns.Clear();
            into.EnemySpawns.Clear();
            var root = transform.Find(RootName);
            if (root == null) return;
            foreach (var tag in root.GetComponentsInChildren<MapPieceTag>())
            {
                var t = tag.transform;
                Vector3 size = t.lossyScale;
                size = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z));
                into.Pieces.Add(new MapPiece
                {
                    Name = tag.name, Kind = tag.Kind, Size = size,
                    Position = Snap(t.position - Vector3.up * (size.y / 2f), snap),
                    Yaw = tag.Kind == MapPieceKind.Elevation ? 0f : Mathf.Round(t.eulerAngles.y * 10f) / 10f,
                });
            }
            var spawns = new List<MapSpawnPoint>(root.GetComponentsInChildren<MapSpawnPoint>());
            spawns.Sort((a, b) => a.Index.CompareTo(b.Index));
            foreach (var s in spawns) into.Spawns(s.Team).Add(Snap(s.transform.position, snap));
            Current = into;
        }

        private static Vector3 Snap(Vector3 p, float snap)
        {
            if (snap <= 0f) return p;
            // Heights keep finer steps (0.05 m): tiers like 0.9 or 1.2 m rarely sit on the ground grid.
            return new Vector3(Mathf.Round(p.x / snap) * snap, Mathf.Round(p.y * 20f) / 20f, Mathf.Round(p.z / snap) * snap);
        }
    }
}
