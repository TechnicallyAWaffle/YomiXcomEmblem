using System.Collections.Generic;
using UnityEngine;

namespace TacticsGame.Core
{
    /// <summary>
    /// How much of a target can an attacker see? Implemented in Unity with raycasts
    /// (see RaycastVisibilityService); the Core simulation only talks to this interface
    /// so it stays headless and testable.
    /// </summary>
    public interface IVisibilityService
    {
        /// <summary>0..1: fraction of the target's body exposed to the attacker.</summary>
        float GetExposure(Unit attacker, Unit target, bool ignoreTerrain);
        bool HasLineOfSight(Unit attacker, Unit target);
    }

    /// <summary>Navmesh access (see NavMeshPathService).</summary>
    public interface IPathService
    {
        /// <summary>Fills pathOut with corners starting at 'from'. False if unreachable.</summary>
        bool TryGetPath(Vector3 from, Vector3 to, List<Vector3> pathOut);
        /// <summary>Clamps a dash destination to walkable space.</summary>
        Vector3 ClampDash(Vector3 from, Vector3 desired);
    }

    /// <summary>Decides commands for AI-controlled units the moment they become ready.</summary>
    public interface IUnitAI
    {
        UnitCommand Decide(BattleSimulation sim, Unit unit);
    }

    // ---- Defaults so the Core runs with no scene at all (tests, prototyping) ----
    public class OpenFieldVisibility : IVisibilityService
    {
        public float GetExposure(Unit attacker, Unit target, bool ignoreTerrain) { return 1f; }
        public bool HasLineOfSight(Unit attacker, Unit target) { return true; }
    }

    public class StraightLinePathService : IPathService
    {
        public bool TryGetPath(Vector3 from, Vector3 to, List<Vector3> pathOut)
        {
            pathOut.Clear();
            pathOut.Add(from);
            pathOut.Add(to);
            return true;
        }
        public Vector3 ClampDash(Vector3 from, Vector3 desired) { return desired; }
    }
}
