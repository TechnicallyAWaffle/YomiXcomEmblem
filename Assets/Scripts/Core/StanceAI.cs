using System.Collections.Generic;
using UnityEngine;

namespace TacticsGame.Core
{
    /// <summary>
    /// An AI that fights in two stances and cycles between them (the Exemplar): Close (in the target's face) and Far (at a
    /// safe distance). Each decision it first takes any opening a style wants whatever the stance (Opportunity: a follow-up
    /// that just opened, a stunned target), then plays its current stance; a stance with nothing it can do right now (its
    /// abilities on cooldown, out of reach) hands over to the other one, so the unit swaps between preferring melee and range
    /// as its abilities come and go. Styles can also switch on purpose (SwitchTo) after a finisher. Each unit keeps its own
    /// stance. Charging, holding, airborne and nobody-left cases, and guarding or dodging (React), are BasicEnemyAI's.
    /// </summary>
    public abstract class StanceCycleAI : ProfileAI
    {
        public enum Stance { Close, Far }

        /// <summary>The stance a unit starts in.</summary>
        public Stance StartStance = Stance.Close;

        private readonly Dictionary<string, Stance> stances = new Dictionary<string, Stance>();

        public Stance StanceOf(Unit unit)
        {
            Stance s;
            return stances.TryGetValue(unit.Id, out s) ? s : StartStance;
        }

        protected void SwitchTo(Unit unit, Stance s) { stances[unit.Id] = s; }

        public override UnitCommand Decide(BattleSimulation sim, Unit unit)
        {
            if (Basic(sim, unit)) return base.Decide(sim, unit);
            var react = React(sim, unit);
            if (react != null) return react;
            var target = PickTarget(sim, unit);
            if (target == null) return UnitCommand.Idle(unit.Id, 0.4f);

            var cmd = Opportunity(sim, unit, target);
            if (cmd != null) return cmd;
            for (int i = 0; i < 2; i++)
            {
                var stance = StanceOf(unit);
                cmd = stance == Stance.Close ? Close(sim, unit, target) : Far(sim, unit, target);
                if (cmd != null) return cmd;
                SwitchTo(unit, stance == Stance.Close ? Stance.Far : Stance.Close);   // nothing to do here: cycle
            }
            return Fallback(sim, unit, target);
        }

        /// <summary>Whom to fight: the nearest enemy by default.</summary>
        protected virtual Unit PickTarget(BattleSimulation sim, Unit unit) { return sim.NearestEnemy(unit); }

        /// <summary>Something to do whatever the stance (null: nothing).</summary>
        protected virtual UnitCommand Opportunity(BattleSimulation sim, Unit unit, Unit target) { return null; }

        /// <summary>The Close stance's move, or null when it has nothing it can do now (the stance then cycles).</summary>
        protected abstract UnitCommand Close(BattleSimulation sim, Unit unit, Unit target);

        /// <summary>The Far stance's move, or null when it has nothing it can do now.</summary>
        protected abstract UnitCommand Far(BattleSimulation sim, Unit unit, Unit target);

        /// <summary>Neither stance had anything: the basic AI's melee or ranged play.</summary>
        protected virtual UnitCommand Fallback(BattleSimulation sim, Unit unit, Unit target) { return base.Decide(sim, unit); }

        /// <summary>A walk straight away from 'target' to 'distance' metres from it (null if rooted or already that far).</summary>
        protected static UnitCommand BackOff(BattleSimulation sim, Unit unit, Unit target, float distance)
        {
            if (unit.IsRooted) return null;
            Vector3 away = unit.Position - target.Position; away.y = 0f;
            if (away.magnitude >= distance - 0.25f) return null;
            away = away.sqrMagnitude > 1e-6f ? away.normalized : Vector3.back;
            var to = sim.WalkablePointNear(target.Position + away * distance, unit.Position);
            return Flat(to, unit.Position) > 0.5f ? UnitCommand.MoveTo(unit.Id, to) : null;
        }
    }

    /// <summary>
    /// The Exemplar, a stance-cycling duelist:
    ///   Close: Spark the Flame when in reach; out of reach it gap-closes with Intrepid Advance rather than walking or dashing
    ///          (walking in only when Advance is on cooldown and Spark is ready).
    ///   Far:   Piercing Retreat away from a target that's too close; Split Apart The Sky once at SafeRange; with only the
    ///          smite ready and the target close, it walks back out to SafeRange first.
    ///   Any stance: Light the Pyre the moment Spark opens it (then Far: retreat, then the smite), and Split Apart The Sky on a
    ///          target still stunned when there is no retreat to make first; the smite hands back to Close.
    /// Its abilities are found by role, so another unit with the same kinds of abilities can use this style: the Opener
    /// without a RequiredStatus (Intrepid Advance), the one with one (Light the Pyre), the basic attack (Spark), Escape
    /// (Piercing Retreat) and AreaBlast (Split Apart The Sky).
    /// </summary>
    public class ExemplarAI : StanceCycleAI
    {
        /// <summary>Metres from its target it counts as safe for the smite (and backs off to).</summary>
        public float SafeRange = 6f;

        private static ActionDefinition Advance(Unit u) { return Find(u, AbilityRole.Opener, a => string.IsNullOrEmpty(a.RequiredStatus)); }
        private static ActionDefinition Pyre(Unit u) { return Find(u, AbilityRole.Opener, a => !string.IsNullOrEmpty(a.RequiredStatus)); }
        private static ActionDefinition Spark(Unit u) { return u.Actions.Find(a => a != null && a.Id == u.BasicAttackId) ?? MeleeStrike(u); }
        private static ActionDefinition Retreat(Unit u) { return Find(u, AbilityRole.Escape, a => a.Targeting == TargetType.Enemy); }
        private static ActionDefinition Smite(Unit u) { return Find(u, AbilityRole.AreaBlast); }

        private static bool Usable(BattleSimulation sim, Unit u, ActionDefinition def, Unit target) { return Ready(sim, u, def) && Reach(sim, u, def, target); }

        protected override UnitCommand Opportunity(BattleSimulation sim, Unit unit, Unit target)
        {
            var pyre = Pyre(unit);
            if (Usable(sim, unit, pyre, target)) { SwitchTo(unit, Stance.Far); return UnitCommand.UseAction(unit.Id, pyre, target.Id); }
            var smite = Smite(unit);
            // A stunned target (Light the Pyre) gets the smite, unless the Far stance can first retreat to a safe range.
            bool retreatFirst = StanceOf(unit) == Stance.Far && Usable(sim, unit, Retreat(unit), target) && Flat(unit.Position, target.Position) < SafeRange;
            if (target.Phase == UnitPhase.Stunned && !retreatFirst && Usable(sim, unit, smite, target)) { SwitchTo(unit, Stance.Close); return UnitCommand.UseAction(unit.Id, smite, target.Id); }
            return null;
        }

        protected override UnitCommand Close(BattleSimulation sim, Unit unit, Unit target)
        {
            var spark = Spark(unit);
            if (Usable(sim, unit, spark, target)) return UnitCommand.UseAction(unit.Id, spark, target.Id);
            var advance = Advance(unit);
            if (Usable(sim, unit, advance, target)) return UnitCommand.UseAction(unit.Id, advance, target.Id);
            if (spark != null && Ready(sim, unit, spark) && !unit.IsRooted) return UnitCommand.UseActionWhenInRange(unit.Id, spark, target.Id);
            return null;
        }

        protected override UnitCommand Far(BattleSimulation sim, Unit unit, Unit target)
        {
            float d = Flat(unit.Position, target.Position);
            var retreat = Retreat(unit);
            if (d < SafeRange && Usable(sim, unit, retreat, target)) return UnitCommand.UseAction(unit.Id, retreat, target.Id);
            var smite = Smite(unit);
            if (smite == null || !Ready(sim, unit, smite)) return null;
            if (d >= SafeRange - 0.25f && Reach(sim, unit, smite, target)) { SwitchTo(unit, Stance.Close); return UnitCommand.UseAction(unit.Id, smite, target.Id); }
            var back = BackOff(sim, unit, target, SafeRange + 0.5f);
            if (back != null) return back;
            if (!Reach(sim, unit, smite, target)) return null;
            SwitchTo(unit, Stance.Close);   // cornered or rooted: smite from here
            return UnitCommand.UseAction(unit.Id, smite, target.Id);
        }
    }
}
