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
            // A status that arms its holder's hits (ArmsHits: Crippling Pursuit's rounds): damage they deal puts that ability's
            // TargetStatusesOnHit on the target too.
            AddHitRule(HitStage.Landed, (sim, hit) =>
            {
                var src = hit.Source;
                if (src == null || hit.Dealt <= 0f) return;
                foreach (var s in src.Statuses.ToArray())
                {
                    if (!s.Has(StatusFlags.ArmsHits) || s.FromAction == null || s.FromAction.TargetStatusesOnHit == null) continue;
                    foreach (var spec in s.FromAction.TargetStatusesOnHit)
                        if (spec != null && spec.AppliesToUnit(src, hit.Target)) sim.ApplyStatus(hit.Target, spec.Create(sim.Now, src.Id, null, src.DurationScale(s.FromAction)), src);
                }
            });
            // A reflecting status (Wrathful Reflection): its share of what the attack dealt (shields and all) and of its stun goes
            // back at the attacker a moment later, with the attack's impact. A reflected hit isn't reflected again.
            AddHitRule(HitStage.Resolved, (sim, hit) => sim.Reflect(hit));
        }

        /// <summary>Tag on a reflected hit (Wrathful Reflection): it isn't reflected again.</summary>
        public const string ReflectedTag = "reflected";

        private void Reflect(HitContext hit)
        {
            var r = hit.Reflector; var src = hit.Source; var target = hit.Target;
            if (r == null || src == null || target == null || src == target || src.Team == target.Team || hit.Negated) return;
            if (hit.Effect != null && hit.Effect.Tag == ReflectedTag) return;
            float taken = hit.Dealt + hit.Shielded;
            if (taken <= 1e-3f) return;
            var def = r.FromAction ?? hit.Definition;
            var back = new AttackEffect
            {
                Source = target, TargetId = src.Id, Definition = def, FlatDamage = taken * r.ReflectShare, FlatStun = hit.StunFor * r.ReflectShare,
                ImpactOverride = hit.Impact, Unerring = true, IgnoreRange = true, IgnoreCover = true, NoStatuses = true, CritChance = 0f, Tag = ReflectedTag,
            };
            ScheduleStrike(back, r.ReflectDelay, target, "reflect", true);
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
            try
            {
                if (!RollToHit(hit)) return;
                if (!RollDodge(hit, snapshot)) return;
                DealDamage(hit);
                if (atk.AfterHit != null) atk.AfterHit(this, hit);
                GainOnHit(hit);
                if (hit.Target.Health <= 0f) return;   // a dead unit needs no reactions or stun
                RunHitRules(HitStage.Landed, hit);
                ReactToHit(hit);
                StunFromHit(hit);
                if (atk.OnLanded != null && hit.Target.IsAlive) atk.OnLanded(this, hit.Target);
            }
            finally { RunHitRules(HitStage.Resolved, hit); }
        }

        /// <summary>The attacker's SelfStatusesOnHit, now that the hit has landed (a block doesn't stop them; a dodge would have).</summary>
        private void GainOnHit(HitContext hit)
        {
            var src = hit.Source; var def = hit.Definition;
            if (src == null || !src.IsAlive || def.SelfStatusesOnHit == null || def.SelfStatusesOnHit.Count == 0) return;
            foreach (var spec in def.SelfStatusesOnHit)
            {
                var s = spec.Create(Now, src.Id, def.LinkedAction, src.DurationScale(def));
                s.TargetId = hit.Target.Id;   // whom it was earned on (RequiredStatusOnTarget follow-ups go at them)
                MarkFollowupWindow(src, s);
                ApplyStatus(src, s, src);
            }
        }

        /// <summary>Who the attack reaches: a dome's owner instead (Sanctity), nobody if the target hid or moved out of reach.</summary>
        private bool ReachTarget(HitContext hit)
        {
            var src = hit.Source; var def = hit.Definition; var atk = hit.Effect;
            // COUNTERSIGN: the Duelist who marked the attacker blinks in front of its ally and takes the hit (Karranos Sigil's
            // parry meets it, against its NEMESIS: that rule runs as the hit is aimed, after this).
            var signer = InterceptCountersigned(src, hit.Target, def, atk);
            if (signer != null)
            {
                Emit(EventType.HitRedirected, signer.Id, hit.Target.Id, new RedirectInfo { ActionId = def.Id, AttackerId = src.Id });
                hit.Target = signer;
                atk.IgnoreRange = true;
            }
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
            // A hitscan shot fires along the line it locked onto: a target that has left it (a dodge, a blink) is missed.
            if (atk.HasLineOfFire && !atk.Unerring && target.Id == atk.TargetId && !LineOfFireCatches(src, def, atk.LineOfFire, target))
            {
                Emit(EventType.Miss, src.Id, target.Id, new MissInfo { Reason = "off_line", ActionId = def.Id });
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
            if (atk.Unerring) { hit.HitChance = 1f; return true; }
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

        /// <summary>
        /// As the hit lands: a counter stance on the target (Mesmerize evades it; Parry takes it). There is no dodge roll here
        /// any more: dodges are rolled DodgeLead before an attack lands (RollIncomingDodges) and are a dash out of its way, so
        /// a unit that dodged but is still in reach is hit. False: countered.
        /// </summary>
        private bool RollDodge(HitContext hit, Dictionary<string, CombatSnapshot> snapshot)
        {
            CombatSnapshot snap;
            if (!snapshot.TryGetValue(hit.Target.Id, out snap)) snap = new CombatSnapshot { Dodge = hit.Target.EffectiveDodge(), Block = hit.Target.EffectiveBlock() };
            hit.Dodge = snap.Dodge; hit.Block = snap.Block;
            bool takesIt;
            if (MeetsWithCounter(hit, out takesIt)) return false;   // a counter stance comes first: it evades the hit
            return true;
        }

        /// <summary>
        /// DodgeLead before they land: every attack winding up (and every free action) rolls the dodges of the enemies it is
        /// going to hit (its target, or those in its area as they stand now), once. A projectile does as it is about to
        /// touch someone (TickProjectiles), a delayed blast as it is about to go off.
        /// </summary>
        private void RollIncomingDodges()
        {
            foreach (var u in State.Units)
                if (u.IsAlive && u.Phase == UnitPhase.Windup && u.ActiveAction != null) RollDodgesFor(u.ActiveAction);
            foreach (var f in freeActions) RollDodgesFor(f);
        }

        private void RollDodgesFor(ActionInstance a)
        {
            if (a.DodgesRolled || a.ResolveAt - Now > DodgeLead + 1e-4f) return;
            a.DodgesRolled = true;
            LockAim(a);   // before the dodges: a dodge dashes off the line it is now set on
            if (a.Actor == null || !a.Behavior.RollsToHit(a.Definition)) return;
            foreach (var t in IncomingTargets(a)) RollDodgeAgainst(a.Actor, t, a.Definition);
        }

        /// <summary>Metres off the line of fire a hitscan shot still catches a body (beyond its radius).</summary>
        public float HitscanTolerance = 0.1f;

        /// <summary>A hitscan shot stops tracking: from now it fires at where its target's chest is now.</summary>
        private void LockAim(ActionInstance a)
        {
            if (a.AimLocked || a.Actor == null || !a.Behavior.IsHitscan(a.Definition)) return;
            var t = GetUnit(a.TargetUnitId);
            if (t == null || !t.IsAlive) return;
            a.AimLocked = true;
            a.AimPoint = t.Position + Vector3.up * (t.BodyHeight * 0.5f);
            Emit(EventType.AimLocked, a.Actor.Id, t.Id, a.AimPoint);
        }

        /// <summary>
        /// Is 'target' on the line 'src' fires along toward 'aim' (from its chest, out to the shot's range)? Within its body
        /// radius (plus HitscanTolerance) of the line, at a height its body covers there.
        /// </summary>
        public bool LineOfFireCatches(Unit src, ActionDefinition def, Vector3 aim, Unit target)
        {
            Vector3 from = src.Position + Vector3.up * (src.BodyHeight * 0.5f);
            Vector3 dir = aim - from;
            Vector3 flat = new Vector3(dir.x, 0f, dir.z);
            if (flat.sqrMagnitude < 1e-6f) return true;   // straight up or down: nothing to step off
            float reach = src.ActionStat(def, ActionStat.Range) * RangeTolerance + target.BodyRadius;
            Vector3 f = flat.normalized;
            Vector3 v = target.Position - from; v.y = 0f;
            float along = Vector3.Dot(v, f);
            if (along < -target.BodyRadius || along > reach) return false;
            float side = (v - f * along).magnitude;
            if (side > target.BodyRadius + HitscanTolerance) return false;
            float h = from.y + dir.y * (along / flat.magnitude);   // the line's height over the target
            return h >= target.Position.y - HitscanTolerance && h <= target.Position.y + target.BodyHeight + HitscanTolerance;
        }

        /// <summary>Who 'a' is going to hit, as things stand: those in its area, else its target unit.</summary>
        private List<Unit> IncomingTargets(ActionInstance a)
        {
            var def = a.Definition;
            if (def.Shape != AreaShape.None && def.Targeting != TargetType.Enemy) return UnitsInArea(a.Actor, def, a.TargetPos);
            var list = new List<Unit>();
            var t = GetUnit(a.TargetUnitId);
            if (t != null) list.Add(t);
            if (def.Shape != AreaShape.None) foreach (var o in UnitsInArea(a.Actor, def, t != null ? t.Position : a.TargetPos)) if (!list.Contains(o)) list.Add(o);
            return list;
        }

        /// <summary>
        /// 'target' rolls its dodge against 'attacker''s 'def' about to land: on a success it dodges (a Dodge event, and a dash
        /// out of the way: picked by its player, or by itself). Not against an ally or itself, not while it can't dash
        /// (rooted, a state that keeps it where it is), and not with a counter stance up (that answers the hit as it lands).
        /// </summary>
        /// <param name="from">With no attacker (a blast of the world's: Temperance), where it comes from: the dash goes away from it.</param>
        public void RollDodgeAgainst(Unit attacker, Unit target, ActionDefinition def, Vector3? from = null)
        {
            if (target == null || !target.IsAlive || target == attacker || IsVoided(target)) return;
            if (attacker == null ? !from.HasValue : target.Team == attacker.Team || !IsTargetableBy(target, attacker)) return;
            if (target.CannotDodge) return;
            if (target.IsRooted || target.AnyState(s => s.BlocksDodgeDash)) return;   // no dodging in place
            if (target.Statuses.Exists(s => s.Has(StatusFlags.Counters))) return;
            if (pendingDashes.Exists(d => d.UnitId == target.Id)) return;   // already dodging this moment
            bool forced = PlayersAlwaysDodge && target.Team == Team.Player;   // the tutorial's dodge lesson
            float dodge = target.EffectiveDodge();
            if (!forced && (dodge <= 0f || rng.NextDouble() >= dodge)) return;   // no roll at all for no chance (the random stream is left as it was)
            OnDodged(attacker, target, def, from);
        }

        /// <summary>
        /// A counter stance (StatusFlags.Counters) takes priority over a dodge: an attack on its holder never rolls dodge.
        /// A stance that also NegatesDamage takes the hit itself (Parry: it lands for nothing, and the parry answers the
        /// damage taken): 'takesIt', and the hit goes on with no dodge roll. Any other evades it as its counter begins
        /// (Mesmerize): the attack misses ("countered") and TriggerType.Countered fires; returns true.
        /// </summary>
        private bool MeetsWithCounter(HitContext hit, out bool takesIt)
        {
            takesIt = false;
            var target = hit.Target; var src = hit.Source;
            var stance = target.Statuses.Find(s => s.Has(StatusFlags.Counters));
            if (stance == null || src == null || src == target || src.Team == target.Team) return false;
            if (stance.Has(StatusFlags.NegatesDamage)) { hit.Dodge = 0f; takesIt = true; return false; }
            Emit(EventType.Miss, src.Id, target.Id, new MissInfo { Reason = "countered", ActionId = hit.Definition.Id });
            Triggers.Fire(new TriggerContext { Type = TriggerType.Countered, Sim = this, Subject = target, Other = src, Action = hit.Definition });
            return true;
        }

        /// <summary>Damage through block (hits don't wear it down), shields, strain and Unravel, the Damage event, and fragile statuses breaking.</summary>
        private void DealDamage(HitContext hit)
        {
            var src = hit.Source; var target = hit.Target; var def = hit.Definition; var atk = hit.Effect;
            // A Transcended Block (NegatesDamage) stops all of it, pierce or not: no damage, no strain, no wear on block.
            hit.Negated = target.HasFlag(StatusFlags.NegatesDamage);
            RunHitRules(HitStage.Damage, hit);
            BreakWeakpointsOnHit(hit);
            // A critical strike: CritDamage more. No roll at all for an ability that can't crit, so other battles' draws don't move.
            float crit = atk.CritChance >= 0f ? atk.CritChance : CritChanceFor(src, def, false);
            if (crit > 0f && !hit.Negated && rng.NextDouble() < crit)
            {
                hit.Critical = true;
                hit.DamageMultiplier *= 1f + Mathf.Max(0f, src.ActionStat(def, ActionStat.CritDamage));
            }
            float missing = def.MissingHealthDamage > 0f ? def.MissingHealthDamage * Mathf.Max(0f, target.MaxHealth - target.Health) : 0f;   // Witness To The End
            float baseDamage = atk.FlatDamage >= 0f ? atk.FlatDamage : src.ActionStat(def, ActionStat.Damage);   // a reflected share: its own number
            hit.Raw = (baseDamage + missing) * atk.DamageMultiplier * hit.DamageMultiplier;
            float effectiveBlock = hit.Negated ? 1f : Mathf.Clamp01(hit.Block * (1f - Mathf.Clamp01(src.ActionStat(def, ActionStat.BlockPierce) + atk.BlockPierceBonus)));
            hit.Dealt = hit.Raw * (1f - effectiveBlock);
            float dealt = hit.Dealt; DevAdjustDamage(src, target, ref dealt); hit.Dealt = dealt;
            EtchingTraits.Dissipate(this, target, hit.Raw - hit.Dealt);   // what Block stopped (Dissipation)
            hit.Reflector = target.Statuses.Find(s => s.ReflectShare > 0f);   // as the hit came in: a shield it breaks still reflects it
            hit.Shielded = AbsorbWithShields(target, hit.Dealt);
            hit.Dealt -= hit.Shielded;
            float asStrain;
            float lost = TakeAsStrain(target, hit.Dealt, out asStrain);   // Strange Axis: it still counts as dealt (triggers, reflects), as strain
            target.Health -= lost;
            target.LastAttackerId = src.Id;
            target.LastHitAction = def;
            target.LastHitTag = atk.Tag;

            // Strain and Unravel (checked against strain BEFORE this hit adds to it). Nothing is immune to Unravel: not Block,
            // not a Transcended Block that stops the damage, not Unstoppable.
            bool atMaxStrain = target.Strain >= target.MaxStrain - 0.001f;
            hit.Unravel = atMaxStrain && hit.Impact == ImpactType.Heavy && !target.Passives.Contains(ForsakenRules.EntropyBeast);   // the Forsaken never Unravels: Entropy Beast turns it Diverged
            if (!hit.Negated) AddStrain(target, src.ActionStat(def, ActionStat.StrainOnTarget) * src.StrainDealtMult());

            Emit(EventType.Damage, src.Id, target.Id, new DamageInfo
            {
                ActionId = def.Id, Amount = lost, BlockedAmount = hit.Raw - hit.Dealt - hit.Shielded, ShieldedAmount = hit.Shielded, Impact = hit.Impact,
                RemainingHealth = target.Health, CausedUnravel = hit.Unravel, Critical = hit.Critical, WeakpointsBroken = hit.WeakpointsBroken, AsStrain = asStrain,
            });
            Triggers.Fire(new TriggerContext { Type = TriggerType.DamageTaken, Sim = this, Subject = target, Other = src, Amount = hit.Dealt, Impact = hit.Impact, Action = def });

            // Taking damage breaks fragile statuses (Marking Shot's stealth); a seal that breaks (BreakStun) stuns the enemy who broke it.
            for (int i = target.Statuses.Count - 1; i >= 0; i--)
            {
                if (i >= target.Statuses.Count || !target.Statuses[i].Has(StatusFlags.BreaksOnDamage)) continue;
                var broken = target.Statuses[i];
                RemoveStatus(target, broken.Id);
                if (broken.BreakStun > 0f && src.IsAlive && src.Team != target.Team) ApplyStun(src, broken.BreakStun, target);
            }
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
            // A status that strikes when it runs out (RUPTURED) carries the ability's LinkedAction: the blow it lands then.
            if (!atk.NoStatuses) foreach (var spec in def.TargetStatusesOnHit) if (spec.AppliesToUnit(src, target)) ApplyStatus(target, spec.Create(Now, src.Id, spec.Has(StatusFlags.StrikesOnExpire) ? def.LinkedAction : null, src.DurationScale(def)), src);
            if (def.RandomStatusesOnHit != null && def.RandomStatusesOnHit.Count > 0)   // one of these, at random (Joyous Dance)
            {
                var pick = def.RandomStatusesOnHit[Mathf.Min(def.RandomStatusesOnHit.Count - 1, (int)(rng.NextDouble() * def.RandomStatusesOnHit.Count))];
                if (pick != null && pick.AppliesToUnit(src, target)) ApplyStatus(target, pick.Create(Now, src.Id, null, src.DurationScale(def)), src);
            }
            if (def.Displace == DisplaceMode.TowardSource || def.Displace == DisplaceMode.AwayFromSource) DisplaceUnit(target, src, def);
            hit.StunFor = (atk.FlatStun >= 0f ? atk.FlatStun : src.ActionStat(def, ActionStat.StunDuration)) * atk.StunMultiplier * (1f + WeakpointBonus * hit.WeakpointsBroken);
            // Weakpoints the hit opens: the ability's own (when its hits carry them), what its user's Etchings add (on enemies:
            // Gaps In Their Mind), and a granted Transcend's (Unwind Their Self).
            int own = ActionBehaviors.Get(def.BehaviorId).WeakpointsOnHit(def) ? def.WeakpointsOnTarget : 0;
            int etched = target.Team != src.Team ? Mathf.Max(0, Mathf.RoundToInt(src.ActionStat(def, ActionStat.Weakpoints)) - def.WeakpointsOnTarget) : 0;
            if (own + etched > 0) AddTargetWeakpoints(src, target, def, own + etched);
            if (atk.ExtraWeakpoints > 0 && target.Team != src.Team && target != src) AddWeakpoints(target, atk.ExtraWeakpoints, target.Position - src.Position, atk.ExtraWeakpointSeconds, src);
            bool willStun = hit.WillStun;
            if (atk.HasPush) Relocate(target, atk.PushTo, DisplaceDuration, true, src);
            else if (!MovesWhatItHits(def)) GuardKnockback(hit);
            // Any damage breaks a charge or a channel, and a Heavy one a windup, unless the unit is Unstoppable (Unravel, below, still does).
            bool unstoppable = target.IsUnstoppable;
            if (target.Phase == UnitPhase.Charging && !unstoppable) Interrupt(target, "charge broken", !willStun);
            foreach (var s in target.States.ToArray()) s.OnDamaged(this, hit);   // breaks a channel, knocks a slam out of the air (ControlState.OnDamaged)
            if (target.Phase == UnitPhase.Windup && target.ActiveAction != null && !unstoppable &&
                (hit.Impact == ImpactType.Heavy || target.ActiveAction.Definition.InterruptedByDamage))
                Interrupt(target, hit.Impact == ImpactType.Heavy ? "heavy impact" : "damage", !willStun);
            if (hit.Shattered) Shatter(target);   // its last weakpoint broke: nothing it gave itself holds what it was doing
        }

        /// <summary>Does 'def' move what it hits by itself (a pull, a knockback, a line push)? Its own move is all a Block gets.</summary>
        private static bool MovesWhatItHits(ActionDefinition def)
        {
            if ((def.Displace == DisplaceMode.TowardSource || def.Displace == DisplaceMode.AwayFromSource) && def.DisplaceDistance > 0f) return true;
            var behavior = ActionBehaviors.Get(def.BehaviorId);
            return behavior != null && behavior.MovesTargets(def);
        }

        /// <summary>
        /// A hit on a raised Block (StatusIds.Blocking) shoves its holder straight away from the attacker,
        /// BlockKnockbackPerDamage metres per point of the hit's damage before block. A Transcended Block stands firm
        /// (ResistsKnockback, in Relocate), as does an Unstoppable unit.
        /// </summary>
        private void GuardKnockback(HitContext hit)
        {
            var target = hit.Target; var src = hit.Source;
            if (src == null || src == target || hit.Raw <= 0f || !target.HasStatus(StatusIds.Blocking)) return;
            Vector3 away = target.Position - src.Position; away.y = 0f;
            if (away.sqrMagnitude < 1e-6f) return;
            float distance = hit.Raw * BlockKnockbackPerDamage;
            if (distance < 0.05f) return;
            Relocate(target, target.Position + away.normalized * distance, DisplaceDuration, true, src);
        }

        /// <summary>'u''s chance to strike critically with 'def': the ability's CritChance (Eye For Weakness), and when it was
        /// Transcended through a granted CritChance Transcend, at least that grant's crit_chance.</summary>
        public float CritChanceFor(Unit u, ActionDefinition def, bool transcended)
        {
            if (u == null || def == null) return 0f;
            float chance = u.ActionStat(def, ActionStat.CritChance);
            if (transcended && u.GrantOf(def) == TranscendGrant.CritChance) chance = Mathf.Max(chance, u.GrantParam(def, "crit_chance", 0.5f));
            return Mathf.Clamp01(chance);
        }

        /// <summary>The hit's stun, or an Unravel in its place.</summary>
        private void StunFromHit(HitContext hit)
        {
            if (hit.Unravel) UnravelUnit(hit.Target, hit.Source);
            else if (hit.StunFor > 0f) ApplyStun(hit.Target, hit.StunFor, hit.Source);
        }

        private void OnDodged(Unit attacker, Unit target, ActionDefinition def, Vector3? from = null)
        {
            Emit(EventType.Dodge, attacker != null ? attacker.Id : null, target.Id, new MissInfo { Reason = "dodge" });
            Triggers.Fire(new TriggerContext { Type = TriggerType.Dodged, Sim = this, Subject = target, Other = attacker, Action = def });
            if (!target.IsAlive || target.IsRooted) return;   // (RollDodgeAgainst rolls none for a unit that can't dash)
            if (target.AnyState(s => s.BlocksDodgeDash)) return;

            if (EtchingTraits.DodgesOnInstinct(target)) { DodgeTo(target, InstinctDodgeSpot(target, attacker)); return; }   // Instinct Overtakes Methods
            var request = new DecisionRequest { Kind = DecisionKind.DodgeDash, UnitId = target.Id, AttackerId = attacker != null ? attacker.Id : null, ThreatPoint = attacker == null ? from : null,
                                                Sidestep = attacker != null && def != null && ActionBehaviors.Get(def.BehaviorId).IsHitscan(def) };
            if (target.PlayerOrdered) pendingDashes.Add(request);
            else ExecuteDash(target, DodgeDirection(target, request));
        }

        /// <summary>The 8-way direction most nearly across the line from 'attacker' (to whichever side a dash gets further).</summary>
        public int SidestepDirection(Unit unit, Unit attacker)
        {
            Vector3 away = unit.Position - attacker.Position; away.y = 0f;
            away = away.sqrMagnitude > 1e-6f ? away.normalized : Vector3.forward;
            Vector3 side = Vector3.Cross(Vector3.up, away);
            int best = 0; float bestScore = float.MinValue;
            for (int i = 0; i < Directions8.Length; i++)
            {
                float across = Mathf.Abs(Vector3.Dot(side, Directions8[i]));
                if (across < 0.6f) continue;   // across the line, give or take 45 degrees
                Vector3 to = Paths.ClampDash(unit.Position, unit.Position + Directions8[i] * unit.DashDistanceNow());
                float s = across + 0.5f * Flat(unit.Position, to) + 0.2f * Vector3.Dot(away, Directions8[i]);   // a little away rather than toward, all else equal
                if (s > bestScore) { bestScore = s; best = i; }
            }
            return best;
        }

        /// <summary>Where a dodge dashes by default: away from its attacker, or from the point a blast of the world's came from.</summary>
        public int DodgeDirection(Unit unit, DecisionRequest dodge)
        {
            var attacker = GetUnit(dodge.AttackerId);
            if (attacker != null && dodge.Sidestep) return SidestepDirection(unit, attacker);
            if (attacker != null) return BestDashDirection(unit, attacker);
            return dodge.ThreatPoint.HasValue ? BestDashDirection(unit, dodge.ThreatPoint.Value) : 0;
        }

        /// <summary>
        /// Instinct Overtakes Methods: where a dodge dashes, straight at the unit's own target: what its windup is aimed at
        /// (a unit, else the point), else what it chases or has queued, else the attacker it dodged. It stops at arm's length
        /// from a unit (no dash if already there), and goes no further than a dash reaches.
        /// </summary>
        private Vector3 InstinctDodgeSpot(Unit unit, Unit attacker)
        {
            var a = unit.ActiveAction;
            string id = a != null ? a.TargetUnitId : unit.Chase != null ? unit.Chase.TargetUnitId : unit.Queued != null ? unit.Queued.TargetUnitId : null;
            var aim = GetUnit(id);
            if (aim == unit || (aim != null && !aim.IsAlive)) aim = null;
            if (aim == null && a != null && a.Definition.Targeting == TargetType.Position) return DashDestination(unit, a.TargetPos);
            if (aim == null) aim = attacker;
            if (aim == null) return unit.Position;
            Vector3 d = aim.Position - unit.Position; d.y = 0f;
            float stop = unit.BodyRadius + aim.BodyRadius + StandingGap;
            if (d.magnitude <= stop + 0.05f) return unit.Position;
            return DashDestination(unit, unit.Position + d.normalized * (d.magnitude - stop));
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
                unit.PhaseEndsAt = Now + DodgeDuration;
            }
            else if (unit.ResumeAction != null) unit.PhaseEndsAt = Now + DodgeDuration;   // dodging again mid-dodge: retry when this dash lands
            Relocate(unit, to, DodgeDuration, false);
        }

        /// <summary>The dash after a mid-windup dodge has ended: start the remembered ability again if it is still possible.</summary>
        private bool TryResume(Unit u)
        {
            var r = u.ResumeAction;
            u.ResumeAction = null;
            if (r == null) return false;
            var def = r.Definition;
            var cmd = new UnitCommand { UnitId = u.Id, Type = CommandType.Action, Action = def, TargetUnitId = r.TargetUnitId, TargetPos = r.TargetPos, SecondTargetId = r.SecondTargetId }
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
