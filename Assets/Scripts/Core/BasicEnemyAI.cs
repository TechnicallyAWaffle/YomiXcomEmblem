using System.Collections.Generic;
using UnityEngine;

namespace TacticsGame.Core
{
    /// <summary>
    /// Enemy AI with a few general tactical rules, decided fresh each time a unit is ready (it keeps no memory):
    ///   Everyone:  an attack winding up on the unit is met with Block (timed so the guard covers the hit; Transcended
    ///              against a heavy blow when strain allows), or, for a ranged unit facing a melee swing, a Dash out of
    ///              its reach (Transcended if a plain dash wouldn't make it).
    ///              Strain at HighStrain or more: get somewhere safe (out of shooters' sight, away from brawlers) and
    ///              Focus until strain is back under RestedStrain.
    ///   Ranged:    keeps KeepDistance from melee enemies (backs off to a spot that still has a shot), otherwise takes
    ///              the shot with the best expected damage, or moves to where it can see someone.
    ///   Melee:     prefers targets it can combo (still stunned when the hit lands), then near and hurt ones; closes in
    ///              through spots out of sight of enemy shooters, short hops at a time, unless the target is close or a
    ///              combo is on (then it chases straight in).
    /// The sim wakes waiting enemies when something starts winding up on them (BattleSimulation.AlertTarget), so the
    /// reactions get their chance.
    /// </summary>
    public class BasicEnemyAI : IUnitAI
    {
        public const float ReleaseAtCharge = 0.6f;

        /// <summary>Ranged units back off when a melee enemy gets this close.</summary>
        public float KeepDistance = 6f;
        /// <summary>Melee units chase straight in once this close to their reach, cover or not.</summary>
        public float DirectChaseGap = 3f;
        /// <summary>How far a single repositioning hop goes.</summary>
        public float HopDistance = 4.5f;
        /// <summary>Melee path scoring: meters of progress one enemy shooter's line of sight is worth giving up.</summary>
        public float ExposurePenalty = 2.5f;
        /// <summary>Guard only while strain stays at least this far below max (a heavy hit at max strain unravels).</summary>
        public float BlockStrainMargin = 5f;
        /// <summary>Strain (fraction of max) at which a unit breaks off to Focus somewhere safe, and where it stops.</summary>
        public float HighStrain = 0.65f, RestedStrain = 0.2f;
        /// <summary>A melee enemy this close makes a spot unsafe to Focus in, seen or not.</summary>
        public float BrawlerDangerDistance = 6f;

        private static readonly List<Vector3> scratch = new List<Vector3>();

        public UnitCommand Decide(BattleSimulation sim, Unit unit)
        {
            if (unit.Phase == UnitPhase.Charging) return DecideCharge(sim, unit, sim.NearestEnemy(unit));
            if (unit.Grapple != null) return UnitCommand.HoldGrapple(unit.Id);   // headbutt until the stun runs out, then throw
            if (sim.NearestEnemy(unit) == null) return UnitCommand.Wait(unit.Id, 1f);
            return React(sim, unit) ?? Rest(sim, unit) ?? (IsRangedUnit(unit) ? DecideRanged(sim, unit) : DecideMelee(sim, unit));
        }

        // ------------------------------------------------------------------ focus (resting off strain)

        /// <summary>
        /// High strain: Focus (Wait) where it is if that is safe, else move somewhere safer first. A unit already focusing
        /// keeps at it until strain is down to RestedStrain (its run of focus is the only memory it needs). Null = fight on.
        /// </summary>
        private UnitCommand Rest(BattleSimulation sim, Unit unit)
        {
            float strain = unit.Strain / Mathf.Max(1f, unit.MaxStrain);
            bool focusing = unit.FocusSince >= 0f;
            if (strain < HighStrain && !(focusing && strain > RestedStrain)) return null;
            int here = Danger(sim, unit, unit.Position);
            if (here == 0) return UnitCommand.Wait(unit.Id, 0.5f);
            Vector3 spot;
            if (!unit.IsRooted && BestSpot(sim, unit, p => -3f * Danger(sim, unit, p) + 0.25f * Mathf.Min(12f, NearestEnemyDistance(sim, unit, p)), out spot) &&
                Danger(sim, unit, spot) < here)
                return UnitCommand.MoveTo(unit.Id, spot);
            // Nowhere better within a hop: focus anyway unless a brawler is right there.
            return NearestMeleeThreat(sim, unit, unit.Position) > BrawlerDangerDistance ? UnitCommand.Wait(unit.Id, 0.5f) : null;
        }

        /// <summary>How many enemies threaten 'p': shooters that could see and reach it, brawlers close by.</summary>
        private int Danger(BattleSimulation sim, Unit unit, Vector3 p)
        {
            var ghost = Ghost(unit, p);
            int n = 0;
            foreach (var e in sim.EnemiesOf(unit))
            {
                float d = Flat(p, e.Position);
                if (IsRangedUnit(e)) { if (d <= MaxRange(e) * 1.1f && sim.Visibility.HasLineOfSight(e, ghost)) n++; }
                else if (d < BrawlerDangerDistance) n++;
            }
            return n;
        }

        private static float NearestEnemyDistance(BattleSimulation sim, Unit unit, Vector3 p)
        {
            float d = float.MaxValue;
            foreach (var e in sim.EnemiesOf(unit)) d = Mathf.Min(d, Flat(p, e.Position));
            return d;
        }

        /// <summary>Charging: build up to ReleaseAtCharge (or the charge's max), then let fly at the nearest enemy.</summary>
        private static UnitCommand DecideCharge(BattleSimulation sim, Unit unit, Unit target)
        {
            if (target == null || sim.ChargeLevel(unit) < ReleaseAtCharge) return UnitCommand.ContinueCharge(unit.Id, 0.25f);
            return UnitCommand.ReleaseCharge(unit.Id, target.Id, target.Position);
        }

        // ------------------------------------------------------------------ reactions

        /// <summary>The attack winding up on this unit that lands first, or null.</summary>
        public static ActionInstance IncomingAttack(BattleSimulation sim, Unit unit)
        {
            ActionInstance first = null;
            foreach (var u in sim.State.Units)
            {
                if (!u.IsAlive || u.Team == unit.Team || u.Phase != UnitPhase.Windup || u.ActiveAction == null) continue;
                var a = u.ActiveAction;
                if (a.TargetUnitId != unit.Id || a.Definition.Targeting != TargetType.Enemy || a.Definition.Damage <= 0f) continue;
                if (first == null || a.ResolveAt < first.ResolveAt) first = a;
            }
            return first;
        }

        private UnitCommand React(BattleSimulation sim, Unit unit)
        {
            var incoming = IncomingAttack(sim, unit);
            if (incoming == null) return null;
            string why;

            // A shooter facing a melee swing: dash out of its reach, Transcended if a plain dash falls short.
            var dash = Find(unit, "dash");
            float reach = incoming.Definition.Range * BattleSimulation.RangeTolerance;
            if (dash != null && IsRangedUnit(unit) && !incoming.Definition.IsRanged && !unit.IsRooted)
            {
                Vector3 to;
                if (sim.CanAfford(unit, dash, out why) && EscapeDash(sim, unit, incoming.Actor, reach, false, out to))
                    return UnitCommand.UseActionAt(unit.Id, dash, to);
                if (Affordable(sim, unit, dash, true) && EscapeDash(sim, unit, incoming.Actor, reach, true, out to))
                    return UnitCommand.UseActionAt(unit.Id, dash, to).Transcended();
            }

            // Otherwise guard, timed so the block is still up when the hit lands. A heavy blow (which would put a plain
            // Block on cooldown, or unravel at max strain) gets a Transcended guard when the strain allows.
            var block = Find(unit, "block");
            if (block != null && Affordable(sim, unit, block, false))
            {
                float guard = GuardSeconds(block);
                float until = incoming.ResolveAt - sim.Now;
                if (until > guard * 0.85f) return UnitCommand.Wait(unit.Id, Mathf.Max(BattleSimulation.TickLength, until - guard * 0.6f));
                bool transcend = incoming.Definition.Impact == ImpactType.Heavy && Affordable(sim, unit, block, true);
                return UnitCommand.UseAction(unit.Id, block, unit.Id).Transcended(transcend);
            }
            return null;
        }

        /// <summary>Usable now, and leaves strain BlockStrainMargin under max (Block may overflow; the AI won't).</summary>
        private bool Affordable(BattleSimulation sim, Unit unit, ActionDefinition def, bool transcend)
        {
            string why;
            if (transcend && !def.CanTranscend) return false;
            return sim.CanAfford(unit, def, out why, null, transcend) &&
                   unit.Strain + sim.StrainCostOf(unit, def, null, transcend) <= unit.MaxStrain - BlockStrainMargin;
        }

        private static float GuardSeconds(ActionDefinition block)
        {
            float d = 0f;
            foreach (var s in block.SelfStatuses) d = Mathf.Max(d, s.Duration);
            return d > 0f ? d : Mathf.Max(0.3f, block.Recovery);
        }

        /// <summary>The dash landing (of 16 directions) furthest from the attacker; true if it ends out of 'reach'.</summary>
        private static bool EscapeDash(BattleSimulation sim, Unit unit, Unit attacker, float reach, bool transcended, out Vector3 to)
        {
            to = unit.Position;
            float best = Flat(unit.Position, attacker.Position);
            for (int i = 0; i < 16; i++)
            {
                float a = i * Mathf.PI / 8f;
                Vector3 p = sim.DashDestination(unit, unit.Position + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 100f, transcended);
                float d = Flat(p, attacker.Position) - 0.4f * MeleeThreats(sim, unit, p, attacker);   // not into another brawler
                if (d > best) { best = d; to = p; }
            }
            return Flat(to, attacker.Position) > reach + 0.2f;
        }

        // ------------------------------------------------------------------ ranged

        private UnitCommand DecideRanged(BattleSimulation sim, Unit unit)
        {
            ActionDefinition shot; Unit target;
            BestShot(sim, unit, unit.Position, out shot, out target);
            float threat = NearestMeleeThreat(sim, unit, unit.Position);

            // Too close to a brawler that is free to come at us: back off, ideally to somewhere that still has a shot.
            // One that is stunned or busy swinging at someone else can be shot instead (else a faster brawler is
            // simply chased around forever).
            if (threat < KeepDistance && !unit.IsRooted && (shot == null || BrawlerPressing(sim, unit)))
            {
                Vector3 spot;
                if (BestSpot(sim, unit, p => RangedSpotScore(sim, unit, p), out spot) &&
                    NearestMeleeThreat(sim, unit, spot) > threat + 1f)
                    return UnitCommand.MoveTo(unit.Id, spot);
            }
            if (shot != null) return UnitCommand.UseAction(unit.Id, shot, target.Id);

            // No shot right now. Cooling down with someone in sight: hold. Otherwise find a firing position.
            if (AnyTargetInSight(sim, unit, unit.Position) || unit.IsRooted) return UnitCommand.Wait(unit.Id, 0.4f);
            Vector3 move;
            if (BestSpot(sim, unit, p => RangedSpotScore(sim, unit, p), out move)) return UnitCommand.MoveTo(unit.Id, move);
            var nearest = sim.NearestEnemy(unit);
            return UnitCommand.MoveTo(unit.Id, sim.ApproachSpot(unit, nearest, Mathf.Max(KeepDistance, MaxRange(unit) * 0.7f)));
        }

        /// <summary>The usable shot with the best expected damage (damage x chance to land) from 'from'. 0 if none.</summary>
        private static float BestShot(BattleSimulation sim, Unit unit, Vector3 from, out ActionDefinition shot, out Unit target)
        {
            shot = null; target = null;
            float best = 0f;
            var shooter = Ghost(unit, from);
            foreach (var def in unit.Actions)
            {
                if (def.Targeting != TargetType.Enemy) continue;
                foreach (var e in sim.EnemiesOf(unit))
                {
                    string why;
                    if (!sim.CanUse(shooter, def, e, out why)) continue;
                    float v = Mathf.Max(1f, def.Damage) * (0.1f + HitChance(sim, shooter, def, e));   // a long shot still beats no shot
                    if (v > best) { best = v; shot = def; target = e; }
                }
            }
            return best;
        }

        private static float HitChance(BattleSimulation sim, Unit attacker, ActionDefinition def, Unit target)
        {
            float exposure = def.IsRanged && !def.PiercesTerrain ? sim.Visibility.GetExposure(attacker, target, false) : 1f;
            float bonus = sim.HeightAccuracyBonus(attacker, target, def);
            foreach (var s in target.Statuses) bonus += s.IncomingAccuracyBonus;
            float dodge = target.Phase == UnitPhase.Stunned ? 0f : target.UnstunnedDodge();
            return Mathf.Clamp01(def.BaseAccuracy * exposure + bonus) * (1f - dodge);
        }

        /// <summary>A firing position: away from brawlers, with someone in range and sight (better from high ground).</summary>
        private float RangedSpotScore(BattleSimulation sim, Unit unit, Vector3 p)
        {
            float score = Mathf.Min(NearestMeleeThreat(sim, unit, p), KeepDistance * 1.5f);
            var ghost = Ghost(unit, p);
            float range = MaxRange(unit);
            foreach (var e in sim.EnemiesOf(unit))
            {
                if (Flat(p, e.Position) > range || !sim.Visibility.HasLineOfSight(ghost, e)) continue;
                score += 6f + (sim.HasHighGround(ghost, e) ? 2f : 0f);
                break;
            }
            return score;
        }

        private static bool AnyTargetInSight(BattleSimulation sim, Unit unit, Vector3 from)
        {
            var ghost = Ghost(unit, from);
            float range = MaxRange(unit);
            foreach (var e in sim.EnemiesOf(unit))
                if (Flat(from, e.Position) <= range && sim.Visibility.HasLineOfSight(ghost, e)) return true;
            return false;
        }

        // ------------------------------------------------------------------ melee

        private UnitCommand DecideMelee(BattleSimulation sim, Unit unit)
        {
            ActionDefinition def; Unit target; bool combo;
            if (!PickMeleeTarget(sim, unit, out def, out target, out combo))
            {
                // Nothing affordable yet: wait it out, out of the shooters' sight if possible.
                Vector3 hide;
                if (!unit.IsRooted && Exposure(sim, unit, unit.Position) > 0 &&
                    BestSpot(sim, unit, p => -ExposurePenalty * Exposure(sim, unit, p) - 0.2f * Flat(p, sim.NearestEnemy(unit).Position), out hide))
                    return UnitCommand.MoveTo(unit.Id, hide);
                return UnitCommand.Wait(unit.Id, 0.4f);
            }

            string why;
            if (sim.CanUse(unit, def, target, out why)) return UnitCommand.UseAction(unit.Id, def, target.Id);
            if (why != "out of range" && why != "no line of sight") return UnitCommand.Wait(unit.Id, 0.3f);
            if (unit.IsRooted) return UnitCommand.Wait(unit.Id, 0.3f);

            // Close, a combo on, or nobody to hide from: straight in (the sim chases and strikes once in reach).
            float gap = Flat(unit.Position, target.Position) - def.Range;
            if (combo || gap <= DirectChaseGap || !AnyEnemyShooters(sim, unit))
                return UnitCommand.UseActionWhenInRange(unit.Id, def, target.Id);

            // Otherwise a hop toward it, through spots the enemy shooters can't see.
            float now = Flat(unit.Position, target.Position);
            Vector3 spot;
            if (BestSpot(sim, unit, p => (now - Flat(p, target.Position)) - ExposurePenalty * Exposure(sim, unit, p), out spot) &&
                Flat(spot, target.Position) < now - 0.5f)
                return UnitCommand.MoveTo(unit.Id, spot);
            return UnitCommand.UseActionWhenInRange(unit.Id, def, target.Id);
        }

        /// <summary>
        /// Target and ability: a combo (target still stunned when the hit would land, travel included) beats everything,
        /// then the nearest, a little in favour of hurt targets. The ability is the unit's first affordable one, or for a
        /// combo the hardest-hitting one that still lands inside the stun.
        /// </summary>
        private static bool PickMeleeTarget(BattleSimulation sim, Unit unit, out ActionDefinition def, out Unit target, out bool combo)
        {
            def = null; target = null; combo = false;
            float best = float.MinValue;
            foreach (var e in sim.EnemiesOf(unit))
            {
                ActionDefinition first = null, comboDef = null;
                foreach (var a in unit.Actions)
                {
                    string why;
                    if (a.Targeting != TargetType.Enemy || !sim.CanAfford(unit, a, out why, e.Id)) continue;
                    if (first == null) first = a;
                    if (a.Damage > 0f && !a.IsCharge && LandsInStun(sim, unit, a, e) && (comboDef == null || a.Damage > comboDef.Damage)) comboDef = a;
                }
                if (first == null) continue;
                float dist = Flat(unit.Position, e.Position);
                float score = -dist + 3f * (1f - e.Health / Mathf.Max(1f, e.MaxHealth)) + (comboDef != null ? 25f : 0f);
                if (score > best) { best = score; target = e; combo = comboDef != null; def = comboDef ?? first; }
            }
            return target != null;
        }

        private static bool LandsInStun(BattleSimulation sim, Unit unit, ActionDefinition def, Unit target)
        {
            if (target.Phase != UnitPhase.Stunned) return false;
            float travel = Mathf.Max(0f, Flat(unit.Position, target.Position) - def.Range) / unit.EffectiveMoveSpeed();
            float lands = sim.Now + travel + def.Windup / unit.EffectiveActionSpeed();
            return target.PhaseEndsAt >= lands + 0.05f;
        }

        /// <summary>How many enemy shooters could see (and reach) the unit standing at 'p'.</summary>
        private static int Exposure(BattleSimulation sim, Unit unit, Vector3 p)
        {
            var ghost = Ghost(unit, p);
            int n = 0;
            foreach (var e in sim.EnemiesOf(unit))
            {
                if (!IsRangedUnit(e) || Flat(p, e.Position) > MaxRange(e) * 1.2f) continue;
                if (sim.Visibility.HasLineOfSight(e, ghost)) n++;
            }
            return n;
        }

        private static bool AnyEnemyShooters(BattleSimulation sim, Unit unit)
        {
            foreach (var e in sim.EnemiesOf(unit)) if (IsRangedUnit(e)) return true;
            return false;
        }

        // ------------------------------------------------------------------ shared

        /// <summary>
        /// Samples reachable spots a hop away (16 directions, two distances), scores each, and returns the best if it beats
        /// standing still. The spot is where the walk would really end (path end), so scoring and the move agree.
        /// </summary>
        private bool BestSpot(BattleSimulation sim, Unit unit, System.Func<Vector3, float> score, out Vector3 spot)
        {
            spot = unit.Position;
            float best = score(unit.Position) + 0.25f;   // moving has to be worth it
            bool found = false;
            for (int r = 1; r <= 2; r++)
                for (int i = 0; i < 16; i++)
                {
                    float a = i * Mathf.PI / 8f + (r == 2 ? Mathf.PI / 16f : 0f);
                    Vector3 want = unit.Position + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (HopDistance * r / 2f);
                    Vector3 free = sim.FreeSpotNear(unit, want, unit.Position, true);
                    if (!sim.PlanPath(unit, unit.Position, free, scratch)) continue;
                    Vector3 end = scratch[scratch.Count - 1];
                    if (PathLength(scratch) > HopDistance * 1.8f || Flat(end, unit.Position) < 0.75f) continue;   // no long detours
                    float s = score(end) - 0.05f * PathLength(scratch);
                    if (s > best) { best = s; spot = end; found = true; }
                }
            return found;
        }

        /// <summary>A unit whose damaging targeted abilities are mostly ranged.</summary>
        public static bool IsRangedUnit(Unit u)
        {
            int ranged = 0, melee = 0;
            foreach (var a in u.Actions)
            {
                if (a.Targeting != TargetType.Enemy || a.Damage <= 0f) continue;
                if (a.IsRanged) ranged++; else melee++;
            }
            return ranged > melee;
        }

        private static float MaxRange(Unit u)
        {
            float r = 0f;
            foreach (var a in u.Actions) if (a.Targeting == TargetType.Enemy && a.IsRanged) r = Mathf.Max(r, a.Range);
            return r;
        }

        /// <summary>Distance from 'p' to the nearest melee enemy of 'unit' (float.MaxValue if none).</summary>
        private static float NearestMeleeThreat(BattleSimulation sim, Unit unit, Vector3 p)
        {
            float d = float.MaxValue;
            foreach (var e in sim.EnemiesOf(unit))
                if (!IsRangedUnit(e)) d = Mathf.Min(d, Flat(p, e.Position));
            return d;
        }

        /// <summary>Is any melee enemy inside KeepDistance free to close in (not stunned, not winding up or recovering on someone else)?</summary>
        private bool BrawlerPressing(BattleSimulation sim, Unit unit)
        {
            foreach (var e in sim.EnemiesOf(unit))
            {
                if (IsRangedUnit(e) || Flat(unit.Position, e.Position) >= KeepDistance) continue;
                if (e.Phase == UnitPhase.Stunned) continue;
                bool busyElsewhere = (e.Phase == UnitPhase.Windup || e.Phase == UnitPhase.Recovery) && !e.Waiting &&
                                     e.LastActionTargetId != unit.Id;
                if (!busyElsewhere) return true;
            }
            return false;
        }

        /// <summary>Melee enemies (other than 'except') within striking distance of 'p'.</summary>
        private static int MeleeThreats(BattleSimulation sim, Unit unit, Vector3 p, Unit except)
        {
            int n = 0;
            foreach (var e in sim.EnemiesOf(unit))
                if (e != except && !IsRangedUnit(e) && Flat(p, e.Position) < 3f) n++;
            return n;
        }

        private static ActionDefinition Find(Unit unit, string behaviorId)
        {
            foreach (var a in unit.Actions) if (a.BehaviorId == behaviorId) return a;
            return null;
        }

        /// <summary>A stand-in for the unit at 'p' (same Id, so sight checks still skip its own body).</summary>
        private static Unit Ghost(Unit unit, Vector3 p)
        {
            if ((p - unit.Position).sqrMagnitude < 1e-6f) return unit;
            var g = unit.Snapshot();
            g.Position = p;
            g.Statuses = unit.Statuses;
            g.CooldownReadyAt = unit.CooldownReadyAt;
            return g;
        }

        private static float PathLength(List<Vector3> path)
        {
            float l = 0f;
            for (int i = 1; i < path.Count; i++) l += Flat(path[i - 1], path[i]);
            return l;
        }

        private static float Flat(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }
    }
}
