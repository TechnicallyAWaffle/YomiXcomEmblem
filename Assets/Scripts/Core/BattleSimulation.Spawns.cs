using System.Collections.Generic;
using UnityEngine;

namespace TacticsGame.Core
{
    /// <summary>
    /// BattleSimulation: things abilities bring into the battle.
    ///   Summons      a new unit joins mid-battle (Horde of Memories' Wraiths): SpawnUnit queues it, and it arrives at the end
    ///                of the tick (UnitSpawned), with its behaviors attached and its innate domes up, ready for orders.
    ///   Blasts       an explosion placed now that goes off later (Tear Apart Reality): BlastPending, then BlastDetonated and
    ///                a hit on every enemy of its owner in the circle.
    ///   Tick damage  damage with no hit roll in small steps (a damaging zone): Damage events, and a DamageTaken trigger.
    ///   Ability fx   a look an ability wants shown (AbilityFx), for the views only.
    /// </summary>
    public partial class BattleSimulation
    {
        // ------------------------------------------------------------------ summons

        private struct PendingSpawn { public UnitDefinition Definition; public Team Team; public Vector3 At; public string SummonerId; public string AIStyle; public System.Action<Unit> Arrived; }

        private readonly List<PendingSpawn> spawns = new List<PendingSpawn>();
        private int spawnCounter;

        /// <summary>Bring a 'def' unit onto 'team' at 'at' (onto the nearest free ground). It arrives at the end of this tick.</summary>
        /// aiStyle: played by that AI style (UnitAIs) even on the player's side (Unit.AIControlled); null = its own AIProfile
        /// on the enemy side, the player's orders on theirs.
        /// arrived: run on the new unit once it is on the field (Split The Minds sends its parts dashing out).
        public void SpawnUnit(UnitDefinition def, Team team, Vector3 at, Unit summoner, string aiStyle = null, System.Action<Unit> arrived = null)
        {
            if (def == null) return;
            spawns.Add(new PendingSpawn { Definition = def, Team = team, At = at, SummonerId = summoner != null ? summoner.Id : null, AIStyle = aiStyle, Arrived = arrived });
        }

        /// <summary>Bring a 'def' unit onto 'team' at 'at' right away, between ticks (a scripted battle placing enemies while it
        /// waits for orders): its UnitSpawned event is at the current time.</summary>
        public void SpawnUnitNow(UnitDefinition def, Team team, Vector3 at)
        {
            SpawnUnit(def, team, at, null);
            AddSpawns();
        }

        // Where a summon's summoner stands (a spot on the map to bring an off-map summon back in from), if it is still here.
        private Vector3? SummonerSpot(PendingSpawn s)
        {
            var summoner = s.SummonerId != null ? GetUnit(s.SummonerId) : null;
            return summoner != null ? summoner.Position : (Vector3?)null;
        }

        private void AddSpawns()
        {
            if (spawns.Count == 0) return;
            var arrivals = new List<KeyValuePair<Unit, System.Action<Unit>>>();
            foreach (var s in spawns)
            {
                string id = (!string.IsNullOrEmpty(s.Definition.Id) ? s.Definition.Id : !string.IsNullOrEmpty(s.Definition.DisplayName) ? s.Definition.DisplayName : "Summon") + "_summon_" + (++spawnCounter);
                var u = Unit.FromDefinition(s.Definition, id, s.Team, Terrain.OnGround(WalkablePointNear(s.At, SummonerSpot(s))), null, null);
                u.Position = Terrain.OnGround(FreeSpotNear(u, u.Position, u.Position, false));
                if (!string.IsNullOrEmpty(s.AIStyle)) { u.AIControlled = true; u.AIProfile = s.AIStyle; u.AI = null; }
                State.Units.Add(u);
                AttachBehaviors(u);
                Emit(EventType.UnitSpawned, s.SummonerId, u.Id, new SpawnInfo { Unit = u.Snapshot(), Definition = s.Definition });
                Arrive(u);
                if (s.Arrived != null) arrivals.Add(new KeyValuePair<Unit, System.Action<Unit>>(u, s.Arrived));
            }
            spawns.Clear();
            foreach (var a in arrivals) a.Value(a.Key);   // after the list is cleared: one may summon again
        }

        // ------------------------------------------------------------------ delayed blasts

        private class Blast { public string Id; public Unit Source; public ActionDefinition Definition; public Vector3 Center; public float Radius, At; }

        private readonly List<Blast> blasts = new List<Blast>();
        private int blastCounter;

        /// <summary>An explosion of 'radius' at 'center' that goes off 'delay' seconds from now, hitting its owner's enemies there
        /// with 'def' (its damage and stun; no cover, no range check).</summary>
        public void ScheduleBlast(Unit source, ActionDefinition def, Vector3 center, float radius, float delay)
        {
            var b = new Blast { Id = "blast_" + (++blastCounter), Source = source, Definition = def, Center = Terrain.OnGround(center), Radius = radius, At = Now + Mathf.Max(0f, delay) };
            blasts.Add(b);
            Emit(EventType.BlastPending, source != null ? source.Id : null, null, new BlastInfo { Id = b.Id, ActionId = def.Id, Center = b.Center, Radius = radius, At = b.At });
        }

        // A hit held back for a moment (Mental Weapon's bullet): it lands at At, out of FromId's mind if set (that is where it is drawn from).
        private class DelayedStrike { public AttackEffect Attack; public float At; public string FromId, FxKind; }
        private readonly List<DelayedStrike> delayedStrikes = new List<DelayedStrike>();

        /// <summary>
        /// Land 'attack' 'delay' seconds from now (StrikeNow then: the target's dodge as it is then). 'from': the unit it seems to
        /// come out of; 'fxKind': the AbilityFx the views get as it goes (from that unit to the target).
        /// </summary>
        public void ScheduleStrike(AttackEffect attack, float delay, Unit from = null, string fxKind = null)
        {
            if (attack == null) return;
            delayedStrikes.Add(new DelayedStrike { Attack = attack, At = Now + Mathf.Max(0f, delay), FromId = from != null ? from.Id : null, FxKind = fxKind });
        }

        private void TickDelayedStrikes()
        {
            for (int i = delayedStrikes.Count - 1; i >= 0; i--)
            {
                var d = delayedStrikes[i];
                if (Now + 1e-4f < d.At) continue;
                delayedStrikes.RemoveAt(i);
                var target = GetUnit(d.Attack.TargetId);
                if (target == null || !target.IsAlive || d.Attack.Source == null) continue;
                var from = GetUnit(d.FromId);
                if (d.FxKind != null) EmitFx(from ?? d.Attack.Source, target, d.FxKind, d.Attack.Definition, target.Position, 0f, (from ?? d.Attack.Source).Position);
                StrikeNow(d.Attack);
            }
        }

        private void TickBlasts()
        {
            TickDelayedStrikes();
            for (int i = blasts.Count - 1; i >= 0; i--)
            {
                var b = blasts[i];
                if (Now + 1e-4f < b.At) continue;
                blasts.RemoveAt(i);
                Emit(EventType.BlastDetonated, b.Source != null ? b.Source.Id : null, null, new BlastInfo { Id = b.Id, ActionId = b.Definition.Id, Center = b.Center, Radius = b.Radius, At = b.At });
                if (b.Source == null) continue;
                foreach (var u in State.Units.ToArray())
                {
                    if (!u.IsAlive || u.Team == b.Source.Team) continue;
                    Vector3 d = u.Position - b.Center; d.y = 0f;
                    if (d.magnitude - u.BodyRadius > b.Radius) continue;
                    StrikeNow(new AttackEffect { Source = b.Source, TargetId = u.Id, Definition = b.Definition, IgnoreRange = true, IgnoreCover = true });
                }
            }
        }

        // ------------------------------------------------------------------ tick damage and looks

        /// <summary>Damage with no hit roll (a zone's), credited to 'source': a Damage event and the DamageTaken trigger.</summary>
        public void DealTickDamage(Unit source, Unit target, float amount, ActionDefinition def)
        {
            if (target == null || !target.IsAlive || amount <= 0f || target.HasFlag(StatusFlags.NegatesDamage)) return;
            target.Health -= amount;
            if (source != null) target.LastAttackerId = source.Id;
            target.LastHitAction = def;
            target.LastHitTag = null;
            Emit(EventType.Damage, source != null ? source.Id : null, target.Id, new DamageInfo { ActionId = def != null ? def.Id : null, Amount = amount, Impact = ImpactType.Light, RemainingHealth = target.Health });
            Triggers.Fire(new TriggerContext { Type = TriggerType.DamageTaken, Sim = this, Subject = target, Other = source, Amount = amount, Impact = ImpactType.Light, Action = def });
        }

        /// <summary>Ask the views for a look of an ability's own (AbilityFx): 'kind' names it ("rift", "unmade"...).</summary>
        public void EmitFx(Unit actor, Unit target, string kind, ActionDefinition def, Vector3 center, float radius = 0f, Vector3? from = null)
        {
            Emit(EventType.AbilityFx, actor != null ? actor.Id : null, target != null ? target.Id : null,
                new FxInfo { Kind = kind, ActionId = def != null ? def.Id : null, Center = center, Radius = radius, From = from ?? (actor != null ? actor.Position : center) });
        }
    }
}
