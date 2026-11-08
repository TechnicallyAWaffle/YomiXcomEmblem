using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TacticsGame.Core
{
    // Deliverance (the final boss) and its parts. Plain data where it can be:
    //   Disassociate      - basic_attack putting PERPLEXITY on its target (TargetStatusesOnHit).
    //   Festival Of Colors - self_buff: faster actions and walking, and the status Joyous Dance needs.
    //   Q-<%~=}\..?9RG*   - area_attack line putting REDUCTION (slowed, StrainPerSecond) on everyone it catches.
    //   Amahaltel         - basic_attack with dodge while it winds up (SelfStatusesDuringWindup).
    //   Widen The Scope   - area_attack line through walls, knocking back (Displace AwayFromSource).

    /// <summary>
    /// Slip (perplexity_step): step behind an enemy holding PERPLEXITY at once, wherever it is and whatever is between: to
    /// the far side of it from where the user stood. In reach of the user's other abilities, or out of trouble.
    /// </summary>
    public class PerplexityStepBehavior : ActionBehavior
    {
        public override AbilityRole DefaultRoles(ActionDefinition def) { return base.DefaultRoles(def) | AbilityRole.Gapcloser | AbilityRole.Escape; }
        public override bool MovesUser { get { return true; } }
        public override bool RollsToHit(ActionDefinition def) { return false; }

        public override bool CanStart(BattleSimulation sim, Unit actor, UnitCommand cmd, out string reason)
        {
            var target = sim.GetUnit(cmd.TargetUnitId);
            reason = actor.IsRooted ? "immobilized" : target == null || !target.HasStatus(StatusIds.Perplexity) ? "needs a PERPLEXED target" : null;
            return reason == null;
        }

        public override void Describe(AbilityTip tip) { tip.Add("Teleport", "behind a PERPLEXED target, at once"); }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            var target = sim.GetUnit(a.TargetUnitId);
            if (target == null || !target.IsAlive || a.Actor.IsRooted) return;
            sim.TeleportUnit(a.Actor, Behind(sim, a.Actor, target), a.Actor);
        }

        /// <summary>The spot on the far side of 'target' from 'actor', a body's width off it.</summary>
        public static Vector3 Behind(BattleSimulation sim, Unit actor, Unit target)
        {
            Vector3 d = target.Position - actor.Position; d.y = 0f;
            d = d.sqrMagnitude > 1e-6f ? d.normalized : Vector3.forward;
            return sim.WalkablePointNear(target.Position + d * (actor.BodyRadius + target.BodyRadius + BattleSimulation.StandingGap), target.Position);
        }
    }

    /// <summary>
    /// Praa's Hatred (praas_hatred): a fast melee chain attack (ChainAttackBehavior; its asset's ChainFollowups is how many
    /// times it can follow up). Past 'heavy_after' (2) follow-ups on the same target, each use lands as a Heavy impact for
    /// 'heavy_damage' (2) times the damage.
    /// </summary>
    public class PraasHatredBehavior : ChainAttackBehavior
    {
        public override IEnumerable<BehaviorParam> Parameters
        {
            get { yield return new BehaviorParam("heavy_after", 2f); yield return new BehaviorParam("heavy_damage", 2f); }
        }

        public override void Describe(AbilityTip tip)
        {
            base.Describe(tip);
            tip.Add("Follow-ups", string.Format("after {0:0}: heavy, {1}", Param(tip.Def, "heavy_after"), AbilityTip.Mult(Param(tip.Def, "heavy_damage")) + " dmg"));
        }

        /// <summary>Which follow-up this use on 'targetId' is (0: it starts a chain).</summary>
        public int FollowupNumber(Unit actor, ActionDefinition def, string targetId)
        {
            var chain = actor.GetStatus(ChainStatus(def));
            return chain != null && chain.TargetId == targetId ? chain.Stacks + 1 : 0;
        }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            int n = a.IsFree ? 0 : FollowupNumber(a.Actor, a.Definition, a.TargetUnitId);
            int first = effects.Count;
            base.OnResolve(sim, a, effects);
            if (n <= Mathf.RoundToInt(Param(a.Definition, "heavy_after"))) return;
            for (int i = first; i < effects.Count; i++)
            {
                var atk = effects[i] as AttackEffect;
                if (atk == null) continue;
                atk.Heavy = true;
                atk.DamageMultiplier *= Param(a.Definition, "heavy_damage");
            }
        }
    }

    /// <summary>
    /// Joyous Dance (joyous_dance): during the windup the user dashes up to 'dash_range' (5m) to the spot furthest from its
    /// enemies that still has the target in Range, then zaps the target: a ranged hit carrying one of the ability's
    /// RandomStatusesOnHit, picked at random.
    /// </summary>
    public class JoyousDanceBehavior : BasicAttackBehavior
    {
        public override IEnumerable<BehaviorParam> Parameters { get { yield return new BehaviorParam("dash_range", 5f); } }

        public override void Describe(AbilityTip tip)
        {
            tip.Add("Dash", "always: up to " + AbilityTip.Metres(Param(tip.Def, "dash_range")) + " away from enemies, for its whole windup");
            base.Describe(tip);
            if (tip.Def.RandomStatusesOnHit != null && tip.Def.RandomStatusesOnHit.Count > 0)
                tip.Add("Inflicts", "one of " + string.Join(", ", tip.Def.RandomStatusesOnHit.Where(s => s != null).Select(s => s.Id).ToArray()) + " at random");
        }

        public override void OnWindupStart(BattleSimulation sim, ActionInstance a)
        {
            var actor = a.Actor;
            var target = sim.GetUnit(a.TargetUnitId);
            if (target == null || actor.IsRooted || actor.IsLifted || actor.HasFlag(StatusFlags.NoDash)) return;
            float dash = Param(a.Definition, "dash_range"), range = a.Stat(ActionStat.Range) * 0.95f;
            // It always dashes: the spot furthest from its enemies that keeps the target in range; failing any, the one nearest the target.
            Vector3 best = actor.Position, closest = actor.Position;
            float bestScore = float.MinValue, closestDist = float.MaxValue;
            for (int r = 1; r <= 2; r++)
                for (int i = 0; i < 16; i++)
                {
                    float ang = i * Mathf.PI / 8f + (r == 2 ? Mathf.PI / 16f : 0f);
                    Vector3 p = sim.WalkablePointNear(actor.Position + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * dash * (r == 1 ? 1f : 0.55f), actor.Position);
                    if (Flat(p, actor.Position) > dash + 0.1f || Flat(p, actor.Position) <= MinDash) continue;
                    float toTarget = Flat(p, target.Position);
                    if (toTarget < closestDist) { closestDist = toTarget; closest = p; }
                    if (toTarget > range) continue;
                    float s = Clearance(sim, actor, p);
                    if (s > bestScore) { bestScore = s; best = p; }
                }
            if (bestScore == float.MinValue) best = closest;
            // The whole windup is the dash: the attack winds up on the way and lands as it ends.
            if (Flat(best, actor.Position) > MinDash) sim.SlideUnitTo(actor, best, Mathf.Max(BattleSimulation.TickLength, a.ResolveAt - sim.Now));
        }

        /// <summary>A spot closer than this isn't a dash.</summary>
        private const float MinDash = 0.3f;

        private static float Clearance(BattleSimulation sim, Unit actor, Vector3 p)
        {
            float near = float.MaxValue;
            foreach (var e in sim.EnemiesOf(actor)) near = Mathf.Min(near, Flat(e.Position, p));
            return near == float.MaxValue ? 0f : near;
        }

        private static float Flat(Vector3 a, Vector3 b) { a.y = b.y = 0f; return Vector3.Distance(a, b); }
    }

    /// <summary>
    /// Close The Loop (close_the_loop): a heavy ranged shot through walls. If the target blocks it (a guard was up) or dodges
    /// it, the shooter teleports beside the target (once a dodge's dash has landed: BattleSimulation.WhenSettled) and puts
    /// FATED on it for 'fated_seconds' (1.5): its actions slowed to 'fated_speed' (0.35).
    /// </summary>
    public class CloseTheLoopBehavior : BasicAttackBehavior
    {
        public override IEnumerable<BehaviorParam> Parameters
        {
            get { yield return new BehaviorParam("fated_seconds", 1.5f); yield return new BehaviorParam("fated_speed", 0.35f); }
        }

        public override void Describe(AbilityTip tip)
        {
            base.Describe(tip);
            tip.Add("Blocked or dodged", "teleport to them, FATED " + AbilityTip.Secs(Param(tip.Def, "fated_seconds")));
        }

        public override void Attach(BattleSimulation sim)
        {
            sim.Triggers.Subscribe(TriggerType.Dodged, ctx =>
            {
                if (ctx.Action == null || ctx.Action.BehaviorId != "close_the_loop") return;
                var shooter = ctx.Other; var target = ctx.Subject; var def = ctx.Action;
                sim.WhenSettled(target, () => Close(sim, shooter, target, def));   // to where the dodge lands
            });
        }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            var target = sim.GetUnit(a.TargetUnitId);
            if (target == null || !target.IsAlive) return;
            var actor = a.Actor; var def = a.Definition;
            effects.Add(new AttackEffect
            {
                Source = actor, TargetId = target.Id, Definition = def, DamageMultiplier = a.DamageMultiplier, Tag = a.Tag,
                OnLanded = (s, t) => { if (t.HasStatus(StatusIds.Blocking)) Close(s, actor, t, def); },
            });
        }

        private void Close(BattleSimulation sim, Unit shooter, Unit target, ActionDefinition def)
        {
            if (shooter == null || target == null || !shooter.IsAlive || !target.IsAlive) return;
            sim.TeleportUnit(shooter, sim.WalkablePointNear(Beside(shooter, target), target.Position), shooter);
            sim.ApplyStatus(target, new StatusEffect
            {
                Id = StatusIds.Fated, SourceId = shooter.Id, ExpiresAt = sim.Now + Param(def, "fated_seconds") * shooter.DurationScale(def),
                Modifiers = new List<Modifier> { Modifier.Mult(StatId.ActionSpeed, Param(def, "fated_speed")) },
            }, shooter);
            sim.EmitFx(shooter, target, "close_the_loop", def, target.Position);
        }

        private static Vector3 Beside(Unit unit, Unit target)
        {
            Vector3 d = unit.Position - target.Position; d.y = 0f;
            d = d.sqrMagnitude > 1e-6f ? d.normalized : Vector3.forward;
            return target.Position + d * (unit.BodyRadius + target.BodyRadius + BattleSimulation.StandingGap);
        }
    }

    /// <summary>
    /// Stitch (stitch): a melee strike whose stun (StunDuration, the meld's length) lifts the target 'lift' (5m) up over
    /// that time (BattleSimulation.LiftUnit) and melds the user into it (BattleSimulation.BeginMeld): untargetable, taking
    /// no damage, healing 'heal_per_second' (70: 7 per 0.1s). When the meld ends, or the target is knocked out of the air,
    /// the user dashes 'exit_dash' (5m) out of it.
    /// </summary>
    public class StitchBehavior : BasicAttackBehavior
    {
        public override IEnumerable<BehaviorParam> Parameters
        {
            get
            {
                yield return new BehaviorParam("lift", 5f);
                yield return new BehaviorParam("heal_per_second", 70f);
                yield return new BehaviorParam("exit_dash", 5f);
            }
        }

        public override void Describe(AbilityTip tip)
        {
            base.Describe(tip);
            tip.Add("Meld", string.Format("lifts them {0}; heal {1:0}/s inside; dash {2} out", AbilityTip.Metres(Param(tip.Def, "lift")), Param(tip.Def, "heal_per_second"), AbilityTip.Metres(Param(tip.Def, "exit_dash"))));
        }

        protected override void DescribeMovement(AbilityTip tip) { }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            var target = sim.GetUnit(a.TargetUnitId);
            if (target == null || !target.IsAlive) return;
            var actor = a.Actor; var def = a.Definition;
            float seconds = a.Stat(ActionStat.StunDuration);
            float lift = Param(def, "lift"), heal = Param(def, "heal_per_second"), exit = Param(def, "exit_dash");
            effects.Add(new AttackEffect
            {
                Source = actor, TargetId = target.Id, Definition = def, DamageMultiplier = a.DamageMultiplier, Tag = a.Tag,
                OnLanded = (s, t) =>
                {
                    if (t.Phase != UnitPhase.Stunned) return;   // a guard that stopped the stun stops the meld
                    s.LiftUnit(t, lift, actor, seconds);
                    s.BeginMeld(actor, t, def, seconds, heal, exit);
                },
            });
        }
    }

    /// <summary>
    /// Split The Minds (split_the_minds): never chosen; it starts by itself when its user's last blow lands (the
    /// split_the_minds passive: BattleSimulation.HoldsOn). As it lands, its SummonUnits appear and the pulse takes
    /// max_health_damage of every other enemy's maximum health and shoves everyone DisplaceDistance away (BattleSimulation.SplitTheMinds).
    /// </summary>
    public class SplitTheMindsBehavior : ActionBehavior
    {
        public override IEnumerable<BehaviorParam> Parameters { get { yield return new BehaviorParam("max_health_damage", 0.75f); } }

        public override bool RollsToHit(ActionDefinition def) { return false; }

        public override bool CanStart(BattleSimulation sim, Unit actor, UnitCommand cmd, out string reason)
        {
            reason = sim.IsSplitting(actor) ? null : "only at death's door";
            return reason == null;
        }

        public override void Describe(AbilityTip tip)
        {
            tip.Add("Split", "into " + (tip.Def.SummonUnits != null ? tip.Def.SummonUnits.Count : 0) + " parts");
            tip.Add("Pulse", (Param(tip.Def, "max_health_damage") * 100f).ToString("0") + "% of every other enemy's max health");
            if (tip.V(ActionStat.DisplaceDistance) > 0f) tip.Add("Pushes everyone", AbilityTip.Metres(tip.V(ActionStat.DisplaceDistance)));
        }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects) { sim.SplitTheMinds(a.Actor, a.Definition, Param(a.Definition, "max_health_damage")); }
    }

    /// <summary>
    /// Imprint Upon Itself (imprint): at a squad unit within Range, copy the last ability it used into the user's own
    /// loadout (BattleSimulation.Imprint). No hit roll. Each use makes the next one slower and shorter: 'windup_step' (0.1s)
    /// more windup, 'recovery_step' (0.1s) more recovery, 'range_step' (2m) less range.
    /// </summary>
    public class ImprintBehavior : ActionBehavior
    {
        public override IEnumerable<BehaviorParam> Parameters
        {
            get
            {
                yield return new BehaviorParam("windup_step", 0.1f);
                yield return new BehaviorParam("recovery_step", 0.1f);
                yield return new BehaviorParam("range_step", 2f);
                yield return new BehaviorParam("cooldown_step", 0.25f);
            }
        }

        public override bool RollsToHit(ActionDefinition def) { return false; }

        public override bool CanStart(BattleSimulation sim, Unit actor, UnitCommand cmd, out string reason)
        {
            var from = sim.GetUnit(cmd.TargetUnitId);
            reason = from == null || from.Team != Team.Player ? "only a squad unit" : !sim.HasImprintable(actor, from) ? "nothing new to copy" : null;
            return reason == null;
        }

        public override void Describe(AbilityTip tip)
        {
            tip.Add("Copies", "a random ability from the target's loadout, into your own");
            tip.Add("Each use", string.Format("+{0}s windup and recovery, +{2}s cooldown, -{1} range", Param(tip.Def, "windup_step").ToString("0.##"), AbilityTip.Metres(Param(tip.Def, "range_step")), Param(tip.Def, "cooldown_step").ToString("0.##")));
        }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            var from = sim.GetUnit(a.TargetUnitId);
            if (from == null || !from.IsAlive) return;
            var def = a.Definition;
            sim.Imprint(a.Actor, from, def, Param(def, "windup_step"), Param(def, "recovery_step"), Param(def, "range_step"), Param(def, "cooldown_step"));
        }
    }

    /// <summary>
    /// EQUIPOSE (equipose): never chosen; Singleminded Perfection casts it at max strain, in place of an Unravel. Unstoppable
    /// through the windup, its strain draining to 0 over it; then a wave over AreaRadius around the user: its damage, and its
    /// TargetStatusesOnHit (the slow) on every enemy caught.
    /// </summary>
    public class EquiposeBehavior : ActionBehavior
    {
        public override bool RollsToHit(ActionDefinition def) { return true; }

        public override bool CanStart(BattleSimulation sim, Unit actor, UnitCommand cmd, out string reason) { reason = "only at max strain"; return false; }

        public override void Describe(AbilityTip tip)
        {
            tip.Add("At max strain", "cast by itself instead of an Unravel; Unstoppable, strain to 0 over the windup");
            base.Describe(tip);
            if (tip.V(ActionStat.AreaRadius) > 0f) tip.Add("Wave", AbilityTip.Metres(tip.V(ActionStat.AreaRadius)) + " around you");
        }

        public override void OnWindupStart(BattleSimulation sim, ActionInstance a)
        {
            var u = a.Actor;
            float seconds = Mathf.Max(BattleSimulation.TickLength, a.ResolveAt - sim.Now);
            sim.ApplyStatus(u, new StatusEffect
            {
                Id = DeliveranceParts.EquiposeStatus, SourceId = u.Id, ExpiresAt = a.ResolveAt + BattleSimulation.TickLength,
                Flags = StatusFlags.Unstoppable | StatusFlags.StunImmune, StrainPerSecond = -u.Strain / seconds,
            }, u);
        }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            var u = a.Actor;
            sim.AddStrain(u, -u.Strain);   // whatever the drain's steps left
            if (u.HasStatus(DeliveranceParts.EquiposeStatus)) sim.RemoveStatus(u, DeliveranceParts.EquiposeStatus);
            sim.EmitFx(u, null, "equipose", a.Definition, u.Position, a.Stat(ActionStat.AreaRadius));
            foreach (var t in sim.UnitsInArea(u, a.Definition, u.Position))
                effects.Add(new AttackEffect { Source = u, TargetId = t.Id, Definition = a.Definition, DamageMultiplier = a.DamageMultiplier, Tag = a.Tag, IgnoreRange = true, IgnoreCover = true });
        }
    }

    /// <summary>
    /// Scry The Meek (scry): channel a killzone of AreaRadius around the user for ChannelSeconds. Any enemy inside it that
    /// starts an ability (Dash and Block included) is shot by the LinkedAction (Ripples) at once, a free reaction the
    /// channel allows. Damage on the user breaks the channel, as any.
    /// </summary>
    public class ScryBehavior : ActionBehavior
    {
        public override bool RollsToHit(ActionDefinition def) { return false; }

        public override void Describe(AbilityTip tip)
        {
            tip.Add("Killzone", AbilityTip.Metres(tip.V(ActionStat.AreaRadius)) + " for " + AbilityTip.Secs(tip.Def.ChannelSeconds) + " (channel)");
            if (tip.Def.LinkedAction != null) tip.Add("Reaction", tip.Def.LinkedAction.DisplayName + " at any enemy using an ability inside");
        }

        public override void Attach(BattleSimulation sim)
        {
            sim.Triggers.Subscribe(TriggerType.ActionStarted, ctx =>
            {
                var user = ctx.Subject;
                if (user == null || !user.IsAlive) return;
                foreach (var s in sim.State.Units.ToArray())
                {
                    if (!s.IsAlive || s.Team == user.Team || !sim.IsChanneling(s)) continue;
                    var a = s.Channel.Action;
                    if (a == null || a.Definition.BehaviorId != "scry" || a.Definition.LinkedAction == null) continue;
                    Vector3 d = user.Position - s.Position; d.y = 0f;
                    if (d.magnitude - user.BodyRadius > s.ActionStat(a.Definition, ActionStat.AreaRadius)) continue;
                    if (!sim.IsTargetableBy(user, s)) continue;
                    sim.QueueFreeAction(s, a.Definition.LinkedAction, user.Id, -1f, 1f, true);
                }
            });
        }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            sim.EmitFx(a.Actor, null, "killzone", a.Definition, a.Actor.Position, a.Stat(ActionStat.AreaRadius));
        }

        public override void OnChannelEnd(BattleSimulation sim, ActionInstance a, bool broken)
        {
            sim.EmitFx(a.Actor, null, "killzone_end", a.Definition, a.Actor.Position);
        }
    }
}
