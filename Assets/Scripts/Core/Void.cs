using System.Collections.Generic;
using UnityEngine;

namespace TacticsGame.Core
{
    /// <summary>A unit folded out of the world (VOIDED): it does nothing and nothing reaches it until EndsAt (or EndVoid).</summary>
    public class VoidState : ControlState
    {
        public float EndsAt;
        public string SourceId;

        public override bool BlocksDodgeDash { get { return true; } }
        public override bool BlocksTeleport { get { return true; } }
        public override void OnDeath(BattleSimulation sim) { Unit.RemoveState(this); }
    }

    /// <summary>Where a unit stood before Cross Beyond Thresholds: damage or a dash sends it back there while the status lasts.</summary>
    public class ThresholdState : ControlState
    {
        public Vector3 Origin;
        public override void OnDeath(BattleSimulation sim) { Unit.RemoveState(this); }
    }

    /// <summary>BattleSimulation: the Strategist's space-folding: VOIDED, swapping two units, Cross Beyond Thresholds' way back.</summary>
    public partial class BattleSimulation
    {
        private class PendingVoid { public string UnitId, SourceId; public float At, Seconds; }
        private readonly List<PendingVoid> pendingVoids = new List<PendingVoid>();

        /// <summary>
        /// Fold 'u' out of the world for 'seconds' (float.MaxValue: until EndVoid): whatever it was doing is dropped, and it
        /// is untargetable, takes no damage, can't be stunned or moved, and can't act. Not an Unstoppable unit someone else
        /// tries to send, nor one mid-split.
        /// </summary>
        public bool BeginVoid(Unit u, float seconds, Unit source)
        {
            if (u == null || !u.IsAlive || IsSplitting(u) || u.GetState<VoidState>() != null || u.GetState<MeldState>() != null) return false;
            if (source != u && u.IsUnstoppable) return false;
            if (u.Phase == UnitPhase.Windup || u.Phase == UnitPhase.Charging) Interrupt(u, "voided", false);
            if (u.Phase == UnitPhase.Channeling) EndChannel(u, "voided", false);
            if (u.Phase == UnitPhase.Moving) HaltWalk(u);
            ClearChase(u); ClearQueued(u, null);
            if (u.Grapple != null) EndGrapple(u);
            if (u.HeldById != null) ReleaseHeld(u);
            ClearHover(u);
            if (u.IsLifted) { u.RemoveState<LiftState>(); u.Position = Terrain.OnGround(u.Position); }
            if (u.IsJumping) { u.Position = u.JumpTo; u.JumpEnd = -1f; }
            if (u.FallLandAt >= 0f || u.GlideEnd >= 0f) { u.Position = Terrain.OnGround(u.GlideEnd >= 0f ? u.GlideTo : u.Position); u.FallLandAt = -1f; u.GlideEnd = -1f; }
            u.ResumeAction = null;
            pendingDashes.RemoveAll(d => d.UnitId == u.Id);
            u.AddState(new VoidState { EndsAt = seconds >= UntimedStatus ? float.MaxValue : Now + Mathf.Max(TickLength, seconds), SourceId = source != null ? source.Id : null });
            ApplyStatus(u, new StatusEffect
            {
                Id = StatusIds.Voided, SourceId = source != null ? source.Id : u.Id, ExpiresAt = float.MaxValue,
                Flags = StatusFlags.Untargetable | StatusFlags.NegatesDamage | StatusFlags.StunImmune | StatusFlags.Unstoppable,
            }, source);
            u.Phase = UnitPhase.Recovery;   // nothing at all while it is gone
            u.PhaseEndsAt = float.MaxValue;
            u.Waiting = false;
            EmitFx(u, null, "void", null, u.Position);
            return true;
        }

        /// <summary>Bring 'u' back from the void, stunned for 'stun' seconds if any (Spatial Chrysalis).</summary>
        public void EndVoid(Unit u, float stun = 0f, Unit source = null)
        {
            var v = u != null ? u.GetState<VoidState>() : null;
            if (v == null) return;
            u.RemoveState(v);
            if (u.HasStatus(StatusIds.Voided)) RemoveStatus(u, StatusIds.Voided);
            if (!u.IsAlive) return;
            u.PhaseEndsAt = Now;   // free again at the end of this tick
            EmitFx(u, null, "unvoid", null, u.Position);
            if (stun > 0f) StunUnit(u, stun, source);
        }

        public bool IsVoided(Unit u) { return u != null && u.GetState<VoidState>() != null; }

        /// <summary>VOID 'u' for 'seconds', starting 'delay' from now (after Scepter Of Tyranny's lift).</summary>
        public void VoidAfter(Unit u, float delay, float seconds, Unit source)
        {
            if (u == null) return;
            pendingVoids.Add(new PendingVoid { UnitId = u.Id, SourceId = source != null ? source.Id : null, At = Now + Mathf.Max(0f, delay), Seconds = seconds });
        }

        private void TickVoids()
        {
            for (int i = pendingVoids.Count - 1; i >= 0; i--)
            {
                var p = pendingVoids[i];
                if (Now + 1e-4f < p.At) continue;
                pendingVoids.RemoveAt(i);
                BeginVoid(GetUnit(p.UnitId), p.Seconds, GetUnit(p.SourceId));
            }
            foreach (var u in State.Units.ToArray())
            {
                var v = u.GetState<VoidState>();
                if (v != null && (!u.IsAlive || Now + 1e-4f >= v.EndsAt)) EndVoid(u);
                var t = u.GetState<ThresholdState>();
                if (t != null && (!u.IsAlive || !u.HasStatus(StatusIds.BeyondThresholds))) u.RemoveState(t);   // the status ran out: it stays where it is
            }
        }

        /// <summary>
        /// Swap where 'a' and 'b' stand, at once (Supersede). Neither moves if either can't be moved by 'source' (Unstoppable,
        /// voided, melded, falling).
        /// </summary>
        public bool SwapUnits(Unit a, Unit b, Unit source)
        {
            if (a == null || b == null || a == b || !a.IsAlive || !b.IsAlive) return false;
            foreach (var u in new[] { a, b })
                if (u.FallLandAt >= 0f || u.AnyState(s => s.BlocksTeleport) || !CanDisplace(u, source)) return false;
            Vector3 pa = Terrain.OnGround(a.Position), pb = Terrain.OnGround(b.Position);
            foreach (var u in new[] { a, b })
            {
                if (u.HeldById != null) ReleaseHeld(u);
                ClearHover(u);
                u.RemoveState<LiftState>();
                u.GlideEnd = -1f;
                if (u.IsJumping) u.JumpEnd = -1f;
                if (u.Phase == UnitPhase.Moving) EndMove(u, false);
            }
            a.Position = pb; b.Position = pa;
            Emit(EventType.Dash, a.Id, null, new DashInfo { From = pa, To = pb, Duration = 0f, Forced = source != a, Teleport = true, Blink = source == a });
            Emit(EventType.Dash, b.Id, null, new DashInfo { From = pb, To = pa, Duration = 0f, Forced = source != b, Teleport = true, Blink = source == b });
            return true;
        }

        /// <summary>Cross Beyond Thresholds: 'u' goes back to where it started from, and the status ends.</summary>
        public void ReturnAcrossThreshold(Unit u)
        {
            var t = u != null ? u.GetState<ThresholdState>() : null;
            if (t == null) return;
            u.RemoveState(t);
            if (u.HasStatus(StatusIds.BeyondThresholds)) RemoveStatus(u, StatusIds.BeyondThresholds);
            if (u.IsAlive) TeleportUnit(u, t.Origin, u);
        }
    }
}
