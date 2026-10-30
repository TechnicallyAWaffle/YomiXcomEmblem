using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using TacticsGame.Core;

namespace TacticsGame.Presentation
{
    /// <summary>Navmesh pathing for the simulation. Requires a baked NavMesh in the scene.</summary>
    public class NavMeshPathService : IPathService
    {
        private readonly NavMeshPath scratch = new NavMeshPath();

        public bool TryGetPath(Vector3 from, Vector3 to, List<Vector3> pathOut)
        {
            pathOut.Clear();
            NavMeshHit hitFrom, hitTo;
            if (!NavMesh.SamplePosition(from, out hitFrom, 2f, NavMesh.AllAreas)) return false;
            if (!NavMesh.SamplePosition(to, out hitTo, 2f, NavMesh.AllAreas)) return false;
            if (!NavMesh.CalculatePath(hitFrom.position, hitTo.position, NavMesh.AllAreas, scratch)) return false;
            if (scratch.status != NavMeshPathStatus.PathComplete && scratch.status != NavMeshPathStatus.PathPartial) return false;
            pathOut.AddRange(scratch.corners);
            return pathOut.Count >= 2;
        }

        public Vector3 ClampDash(Vector3 from, Vector3 desired)
        {
            NavMeshHit hit;
            if (NavMesh.Raycast(from, desired, out hit, NavMesh.AllAreas)) return hit.position;   // stopped at a navmesh edge
            return desired;
        }
    }
}
