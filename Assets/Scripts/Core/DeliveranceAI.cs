using System.Linq;
using UnityEngine;

namespace TacticsGame.Core
{
    /// <summary>
    /// Deliverance, melded into one (phase one): goes for the nearest foe, the squad and the enemy's reinforcements alike.
    /// Its strain pool is deep, so it guards and dashes freely, Transcended whenever the strain allows. Up close it opens
    /// with Disassociate (PERPLEXITY), then rains Praa's Hatred on the same target until something stops it. A perplexed
    /// target is a door: it Slips behind one to get in reach, or out of trouble. Festival Of Colors when foes are near, and
    /// Joyous Dance while it lasts; Close The Loop at range; Stitch to heal when hurt; its corrupted line on a crowd.
    /// Abilities are found by behavior, so a kit built the same way plays the same.
    /// </summary>
    public class DeliveranceAI : KitAI
    {
        /// <summary>Guard Transcended while strain stays under this share of max after paying.</summary>
        public float TranscendBelow = 0.85f;
        /// <summary>Stitch once its health is under this share.</summary>
        public float StitchBelow = 0.8f;
        /// <summary>Festival Of Colors when a foe is within this.</summary>
        public float FestivalWithin = 12f;
        /// <summary>Close The Loop only at targets at least this far (closer, it fights hand to hand).</summary>
        public float LoopBeyond = 5f;

        public DeliveranceAI() { HighStrain = 0.88f; RestedStrain = 0.3f; BlockStrainMargin = 4f; }

        public override UnitCommand Decide(BattleSimulation sim, Unit unit)
        {
            if (Basic(sim, unit)) return base.Decide(sim, unit);
            var slip = ByBehavior(unit, "perplexity_step");
            var react = Guard(sim, unit, slip);
            if (react != null) return react;
            var rest = StrainFraction(unit) >= HighStrain ? Rest(sim, unit) : null;
            if (rest != null) return rest;

            var hatred = ByBehavior(unit, "praas_hatred");
            var disassociate = Find(unit, AbilityRole.Strike, a => !a.IsRanged && a.TargetStatusesOnHit.Exists(s => s != null && s.Id == StatusIds.Perplexity))
                               ?? ByBehavior(unit, "basic_attack", a => a.TargetStatusesOnHit.Exists(s => s != null && s.Id == StatusIds.Perplexity));
            var festival = ByBehavior(unit, "self_buff", a => a.SelfStatuses.Exists(s => s != null && s.Id == StatusIds.Festival));
            var dance = ByBehavior(unit, "joyous_dance");
            var loop = ByBehavior(unit, "close_the_loop");
            var stitch = ByBehavior(unit, "stitch");
            var line = ByBehavior(unit, "area_attack", a => a.Shape == AreaShape.Line);
            var dash = Find(unit, AbilityRole.Dash);
            var target = Target(sim, unit, hatred);
            if (target == null) return UnitCommand.Idle(unit.Id, 0.4f);
            float d = Flat(unit.Position, target.Position);

            // Keep the chain going on the same target while it can.
            var chainer = hatred != null ? ActionBehaviors.Get(hatred.BehaviorId) as ChainAttackBehavior : null;
            if (chainer != null && chainer.InChain(unit, hatred, target.Id) && Reach(sim, unit, hatred, target)) return UnitCommand.UseAction(unit.Id, hatred, target.Id);

            if (Ready(sim, unit, dance) && Reach(sim, unit, dance, target)) return UnitCommand.UseAction(unit.Id, dance, target.Id);
            if (Ready(sim, unit, festival) && d <= FestivalWithin) return UnitCommand.UseAction(unit.Id, festival, unit.Id);
            if (Ready(sim, unit, stitch) && unit.Health < unit.MaxHealth * StitchBelow && Reach(sim, unit, stitch, target)) return UnitCommand.UseAction(unit.Id, stitch, target.Id);
            if (Ready(sim, unit, line) && d <= unit.ActionStat(line, ActionStat.Range) && LineCatches(sim, unit, line, target.Position) >= 1)
                return UnitCommand.UseActionAt(unit.Id, line, target.Position);
            if (Ready(sim, unit, loop) && d >= LoopBeyond && Reach(sim, unit, loop, target)) return UnitCommand.UseAction(unit.Id, loop, target.Id);

            // Hand to hand: PERPLEXITY first, then Praa's Hatred.
            if (Ready(sim, unit, disassociate) && !target.HasStatus(StatusIds.Perplexity) && Reach(sim, unit, disassociate, target)) return UnitCommand.UseAction(unit.Id, disassociate, target.Id);
            if (Ready(sim, unit, hatred) && Reach(sim, unit, hatred, target)) return UnitCommand.UseAction(unit.Id, hatred, target.Id);
            if (Ready(sim, unit, disassociate) && Reach(sim, unit, disassociate, target)) return UnitCommand.UseAction(unit.Id, disassociate, target.Id);

            // Out of reach: through the door if the target is perplexed, else dash in (Transcended from further), else walk.
            if (!unit.IsRooted)
            {
                if (Ready(sim, unit, slip) && target.HasStatus(StatusIds.Perplexity) && Reach(sim, unit, slip, target)) return UnitCommand.UseAction(unit.Id, slip, target.Id);
                var strike = hatred ?? disassociate;
                float reach = strike != null ? unit.ActionStat(strike, ActionStat.Range) : 2f;
                if (dash != null && d > reach + 0.5f)
                {
                    float plain = sim.DashReach(unit), far = sim.DashReach(unit, true);
                    if (d <= plain + reach && Affordable(sim, unit, dash, false)) return UnitCommand.UseActionAt(unit.Id, dash, Beside(unit, target));
                    if (d <= far + reach && Affordable(sim, unit, dash, true) && Room(sim, unit, dash, true)) return UnitCommand.UseActionAt(unit.Id, dash, Beside(unit, target)).Transcended();
                }
                if (strike != null) return UnitCommand.UseActionWhenInRange(unit.Id, strike, target.Id);
            }
            return UnitCommand.Idle(unit.Id, 0.3f);
        }

        /// <summary>The foe to fight: the one its Praa's Hatred chain is on, else the nearest (the squad and the reinforcements alike).</summary>
        private static Unit Target(BattleSimulation sim, Unit unit, ActionDefinition hatred)
        {
            if (hatred != null)
            {
                var chainer = ActionBehaviors.Get(hatred.BehaviorId) as ChainAttackBehavior;
                var chain = chainer != null ? unit.GetStatus(chainer.ChainStatus(hatred)) : null;
                var on = chain != null ? sim.GetUnit(chain.TargetId) : null;
                if (on != null && on.IsAlive && sim.IsTargetableBy(on, unit)) return on;
            }
            return sim.NearestEnemy(unit);
        }

        /// <summary>
        /// The liberal guard: an attack winding up on it is met with a Slip away (behind a perplexed foe, if one is far from
        /// the attacker), else a Block, Transcended whenever the strain stays under TranscendBelow, else a Dash out of
        /// melee reach (Transcended likewise).
        /// </summary>
        private UnitCommand Guard(BattleSimulation sim, Unit unit, ActionDefinition slip)
        {
            var incoming = IncomingAttack(sim, unit);
            if (incoming == null) return null;
            if (Ready(sim, unit, slip) && !unit.IsRooted)
            {
                Unit door = null;
                foreach (var e in sim.EnemiesOf(unit))
                    if (e.HasStatus(StatusIds.Perplexity) && e != incoming.Actor && Flat(e.Position, incoming.Actor.Position) > incoming.Stat(ActionStat.Range) + 2f && Reach(sim, unit, slip, e)) { door = e; break; }
                if (door != null) return UnitCommand.UseAction(unit.Id, slip, door.Id);
            }
            var block = Find(unit, AbilityRole.Guard);
            if (block != null && Affordable(sim, unit, block, false))
            {
                float guard = GuardSeconds(block);
                float until = incoming.ResolveAt - sim.Now;
                if (until > guard * 0.85f) return UnitCommand.Idle(unit.Id, Mathf.Max(BattleSimulation.TickLength, until - guard * 0.6f));
                bool transcend = Affordable(sim, unit, block, true) && Room(sim, unit, block, true);
                return UnitCommand.UseAction(unit.Id, block, unit.Id).Transcended(transcend);
            }
            var dash = Find(unit, AbilityRole.Dash);
            if (dash != null && !incoming.Definition.IsRanged && !unit.IsRooted)
            {
                float reach = incoming.Stat(ActionStat.Range) * BattleSimulation.RangeTolerance;
                Vector3 to;
                if (Affordable(sim, unit, dash, true) && Room(sim, unit, dash, true) && EscapeDash(sim, unit, incoming.Actor, reach, true, out to)) return UnitCommand.UseActionAt(unit.Id, dash, to).Transcended();
                if (Affordable(sim, unit, dash, false) && EscapeDash(sim, unit, incoming.Actor, reach, false, out to)) return UnitCommand.UseActionAt(unit.Id, dash, to);
            }
            return null;
        }

        /// <summary>Paying for 'def' leaves its strain under TranscendBelow.</summary>
        private bool Room(BattleSimulation sim, Unit unit, ActionDefinition def, bool transcend)
        {
            return (unit.Strain + sim.StrainCostOf(unit, def, null, transcend)) / Mathf.Max(1f, unit.MaxStrain) <= TranscendBelow;
        }

        /// <summary>How many foes a line aimed at 'at' would catch.</summary>
        private static int LineCatches(BattleSimulation sim, Unit unit, ActionDefinition line, Vector3 at) { return sim.UnitsInArea(unit, line, at).Count; }
    }

    /// <summary>
    /// A part of Deliverance (phase two). It starts with nothing but Imprint Upon Itself, so its game is copying: until it
    /// has ImprintFirstUntil (5) copies, Imprint comes first whenever it is ready (copying the best new ability a squad
    /// unit last used, or walking into reach of one); after that, only when nothing it copied is worth using. It fights
    /// with what it has copied, picked for the range it is at (RangeFit: no long-range shot at point blank), using each
    /// the way its shape says (a strike at the nearest foe it reaches, an area at a foe, a buff when foes are
    /// close, a heal on the most hurt ally, a blink to close in). The parts differ in how (its numbers below): Syne keeps its
    /// distance, Alio Transcends into ghosts while its strain is low, Revl never lets up, Wex stays with its allies.
    /// </summary>
    public class DeliverancePartAI : KitAI
    {
        /// <summary>The distance it likes to fight from, kit allowing (it closes to its copied abilities' reach).</summary>
        public float PreferredRange = 3f;
        /// <summary>Never rests or backs off; guards only when nothing in its kit can be used.</summary>
        public bool Relentless;
        /// <summary>Transcend imprinted abilities that cost strain while strain is under this share (-1: never). Alio's ghosts.</summary>
        public float TranscendBelow = -1f;
        /// <summary>Keeps within this many metres of its nearest ally (0: doesn't care). Wex, whose Block aura reaches 15m.</summary>
        public float StayNearAllies = 0f;
        /// <summary>Until it has copied this many abilities, Imprint comes before everything else it could do: when it is ready
        /// and someone has something new to copy, it uses it, or goes and gets in its reach. After that it is one more option,
        /// used when nothing it copied is worth using.</summary>
        public int ImprintFirstUntil = 5;
        /// <summary>A copied ability reaching farther than LongReach is a long-range one: aimed at a target closer than
        /// CloseShare of its range it is worth less (down to PointBlankValue of its worth, at point blank), and a part that
        /// isn't Relentless won't use it under PoorFit: it steps back to its distance instead.</summary>
        public float LongReach = 7f, CloseShare = 0.4f, PointBlankValue = 0.25f, PoorFit = 0.5f;

        public override UnitCommand Decide(BattleSimulation sim, Unit unit)
        {
            if (Basic(sim, unit)) return base.Decide(sim, unit);
            if (!Relentless)
            {
                var r = React(sim, unit) ?? (StrainFraction(unit) >= HighStrain ? Rest(sim, unit) : null);
                if (r != null) return r;
            }
            bool imprintFirst = unit.Imprinted.Count < ImprintFirstUntil;
            if (imprintFirst)
            {
                var imprint = ImprintCommand(sim, unit, true);
                if (imprint != null) return imprint;
            }

            // The best thing its kit can do right now, for the range it is at (RangeFit).
            UnitCommand best = null; float bestValue = float.MinValue; ActionDefinition bestDef = null;
            UnitCommand poor = null; float poorValue = float.MinValue; ActionDefinition poorDef = null;   // the best of those too close to use
            foreach (var def in unit.Actions)
            {
                if (def == null || !unit.Imprinted.Contains(def.Id) || !Ready(sim, unit, def)) continue;
                var cmd = Use(sim, unit, def);
                if (cmd == null) continue;
                float fit = RangeFit(sim, unit, def, cmd);
                float v = Value(def) * fit;
                if (fit < PoorFit && !Relentless && !unit.IsRooted)   // too close for it: get some room first
                {
                    if (v > poorValue) { poorValue = v; poor = cmd; poorDef = def; }
                    continue;
                }
                if (v > bestValue) { bestValue = v; best = cmd; bestDef = def; }
            }
            if (best == null && !imprintFirst)
            {
                var imprint = ImprintCommand(sim, unit, false);
                if (imprint != null) return imprint;
            }
            if (best == null && poor != null)
            {
                var near = sim.NearestEnemy(unit);
                var away = near != null ? StepAway(sim, unit, near.Position, 4f) : null;
                if (away != null && Flat(away.TargetPos, unit.Position) > 1.5f) return away;
                best = poor; bestDef = poorDef;   // cornered: use it anyway
            }
            if (best != null)
            {
                if (TranscendBelow > 0f && StrainFraction(unit) < TranscendBelow && sim.StrainCostOf(unit, bestDef, best.TargetUnitId) > 0f) best = best.Transcended();
                return best;
            }
            if (Relentless)
            {
                var r = React(sim, unit);
                if (r != null) return r;
            }
            return Position(sim, unit);
        }

        /// <summary>
        /// Imprint, when it is ready: at the nearest squad unit within reach that has something new in its loadout (what it
        /// copies is random). 'approach': with no one in reach who has, walk toward the nearest who has (null if no one has).
        /// </summary>
        private UnitCommand ImprintCommand(BattleSimulation sim, Unit unit, bool approach)
        {
            var imprint = ByBehavior(unit, "imprint");
            if (!Ready(sim, unit, imprint)) return null;
            Unit from = null, nearest = null; float fromDist = float.MaxValue, nearestDist = float.MaxValue;
            foreach (var e in sim.EnemiesOf(unit))
            {
                if (e.Team != Team.Player || !sim.HasImprintable(unit, e)) continue;
                float d = Flat(unit.Position, e.Position);
                if (d < nearestDist) { nearestDist = d; nearest = e; }
                if (d < fromDist && Reach(sim, unit, imprint, e)) { fromDist = d; from = e; }
            }
            if (from != null) return UnitCommand.UseAction(unit.Id, imprint, from.Id);
            if (!approach || nearest == null || unit.IsRooted) return null;
            return Approach(sim, unit, nearest, unit.ActionStat(imprint, ActionStat.Range) * 0.8f);
        }

        /// <summary>
        /// How well 'def' suits the range 'cmd' would use it at, 0..1: 1 for a short-reach ability or a target far enough off;
        /// a long-range one (beyond LongReach) aimed closer than CloseShare of its range falls toward PointBlankValue.
        /// </summary>
        protected float RangeFit(BattleSimulation sim, Unit unit, ActionDefinition def, UnitCommand cmd)
        {
            float range = unit.ActionStat(def, ActionStat.Range);
            if (range <= LongReach || def.Targeting == TargetType.Self || def.Targeting == TargetType.None || def.Targeting == TargetType.Ally) return 1f;
            if (def.Targeting == TargetType.Position && ActionBehaviors.Get(def.BehaviorId).MovesUser) return 1f;   // a blink to close in: Use already judged it
            Vector3 at;
            var t = cmd.TargetUnitId != null ? sim.GetUnit(cmd.TargetUnitId) : null;
            if (t != null) { if (t.Team == unit.Team) return 1f; at = t.Position; }
            else at = cmd.TargetPos;
            float close = range * CloseShare, d = Flat(unit.Position, at);
            return d >= close ? 1f : Mathf.Lerp(PointBlankValue, 1f, d / Mathf.Max(0.01f, close));
        }

        /// <summary>How 'def' would be used now, by its shape; null if now isn't the time.</summary>
        protected UnitCommand Use(BattleSimulation sim, Unit unit, ActionDefinition def)
        {
            string why;
            var nearest = sim.NearestEnemy(unit);
            if (nearest == null) return null;
            switch (def.Targeting)
            {
                case TargetType.Enemy:
                {
                    // The nearest it can hit; for a long-range one, the nearest not too close for it if there is one.
                    float range = unit.ActionStat(def, ActionStat.Range), close = range > LongReach ? range * CloseShare : 0f;
                    var t = sim.EnemiesOf(unit).Where(e => sim.CanUse(unit, def, e, out why))
                                               .OrderBy(e => Flat(unit.Position, e.Position) < close ? 1 : 0).ThenBy(e => Flat(unit.Position, e.Position)).FirstOrDefault();
                    return t != null ? UnitCommand.UseAction(unit.Id, def, t.Id) : null;
                }
                case TargetType.Self:
                case TargetType.None:
                {
                    float reach = def.Shape == AreaShape.Circle && def.Damage > 0f ? unit.ActionStat(def, ActionStat.AreaRadius) : 10f;
                    return EnemiesWithin(sim, unit, unit.Position, reach) > 0 ? UnitCommand.UseAction(unit.Id, def, unit.Id) : null;
                }
                case TargetType.Ally:
                case TargetType.AnyUnit:
                {
                    if (def.Heal > 0f) { var hurt = MostHurtAlly(sim, unit, def, 0.8f); return hurt != null ? UnitCommand.UseAction(unit.Id, def, hurt.Id) : null; }
                    if (Flat(unit.Position, nearest.Position) > 10f) return null;
                    var ally = sim.State.Units.Where(a => a.IsAlive && a.Team == unit.Team && (a != unit || !def.CannotTargetSelf) && sim.CanUse(unit, def, a, out why, false))
                                              .OrderBy(a => a == unit ? 1 : 0).ThenBy(a => Flat(a.Position, nearest.Position)).FirstOrDefault();
                    return ally != null ? UnitCommand.UseAction(unit.Id, def, ally.Id) : null;
                }
                case TargetType.Position:
                {
                    float range = unit.ActionStat(def, ActionStat.Range), d = Flat(unit.Position, nearest.Position);
                    if (ActionBehaviors.Get(def.BehaviorId).MovesUser)
                    {
                        if (d <= FightDistance(unit) + 2f || unit.IsRooted) return null;   // a blink or leap: to close in
                        return UnitCommand.UseActionAt(unit.Id, def, sim.ApproachSpot(unit, nearest, FightDistance(unit)));
                    }
                    return d <= range ? UnitCommand.UseActionAt(unit.Id, def, nearest.Position) : null;
                }
            }
            return null;
        }

        /// <summary>What a copied ability is worth to it: damage, heavy blows, areas, pushes and heals count.</summary>
        protected static float Value(ActionDefinition def)
        {
            if (def == null) return 0f;
            return def.Damage + (def.Impact == ImpactType.Heavy ? 10f : 0f) + (def.Shape != AreaShape.None ? 5f : 0f) + (def.DisplaceDistance > 0f ? 4f : 0f)
                 + def.Heal * 0.8f + (def.SelfStatuses != null && def.SelfStatuses.Count > 0 ? 6f : 0f) + 1f;
        }

        /// <summary>Where it wants to be from its foes: PreferredRange, or closer if its copied attacks don't reach that far.</summary>
        protected float FightDistance(Unit unit)
        {
            float reach = 0f;
            foreach (var def in unit.Actions)
                if (def != null && unit.Imprinted.Contains(def.Id) && (def.Targeting == TargetType.Enemy || (def.Targeting == TargetType.Position && def.Damage > 0f)))
                    reach = Mathf.Max(reach, unit.ActionStat(def, ActionStat.Range));
            return reach > 0f ? Mathf.Min(PreferredRange, reach * 0.85f) : PreferredRange;
        }

        /// <summary>Nothing to use: get into position (by its allies, at its distance, or in reach of a squad unit to copy).</summary>
        private UnitCommand Position(BattleSimulation sim, Unit unit)
        {
            var target = sim.NearestEnemy(unit);
            if (target == null || unit.IsRooted) return UnitCommand.Idle(unit.Id, 0.3f);
            if (StayNearAllies > 0f)
            {
                var ally = sim.State.Units.Where(a => a.IsAlive && a != unit && a.Team == unit.Team && !a.IsGhost).OrderBy(a => Flat(a.Position, unit.Position)).FirstOrDefault();
                if (ally != null && Flat(ally.Position, unit.Position) > StayNearAllies) return Approach(sim, unit, ally, StayNearAllies * 0.5f);
            }
            // A strike it has but can't land from here: go and land it.
            foreach (var def in unit.Actions)
                if (def != null && unit.Imprinted.Contains(def.Id) && def.Targeting == TargetType.Enemy && Ready(sim, unit, def) && !def.IsCharge)
                    return UnitCommand.UseActionWhenInRange(unit.Id, def, target.Id);
            // Nothing copied yet, or all of it cooling down: close to Imprint's reach of someone to copy.
            var imprint = ByBehavior(unit, "imprint");
            float d = Flat(unit.Position, target.Position), want = FightDistance(unit);
            if (unit.Imprinted.Count == 0 && imprint != null) want = Mathf.Min(want, unit.ActionStat(imprint, ActionStat.Range) * 0.8f);
            if (d > want + 1f) return Approach(sim, unit, target, want);
            if (!Relentless && d < want - 3f && want > 5f) return StepAway(sim, unit, target.Position, 3f);
            return UnitCommand.Idle(unit.Id, 0.3f);
        }
    }

    /// <summary>Syne: keeps its distance (Path To Victory's range lets it), hiding behind SEE BEFORE ITSELF.</summary>
    public class SyneAI : DeliverancePartAI { public const string Style = "syne"; public SyneAI() { PreferredRange = 13f; KeepDistance = 6f; } }

    /// <summary>Alio: slow and cheap to cast (Plight Upon Structure); while its strain is under half it Transcends into ghosts.</summary>
    public class AlioAI : DeliverancePartAI { public const string Style = "alio"; public AlioAI() { PreferredRange = 7f; TranscendBelow = 0.5f; } }

    /// <summary>Revl: extremely aggressive. It never rests or backs off; at max strain its EQUIPOSE goes off by itself.</summary>
    public class RevlAI : DeliverancePartAI { public const string Style = "revl"; public RevlAI() { PreferredRange = 2f; Relentless = true; } }

    /// <summary>Wex: stays with its allies, inside its Block aura's reach, and guards cheaply.</summary>
    public class WexAI : DeliverancePartAI { public const string Style = "wex"; public WexAI() { PreferredRange = 4f; StayNearAllies = 10f; BlockStrainMargin = 2f; } }
}
