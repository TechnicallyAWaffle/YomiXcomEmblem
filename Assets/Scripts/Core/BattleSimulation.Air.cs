using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TacticsGame.Core
{
    /// <summary>BattleSimulation: Lifts (Amblivate), hovers (Skyward Demise) and teleports.</summary>
    public partial class BattleSimulation
    {
        // ------------------------------------------------------------------ lifts (Amblivate) and teleports (Wrinkles of Reality)

        /// <summary>How long a lifted unit takes to rise to its height.</summary>
        public float LiftRiseSeconds = 0.4f;

        /// <summary>
        /// Raise a stunned unit 'height' metres straight up and hold it there until its stun runs out, then drop it: a fall
        /// (StartFall) from that height, with the fall damage that goes with it, credited to 'source'. Nothing moves it while
        /// it hangs there, and melee can't reach it.
        /// </summary>
        /// <param name="riseSeconds">How long it takes to rise (0: LiftRiseSeconds). Stitch raises its target over the meld.</param>
        public void LiftUnit(Unit target, float height, Unit source, float riseSeconds = 0f)
        {
            float rise = riseSeconds > 0f ? riseSeconds : LiftRiseSeconds;
            if (!target.IsAlive || target.Phase != UnitPhase.Stunned || target.IsLifted || target.FallLandAt >= 0f || height <= 0f || !CanDisplace(target, source)) return;
            if (target.HeldById != null) ReleaseHeld(target);
            if (target.Grapple != null) EndGrapple(target);
            target.GlideEnd = -1f;
            var lift = target.AddState(new LiftState
            {
                From = target.Position, To = target.Position + Vector3.up * height, Start = Now, RiseEnd = Now + rise,
                SourceId = source != null ? source.Id : null,
            });
            lift.DropAt = Mathf.Max(lift.RiseEnd, target.PhaseEndsAt);
            target.PhaseEndsAt = lift.DropAt;   // never free to act in mid-air
            Emit(EventType.Lift, source != null ? source.Id : null, target.Id,
                new LiftInfo { From = lift.From, To = lift.To, RiseSeconds = rise, DropAt = lift.DropAt });
        }

        private void TickLifts()
        {
            foreach (var u in State.Units)
            {
                var lift = u.Lift;   // lifts and hovers (an aerial slam moves itself: TickAerialSlams)
                if (lift == null || !u.IsAlive) continue;
                float k = Mathf.Clamp01((Now - lift.Start) / Mathf.Max(1e-4f, lift.RiseEnd - lift.Start));
                u.Position = Vector3.Lerp(lift.From, lift.To, k * k * (3f - 2f * k));
                if (Now + 1e-4f < lift.DropAt) continue;
                if (lift is DropState) { EndDrop(u, (DropState)lift); continue; }
                if (lift is HoverState) { EndHover(u, false, null); continue; }
                u.RemoveState(lift);
                Vector3 landing = Terrain.OnGround(u.Position);
                StartFall(u, landing, u.Position.y - landing.y, 0f, GetUnit(lift.SourceId), false);
            }
        }

        /// <summary>Seconds a leap takes to rise to its hover point, and a hover's gentle way down.</summary>
        public float HoverRiseSeconds = 0.5f, HoverDescendSeconds = 0.45f;

        /// <summary>
        /// Leap 'unit' up to 'to' (in the air) and hover there until 'until' (Skyward Demise). Unlike a lift it is free to act
        /// up there, but not to move: walking, dashing and other moves are refused (see TryStartCommand). It glides back down
        /// when the time is up, or once an ability it starts in the air has landed or been interrupted, and 'statuses' come off
        /// it then. Damage knocks it down instead: a fall (StartFall) with its stun and fall damage (HoverState.OnStruck).
        /// </summary>
        /// riseSeconds / descendSeconds (0 = HoverRiseSeconds / HoverDescendSeconds): how long it takes up and back down (a
        /// flight timed to an ability's windup and recovery). permanent: it never comes down of itself (the Monarch's Ascension).
        /// False if the unit couldn't take off (already in the air, falling, dead).
        public bool HoverUnit(Unit unit, Vector3 to, float until, List<string> statuses, float riseSeconds = 0f, float descendSeconds = 0f, bool permanent = false)
        {
            if (!unit.IsAlive || unit.IsLifted || unit.FallLandAt >= 0f) return false;
            if (unit.HeldById != null) ReleaseHeld(unit);
            if (unit.Phase == UnitPhase.Moving) EndMove(unit, false);
            unit.GlideEnd = -1f;
            float rise = riseSeconds > 0f ? riseSeconds : HoverRiseSeconds;
            var hover = unit.AddState(new HoverState { From = unit.Position, To = to, Start = Now, RiseEnd = Now + rise, SourceId = unit.Id, Statuses = statuses, DescendSeconds = descendSeconds, Permanent = permanent });
            hover.DropAt = permanent ? float.MaxValue : Mathf.Max(hover.RiseEnd, until);
            Emit(EventType.Lift, unit.Id, unit.Id,
                new LiftInfo { From = hover.From, To = hover.To, RiseSeconds = rise, DropAt = hover.DropAt, Hover = true });
            return true;
        }

        /// <summary>A hover ends: its statuses come off, and the unit glides down (or, knocked down by damage, falls).</summary>
        internal void EndHover(Unit u, bool knockedDown, Unit source)
        {
            if (!u.Hovering) return;
            float descend = u.Hover.DescendSeconds > 0f ? u.Hover.DescendSeconds : HoverDescendSeconds;
            ClearHover(u);
            if (!u.IsAlive) return;
            Vector3 ground = Terrain.OnGround(u.Position);
            Vector3 landing = Terrain.OnGround(FreeSpotNear(u, ground, ground, false));   // not on top of whoever is below
            if (knockedDown) { StartFall(u, landing, u.Position.y - landing.y, 0f, source, false); return; }
            u.GlideFrom = u.Position;
            u.GlideTo = landing;
            u.GlideStart = Now;
            u.GlideEnd = Now + descend;
            Emit(EventType.Dash, u.Id, null, new DashInfo { From = u.Position, To = landing, Duration = descend, Fall = true });
        }

        /// <summary>The hover's statuses come off and its lift ends (no landing: callers move the unit themselves).</summary>
        private void ClearHover(Unit u)
        {
            var hover = u.Hover;
            if (hover == null) return;
            u.RemoveState(hover);
            if (hover.Statuses != null) foreach (var id in hover.Statuses) RemoveStatus(u, id);
        }

        /// <summary>
        /// The walkable point nearest 'p' (the navmesh's, where there is one): where a teleport can land. A point too far off
        /// the map for the navmesh to place it (past its edge) is brought back in along the line from 'from' (a spot on the
        /// map, such as where the unit stands), stopping at the edge; without 'from' it is left as it is.
        /// </summary>
        public Vector3 WalkablePointNear(Vector3 p, Vector3? from = null)
        {
            var path = new List<Vector3>();
            if (Paths.TryGetPath(p, p, path) && path.Count > 0) return path[path.Count - 1];
            return from.HasValue ? Paths.ClampDash(from.Value, p) : Terrain.OnGround(p);
        }

        /// <summary>
        /// Move a unit to 'to' at once (Wrinkles of Reality): no path, no walls, no fall. Whatever it was doing in motion stops
        /// (a walk, a dash, a jump, a lift; a grip on it breaks); an action it was winding up carries on from the new spot.
        /// </summary>
        public void TeleportUnit(Unit unit, Vector3 to, Unit source)
        {
            if (unit == null || !unit.IsAlive || unit.FallLandAt >= 0f || unit.AnyState(s => s.BlocksTeleport) || !CanDisplace(unit, source)) return;
            if (unit.HeldById != null) ReleaseHeld(unit);
            ClearHover(unit);
            unit.RemoveState<LiftState>();
            unit.GlideEnd = -1f;
            if (unit.IsJumping) { unit.JumpEnd = -1f; unit.Position = Terrain.OnGround(unit.Position); }
            Vector3 from = unit.Position;
            to = Terrain.OnGround(WalkablePointNear(to, from));   // never off the map, whoever picked the spot
            to = Terrain.OnGround(FreeSpotNear(unit, to, to, false));   // crowded: the nearest free spot around the destination
            if (unit.Phase == UnitPhase.Moving) EndMove(unit, false);
            unit.Position = to;
            Emit(EventType.Dash, unit.Id, null, new DashInfo { From = from, To = to, Duration = 0f, Forced = source != unit, Teleport = true, Blink = source == unit });
        }
    }
}
