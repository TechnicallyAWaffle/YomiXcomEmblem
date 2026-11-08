using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TacticsGame.Core
{
    /// <summary>BattleSimulation: Dashes, shoves, falls, jumps, and starting moves and actions (costs, windup statuses).</summary>
    public partial class BattleSimulation
    {
        // ------------------------------------------------------------------ dashes, shoves and pulls

        public bool IsGliding(Unit u) { return Now < u.GlideEnd; }

        private void AdvanceGlides()
        {
            foreach (var u in State.Units)
            {
                if (u.GlideEnd < 0f) continue;
                float k = u.GlideEnd > u.GlideStart ? Mathf.Clamp01((Now - u.GlideStart) / (u.GlideEnd - u.GlideStart)) : 1f;
                Vector3 p = Vector3.Lerp(u.GlideFrom, u.GlideTo, k);
                float ground = Terrain.HeightAt(p);
                // A fall keeps its height a while, then drops faster and faster (never below the ground it is over).
                p.y = u.FallLandAt >= 0f ? Mathf.Max(ground, u.GlideFrom.y + (u.GlideTo.y - u.GlideFrom.y) * k * k) : ground;
                u.Position = p;
                if (k < 1f) continue;
                u.GlideEnd = -1f;
                if (u.FallLandAt >= 0f) Land(u);
            }
        }

        /// <summary>
        /// Knock 'unit' into a fall toward 'landing' (on lower ground), 'drop' meters down. 'leadSeconds' of sideways
        /// travel come first (a shove off a ledge); the drop itself takes sqrt(2h/g). The unit is stunned through the fall
        /// (Block doesn't help) and takes fall damage when it lands (see Land).
        /// </summary>
        /// speed / damageMult: a thrown-down fall (SlamToGround) is that many times as fast and as damaging.
        private void StartFall(Unit unit, Vector3 landing, float drop, float leadSeconds, Unit source, bool fromJump, float speed = 1f, float damageMult = 1f)
        {
            drop = Mathf.Max(0f, drop);
            float seconds = Mathf.Max(TickLength, leadSeconds + Mathf.Sqrt(2f * drop / Mathf.Max(0.1f, FallGravity)) / Mathf.Max(0.01f, speed));
            landing = Terrain.OnGround(landing);
            unit.GlideFrom = unit.Position;
            unit.GlideTo = landing;
            unit.GlideStart = Now;
            unit.GlideEnd = Now + seconds;
            unit.FallLandAt = unit.GlideEnd;
            unit.FallDamage = (FallDamageBase + FallDamagePerMeter * drop) * damageMult;
            unit.FallSourceId = source != null ? source.Id : null;
            Emit(EventType.Dash, unit.Id, null, new DashInfo { From = unit.Position, To = landing, Duration = seconds, Forced = true, Fall = true });
            Emit(EventType.Fall, source != null ? source.Id : null, unit.Id, new FallInfo { Height = drop, Duration = seconds, FromJump = fromJump });
            ApplyStun(unit, seconds + FallLandingSeconds, source, true);
        }

        /// <summary>A falling unit hits the ground: fall damage, credited to whoever knocked it down.</summary>
        private void Land(Unit u)
        {
            u.FallLandAt = -1f;
            if (!u.IsAlive) return;
            var source = GetUnit(u.FallSourceId);
            float dealt = u.FallDamage;
            u.Health -= dealt;
            if (source != null) u.LastAttackerId = source.Id;
            u.LastHitAction = null;
            u.LastHitTag = "fall";
            Emit(EventType.Damage, source != null ? source.Id : null, u.Id, new DamageInfo
            {
                ActionId = "fall", Amount = dealt, Impact = ImpactType.Light, RemainingHealth = u.Health,
            });
        }

        /// <summary>Hit, stunned or shoved mid-jump: the jump fails and the unit falls to the lower of its two sides.</summary>
        private void BreakJump(Unit u, Unit source)
        {
            if (!u.IsJumping) return;
            Vector3 low = u.JumpFrom.y <= u.JumpTo.y ? u.JumpFrom : u.JumpTo;
            float drop = u.Position.y - low.y;
            u.JumpEnd = -1f;
            u.Move = null;
            ClearChase(u);
            if (u.Phase == UnitPhase.Moving) u.Phase = UnitPhase.Ready;
            Emit(EventType.MoveEnded, u.Id, null, new MoveEndedInfo { Position = u.Position });
            StartFall(u, low, drop, 0f, source, true);
        }

        private void StartJump(Unit u, Vector3 from, Vector3 to)
        {
            u.Position = from;
            u.JumpFrom = from;
            u.JumpTo = to;
            u.JumpStart = Now;
            float seconds = TerrainMap.JumpSeconds(to.y - from.y);
            u.JumpEnd = Now + seconds;
            Emit(EventType.Jump, u.Id, null, new JumpInfo { From = from, To = to, Duration = seconds });
        }

        private void StartMove(Unit unit, List<Vector3> path)
        {
            unit.Phase = UnitPhase.Moving;
            float moveSpeed = unit.EffectiveMoveSpeed();
            unit.Move = new ActiveMove { Path = path, NextIndex = 1, Speed = moveSpeed };
            Emit(EventType.MoveStarted, unit.Id, null, new MoveInfo { Path = new List<Vector3>(path), Speed = moveSpeed });
        }

        /// <summary>windupLeft &gt;= 0 resumes an action a dodge cut short: that much windup, and no costs (already paid).</summary>
        /// <param name="charge">0..1 for a released charge ability (see ReleaseCharge).</param>
        private void StartAction(Unit unit, UnitCommand cmd, ActionBehavior behavior, float windupLeft = -1f, float charge = 0f)
        {
            var def = cmd.Action;
            float speed = unit.EffectiveActionSpeed();
            bool resuming = windupLeft >= 0f;
            bool transcend = cmd.Transcend && unit.CanTranscend(def);
            // Plight Upon Structure (Alio): an imprinted ability that costs strain, Transcended, is cast again by a ghost instead.
            bool ghost = !resuming && cmd.Transcend && unit.Passives.Contains(DeliveranceParts.PlightUponStructure) && unit.Imprinted.Contains(def.Id)
                         && StrainCostOf(unit, def, cmd.TargetUnitId) > 0f;
            if (ghost) transcend = false;
            var grant = transcend ? unit.GrantOf(def) : TranscendGrant.None;   // a Transcend an Etching gave it
            float windup = resuming ? windupLeft : WindupOf(unit, def) * (transcend && def.CanTranscend ? def.TranscendWindupMultiplier : 1f) * EtchingWindupScale(unit, def, cmd)
                * (grant == TranscendGrant.QuickWindup ? Mathf.Max(0f, unit.GrantParam(def, "windup_mult", 0.25f)) : 1f);   // Quickness And Lethargy
            if (!resuming) EtchingTraits.SpendRevenge(this, unit);   // spent by the next ability, whatever it is
            unit.Waiting = false;
            if (!resuming && unit.Queued != cmd && !behavior.KeepsQueuedOrder) ClearQueued(unit, null);   // using another ability overrides a queued ranged one (a Dash doesn't)
            var inst = new ActionInstance
            {
                Actor = unit, Definition = def, Behavior = behavior,
                TargetUnitId = cmd.TargetUnitId, TargetPos = cmd.TargetPos,
                StartedAt = Now, ResolveAt = Now + windup, Charge = charge,
                Transcended = transcend,
                NoRecovery = grant == TranscendGrant.NoRecovery,
                RecoveryScale = grant == TranscendGrant.QuickWindup ? Mathf.Max(0f, unit.GrantParam(def, "recovery_mult", 1.25f)) : 1f,
            };
            unit.Phase = UnitPhase.Windup;
            unit.PhaseEndsAt = inst.ResolveAt;
            unit.ActiveAction = inst;
            unit.LastActionId = def.Id;
            unit.LastActionTargetId = cmd.TargetUnitId;
            unit.LastActionPos = cmd.TargetPos;
            if (!resuming)
            {

                inst.UnravelsActor = def.UnravelsOverMaxStrain && unit.Strain + StrainCostOf(unit, def, cmd.TargetUnitId, inst.Transcended) > unit.MaxStrain + 1e-4f;
                PayCosts(unit, def, behavior, cmd.TargetUnitId, inst.Transcended);
                if (unit.Hover != null && !unit.Hover.Permanent) unit.Hover.EndsAfterAction = true;   // one ability from the air, then it comes down
            }
            var mesmer = unit.Mesmer;
            if (mesmer != null && mesmer.Strike != null && def == mesmer.Strike && mesmer.StrikesLeft > 0) mesmer.StrikesLeft--;

            // Body On Strings: a Transcend that makes the windup Unstoppable.
            if (!resuming && grant == TranscendGrant.UnstoppableWindup)
                ApplyStatus(unit, new StatusEffect { Id = StatusIds.Unstoppable, SourceId = unit.Id, ExpiresAt = inst.ResolveAt + TickLength, Flags = StatusFlags.Unstoppable }, unit);
            // Flow State: a Transcend that gives Dodge for a while from the cast.
            if (!resuming && grant == TranscendGrant.DodgeOnCast)
                ApplyStatus(unit, new StatusEffect
                {
                    Id = EtchingTraits.FlowStateStatus, SourceId = unit.Id, ExpiresAt = Now + Mathf.Max(TickLength, unit.GrantParam(def, "seconds", 2f)),
                    Modifiers = new List<Modifier> { Modifier.Add(StatId.Dodge, unit.GrantParam(def, "dodge", 0.75f)) },
                }, unit);
            behavior.OnWindupStart(this, inst);
            Emit(EventType.ActionStarted, unit.Id, cmd.TargetUnitId,
                new ActionStartedInfo { ActionId = def.Id, Windup = windup, Recovery = inst.NoRecovery ? 0f : unit.ActionStat(def, ActionStat.Recovery) * inst.RecoveryScale / speed, TargetPos = inst.TargetPos, Transcended = inst.Transcended });   // inst.TargetPos: where OnWindupStart aimed it (the smite's circle)
            ApplyWindupStatuses(inst);
            Triggers.Fire(new TriggerContext { Type = TriggerType.ActionStarted, Sim = this, Subject = unit, Action = def });
            if (!resuming) AlertTarget(unit, cmd.TargetUnitId);
            if (ghost) SummonGhost(unit, cmd);
        }

        /// <summary>ActionDefinition.TargetStatusesDuringWindup / SelfStatusesDuringWindup: on the target / the actor while the windup
        /// runs (until ClearWindupStatuses).</summary>
        private void ApplyWindupStatuses(ActionInstance a)
        {
            var target = GetUnit(a.TargetUnitId);
            if (target != null && target != a.Actor) ApplyWhileWinding(a, target, a.Definition.TargetStatusesDuringWindup, true);
            ApplyWhileWinding(a, a.Actor, a.Definition.SelfStatusesDuringWindup, false);
        }

        private void ApplyWhileWinding(ActionInstance a, Unit on, List<StatusSpec> specs, bool checkSide)
        {
            if (specs == null) return;
            foreach (var spec in specs)
            {
                if (checkSide && !spec.AppliesToUnit(a.Actor, on)) continue;
                var s = spec.Create(Now, a.Actor.Id);
                s.ExpiresAt = float.MaxValue;   // taken off when the windup ends, however long it runs
                ApplyStatus(on, s, a.Actor);
            }
        }

        /// <summary>The windup ended (landed, interrupted, or the actor died): take its TargetStatusesDuringWindup back off.</summary>
        private void ClearWindupStatuses(ActionInstance a)
        {
            if (a == null) return;
            RemoveWhileWinding(a, GetUnit(a.TargetUnitId), a.Definition.TargetStatusesDuringWindup);
            RemoveWhileWinding(a, a.Actor, a.Definition.SelfStatusesDuringWindup);
        }

        private void RemoveWhileWinding(ActionInstance a, Unit on, List<StatusSpec> specs)
        {
            if (specs == null || on == null) return;
            foreach (var spec in specs)
            {
                var s = on.GetStatus(spec.Id);
                if (s != null && s.SourceId == a.Actor.Id && s.ExpiresAt >= UntimedStatus) RemoveStatus(on, spec.Id);
            }
        }

        /// <summary>
        /// An attack started winding up on an AI-controlled (enemy team) unit: if it is only waiting, or walking with a
        /// Block or Dash it can afford, hand it back for orders now so the AI can guard or get away (see BasicEnemyAI).
        /// </summary>
        private void AlertTarget(Unit attacker, string targetId)
        {
            if (!AlertAiTargets) return;
            var t = GetUnit(targetId);
            if (t == null || !t.IsAlive || t.Team == attacker.Team || t.Team == Team.Player) return;
            if (t.Phase == UnitPhase.Recovery && t.Waiting) { EndRecoveryNow(t); return; }
            if (t.Phase != UnitPhase.Moving || t.IsJumping || t.Chase != null || IsGliding(t)) return;
            string reason;
            foreach (var def in t.Actions)
                if ((def.BehaviorId == "block" || def.BehaviorId == "dash") && CanAfford(t, def, out reason)) { EndMove(t); return; }
        }

        /// <summary>Cooldown, strain, energy, a consumed required status and on-windup self statuses: what starting it costs.</summary>
        private void PayCosts(Unit unit, ActionDefinition def, ActionBehavior behavior, string targetId, bool transcended = false)
        {
            float energyCost = behavior.EnergyCost(this, unit, def);   // before anything below changes the discount
            float strainCost = StrainCostOf(unit, def, targetId, transcended);
            float cooldown = unit.ActionStat(def, ActionStat.Cooldown);
            bool echo = transcended && unit.GrantOf(def) == TranscendGrant.NoCooldown;   // Echo: a Transcend with no cooldown
            if (cooldown > 0f && !echo) unit.CooldownReadyAt[def.Id] = Now + cooldown;
            EtchingTraits.SufferPastMax(this, unit, def, strainCost);   // before the strain lands: does it go past max?
            AddStrain(unit, strainCost);
            if (strainCost > 0f) Triggers.Fire(new TriggerContext { Type = TriggerType.StrainSpent, Sim = this, Subject = unit, Action = def, Amount = strainCost });
            if (energyCost > 0f) AddEnergy(unit, -energyCost);
            if (def.HealthCost > 0f)
            {
                float paid = unit.ActionStat(def, ActionStat.HealthCost);
                unit.Health -= paid;   // CanAfford keeps it above 0
                Emit(EventType.Damage, unit.Id, unit.Id, new DamageInfo { ActionId = def.Id, Amount = paid, Impact = ImpactType.Light, RemainingHealth = unit.Health });
            }
            if (def.ConsumesRequiredStatus && !def.ConsumeOnResolve)
                foreach (var id in def.RequiredStatuses()) RemoveStatus(unit, id);
            if (def.SelfStatusesOnWindup) ApplySelfStatuses(unit, def);
        }

        private void ApplySelfStatuses(Unit unit, ActionDefinition def)
        {
            foreach (var spec in def.SelfStatuses) ApplyStatus(unit, SelfStatus(unit, def, spec), unit);
        }

        /// <summary>One of an ability's SelfStatuses for its user, made now (it remembers the ability: StatusEffect.FromAction).</summary>
        private StatusEffect SelfStatus(Unit unit, ActionDefinition def, StatusSpec spec)
        {
            var s = spec.Create(Now, unit.Id, def.LinkedAction, unit.DurationScale(def));
            s.FromAction = def;
            return s;
        }
    }
}
