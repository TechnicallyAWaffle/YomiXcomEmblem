using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TacticsGame.Core
{
    /// <summary>BattleSimulation: The tick: movement, traps, windups, resolving actions, deaths and timers.</summary>
    public partial class BattleSimulation
    {
        // ------------------------------------------------------------------ the tick

        private void Step()
        {
            tick++;
            TickFocus();
            TickLifts();
            TickAerialSlams();
            AdvanceGlides();
            AdvanceMovement();
            TickGrapples();   // after movement, so the held unit is carried to where the grappler walked
            TickOutOfRangeOrders();
            TickCharges();
            TickAuras();
            TickChannels();
            CheckTraps();
            TickWindups();
            ResolveDueActions();
            ProcessDeaths();
            ExpireTimers();
            RecoverStrain();
        }

        private void AdvanceMovement()
        {
            foreach (var u in State.Units)
            {
                if (u.Phase != UnitPhase.Moving || u.Move == null) continue;
                if (u.IsJumping)
                {
                    float k = Mathf.Clamp01((Now - u.JumpStart) / Mathf.Max(1e-4f, u.JumpEnd - u.JumpStart));
                    Vector3 p = Vector3.Lerp(u.JumpFrom, u.JumpTo, k);
                    p.y = TerrainMap.JumpY(u.JumpFrom, u.JumpTo, k);
                    u.Position = p;
                    if (k < 1f) continue;
                    u.Position = u.JumpTo;
                    u.JumpEnd = -1f;
                    if (u.Move.NextIndex < u.Move.Path.Count)
                    {
                        // Landed: the walk carries on from here.
                        var rest = new List<Vector3> { u.Position };
                        for (int i = u.Move.NextIndex; i < u.Move.Path.Count; i++) rest.Add(u.Move.Path[i]);
                        Emit(EventType.MoveStarted, u.Id, null, new MoveInfo { Path = rest, Speed = u.Move.Speed });
                    }
                }
                else WalkStep(u);
                if (!u.IsJumping && u.Move.NextIndex >= u.Move.Path.Count && u.Chase == null) EndMove(u);   // a chaser waits there for its next re-path
            }
        }

        /// <summary>
        /// One tick of walking along the path, on the ground. Where the ground ahead changes height by more than
        /// TerrainMap.StepHeight the unit stops a body's width short of the edge and jumps it (StartJump), landing a
        /// body's width past it; the walk resumes once it lands.
        /// </summary>
        private void WalkStep(Unit u)
        {
            float remaining = u.Move.Speed * TickLength;
            while (remaining > 1e-6f && u.Move.NextIndex < u.Move.Path.Count)
            {
                Vector3 pos = u.Position;
                Vector3 next = u.Move.Path[u.Move.NextIndex];
                Vector3 d = next - pos; d.y = 0f;
                float dist = d.magnitude;
                if (dist < 1e-5f) { u.Position = Terrain.OnGround(next); u.Move.NextIndex++; continue; }
                d /= dist;
                float step = Mathf.Min(dist, remaining);
                float ground = Terrain.HeightAt(pos);
                // Look a body radius ahead (within this leg), so the unit takes off before its body meets the ledge.
                float reach = step + Mathf.Min(u.BodyRadius, dist - step);
                float edge = Terrain.FirstEdge(pos, pos + d * reach, ground);
                if (edge >= 0f)
                {
                    float takeoff = Mathf.Clamp(edge - u.BodyRadius, 0f, step);
                    float land = Mathf.Min(edge + u.BodyRadius, dist);
                    if (land >= dist - 1e-4f) u.Move.NextIndex++;
                    Vector3 from = pos + d * takeoff; from.y = ground;
                    StartJump(u, from, Terrain.OnGround(pos + d * land));
                    return;
                }
                u.Position = Terrain.OnGround(pos + d * step);
                remaining -= step;
                if (step >= dist) u.Move.NextIndex++;
            }
        }

        /// <summary>Armed traps spring on the first unit of another team inside them; unsprung ones expire.</summary>
        private void CheckTraps()
        {
            for (int i = State.Traps.Count - 1; i >= 0; i--)
            {
                var t = State.Traps[i];
                if (t.ExpiresAt <= Now)
                {
                    State.Traps.RemoveAt(i);
                    Emit(EventType.TrapRemoved, t.OwnerId, null, t.Id);
                    continue;
                }
                if (Now + 1e-4f < t.ArmsAt) continue;

                Unit victim = null;
                foreach (var u in State.Units)
                {
                    if (!u.IsAlive || u.Team == t.OwnerTeam) continue;
                    Vector3 d = u.Position - t.Position; d.y = 0f;
                    if (d.magnitude <= t.Radius + u.BodyRadius) { victim = u; break; }
                }
                if (victim == null) continue;

                State.Traps.RemoveAt(i);
                Emit(EventType.TrapTriggered, t.OwnerId, victim.Id, t.Id);
                var owner = GetUnit(t.OwnerId);
                foreach (var spec in t.Statuses) ApplyStatus(victim, spec.Create(Now, t.OwnerId), owner);
            }
        }

        private void EndMove(Unit u, bool settle = true)
        {
            u.Move = null;
            ClearChase(u);   // cancelled, rooted or dashed out of a chase: it is over
            u.Phase = UnitPhase.Ready;
            Emit(EventType.MoveEnded, u.Id, null, new MoveEndedInfo { Position = u.Position });
            if (settle) SettleOutOfBodies(u);
            Emit(EventType.UnitReady, u.Id, null);
            Triggers.Fire(new TriggerContext { Type = TriggerType.UnitReady, Sim = this, Subject = u });
        }

        private void TickWindups()
        {
            foreach (var u in State.Units)
            {
                if (u.Phase != UnitPhase.Windup || u.ActiveAction == null || !u.IsAlive) continue;
                if (!u.ActiveAction.Behavior.OnWindupTick(this, u.ActiveAction)) Interrupt(u, "condition lost");
            }
            foreach (var f in freeActions.ToArray())
            {
                if (!f.Actor.IsAlive) { freeActions.Remove(f); continue; }
                if (!f.Behavior.OnWindupTick(this, f)) { freeActions.Remove(f); Emit(EventType.ActionInterrupted, f.Actor.Id, null, new InterruptInfo { Reason = "condition lost" }); }
            }
        }

        private void ResolveDueActions()
        {
            var due = new List<ActionInstance>();
            foreach (var u in State.Units)
                if (u.Phase == UnitPhase.Windup && u.ActiveAction != null && Now + 1e-4f >= u.ActiveAction.ResolveAt)
                    due.Add(u.ActiveAction);
            foreach (var f in freeActions.ToArray())
                if (Now + 1e-4f >= f.ResolveAt) { due.Add(f); freeActions.Remove(f); }
            if (due.Count == 0) return;

            // Snapshot first: simultaneous attacks all see the same dodge/block.
            var snapshot = new Dictionary<string, CombatSnapshot>();
            foreach (var u in State.Units)
                snapshot[u.Id] = new CombatSnapshot { Dodge = u.EffectiveDodge(), Block = u.EffectiveBlock() };

            var effects = new List<SimEffect>();
            foreach (var a in due)
            {
                int first = effects.Count;
                a.Behavior.OnResolve(this, a, effects);
                // The actor's damage statuses (Wrath) count as of now: baked in before anything below can take them away.
                float dealt = a.Actor.DamageDealtMult();
                for (int i = first; i < effects.Count; i++)
                {
                    var atk = effects[i] as AttackEffect;
                    if (atk != null && atk.Source == a.Actor) atk.DamageMultiplier *= dealt;
                }
                if (!a.IsFree) ClearWindupStatuses(a);
                if (a.Definition.ConsumesRequiredStatus && a.Definition.ConsumeOnResolve)
                    foreach (var id in a.Definition.RequiredStatuses()) RemoveStatus(a.Actor, id);
                if (!a.Definition.SelfStatusesOnWindup)
                    foreach (var spec in a.Definition.SelfStatuses)
                        effects.Add(new ApplyStatusEffect { Source = a.Actor, TargetId = a.Actor.Id, Status = spec.Create(Now, a.Actor.Id, a.Definition.LinkedAction) });
                if (a.Definition.EnergyGain > 0f) AddEnergy(a.Actor, a.Definition.EnergyGain);
                float recovery = a.IsFree ? 0f : a.Stat(ActionStat.Recovery) / a.Actor.EffectiveActionSpeed();
                Emit(EventType.ActionResolved, a.Actor.Id, a.TargetUnitId, new ActionResolvedInfo { ActionId = a.Definition.Id, Recovery = recovery });
                if (!a.IsFree)
                {
                    a.Actor.ActiveAction = null;
                    a.Actor.Phase = UnitPhase.Recovery;
                    a.Actor.PhaseEndsAt = Now + recovery;
                    if (a.Definition.ChannelSeconds > 0f && a.Actor.IsAlive) StartChannel(a);   // the recovery waits for the channel
                }
                Triggers.Fire(new TriggerContext { Type = TriggerType.ActionResolved, Sim = this, Subject = a.Actor, Action = a.Definition });
            }

            foreach (var e in effects) ApplyEffect(e, snapshot);

            // After its hit has landed: an over-max Praa's Truth unravels its user; an ability used from a hover brings it down.
            foreach (var a in due)
            {
                if (a.IsFree || !a.Actor.IsAlive) continue;
                if (a.UnravelsActor) UnravelUnit(a.Actor, a.Actor);
                if (a.Actor.HoverEndsAfterAction) EndHover(a.Actor, false, null);
            }
        }

        private void ProcessDeaths()
        {
            foreach (var u in State.Units)
            {
                if (u.Health > 0f || u.Phase == UnitPhase.Dead) continue;
                // Killed in mid-air (jumping, shoved, falling, lifted): drop the body where it was headed.
                bool airborne = u.IsJumping || u.GlideEnd >= 0f || u.IsLifted;
                if (u.IsJumping) u.Position = u.JumpFrom.y <= u.JumpTo.y ? u.JumpFrom : u.JumpTo;
                else if (u.GlideEnd >= 0f) u.Position = u.GlideTo;
                else if (u.IsLifted) u.Position = Terrain.OnGround(u.Position);
                if (airborne) Emit(EventType.MoveEnded, u.Id, null, new MoveEndedInfo { Position = u.Position });
                if (u.Phase == UnitPhase.Windup) ClearWindupStatuses(u.ActiveAction);
                foreach (var s in u.States.ToArray()) s.OnDeath(this);   // lets go of a channel, a grip, a lift (ControlState.OnDeath)
                u.Phase = UnitPhase.Dead;
                u.ActiveAction = null;
                u.Move = null;
                u.Chase = u.Queued = null;
                u.GlideEnd = -1f;
                u.JumpEnd = -1f;
                u.FallLandAt = -1f;
                pendingDashes.RemoveAll(d => d.UnitId == u.Id);
                queues.Remove(u.Id);
                Emit(EventType.Death, u.LastAttackerId, u.Id);
                Triggers.Fire(new TriggerContext { Type = TriggerType.Killed, Sim = this, Subject = u, Other = GetUnit(u.LastAttackerId), Action = u.LastHitAction, Tag = u.LastHitTag });
            }

            bool playersAlive = State.Units.Any(u => u.Team == Team.Player && u.IsAlive);
            bool enemiesAlive = State.Units.Any(u => u.Team == Team.Enemy && u.IsAlive);
            if (playersAlive && enemiesAlive) return;
            Outcome = !playersAlive && !enemiesAlive ? BattleOutcome.Draw
                    : playersAlive ? BattleOutcome.PlayerVictory : BattleOutcome.PlayerDefeat;
            Emit(EventType.BattleEnd, null, null);
        }

        private void ExpireTimers()
        {
            foreach (var u in State.Units)
            {
                if (!u.IsAlive) continue;
                for (int i = u.Statuses.Count - 1; i >= 0; i--)
                {
                    if (u.Statuses[i].ExpiresAt > Now) continue;
                    float oldAction = u.EffectiveActionSpeed(), oldMove = u.EffectiveMoveSpeed();
                    Emit(EventType.StatusExpired, u.Id, null, u.Statuses[i].Id);
                    u.Statuses.RemoveAt(i);
                    HandleSpeedChange(u, oldAction, oldMove);
                }
                bool locked = u.Phase == UnitPhase.Recovery || u.Phase == UnitPhase.Stunned;
                if (locked && Now + 1e-4f >= u.PhaseEndsAt)
                {
                    u.Phase = UnitPhase.Ready;
                    u.Waiting = false;
                    if (u.ResumeAction != null && TryResume(u)) continue;
                    Emit(EventType.UnitReady, u.Id, null);
                    Triggers.Fire(new TriggerContext { Type = TriggerType.UnitReady, Sim = this, Subject = u });
                }
            }

            for (int i = State.Occluders.Count - 1; i >= 0; i--)
            {
                if (State.Occluders[i].ExpiresAt > Now) continue;
                Emit(EventType.DeployableRemoved, null, null, State.Occluders[i].Id);
                State.Occluders.RemoveAt(i);
            }
        }
    }
}
