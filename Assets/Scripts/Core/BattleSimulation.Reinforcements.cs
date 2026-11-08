using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TacticsGame.Core
{
    /// <summary>
    /// An AI that has a say in where its unit drops in as a reinforcement (BattleSimulation.ScheduleDrop): the unit it wants
    /// to land near (a melee unit lands within MeleeDropDistance of it), or null to land like a ranged unit, well away from
    /// everyone. BasicEnemyAI answers for every style (overridable); AIRouter asks the unit's own.
    /// </summary>
    public interface IDropPreference
    {
        Unit DropNear(BattleSimulation sim, Unit unit);
    }

    /// <summary>
    /// Dropping in from the sky (a reinforcement): a lift downward from DropHeight to the ground. The unit can't act on the
    /// way down; a damaging hit knocks it out of the sky into a fall (stun and fall damage) from wherever it is.
    /// </summary>
    public class DropState : LiftState
    {
        public override void OnStruck(BattleSimulation sim, HitContext hit)
        {
            if (hit.Dealt > 0f && !Unit.IsUnstoppable) sim.KnockOutOfDrop(Unit, hit.Source);
        }
    }

    /// <summary>
    /// BattleSimulation: reinforcements. An encounter's later waves (EnemyEntry.ArriveAfter) are scheduled at the start; each
    /// arrives at its time dropped from DropHeight metres (DropStarted), taking DropSeconds to land. Where depends on the range
    /// it prefers: melee units land within MeleeDropDistance of the unit their AI picks (IDropPreference: by default the most
    /// hurt enemy); ranged ones at a random open spot at least RangedDropClearance from every unit.
    /// </summary>
    public partial class BattleSimulation
    {
        public float DropHeight = 7f;
        public float DropSeconds = 1.2f;
        public float MeleeDropDistance = 4f;
        public float RangedDropClearance = 10f;

        private class PendingDrop { public UnitDefinition Definition; public Team Team; public float At; public List<UpgradeDefinition> Upgrades; public bool Anywhere; }
        private readonly List<PendingDrop> drops = new List<PendingDrop>();
        private int dropCounter;

        /// <summary>Drop a 'def' unit onto 'team' from the sky 'at' seconds into the battle (now, if that has passed).</summary>
        /// <param name="anywhere">Land at a random open spot (RandomDropClearance from everyone), whatever the unit prefers.</param>
        public void ScheduleDrop(UnitDefinition def, Team team, float at, IList<UpgradeDefinition> upgrades = null, bool anywhere = false)
        {
            if (def == null) return;
            drops.Add(new PendingDrop { Definition = def, Team = team, At = at, Upgrades = upgrades != null ? new List<UpgradeDefinition>(upgrades) : null, Anywhere = anywhere });
        }

        // ---- endless waves (the final encounter): every 'every' seconds, 'count' of the pool drop in at random spots

        /// <summary>Clearance from every unit a random drop looks for (it takes the clearest spot it finds).</summary>
        public float RandomDropClearance = 5f;
        private List<UnitDefinition> wavePool;
        private Team waveTeam;
        private float waveEvery, waveNext;
        private int waveCount, waveCap, wavePick;

        /// <summary>
        /// Keep reinforcements coming for the rest of the battle (or until StopReinforcementWaves): from 'firstAt' seconds,
        /// every 'every' seconds 'count' units of 'pool' (in turn) drop onto 'team' at random spots, as long as fewer than
        /// 'cap' of that side are up (0 = no limit).
        /// </summary>
        public void StartReinforcementWaves(IList<UnitDefinition> pool, Team team, float firstAt, float every, int count, int cap)
        {
            wavePool = pool != null ? pool.Where(p => p != null).ToList() : null;
            if (wavePool == null || wavePool.Count == 0 || every <= 0f || count <= 0) { wavePool = null; return; }
            waveTeam = team; waveNext = firstAt; waveEvery = every; waveCount = count; waveCap = cap;
        }

        /// <summary>No more waves; the drops already on their way still come, unless 'cancelPending' (Split The Minds).</summary>
        public void StopReinforcementWaves(bool cancelPending)
        {
            wavePool = null;
            if (cancelPending) drops.RemoveAll(d => d.Team == waveTeam && d.Anywhere);
        }

        /// <summary>Are endless waves running?</summary>
        public bool WavesRunning { get { return wavePool != null; } }

        private void TickReinforcementWaves()
        {
            if (wavePool == null || Now + 1e-4f < waveNext) return;
            waveNext = Now + waveEvery;
            int up = State.Units.Count(u => u.IsAlive && u.Team == waveTeam && !u.IsBoss) + drops.Count(d => d.Team == waveTeam);
            for (int i = 0; i < waveCount && (waveCap <= 0 || up < waveCap); i++, up++)
                ScheduleDrop(wavePool[wavePick++ % wavePool.Count], waveTeam, Now, null, true);
        }

        /// <summary>Reinforcements still to come.</summary>
        public int PendingDrops { get { return drops.Count; } }

        /// <summary>The unit is on its way down (DropState).</summary>
        public bool IsDropping(Unit u) { return u != null && u.States.Any(s => s is DropState); }

        private void TickDrops()
        {
            for (int i = 0; i < drops.Count; i++)
            {
                var d = drops[i];
                if (Now + 1e-4f < d.At) continue;
                drops.RemoveAt(i--);
                DropIn(d);
            }
        }

        private void DropIn(PendingDrop d)
        {
            string id = (!string.IsNullOrEmpty(d.Definition.Id) ? d.Definition.Id : !string.IsNullOrEmpty(d.Definition.DisplayName) ? d.Definition.DisplayName : "Reinforcement") + "_drop_" + (++dropCounter);
            var u = Unit.FromDefinition(d.Definition, id, d.Team, Vector3.zero, null, d.Upgrades);
            Vector3 landing = Terrain.OnGround(d.Anywhere ? OpenSpot(u, RandomDropClearance) : DropPoint(u));
            u.Position = landing + Vector3.up * DropHeight;
            State.Units.Add(u);
            AttachBehaviors(u);
            Emit(EventType.UnitSpawned, null, u.Id, new SpawnInfo { Unit = u.Snapshot(), Definition = d.Definition });
            Arrive(u);
            u.AddState(new DropState { From = u.Position, To = landing, Start = Now, RiseEnd = Now + DropSeconds, DropAt = Now + DropSeconds });
            u.Phase = UnitPhase.Recovery;   // nothing to do until it lands
            u.PhaseEndsAt = Now + DropSeconds;
            Emit(EventType.DropStarted, null, u.Id, new DropInfo { From = u.Position, To = landing, Seconds = DropSeconds });
        }

        /// <summary>A dropping unit lands where it was headed and is ready once its phase runs out.</summary>
        private void EndDrop(Unit u, DropState drop)
        {
            u.RemoveState(drop);
            u.Position = Terrain.OnGround(FreeSpotNear(u, drop.To, drop.To, false));
            Emit(EventType.MoveEnded, u.Id, null, new MoveEndedInfo { Position = u.Position });
        }

        /// <summary>Hit on the way down: it falls the rest of the way, with the fall's stun and damage, credited to 'source'.</summary>
        internal void KnockOutOfDrop(Unit u, Unit source)
        {
            var drop = u.States.OfType<DropState>().FirstOrDefault();
            if (drop == null || !u.IsAlive) return;
            u.RemoveState(drop);
            Vector3 landing = Terrain.OnGround(FreeSpotNear(u, drop.To, drop.To, false));
            StartFall(u, landing, u.Position.y - landing.y, 0f, source, false);
        }

        /// <summary>Does this unit fight up close? Its basic attack is melee (or, with none, most of its attacks are).</summary>
        public static bool PrefersMelee(Unit u)
        {
            var basic = u.Actions.Find(a => a != null && a.Id == u.BasicAttackId);
            if (basic != null) return !basic.IsRanged;
            var attacks = u.Actions.FindAll(a => a != null && a.Targeting == TargetType.Enemy && a.Damage > 0f);
            return attacks.Count > 0 && attacks.Count(a => !a.IsRanged) * 2 >= attacks.Count;
        }

        /// <summary>The default drop target for a melee unit: the enemy of its team with the least health left (as a share).</summary>
        public Unit DefaultDropTarget(Unit u)
        {
            if (!PrefersMelee(u)) return null;
            return State.Units.Where(o => o.IsAlive && o.Team != u.Team).OrderBy(o => o.Health / Mathf.Max(1f, o.MaxHealth)).ThenBy(o => o.Id).FirstOrDefault();
        }

        /// <summary>Where 'u' lands: near the unit its AI picks (melee), else an open spot clear of everyone (ranged).</summary>
        private Vector3 DropPoint(Unit u)
        {
            var pref = EnemyAI as IDropPreference;
            var near = pref != null ? pref.DropNear(this, u) : DefaultDropTarget(u);
            if (near != null && near.IsAlive)
            {
                for (int i = 0; i < 24; i++)
                {
                    float ang = RandomValue() * Mathf.PI * 2f, dist = Mathf.Lerp(1.5f, Mathf.Max(1.5f, MeleeDropDistance), RandomValue());
                    var p = WalkablePointNear(near.Position + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * dist, near.Position);
                    if (InArea(p) && Flat(p, near.Position) <= MeleeDropDistance + 0.05f && !Crowded(p, u)) return p;
                }
                return near.Position + Vector3.forward * 1.5f;
            }
            Vector3 best = Vector3.zero; float bestClear = -1f;
            for (int i = 0; i < 40; i++)
            {
                var a = Terrain.AreaMin + Vector2.one * 2f; var b = Terrain.AreaMax - Vector2.one * 2f;
                var p = WalkablePointNear(new Vector3(Mathf.Lerp(a.x, b.x, RandomValue()), 0f, Mathf.Lerp(a.y, b.y, RandomValue())), Vector3.zero);
                if (!InArea(p)) continue;
                float clear = State.Units.Where(o => o.IsAlive && o != u).Select(o => Flat(p, o.Position)).DefaultIfEmpty(float.MaxValue).Min();
                if (clear >= RangedDropClearance) return p;
                if (clear > bestClear) { bestClear = clear; best = p; }
            }
            return best;
        }

        /// <summary>A random open spot on the map at least 'clearance' from every unit (else the clearest of those tried).</summary>
        public Vector3 OpenSpot(Unit u, float clearance)
        {
            Vector3 best = Vector3.zero; float bestClear = -1f;
            for (int i = 0; i < 40; i++)
            {
                var a = Terrain.AreaMin + Vector2.one * 2f; var b = Terrain.AreaMax - Vector2.one * 2f;
                var p = WalkablePointNear(new Vector3(Mathf.Lerp(a.x, b.x, RandomValue()), 0f, Mathf.Lerp(a.y, b.y, RandomValue())), Vector3.zero);
                if (!InArea(p)) continue;
                float clear = State.Units.Where(o => o.IsAlive && o != u).Select(o => Flat(p, o.Position)).DefaultIfEmpty(float.MaxValue).Min();
                if (clear >= clearance) return p;
                if (clear > bestClear) { bestClear = clear; best = p; }
            }
            return best;
        }

        private bool InArea(Vector3 p)
        {
            return p.x >= Terrain.AreaMin.x && p.x <= Terrain.AreaMax.x && p.z >= Terrain.AreaMin.y && p.z <= Terrain.AreaMax.y;
        }

        private bool Crowded(Vector3 p, Unit u)
        {
            return State.Units.Any(o => o.IsAlive && o != u && Flat(p, o.Position) < o.BodyRadius + u.BodyRadius + 0.2f);
        }

        private static float Flat(Vector3 a, Vector3 b) { a.y = b.y = 0f; return Vector3.Distance(a, b); }
    }
}
