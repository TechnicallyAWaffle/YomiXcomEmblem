using System.Collections.Generic;
using UnityEngine;

namespace TacticsGame.Core
{
    /// <summary>What a pack is doing (PackAI).</summary>
    public enum PackMood { Prowl, Retaliate, Attack }

    /// <summary>
    /// A pack: units of one kind on one side that fight together (PackAI). Its leader is one of them; Mood is what the
    /// whole pack is doing, Prey whom it is after. The rest is the pack's notes: which openings it has already gone for, so
    /// one stun or one drop under half health sets it off once.
    /// </summary>
    public class Pack
    {
        public int Id;
        public string Kind;
        public Team Team;
        public readonly List<string> Members = new List<string>();
        public string LeaderId;
        public PackMood Mood = PackMood.Prowl;
        public float MoodUntil;
        public string PreyId;
        public float OrbitSign = 1f;
        public float UpdatedAt = -1f;
        public float SignalSeen = -1f;
        public readonly HashSet<string> LowHealthSeen = new HashSet<string>();
        public readonly Dictionary<string, float> ControlSeen = new Dictionary<string, float>();
        public readonly Dictionary<string, float> GuardSeen = new Dictionary<string, float>();
    }

    /// <summary>A battle's packs (BattleSimulation.Memory).</summary>
    public class PackBook
    {
        public readonly List<Pack> Packs = new List<Pack>();
        public int NextId;
        public Pack Of(string unitId) { return Packs.Find(p => p.Members.Contains(unitId)); }
    }

    /// <summary>
    /// Pack hunters (the Howler; any kit can be played this way). Units of one kind on one side that start out (or arrive)
    /// within PackRadius of each other form a pack, and one of them, picked at random, leads it; when the leader falls
    /// another is picked at random. The leader decides what the pack does (its Mood):
    ///   Prowl:     the leader keeps SafeDistance from the enemy and circles them a hop at a time; the others keep in a
    ///              loose ring about it (GroupRadius).
    ///   Retaliate: an enemy within RetaliateRange of any of the pack: the others swarm it and strike it, never more than
    ///              LeashRange from the leader; the leader keeps prowling.
    ///   Attack:    the leader's Signal ability (Howling Lament) lands on a target, or an enemy within WatchRange of the
    ///              leader drops under LowHealth of its health, has its guard broken, or is put into a stun or recovery
    ///              longer than ControlSeconds: for AttackSeconds every one of them (the leader too) is AttackSpeedBonus
    ///              faster and hunts that one target relentlessly. Then back to Prowl.
    /// The leader howls (Signal) whenever it is ready and has someone to call the pack onto (the most hurt enemy in reach).
    /// Packs never Focus. The pack's state lives on the battle (BattleSimulation.Memory), so one style serves every battle.
    /// Abilities are found by role: the Strike (the basic attack) and the Signal.
    /// </summary>
    public class PackAI : ProfileAI, IBattleWatcher
    {
        public float PackRadius = 25f;
        public float SafeDistance = 12f;
        public float ProwlStep = 30f;          // degrees round the enemy per prowl hop
        public float GroupRadius = 3.5f;
        public float RetaliateRange = 7f;
        public float LeashRange = 15f;
        public float WatchRange = 30f;
        public float LowHealth = 0.5f;
        public float ControlSeconds = 1.5f;
        public float AttackSeconds = 3f;
        public float AttackSpeedBonus = 4f;

        /// <summary>The status a pack on the attack has (AttackSpeedBonus move speed).</summary>
        public const string FrenzyStatus = "frenzy";

        /// <summary>Which pack kind a unit is: its class, by name (packs are one kind each).</summary>
        protected virtual string KindOf(Unit u) { return u.DisplayName; }

        protected static ActionDefinition Strike(Unit u) { return u.Actions.Find(a => a != null && a.Id == u.BasicAttackId) ?? MeleeStrike(u) ?? Find(u, AbilityRole.Strike); }
        protected static ActionDefinition Signal(Unit u) { return Find(u, AbilityRole.Signal); }

        public override UnitCommand Decide(BattleSimulation sim, Unit unit)
        {
            if (Basic(sim, unit)) return base.Decide(sim, unit);
            var pack = PackOf(sim, unit);
            UpdatePack(sim, pack);
            var react = React(sim, unit);
            if (react != null) return react;
            var leader = sim.GetUnit(pack.LeaderId);
            bool leads = leader == unit;
            var prey = sim.GetUnit(pack.PreyId);

            if (pack.Mood == PackMood.Attack && prey != null && prey.IsAlive) return Hunt(sim, unit, prey);
            if (leads)
            {
                var call = Howl(sim, unit);
                if (call != null) return call;
                return Prowl(sim, unit, pack);
            }
            if (pack.Mood == PackMood.Retaliate && prey != null && prey.IsAlive) return Swarm(sim, unit, prey, leader);
            return Follow(sim, unit, pack, leader);
        }

        /// <summary>Between decisions (IBattleWatcher): the pack notices an opening or an intruder at once, and the members it
        /// sends in are woken from a pause or a walk to go.</summary>
        public void Watch(BattleSimulation sim, Unit unit)
        {
            if (unit.Phase == UnitPhase.Charging || unit.IsLifted) return;
            var pack = PackOf(sim, unit);
            var mood = pack.Mood;
            string prey = pack.PreyId;
            UpdatePack(sim, pack);
            if (pack.Mood == PackMood.Retaliate && (mood != PackMood.Retaliate || prey != pack.PreyId))
                foreach (var id in pack.Members) if (id != pack.LeaderId) sim.Wake(sim.GetUnit(id));
        }

        // ------------------------------------------------------------------ the pack

        /// <summary>The unit's pack: the one it is in, else one of its kind whose leader is within PackRadius, else a new one
        /// gathering every unpacked unit of its kind it can reach a PackRadius at a time (its leader picked at random).</summary>
        public Pack PackOf(BattleSimulation sim, Unit unit)
        {
            var book = sim.Memory<PackBook>();
            var pack = book.Of(unit.Id);
            if (pack != null) return pack;
            string kind = KindOf(unit);
            foreach (var p in book.Packs)
            {
                var l = sim.GetUnit(p.LeaderId);
                if (p.Kind == kind && p.Team == unit.Team && l != null && l.IsAlive && Flat(l.Position, unit.Position) <= PackRadius) { p.Members.Add(unit.Id); return p; }
            }
            pack = new Pack { Id = ++book.NextId, Kind = kind, Team = unit.Team };
            pack.Members.Add(unit.Id);
            for (int i = 0; i < pack.Members.Count; i++)
            {
                var m = sim.GetUnit(pack.Members[i]);
                foreach (var u in sim.State.Units)
                    if (u.IsAlive && u.Team == unit.Team && !pack.Members.Contains(u.Id) && book.Of(u.Id) == null && KindOf(u) == kind && Flat(u.Position, m.Position) <= PackRadius)
                        pack.Members.Add(u.Id);
            }
            pack.OrbitSign = Roll01("orbit" + pack.Id, 0) < 0.5f ? -1f : 1f;
            book.Packs.Add(pack);
            PickLeader(sim, pack);
            return pack;
        }

        private static void PickLeader(BattleSimulation sim, Pack pack)
        {
            pack.Members.RemoveAll(id => { var u = sim.GetUnit(id); return u == null || !u.IsAlive; });
            if (pack.Members.Count == 0) { pack.LeaderId = null; return; }
            int i = Mathf.Min(pack.Members.Count - 1, (int)(Roll01("pack" + pack.Id, Mathf.FloorToInt(sim.Now * 40f)) * pack.Members.Count));
            pack.LeaderId = pack.Members[i];
        }

        /// <summary>Once a tick per pack: drop the fallen, a new leader if it fell, and the mood (see the class summary).</summary>
        protected void UpdatePack(BattleSimulation sim, Pack pack)
        {
            if (pack.UpdatedAt >= sim.Now - 1e-5f) return;
            pack.UpdatedAt = sim.Now;
            var leader = sim.GetUnit(pack.LeaderId);
            if (leader == null || !leader.IsAlive) { PickLeader(sim, pack); leader = sim.GetUnit(pack.LeaderId); }
            else pack.Members.RemoveAll(id => { var u = sim.GetUnit(id); return u == null || !u.IsAlive; });
            if (leader == null) return;

            var prey = sim.GetUnit(pack.PreyId);
            if (pack.Mood == PackMood.Attack && (sim.Now >= pack.MoodUntil || prey == null || !prey.IsAlive)) { pack.Mood = PackMood.Prowl; pack.PreyId = null; }

            var opening = Opening(sim, pack, leader);
            if (opening != null && pack.Mood != PackMood.Attack) { StartAttack(sim, pack, opening); return; }
            if (pack.Mood == PackMood.Attack) return;

            // Retaliate: an enemy too close to any of the pack (the nearest such one).
            Unit intruder = null;
            float best = RetaliateRange;
            foreach (var id in pack.Members)
            {
                var m = sim.GetUnit(id);
                foreach (var e in sim.EnemiesOf(m))
                {
                    float d = Flat(e.Position, m.Position);
                    if (d <= best && Flat(e.Position, leader.Position) <= LeashRange + 2f) { best = d; intruder = e; }
                }
            }
            if (intruder != null) { pack.Mood = PackMood.Retaliate; pack.PreyId = intruder.Id; }
            else if (pack.Mood == PackMood.Retaliate && !StillClose(sim, pack, prey)) { pack.Mood = PackMood.Prowl; pack.PreyId = null; }
        }

        private bool StillClose(BattleSimulation sim, Pack pack, Unit prey)
        {
            if (prey == null || !prey.IsAlive) return false;
            var leader = sim.GetUnit(pack.LeaderId);
            if (leader != null && Flat(prey.Position, leader.Position) > LeashRange + 4f) return false;
            foreach (var id in pack.Members) { var m = sim.GetUnit(id); if (m != null && Flat(m.Position, prey.Position) <= RetaliateRange + 3f) return true; }
            return false;
        }

        /// <summary>An enemy the pack should set on now (the leader's fresh signal first), or null. Every opening is noted
        /// as it is seen, so each sets the pack off once.</summary>
        private Unit Opening(BattleSimulation sim, Pack pack, Unit leader)
        {
            Unit found = null;
            SignalInfo s;
            if (sim.LastSignal(leader.Id, out s) && s.At > pack.SignalSeen)
            {
                pack.SignalSeen = s.At;
                var t = sim.GetUnit(s.TargetId);
                if (t != null && t.IsAlive) found = t;
            }
            foreach (var e in sim.EnemiesOf(leader))
            {
                bool watched = Flat(e.Position, leader.Position) <= WatchRange;
                // Under half health: once, until it is healed back over.
                if (e.Health / Mathf.Max(1f, e.MaxHealth) < LowHealth) { if (pack.LowHealthSeen.Add(e.Id) && watched && found == null) found = e; }
                else pack.LowHealthSeen.Remove(e.Id);
                // Guard broken: once per break.
                var gb = e.GetStatus(StatusIds.GuardBroken);
                float seen;
                if (gb != null && (!pack.GuardSeen.TryGetValue(e.Id, out seen) || Mathf.Abs(seen - gb.ExpiresAt) > 1e-3f))
                {
                    pack.GuardSeen[e.Id] = gb.ExpiresAt;
                    if (watched && found == null) found = e;
                }
                // Held in a long stun or recovery: once per hold.
                bool held = (e.Phase == UnitPhase.Stunned || e.Phase == UnitPhase.Recovery) && !e.Waiting && e.PhaseEndsAt - sim.Now > ControlSeconds;
                if (held && (!pack.ControlSeen.TryGetValue(e.Id, out seen) || Mathf.Abs(seen - e.PhaseEndsAt) > 1e-3f))
                {
                    pack.ControlSeen[e.Id] = e.PhaseEndsAt;
                    if (watched && found == null) found = e;
                }
            }
            return found;
        }

        private void StartAttack(BattleSimulation sim, Pack pack, Unit prey)
        {
            pack.Mood = PackMood.Attack;
            pack.PreyId = prey.Id;
            pack.MoodUntil = sim.Now + AttackSeconds;
            foreach (var id in pack.Members)
            {
                var m = sim.GetUnit(id);
                if (m == null || !m.IsAlive) continue;
                sim.ApplyStatus(m, new StatusEffect
                {
                    Id = FrenzyStatus, SourceId = pack.LeaderId, ExpiresAt = pack.MoodUntil,
                    Modifiers = new List<Modifier> { Modifier.Add(StatId.MoveSpeed, AttackSpeedBonus) },
                }, sim.GetUnit(pack.LeaderId));
                if (m.Phase == UnitPhase.Ready || m.Phase == UnitPhase.Moving || m.Waiting) sim.Wake(m);   // set off at once
            }
        }

        // ------------------------------------------------------------------ what each one does

        /// <summary>Attack: straight at the prey with the strike, whatever else is about.</summary>
        protected UnitCommand Hunt(BattleSimulation sim, Unit unit, Unit prey)
        {
            var strike = Strike(unit);
            if (strike != null && Ready(sim, unit, strike)) return UnitCommand.UseActionWhenInRange(unit.Id, strike, prey.Id);
            if (unit.IsRooted || Flat(unit.Position, prey.Position) <= unit.BodyRadius + prey.BodyRadius + 1f) return UnitCommand.Idle(unit.Id, 0.15f);
            return UnitCommand.MoveTo(unit.Id, Beside(unit, prey));
        }

        /// <summary>Retaliate: at the intruder, but never past LeashRange from the leader (it waits at the leash's end).</summary>
        protected UnitCommand Swarm(BattleSimulation sim, Unit unit, Unit prey, Unit leader)
        {
            if (leader != null && Flat(prey.Position, leader.Position) > LeashRange)
            {
                Vector3 d = prey.Position - leader.Position; d.y = 0f;
                Vector3 edge = sim.WalkablePointNear(leader.Position + d.normalized * (LeashRange - 1f), unit.Position);
                return Flat(edge, unit.Position) > 1f && !unit.IsRooted ? UnitCommand.MoveTo(unit.Id, edge) : UnitCommand.Idle(unit.Id, 0.3f);
            }
            return Hunt(sim, unit, prey);
        }

        /// <summary>The leader's howl: when it is ready and someone is in reach, at the most hurt of them.</summary>
        protected UnitCommand Howl(BattleSimulation sim, Unit unit)
        {
            var call = Signal(unit);
            if (call == null || !Ready(sim, unit, call)) return null;
            Unit best = null;
            float worst = 2f;
            foreach (var e in sim.EnemiesOf(unit))
            {
                if (!Reach(sim, unit, call, e)) continue;
                float frac = e.Health / Mathf.Max(1f, e.MaxHealth) + 0.01f * Flat(e.Position, unit.Position);
                if (frac < worst) { worst = frac; best = e; }
            }
            return best != null ? UnitCommand.UseAction(unit.Id, call, best.Id) : null;
        }

        /// <summary>Prowl (the leader): back off anyone inside SafeDistance, else circle the enemy a hop at a time.</summary>
        protected UnitCommand Prowl(BattleSimulation sim, Unit unit, Pack pack)
        {
            if (unit.IsRooted) return UnitCommand.Idle(unit.Id, 0.4f);
            var near = sim.NearestEnemy(unit);
            if (near == null) return UnitCommand.Idle(unit.Id, 0.5f);
            float d = Flat(unit.Position, near.Position);
            if (d < SafeDistance - 2f)
            {
                var back = BackOffFrom(sim, unit, near, SafeDistance + 1f);
                if (back != null) return back;
            }
            // Circle the enemies' middle at SafeDistance from the nearest one.
            Vector3 c = EnemyCentre(sim, unit, near);
            Vector3 from = unit.Position - c; from.y = 0f;
            if (from.sqrMagnitude < 1e-4f) from = Vector3.forward;
            float radius = Mathf.Max(SafeDistance + 1f, Flat(c, near.Position) + SafeDistance);
            float a = Mathf.Atan2(from.z, from.x) + pack.OrbitSign * ProwlStep * Mathf.Deg2Rad;
            Vector3 want = c + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius;
            Vector3 step = want - unit.Position; step.y = 0f;
            if (step.magnitude > HopDistance * 1.4f) want = unit.Position + step.normalized * HopDistance * 1.4f;
            Vector3 to = sim.WalkablePointNear(want, unit.Position);
            if (Flat(to, unit.Position) < 1f) { pack.OrbitSign = -pack.OrbitSign; return UnitCommand.Idle(unit.Id, 0.3f); }   // walled in: the other way next time
            return UnitCommand.MoveTo(unit.Id, to);
        }

        /// <summary>Prowl (the others): a loose ring about the leader; close enough is close enough.</summary>
        protected UnitCommand Follow(BattleSimulation sim, Unit unit, Pack pack, Unit leader)
        {
            if (leader == null || unit.IsRooted) return UnitCommand.Idle(unit.Id, 0.4f);
            int i = Mathf.Max(0, pack.Members.IndexOf(unit.Id));
            int n = Mathf.Max(1, pack.Members.Count);
            float a = i * Mathf.PI * 2f / n + pack.Id;
            Vector3 slot = leader.Position + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * GroupRadius;
            if (Flat(unit.Position, slot) <= 2.5f) return UnitCommand.Idle(unit.Id, 0.35f);
            return UnitCommand.MoveTo(unit.Id, sim.WalkablePointNear(slot, unit.Position));
        }

        private static Vector3 EnemyCentre(BattleSimulation sim, Unit unit, Unit near)
        {
            Vector3 sum = Vector3.zero;
            int n = 0;
            foreach (var e in sim.EnemiesOf(unit))
                if (Flat(e.Position, near.Position) <= 12f) { sum += e.Position; n++; }
            return n > 0 ? sum / n : near.Position;
        }

        /// <summary>A walk straight away from 'from' to 'distance' metres from it (null if already that far).</summary>
        private static UnitCommand BackOffFrom(BattleSimulation sim, Unit unit, Unit from, float distance)
        {
            Vector3 away = unit.Position - from.Position; away.y = 0f;
            away = away.sqrMagnitude > 1e-6f ? away.normalized : Vector3.back;
            var to = sim.WalkablePointNear(from.Position + away * distance, unit.Position);
            return Flat(to, unit.Position) > 0.75f ? UnitCommand.MoveTo(unit.Id, to) : null;
        }
    }

    /// <summary>The Howler: a PackAI with its numbers (prowls at 12m, retaliates inside 7m, leashed at 15m).</summary>
    public class HowlerAI : PackAI
    {
        public const string Style = "howler";
    }

    /// <summary>
    /// A floating caster that keeps its distance (the Matriarch; any kit with these roles plays it): its Bulwark (Carapace)
    /// when an enemy gets within BulwarkThreat, or it is hurt (under BulwarkHealth) with one inside PreferredRange; its Reel
    /// (Captivate) at the most hurt enemy it can reach; its Volley (Needlestorm) whenever an enemy is within VolleyRange.
    /// Otherwise it keeps PreferredRange from the nearest enemy: backing off brawlers (BasicEnemyAI's KeepDistance) to spots
    /// still in reach, walking in when it has drifted too far. Guards, dashes and rests like any unit.
    /// </summary>
    public class MatriarchAI : KitAI
    {
        public const string Style = "matriarch";
        public float PreferredRange = 11f;
        public float VolleyRange = 18f;
        public float BulwarkThreat = 5f;
        public float BulwarkHealth = 0.6f;

        public MatriarchAI() { KeepDistance = 8f; }

        public override UnitCommand Decide(BattleSimulation sim, Unit unit)
        {
            if (Basic(sim, unit)) return base.Decide(sim, unit);
            var r = React(sim, unit) ?? Rest(sim, unit);
            if (r != null) return r;
            var target = sim.NearestEnemy(unit);
            float near = Flat(unit.Position, target.Position);

            var bulwark = Find(unit, AbilityRole.Bulwark);
            if (Ready(sim, unit, bulwark) && (near < BulwarkThreat || (StrainFraction(unit) < 0.9f && unit.Health / Mathf.Max(1f, unit.MaxHealth) < BulwarkHealth && near < PreferredRange)))
                return UnitCommand.UseAction(unit.Id, bulwark, unit.Id);

            var reel = Find(unit, AbilityRole.Reel);
            if (Ready(sim, unit, reel))
            {
                Unit best = null;
                float worst = 2f;
                foreach (var e in sim.EnemiesOf(unit))
                {
                    if (!Reach(sim, unit, reel, e)) continue;
                    float frac = e.Health / Mathf.Max(1f, e.MaxHealth);
                    if (frac < worst) { worst = frac; best = e; }
                }
                if (best != null) return UnitCommand.UseAction(unit.Id, reel, best.Id);
            }

            var volley = Find(unit, AbilityRole.Volley);
            if (Ready(sim, unit, volley) && near <= VolleyRange)
                return volley.Targeting == TargetType.Enemy ? UnitCommand.UseAction(unit.Id, volley, target.Id) : UnitCommand.UseAction(unit.Id, volley, unit.Id);

            if (unit.IsRooted) return UnitCommand.Idle(unit.Id, 0.3f);
            if (NearestMeleeThreat(sim, unit, unit.Position) < KeepDistance)
            {
                Vector3 spot;
                if (BestSpot(sim, unit, p => KiteScore(sim, unit, p), out spot)) return UnitCommand.MoveTo(unit.Id, spot);
            }
            if (near > PreferredRange + 4f) return Approach(sim, unit, target, PreferredRange);
            return UnitCommand.Idle(unit.Id, 0.3f);
        }

        /// <summary>Away from brawlers, but near PreferredRange of the nearest enemy.</summary>
        private float KiteScore(BattleSimulation sim, Unit unit, Vector3 p)
        {
            float threat = Mathf.Min(NearestMeleeThreat(sim, unit, p), KeepDistance * 1.5f);
            float off = Mathf.Abs(NearestEnemyDistance(sim, unit, p) - PreferredRange);
            return threat - 0.5f * off;
        }
    }
}
