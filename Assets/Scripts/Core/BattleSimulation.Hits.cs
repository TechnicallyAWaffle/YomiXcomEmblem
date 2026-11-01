using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TacticsGame.Core
{
    /// <summary>BattleSimulation: Applying effects: attacks, dodges, dodge dashes and resumed actions.</summary>
    public partial class BattleSimulation
    {
        // ------------------------------------------------------------------ effects

        private void ApplyEffect(SimEffect effect, Dictionary<string, CombatSnapshot> snapshot)
        {
            var attack = effect as AttackEffect;
            if (attack != null) { ApplyAttack(attack, snapshot); return; }

            var heal = effect as HealEffect;
            if (heal != null)
            {
                var t = GetUnit(heal.TargetId);
                if (t == null || !t.IsAlive) return;
                t.Health = Mathf.Min(t.MaxHealth, t.Health + heal.Amount);
                Emit(EventType.Heal, heal.Source != null ? heal.Source.Id : null, t.Id, heal.Amount);
                return;
            }

            var deploy = effect as DeployEffect;
            if (deploy != null)
            {
                var o = deploy.Occluder;
                if (string.IsNullOrEmpty(o.Id)) o.Id = "deployable_" + (++deployCounter);
                State.Occluders.Add(o);
                Emit(EventType.DeployableSpawned, deploy.Source != null ? deploy.Source.Id : null, null,
                    new DeployableInfo { Id = o.Id, Position = o.Position, Radius = o.Radius, Height = o.Height, ExpiresAt = o.ExpiresAt });
                return;
            }

            var status = effect as ApplyStatusEffect;
            if (status != null)
            {
                ApplyStatus(GetUnit(status.TargetId), status.Status, status.Source);
                return;
            }

            var displace = effect as DisplaceEffect;
            if (displace != null)
            {
                DisplaceUnit(GetUnit(displace.TargetId), displace.Source, displace.Definition);
                return;
            }

            var trap = effect as TrapEffect;
            if (trap != null)
            {
                var t = trap.Trap;
                if (string.IsNullOrEmpty(t.Id)) t.Id = "trap_" + (++trapCounter);
                State.Traps.Add(t);
                Emit(EventType.TrapPlaced, t.OwnerId, null,
                    new TrapInfo { Id = t.Id, Position = t.Position, Radius = t.Radius, ArmsAt = t.ArmsAt, OwnerTeam = t.OwnerTeam });
            }
        }

        // ------------------------------------------------------------------ the hit pipeline

        private readonly Dictionary<HitStage, List<HitRule>> hitRules = new Dictionary<HitStage, List<HitRule>>();

        /// <summary>
        /// Add a rule every hit passes through at 'stage' (in the order added): a passive, an upgrade or a behavior that changes
        /// hits in general ("+20% damage on marked targets") without a case in ApplyAttack.
        /// </summary>
        public void AddHitRule(HitStage stage, HitRule rule)
        {
            List<HitRule> list;
            if (!hitRules.TryGetValue(stage, out list)) hitRules[stage] = list = new List<HitRule>();
            list.Add(rule);
        }

        private void RunHitRules(HitStage stage, HitContext hit)
        {
            List<HitRule> list;
            if (!hitRules.TryGetValue(stage, out list)) return;
            for (int i = 0; i < list.Count; i++) list[i](this, hit);
        }

        /// <summary>The rules every battle has (added once, in the constructor).</summary>
        private void AddBuiltInHitRules()
        {
            // An ability's StunnedDamageMultiplier (Lethal Prescience): more against a target stunned as the hit lands.
            AddHitRule(HitStage.Damage, (sim, hit) => { if (hit.Target.Phase == UnitPhase.Stunned) hit.DamageMultiplier *= hit.Definition.StunnedDamageMultiplier; });
        }

        /// <summary>
        /// One attack on one unit, in order: who it really reaches (a dome redirects, hidden or out-of-range targets are
        /// missed), the hit roll, the dodge roll, damage through block, strain and Unravel, then the target's reactions
        /// (statuses, pushes, interrupts) and the stun. Each step is its own method; HitRules hook in between.
        /// </summary>
        private void ApplyAttack(AttackEffect atk, Dictionary<string, CombatSnapshot> snapshot)
        {
            var hit = new HitContext { Source = atk.Source, Definition = atk.Definition, Effect = atk, Target = GetUnit(atk.TargetId) };
            if (hit.Target == null || hit.Target.Phase == UnitPhase.Dead) return;
            if (!ReachTarget(hit)) return;
            if (!RollToHit(hit)) return;
            if (!RollDodge(hit, snapshot)) return;
            DealDamage(hit);
            if (hit.Target.Health <= 0f) return;   // a dead unit needs no reactions or stun
            RunHitRules(HitStage.Landed, hit);
            ReactToHit(hit);
            StunFromHit(hit);
            if (atk.OnLanded != null && hit.Target.IsAlive) atk.OnLanded(this, hit.Target);
        }

        /// <summary>Who the attack reaches: a dome's owner instead (Sanctity), nobody if the target hid or moved out of reach.</summary>
        private bool ReachTarget(HitContext hit)
        {
            var src = hit.Source; var def = hit.Definition; var atk = hit.Effect;
            // A dome (Sanctity) takes projectiles and melee hits meant for those sheltering in it.
            var guard = RedirectTarget(src, def, hit.Target);
            if (guard == null) { Emit(EventType.Miss, src.Id, hit.Target.Id, new MissInfo { Reason = "absorbed", ActionId = def.Id }); return false; }
            if (guard != hit.Target)
            {
                Emit(EventType.HitRedirected, guard.Id, hit.Target.Id, new RedirectInfo { ActionId = def.Id, AttackerId = src.Id });
                hit.Target = guard;
                atk.IgnoreRange = true;   // it reaches the dome's owner wherever it stands
            }
            var target = hit.Target;

            // A target that went hidden during the windup can't be found.
            if (!IsTargetableBy(target, src))
            {
                Emit(EventType.Miss, src.Id, target.Id, new MissInfo { Reason = "hidden" });
                return false;
            }
            // Range: melee whiffs if the target moved away during the windup; area hits only need to be in the area.
            if (!atk.IgnoreRange && Vector3.Distance(src.Position, target.Position) > src.ActionStat(def, ActionStat.Range) * RangeTolerance)
            {
                Emit(EventType.Miss, src.Id, target.Id, new MissInfo { Reason = "out_of_range" });
                return false;
            }
            return true;
        }

        /// <summary>The hit roll: accuracy x body exposure (ranged vs cover) + bonuses (high ground, Marked, rules).</summary>
        private bool RollToHit(HitContext hit)
        {
            var src = hit.Source; var target = hit.Target; var def = hit.Definition; var atk = hit.Effect;
            hit.Exposure = (!atk.IgnoreCover && def.IsRanged && !def.PiercesTerrain) ? Visibility.GetExposure(src, target, false) : 1f;
            hit.AccuracyBonus = atk.AccuracyBonus + HeightAccuracyBonus(src, target, def);
            hit.AccuracyBonus += target.IncomingAccuracyBonus();
            RunHitRules(HitStage.Aiming, hit);
            float accuracy = src.ActionStat(def, ActionStat.Accuracy);
            hit.HitChance = Mathf.Clamp01(accuracy * hit.Exposure + hit.AccuracyBonus);
            double roll = rng.NextDouble();
            if (roll < hit.HitChance) return true;

            // A roll that would have hit in the open was stopped by cover: the shot strikes that cover. Where on it comes
            // from the same roll (no extra draw), so the outcome stays seeded.
            var miss = new MissInfo { Reason = "miss", ActionId = def.Id };
            float openChance = Mathf.Clamp01(accuracy + hit.AccuracyBonus);
            if (hit.Exposure < 0.999f && roll < openChance)
            {
                miss.Reason = "cover";
                var sight = Visibility as SimVisibility;
                float pick = (float)((roll - hit.HitChance) / Mathf.Max(1e-6f, openChance - hit.HitChance));
                Vector3 at;
                if (sight != null && sight.TryCoverPoint(src, target, pick, out at)) { miss.HitCover = true; miss.CoverPoint = at; }
            }
            Emit(EventType.Miss, src.Id, target.Id, miss);
            return false;
        }

        /// <summary>The dodge roll, against the snapshot taken as the tick began (stunned units snapshot at 0). False: dodged.</summary>
        private bool RollDodge(HitContext hit, Dictionary<string, CombatSnapshot> snapshot)
        {
            CombatSnapshot snap;
            if (!snapshot.TryGetValue(hit.Target.Id, out snap)) snap = new CombatSnapshot { Dodge = hit.Target.EffectiveDodge(), Block = hit.Target.EffectiveBlock() };
            hit.Dodge = snap.Dodge; hit.Block = snap.Block;
            if (rng.NextDouble() >= snap.Dodge) return true;
            OnDodged(hit.Source, hit.Target, hit.Definition);
            return false;
        }

        /// <summary>Damage through block (and its wear), strain and Unravel, the Damage event, and fragile statuses breaking.</summary>
        private void DealDamage(HitContext hit)
        {
            var src = hit.Source; var target = hit.Target; var def = hit.Definition; var atk = hit.Effect;
            // A Transcended Block (NegatesDamage) stops all of it, pierce or not: no damage, no strain, no wear on block.
            hit.Negated = target.HasFlag(StatusFlags.NegatesDamage);
            RunHitRules(HitStage.Damage, hit);
            hit.Raw = src.ActionStat(def, ActionStat.Damage) * atk.DamageMultiplier * hit.DamageMultiplier;
            float effectiveBlock = hit.Negated ? 1f : Mathf.Clamp01(hit.Block * (1f - Mathf.Clamp01(src.ActionStat(def, ActionStat.BlockPierce) + atk.BlockPierceBonus)));
            hit.Dealt = hit.Raw * (1f - effectiveBlock);
            target.Health -= hit.Dealt;
            target.LastAttackerId = src.Id;
            target.LastHitAction = def;
            target.LastHitTag = atk.Tag;
            if (hit.Block > 0f && !hit.Negated) target.ReduceBlock(def.Impact == ImpactType.Heavy ? HeavyBlockBreak : LightBlockBreak);

            // Strain and Unravel (checked against strain BEFORE this hit adds to it). Nothing is immune to Unravel: not Block,
            // not a Transcended Block that stops the damage, not Unstoppable.
            bool atMaxStrain = target.Strain >= target.MaxStrain - 0.001f;
            hit.Unravel = atMaxStrain && def.Impact == ImpactType.Heavy;
            if (!hit.Negated) AddStrain(target, src.ActionStat(def, ActionStat.StrainOnTarget));

            Emit(EventType.Damage, src.Id, target.Id, new DamageInfo
            {
                ActionId = def.Id, Amount = hit.Dealt, BlockedAmount = hit.Raw - hit.Dealt, Impact = def.Impact,
                RemainingHealth = target.Health, CausedUnravel = hit.Unravel,
            });
            Triggers.Fire(new TriggerContext { Type = TriggerType.DamageTaken, Sim = this, Subject = target, Other = src, Amount = hit.Dealt, Impact = def.Impact, Action = def });

            // Taking damage breaks fragile statuses (Marking Shot's stealth).
            for (int i = target.Statuses.Count - 1; i >= 0; i--)
                if (target.Statuses[i].Has(StatusFlags.BreaksOnDamage)) RemoveStatus(target, target.Statuses[i].Id);
        }

        /// <summary>
        /// What the hit does to a living target besides damage: out of a jump or hover, its on-hit statuses, pulls and pushes,
        /// and breaking what it was doing (a charge or channel on any damage, a windup on a Heavy one) unless it is Unstoppable.
        /// </summary>
        private void ReactToHit(HitContext hit)
        {
            var src = hit.Source; var target = hit.Target; var def = hit.Definition; var atk = hit.Effect;
            BreakJump(target, src);   // any hit that lands knocks a jumping unit out of the air
            foreach (var s in target.States.ToArray()) s.OnStruck(this, hit);   // damage knocks a hovering unit down (ControlState.OnStruck)
            foreach (var spec in def.TargetStatusesOnHit) if (spec.AppliesToUnit(src, target)) ApplyStatus(target, spec.Create(Now, src.Id), src);
            if (def.Displace == DisplaceMode.TowardSource || def.Displace == DisplaceMode.AwayFromSource) DisplaceUnit(target, src, def);
            hit.StunFor = src.ActionStat(def, ActionStat.StunDuration) * atk.StunMultiplier;
            bool willStun = hit.WillStun;
            if (atk.HasPush) Relocate(target, atk.PushTo, DisplaceDuration, true, src);
            // Any damage breaks a charge or a channel, and a Heavy one a windup, unless the unit is Unstoppable (Unravel, below, still does).
            bool unstoppable = target.IsUnstoppable;
            if (target.Phase == UnitPhase.Charging && !unstoppable) Interrupt(target, "charge broken", !willStun);
            foreach (var s in target.States.ToArray()) s.OnDamaged(this, hit);   // breaks a channel, knocks a slam out of the air (ControlState.OnDamaged)
            if (target.Phase == UnitPhase.Windup && target.ActiveAction != null && !unstoppable &&
                (def.Impact == ImpactType.Heavy || target.ActiveAction.Definition.InterruptedByDamage))
                Interrupt(target, def.Impact == ImpactType.Heavy ? "heavy impact" : "damage", !willStun);
        }

        /// <summary>The hit's stun, or an Unravel in its place.</summary>
        private void StunFromHit(HitContext hit)
        {
            if (hit.Unravel) UnravelUnit(hit.Target, hit.Source);
            else if (hit.StunFor > 0f) ApplyStun(hit.Target, hit.StunFor, hit.Source);
        }

        private void OnDodged(Unit attacker, Unit target, ActionDefinition def)
        {
            Emit(EventType.Dodge, attacker.Id, target.Id, new MissInfo { Reason = "dodge" });
            Triggers.Fire(new TriggerContext { Type = TriggerType.Dodged, Sim = this, Subject = target, Other = attacker, Action = def });
            if (!target.IsAlive || target.IsRooted) return;   // rooted units still dodge, but can't dash
            if (target.AnyState(s => s.BlocksDodgeDash)) return;   // a Mesmerize counter, a channel, a slam: it dodges where it stands

            if (target.Team == Team.Player)
                pendingDashes.Add(new DecisionRequest { Kind = DecisionKind.DodgeDash, UnitId = target.Id, AttackerId = attacker.Id });
            else
                ExecuteDash(target, BestDashDirection(target, attacker));
        }

        private void ExecuteDash(Unit unit, int direction8)
        {
            int idx = ((direction8 % 8) + 8) % 8;
            Vector3 from = unit.Position;
            DodgeTo(unit, Paths.ClampDash(from, from + Directions8[idx] * unit.DashDistanceNow()));
        }

        /// <summary>
        /// Dodge dash. Dashing out of a windup cancels it, but the unit remembers the ability and target and retries
        /// them when the dash ends, keeping the windup progress it had and paying nothing twice (see TryResume).
        /// Dashing nowhere (picking your own spot) stands firm and leaves the windup running.
        /// </summary>
        private void DodgeTo(Unit unit, Vector3 to)
        {
            if ((to - unit.Position).sqrMagnitude <= 1e-4f) return;
            var a = unit.ActiveAction;
            if (unit.Phase == UnitPhase.Windup && a != null)
            {
                float left = Mathf.Max(0f, a.ResolveAt - Now);
                Interrupt(unit, "dodge", false);
                unit.ResumeAction = a;
                unit.ResumeWindupLeft = left;
                unit.Phase = UnitPhase.Recovery;
                unit.PhaseEndsAt = Now + DashDuration;
            }
            else if (unit.ResumeAction != null) unit.PhaseEndsAt = Now + DashDuration;   // dodging again mid-dodge: retry when this dash lands
            Relocate(unit, to, DashDuration, false);
        }

        /// <summary>The dash after a mid-windup dodge has ended: start the remembered ability again if it is still possible.</summary>
        private bool TryResume(Unit u)
        {
            var r = u.ResumeAction;
            u.ResumeAction = null;
            if (r == null) return false;
            var def = r.Definition;
            var cmd = new UnitCommand { UnitId = u.Id, Type = CommandType.Action, Action = def, TargetUnitId = r.TargetUnitId, TargetPos = r.TargetPos }
                .Transcended(r.Transcended);
            string reason;
            bool ok = CanUse(u, def, GetUnit(r.TargetUnitId), out reason, false);   // already paid for: skip resource checks
            if (ok && def.Targeting == TargetType.Position && def.Shape != AreaShape.Line && Vector3.Distance(u.Position, r.TargetPos) > u.ActionStat(def, ActionStat.Range)) { ok = false; reason = "out of range"; }
            if (ok && !r.Behavior.CanStart(this, u, cmd, out reason)) ok = false;
            if (!ok)
            {
                Emit(EventType.ActionInterrupted, u.Id, null, new InterruptInfo { Reason = "resume failed: " + reason });
                return false;
            }
            StartAction(u, cmd, r.Behavior, u.ResumeWindupLeft);
            return true;
        }
    }
}
