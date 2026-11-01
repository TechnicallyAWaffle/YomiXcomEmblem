using System.Collections.Generic;
using UnityEngine;

namespace TacticsGame.Core
{
    /// <summary>
    /// AI styles by id. A style is a BasicEnemyAI subclass that keeps the shared tactics (reacting to incoming attacks,
    /// resting off strain, picking safe spots) and changes what the unit goes for, finding its abilities by role
    /// (AbilityRole), so it plays any kit that has them. A unit picks its style by its AIProfile string, or by its AI asset
    /// (AIProfileDefinition: a style plus tuned numbers). "auto" picks the style from the unit's abilities (StyleFor), so a
    /// new enemy needs no AI code. Styles keep no memory between decisions, so one instance serves every unit using it.
    /// </summary>
    public static class UnitAIs
    {
        /// <summary>The style that picks a style from the unit's abilities.</summary>
        public const string Auto = "auto";

        private static readonly Dictionary<string, System.Func<IUnitAI>> styles = new Dictionary<string, System.Func<IUnitAI>>
        {
            { "", () => new BasicEnemyAI() },
            { "sniper", () => new SniperAI() },
            { "assassin", () => new AssassinAI() },
            { "skirmisher", () => new SkirmisherAI() },
            { "healer", () => new HealerAI() },
            { "caster", () => new CasterAI() },
            { "brutebeast", () => new BrutebeastAI() },
        };

        private static readonly Dictionary<string, IUnitAI> shared = new Dictionary<string, IUnitAI>();

        /// <summary>The style ids there are (for editors), "" being the basic AI.</summary>
        public static IEnumerable<string> Styles { get { return styles.Keys; } }

        /// <summary>Add a style: 'make' builds one (an asset's tuning is applied to its own instance).</summary>
        public static void Register(string id, System.Func<IUnitAI> make) { styles[id] = make; shared.Remove(id); }
        public static void Register(string id, IUnitAI ai) { Register(id, () => ai); }

        /// <summary>A new instance of a style (null for an unknown one).</summary>
        public static IUnitAI Create(string id)
        {
            System.Func<IUnitAI> make;
            return styles.TryGetValue(id ?? "", out make) ? make() : null;
        }

        /// <summary>The shared instance of style 'id', or null (none, empty, or unknown).</summary>
        public static IUnitAI Get(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            IUnitAI ai;
            if (!shared.TryGetValue(id, out ai) && styles.ContainsKey(id)) shared[id] = ai = Create(id);
            return ai;
        }

        /// <summary>
        /// The style for a unit's kit (style "auto"), from its abilities' roles: a charged heal plays the healer; a snipe
        /// and a mark the sniper; a rush the brutebeast; a chain attack the skirmisher; an opener or a blink with a melee
        /// strike the assassin; a ranged unit with an area blast the caster; anything else the basic AI ("").
        /// </summary>
        public static string StyleFor(Unit unit)
        {
            if (AbilityRoles.Any(unit, AbilityRole.Heal, a => a.IsCharge)) return "healer";
            if (AbilityRoles.Any(unit, AbilityRole.Snipe) && AbilityRoles.Any(unit, AbilityRole.Mark)) return "sniper";
            if (AbilityRoles.Any(unit, AbilityRole.Rush)) return "brutebeast";
            if (AbilityRoles.Any(unit, AbilityRole.Chain, a => !a.IsRanged)) return "skirmisher";
            bool melee = AbilityRoles.Any(unit, AbilityRole.Strike, a => !a.IsRanged);
            if (AbilityRoles.Any(unit, AbilityRole.Opener) || (melee && AbilityRoles.Any(unit, AbilityRole.Gapcloser))) return "assassin";
            if (AbilityRoles.Any(unit, AbilityRole.AreaBlast) && unit.Actions.Exists(a => a != null && a.Targeting == TargetType.Enemy && a.IsRanged)) return "caster";
            return "";
        }
    }

    /// <summary>
    /// The default enemy AI: each unit is played by its AI asset (a style with tuned numbers), else its AIProfile style
    /// ("auto" picks one from its abilities), else BasicEnemyAI.
    /// </summary>
    public class AIRouter : IUnitAI
    {
        private readonly BasicEnemyAI fallback = new BasicEnemyAI();
        // One AI per asset and style ("auto" can pick differently for different kits), built once with the asset's numbers.
        private readonly Dictionary<KeyValuePair<AIProfileDefinition, string>, IUnitAI> tuned = new Dictionary<KeyValuePair<AIProfileDefinition, string>, IUnitAI>();

        public UnitCommand Decide(BattleSimulation sim, Unit unit)
        {
            return For(unit).Decide(sim, unit);
        }

        /// <summary>The AI playing 'unit'.</summary>
        public IUnitAI For(Unit unit)
        {
            var asset = unit.AI;
            if (asset != null)
            {
                string style = asset.Style == UnitAIs.Auto ? UnitAIs.StyleFor(unit) : asset.Style ?? "";
                var key = new KeyValuePair<AIProfileDefinition, string>(asset, style);
                IUnitAI ai;
                if (!tuned.TryGetValue(key, out ai))
                {
                    ai = UnitAIs.Create(style) ?? new BasicEnemyAI();
                    asset.ApplyTo(ai);
                    tuned[key] = ai;
                }
                return ai;
            }
            string id = unit.AIProfile == UnitAIs.Auto ? UnitAIs.StyleFor(unit) : unit.AIProfile;
            return UnitAIs.Get(id) ?? fallback;
        }
    }

    /// <summary>Helpers the profiles share on top of BasicEnemyAI's.</summary>
    public abstract class ProfileAI : BasicEnemyAI
    {
        /// <summary>The cases every profile hands to BasicEnemyAI: charging, holding someone, up in the air, no one to fight.</summary>
        protected bool Basic(BattleSimulation sim, Unit unit)
        {
            return unit.Phase == UnitPhase.Charging || unit.Grapple != null || unit.IsLifted || sim.NearestEnemy(unit) == null;
        }

        /// <summary>The unit's plain melee strike.</summary>
        protected static ActionDefinition MeleeStrike(Unit unit) { return Find(unit, AbilityRole.Strike, a => !a.IsRanged); }

        protected static bool Ready(BattleSimulation sim, Unit unit, ActionDefinition def)
        {
            string why;
            return def != null && sim.CanAfford(unit, def, out why);
        }

        protected static bool Reach(BattleSimulation sim, Unit unit, ActionDefinition def, Unit target)
        {
            string why;
            return def != null && target != null && sim.CanUse(unit, def, target, out why);
        }

        /// <summary>Where to stand to hit 'target' from 'unit's side, a body's width off it.</summary>
        protected static Vector3 Beside(Unit unit, Unit target)
        {
            Vector3 d = unit.Position - target.Position; d.y = 0f;
            d = d.sqrMagnitude > 1e-6f ? d.normalized : Vector3.forward;
            return target.Position + d * (unit.BodyRadius + target.BodyRadius + 0.3f);
        }

        /// <summary>The safest point a blink (or dash) of up to 'range' can reach: fewest threats, then furthest from enemies.
        /// False if nowhere beats staying put.</summary>
        protected bool SafeJump(BattleSimulation sim, Unit unit, float range, out Vector3 to)
        {
            to = unit.Position;
            float best = -3f * Danger(sim, unit, unit.Position) + 0.3f * Mathf.Min(15f, NearestEnemyDistance(sim, unit, unit.Position)) + 0.5f;
            bool found = false;
            for (int r = 1; r <= 2; r++)
                for (int i = 0; i < 16; i++)
                {
                    float a = i * Mathf.PI / 8f + (r == 2 ? Mathf.PI / 16f : 0f);
                    Vector3 p = sim.WalkablePointNear(unit.Position + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * range * (r == 1 ? 1f : 0.6f));
                    float s = -3f * Danger(sim, unit, p) + 0.3f * Mathf.Min(15f, NearestEnemyDistance(sim, unit, p));
                    if (s > best) { best = s; to = p; found = true; }
                }
            return found;
        }

        protected float StrainFraction(Unit unit) { return unit.Strain / Mathf.Max(1f, unit.MaxStrain); }
    }

    /// <summary>
    /// Sniper: keeps its distance (backs off brawlers to a spot that still has a shot), marks a target with its Marking
    /// Shot, then snipes marked targets. It never snipes an unmarked target; with no mark up it takes plain rifle shots.
    /// </summary>
    public class SniperAI : ProfileAI
    {
        public SniperAI() { KeepDistance = 9f; }

        private static ActionDefinition Marker(Unit unit) { return Find(unit, AbilityRole.Mark); }

        public override UnitCommand Decide(BattleSimulation sim, Unit unit)
        {
            if (Basic(sim, unit)) return base.Decide(sim, unit);
            var r = React(sim, unit) ?? Rest(sim, unit);
            if (r != null) return r;

            float threat = NearestMeleeThreat(sim, unit, unit.Position);
            Vector3 spot;
            if (threat < KeepDistance && !unit.IsRooted && BrawlerPressing(sim, unit) &&
                BestSpot(sim, unit, p => RangedSpotScore(sim, unit, p), out spot) && NearestMeleeThreat(sim, unit, spot) > threat + 1f)
                return UnitCommand.MoveTo(unit.Id, spot);

            var snipe = Find(unit, AbilityRole.Snipe);
            var mark = Marker(unit);
            // 1. Snipe a marked target: the likeliest hit, finishing hurt ones first.
            Unit best = null;
            float bestScore = float.MinValue;
            if (Ready(sim, unit, snipe))
                foreach (var e in sim.EnemiesOf(unit))
                {
                    if (!e.HasStatus(StatusIds.Marked) || !Reach(sim, unit, snipe, e)) continue;
                    float s = HitChance(sim, unit, snipe, e) + 0.5f * (1f - e.Health / Mathf.Max(1f, e.MaxHealth));
                    if (s > bestScore) { bestScore = s; best = e; }
                }
            if (best != null) return UnitCommand.UseAction(unit.Id, snipe, best.Id);

            // 2. Mark the target most worth sniping: one the snipe can reach, likely to be hit, hurt.
            if (Ready(sim, unit, mark))
                foreach (var e in sim.EnemiesOf(unit))
                {
                    if (e.HasStatus(StatusIds.Marked) || !Reach(sim, unit, mark, e)) continue;
                    float s = HitChance(sim, unit, mark, e) + (snipe != null && Reach(sim, unit, snipe, e) ? 0.5f : 0f) + 0.3f * (1f - e.Health / Mathf.Max(1f, e.MaxHealth));
                    if (s > bestScore) { bestScore = s; best = e; }
                }
            if (best != null) return UnitCommand.UseAction(unit.Id, mark, best.Id);

            // 3. Otherwise a plain shot (never an unmarked snipe).
            ActionDefinition shot; Unit target;
            BestShot(sim, unit, unit.Position, out shot, out target, d => d != snipe && d != mark);
            if (shot != null) return UnitCommand.UseAction(unit.Id, shot, target.Id);

            // 4. Nothing to shoot. With the mark ready but nobody in its reach, close in until someone is (else it would wait
            //    forever on targets only the snipe reaches). Otherwise hold if someone is in sight (cooling down), or find a spot.
            if (Ready(sim, unit, mark) && !unit.IsRooted)
            {
                if (BestSpot(sim, unit, p => MarkSpotScore(sim, unit, mark, p), out spot)) return UnitCommand.MoveTo(unit.Id, spot);
                var near = sim.NearestEnemy(unit);
                return UnitCommand.MoveTo(unit.Id, sim.ApproachSpot(unit, near, unit.ActionStat(mark, ActionStat.Range) * 0.85f));
            }
            if (AnyTargetInSight(sim, unit, unit.Position) || unit.IsRooted) return UnitCommand.Idle(unit.Id, 0.4f);
            if (BestSpot(sim, unit, p => RangedSpotScore(sim, unit, p), out spot)) return UnitCommand.MoveTo(unit.Id, spot);
            var nearest = sim.NearestEnemy(unit);
            return UnitCommand.MoveTo(unit.Id, sim.ApproachSpot(unit, nearest, Mathf.Max(KeepDistance, MaxRange(unit) * 0.6f)));
        }

        /// <summary>A spot to mark from: someone in the mark's reach and sight scores high, and brawlers stay far.</summary>
        private float MarkSpotScore(BattleSimulation sim, Unit unit, ActionDefinition mark, Vector3 p)
        {
            float score = Mathf.Min(NearestMeleeThreat(sim, unit, p), KeepDistance * 1.5f);
            var ghost = Ghost(unit, p);
            foreach (var e in sim.EnemiesOf(unit))
                if (Reach(sim, ghost, mark, e)) { score += 8f; break; }
            return score;
        }
    }

    /// <summary>
    /// Assassin: hunts, going for ranged units first. It spends strain freely to close in (Dash, Polystep beside the target
    /// when it is beyond a dash), opens with Hidden Strike when it can, then Strikes. At high strain it breaks off: a Polystep
    /// to the safest spot it can reach if it is threatened, then rests (Focus) like everyone.
    /// </summary>
    public class AssassinAI : ProfileAI
    {
        /// <summary>Metres of extra distance a ranged target is worth chasing over a melee one.</summary>
        public float RangedTargetBonus = 12f;

        public override UnitCommand Decide(BattleSimulation sim, Unit unit)
        {
            if (Basic(sim, unit)) return base.Decide(sim, unit);
            var react = React(sim, unit);
            if (react != null) return react;

            var escape = Find(unit, AbilityRole.Escape);
            var blink = Find(unit, AbilityRole.Gapcloser);
            var hidden = Find(unit, AbilityRole.Opener);
            var strike = MeleeStrike(unit);
            var dash = Find(unit, AbilityRole.Dash);

            float strain = StrainFraction(unit);
            if (!unit.HasStatus(StatusIds.LastStand) && (strain >= HighStrain || (unit.FocusSince >= 0f && strain > RestedStrain)))
            {
                Vector3 away;
                if (Danger(sim, unit, unit.Position) > 0 && !unit.IsRooted && Ready(sim, unit, escape) && SafeJump(sim, unit, unit.ActionStat(escape, ActionStat.Range), out away) &&
                    !StandsGround(sim, unit))
                    return UnitCommand.UseActionAt(unit.Id, escape, away);
                var rest = Rest(sim, unit);
                if (rest != null) return rest;   // nowhere safe and someone is on it: fight on
            }

            var prey = Prey(sim, unit);
            if (prey == null || strike == null) return UnitCommand.Idle(unit.Id, 0.4f);
            if (Ready(sim, unit, hidden) && Reach(sim, unit, hidden, prey)) return UnitCommand.UseAction(unit.Id, hidden, prey.Id);
            if (Reach(sim, unit, strike, prey)) return UnitCommand.UseAction(unit.Id, strike, prey.Id);
            if (!unit.IsRooted)
            {
                float d = Flat(unit.Position, prey.Position);
                float dashReach = dash != null ? sim.DashReach(unit) : 0f;
                if (Ready(sim, unit, blink) && d > dashReach + unit.ActionStat(strike, ActionStat.Range) && d <= unit.ActionStat(blink, ActionStat.Range) + unit.ActionStat(strike, ActionStat.Range))
                    return UnitCommand.UseActionAt(unit.Id, blink, Beside(unit, prey));
                if (dash != null && d > unit.ActionStat(strike, ActionStat.Range) + 0.5f && Affordable(sim, unit, dash, false))
                    return UnitCommand.UseActionAt(unit.Id, dash, Beside(unit, prey));
            }
            return UnitCommand.UseActionWhenInRange(unit.Id, strike, prey.Id);
        }

        /// <summary>The enemy to hunt: the nearest, with ranged ones counted RangedTargetBonus closer and hurt ones a little.</summary>
        private Unit Prey(BattleSimulation sim, Unit unit)
        {
            Unit best = null;
            float bestScore = float.MinValue;
            foreach (var e in sim.EnemiesOf(unit))
            {
                float s = -Flat(unit.Position, e.Position) + (IsRangedUnit(e) ? RangedTargetBonus : 0f) + 4f * (1f - e.Health / Mathf.Max(1f, e.MaxHealth));
                if (s > bestScore) { bestScore = s; best = e; }
            }
            return best;
        }
    }

    /// <summary>
    /// Skirmisher: dives straight at the nearest enemy and keeps Flowing Death going (each one faster than the last), with
    /// Strike as the fallback. An attack winding up on it is met with Mental Currents (timed so the dodge is up when it
    /// lands), or Block/Dash as usual. At high strain it backs off to rest.
    /// </summary>
    public class SkirmisherAI : ProfileAI
    {
        private static ActionDefinition DodgeBuff(Unit unit) { return Find(unit, AbilityRole.DodgeBuff); }

        public override UnitCommand Decide(BattleSimulation sim, Unit unit)
        {
            if (Basic(sim, unit)) return base.Decide(sim, unit);
            var incoming = IncomingAttack(sim, unit);
            var currents = DodgeBuff(unit);
            if (incoming != null && Ready(sim, unit, currents))
            {
                float lasts = 0f;
                foreach (var s in currents.SelfStatuses) lasts = Mathf.Max(lasts, s.Duration);
                float lands = incoming.ResolveAt - sim.Now, takesHold = sim.WindupOf(unit, currents);
                if (lands >= takesHold && lands <= lasts * 0.9f) return UnitCommand.UseAction(unit.Id, currents, unit.Id);
            }
            var r = React(sim, unit) ?? Rest(sim, unit);
            if (r != null) return r;

            var target = sim.NearestEnemy(unit);
            var flow = Find(unit, AbilityRole.Chain);
            var strike = MeleeStrike(unit);
            foreach (var def in new[] { flow, strike })
            {
                if (!Ready(sim, unit, def)) continue;
                if (Reach(sim, unit, def, target)) return UnitCommand.UseAction(unit.Id, def, target.Id);
                if (!unit.IsRooted) return UnitCommand.UseActionWhenInRange(unit.Id, def, target.Id);   // straight in, cover or not
            }
            return UnitCommand.Idle(unit.Id, 0.3f);
        }
    }

    /// <summary>
    /// Healer: keeps near its allies and away from enemies. Someone hurt in reach: it charges Deluge of Creation to full and
    /// heals whoever is worst off, releasing early only when in danger (a brawler close by, or an attack winding up on it).
    /// Pressed, it Repels the nearest enemy or runs. At high strain it retreats to rest.
    /// </summary>
    public class HealerAI : ProfileAI
    {
        /// <summary>A melee enemy this close (or an attack winding up on it) is danger.</summary>
        public float DangerDistance = 4.5f;
        /// <summary>Allies under this share of their health are worth a heal.</summary>
        public float HealBelow = 0.85f;
        /// <summary>It tries to stay within this many metres of an ally.</summary>
        public float StayNearAllies = 9f;

        public HealerAI() { KeepDistance = 7f; }

        private bool InDanger(BattleSimulation sim, Unit unit)
        {
            return NearestMeleeThreat(sim, unit, unit.Position) < DangerDistance || IncomingAttack(sim, unit) != null;
        }

        protected override UnitCommand DecideCharge(BattleSimulation sim, Unit unit, Unit target)
        {
            var def = unit.ActiveAction != null ? unit.ActiveAction.Definition : null;
            if (def == null || def.Targeting != TargetType.Ally) return base.DecideCharge(sim, unit, target);
            bool danger = InDanger(sim, unit);
            bool full = sim.ChargeLevel(unit) >= 1f - 1e-3f;
            if (!full && !danger) return UnitCommand.ContinueCharge(unit.Id, 0.25f);
            // Whoever is worst off; at full charge with nobody hurt any more, the lowest anyway (the charge must go somewhere).
            var heal = MostHurtAlly(sim, unit, def) ?? MostHurtAlly(sim, unit, def, 1.01f);
            return heal != null ? UnitCommand.ReleaseCharge(unit.Id, heal.Id, heal.Position) : UnitCommand.ContinueCharge(unit.Id, 0.25f);
        }

        public override UnitCommand Decide(BattleSimulation sim, Unit unit)
        {
            if (Basic(sim, unit)) return base.Decide(sim, unit);
            var r = React(sim, unit) ?? Rest(sim, unit);
            if (r != null) return r;

            var repel = Find(unit, AbilityRole.Peel);
            var deluge = Find(unit, AbilityRole.Heal, a => a.IsCharge);   // charged to full, aimed when released
            var close = sim.NearestEnemy(unit);
            bool danger = InDanger(sim, unit);
            Vector3 spot;

            // Pressed: push them off, or get away (somewhere still near the team).
            if (danger)
            {
                if (Ready(sim, unit, repel) && Flat(unit.Position, close.Position) <= unit.ActionStat(repel, ActionStat.Range)) return UnitCommand.UseActionAt(unit.Id, repel, close.Position);
                if (!unit.IsRooted && !unit.HasStatus(StatusIds.LastStand) && BestSpot(sim, unit, p => SupportSpotScore(sim, unit, p, null), out spot) &&
                    !StandsGround(sim, unit))
                    return UnitCommand.MoveTo(unit.Id, spot);
            }

            // Someone hurt in reach: charge the heal (it picks the target when it is released).
            if (!danger && Ready(sim, unit, deluge) && MostHurtAlly(sim, unit, deluge, HealBelow) != null) return UnitCommand.UseAction(unit.Id, deluge, unit.Id);

            // Someone hurt out of reach: go to them, staying clear of enemies.
            var hurt = WorstAlly(sim, unit, HealBelow);
            if (hurt != null && deluge != null && !unit.IsRooted && Flat(unit.Position, hurt.Position) > unit.ActionStat(deluge, ActionStat.Range) * 0.8f &&
                BestSpot(sim, unit, p => SupportSpotScore(sim, unit, p, hurt), out spot))
                return UnitCommand.MoveTo(unit.Id, spot);

            // Stay with the team.
            var ally = NearestAlly(sim, unit);
            if (ally != null && !unit.IsRooted && Flat(unit.Position, ally.Position) > StayNearAllies &&
                BestSpot(sim, unit, p => SupportSpotScore(sim, unit, p, ally), out spot))
                return UnitCommand.MoveTo(unit.Id, spot);

            // Nothing to mend: Repel whoever is in its line. The last one standing has no one to stay with, and one making a
            // last stand won't back off: they go after them.
            if (Ready(sim, unit, repel) && Flat(unit.Position, close.Position) <= unit.ActionStat(repel, ActionStat.Range)) return UnitCommand.UseActionAt(unit.Id, repel, close.Position);
            if ((ally == null || unit.HasStatus(StatusIds.LastStand)) && repel != null && !unit.IsRooted && Flat(unit.Position, close.Position) > unit.ActionStat(repel, ActionStat.Range) * 0.7f)
                return UnitCommand.MoveTo(unit.Id, sim.ApproachSpot(unit, close, unit.ActionStat(repel, ActionStat.Range) * 0.7f));
            return UnitCommand.Idle(unit.Id, 0.4f);
        }

        /// <summary>A spot for the healer: safe (few threats, away from brawlers) and, if given, near 'ally'.</summary>
        private float SupportSpotScore(BattleSimulation sim, Unit unit, Vector3 p, Unit ally)
        {
            float s = -3f * Danger(sim, unit, p) + 0.3f * Mathf.Min(KeepDistance * 1.5f, NearestMeleeThreat(sim, unit, p));
            if (ally != null) s -= 0.8f * Mathf.Max(0f, Flat(p, ally.Position) - StayNearAllies * 0.6f);
            return s;
        }

        private static Unit WorstAlly(BattleSimulation sim, Unit unit, float below)
        {
            Unit best = null;
            float worst = below;
            foreach (var u in sim.State.Units)
            {
                if (!u.IsAlive || u.Team != unit.Team) continue;
                float frac = u.Health / Mathf.Max(1f, u.MaxHealth);
                if (frac < worst) { worst = frac; best = u; }
            }
            return best;
        }

        private static Unit NearestAlly(BattleSimulation sim, Unit unit)
        {
            Unit best = null;
            float d = float.MaxValue;
            foreach (var u in sim.State.Units)
            {
                if (!u.IsAlive || u.Team != unit.Team || u == unit) continue;
                float dd = Flat(unit.Position, u.Position);
                if (dd < d) { d = dd; best = u; }
            }
            return best;
        }
    }

    /// <summary>
    /// Caster: a shooter (BasicEnemyAI's ranged play, with Kinetic Blast) that drops Distort Space where it will catch the
    /// most enemies. Someone who can't step out before it detonates (stunned, winding up, recovering, charging or focusing
    /// past that moment) counts in full, anyone else for half: it casts for one such target or two of the others.
    /// </summary>
    public class CasterAI : ProfileAI
    {
        public override UnitCommand Decide(BattleSimulation sim, Unit unit)
        {
            if (Basic(sim, unit)) return base.Decide(sim, unit);
            var r = React(sim, unit) ?? Rest(sim, unit);
            if (r != null) return r;
            var blast = Find(unit, AbilityRole.AreaBlast);
            bool pressed = NearestMeleeThreat(sim, unit, unit.Position) < KeepDistance && BrawlerPressing(sim, unit);
            Vector3 at;
            if (!pressed && Ready(sim, unit, blast) && BestBlast(sim, unit, blast, out at)) return UnitCommand.UseActionAt(unit.Id, blast, at);
            return DecideRanged(sim, unit);
        }

        /// <summary>The enemy position whose circle scores best (see the class summary); false if none scores a full target.</summary>
        private static bool BestBlast(BattleSimulation sim, Unit unit, ActionDefinition def, out Vector3 at)
        {
            at = unit.Position;
            float best = 0.99f, lands = sim.Now + sim.WindupOf(unit, def);
            foreach (var e in sim.EnemiesOf(unit))
            {
                if (Vector3.Distance(unit.Position, e.Position) > unit.ActionStat(def, ActionStat.Range)) continue;
                float score = 0f;
                foreach (var o in sim.EnemiesOf(unit))
                    if (Flat(o.Position, e.Position) <= unit.ActionStat(def, ActionStat.AreaRadius) + o.BodyRadius * 0.5f) score += StaysPut(o, lands) ? 1f : 0.5f;
                if (score > best) { best = score; at = e.Position; }
            }
            return best > 0.99f;
        }

        private static bool StaysPut(Unit e, float until)
        {
            if (e.FocusSince >= 0f || e.Phase == UnitPhase.Charging) return true;
            return (e.Phase == UnitPhase.Stunned || e.Phase == UnitPhase.Windup || e.Phase == UnitPhase.Recovery) && e.PhaseEndsAt >= until;
        }
    }

    /// <summary>
    /// Brutebeast: a brawler (BasicEnemyAI's melee play, with Ravage). It Charges when the rush would pin someone against a
    /// wall (the most caught wins), and takes Ravage's follow-up only when it would be a combo: the target it just hit
    /// still stunned when the follow-up lands. Otherwise the follow-up is left alone.
    /// </summary>
    public class BrutebeastAI : ProfileAI
    {
        public override UnitCommand Decide(BattleSimulation sim, Unit unit)
        {
            if (Basic(sim, unit)) return base.Decide(sim, unit);
            var r = React(sim, unit) ?? Rest(sim, unit);
            if (r != null) return r;

            var follow = Find(unit, AbilityRole.Combo);
            var last = sim.GetUnit(unit.LastActionTargetId);
            if (follow != null && last != null && last.IsAlive && last.Team != unit.Team && Ready(sim, unit, follow) &&
                Reach(sim, unit, follow, last) && LandsInStun(sim, unit, follow, last))
                return UnitCommand.UseAction(unit.Id, follow, last.Id);

            var charge = Find(unit, AbilityRole.Rush);
            Vector3 aim;
            if (!unit.IsRooted && Ready(sim, unit, charge) && PinCharge(sim, unit, charge, out aim)) return UnitCommand.UseActionAt(unit.Id, charge, aim);
            return DecideMelee(sim, unit, a => a != follow);
        }

        /// <summary>A point to Charge at that slams someone into a wall: toward each enemy in reach, the one catching the most.</summary>
        private static bool PinCharge(BattleSimulation sim, Unit unit, ActionDefinition charge, out Vector3 aim)
        {
            aim = unit.Position;
            int best = 0;
            foreach (var e in sim.EnemiesOf(unit))
            {
                Vector3 d = e.Position - unit.Position; d.y = 0f;
                float dist = d.magnitude;
                if (dist < 0.1f || dist > unit.ActionStat(charge, ActionStat.Range)) continue;
                Vector3 at = unit.Position + d / dist * Mathf.Min(dist, unit.ActionStat(charge, ActionStat.Range) - 0.2f);
                var plan = ChargeBehavior.PlanCharge(sim, unit, charge, at);
                if (plan.Walled && plan.Caught.Count > best) { best = plan.Caught.Count; aim = at; }
            }
            return best > 0;
        }
    }
}
