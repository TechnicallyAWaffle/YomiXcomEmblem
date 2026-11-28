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

        // World: nobody's (Temperance's): it hits every unit in it for Damage and Stun; ActionId names it.
        private class Blast { public string Id; public Unit Source; public ActionDefinition Definition; public Vector3 Center; public float Radius, At; public bool DodgesRolled; public bool World; public float Damage, Stun; public string ActionId; }

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
        private class DelayedStrike { public AttackEffect Attack; public float At; public string FromId, FxKind; public bool SourceMayBeDown; }
        private readonly List<DelayedStrike> delayedStrikes = new List<DelayedStrike>();

        /// <summary>
        /// Land 'attack' 'delay' seconds from now (StrikeNow then: the target's dodge as it is then). 'from': the unit it seems to
        /// come out of; 'fxKind': the AbilityFx the views get as it goes (from that unit to the target).
        /// </summary>
        /// <param name="sourceMayBeDown">It lands even if its source has fallen by then (a reflected share: the blow was already struck).</param>
        public void ScheduleStrike(AttackEffect attack, float delay, Unit from = null, string fxKind = null, bool sourceMayBeDown = false)
        {
            if (attack == null) return;
            delayedStrikes.Add(new DelayedStrike { Attack = attack, At = Now + Mathf.Max(0f, delay), FromId = from != null ? from.Id : null, FxKind = fxKind, SourceMayBeDown = sourceMayBeDown });
        }

        // Something an ability does a moment later (Opening Theorem's bound once its dash arrives): run at At, if Unit is still alive.
        private class Delayed { public Unit Unit; public float At; public System.Action Act; }
        private readonly List<Delayed> delayed = new List<Delayed>();

        /// <summary>Run 'act' 'delay' seconds from now, on the simulation's clock, unless 'unit' has died by then.</summary>
        public void After(Unit unit, float delay, System.Action act)
        {
            if (act != null) delayed.Add(new Delayed { Unit = unit, At = Now + Mathf.Max(0f, delay), Act = act });
        }

        private void TickDelayedStrikes()
        {
            for (int i = 0; i < delayed.Count; i++)
            {
                var d = delayed[i];
                if (Now + 1e-4f < d.At) continue;
                delayed.RemoveAt(i--);
                if (d.Unit == null || d.Unit.IsAlive) d.Act();
            }
            for (int i = delayedStrikes.Count - 1; i >= 0; i--)
            {
                var d = delayedStrikes[i];
                if (Now + 1e-4f < d.At) continue;
                delayedStrikes.RemoveAt(i);
                var target = GetUnit(d.Attack.TargetId);
                if (target == null || !target.IsAlive || d.Attack.Source == null) continue;
                var from = GetUnit(d.FromId);
                if (d.FxKind != null) EmitFx(from ?? d.Attack.Source, target, d.FxKind, d.Attack.Definition, target.Position, 0f, (from ?? d.Attack.Source).Position);
                StrikeNow(d.Attack, d.SourceMayBeDown);
            }
        }

        private void TickBlasts()
        {
            TickDelayedStrikes();
            for (int i = blasts.Count - 1; i >= 0; i--)
            {
                var b = blasts[i];
                if (!b.DodgesRolled && (b.Source != null || b.World) && b.At - Now <= DodgeLead + 1e-4f)
                {
                    // About to go off: those in it roll their dodge (a dash out of it; away from the centre for a world blast).
                    b.DodgesRolled = true;
                    foreach (var u in State.Units.ToArray())
                    {
                        if (!u.IsAlive || (!b.World && u.Team == b.Source.Team)) continue;
                        Vector3 d = u.Position - b.Center; d.y = 0f;
                        if (d.magnitude - u.BodyRadius <= b.Radius) RollDodgeAgainst(b.World ? null : b.Source, u, b.Definition, b.Center);
                    }
                }
                if (Now + 1e-4f < b.At) continue;
                blasts.RemoveAt(i);
                Emit(EventType.BlastDetonated, b.Source != null ? b.Source.Id : null, null, new BlastInfo { Id = b.Id, ActionId = b.ActionId ?? b.Definition.Id, Center = b.Center, Radius = b.Radius, At = b.At });
                if (b.Source == null && !b.World) continue;
                foreach (var u in State.Units.ToArray())
                {
                    if (!u.IsAlive || IsVoided(u) || (!b.World && u.Team == b.Source.Team)) continue;
                    Vector3 d = u.Position - b.Center; d.y = 0f;
                    if (d.magnitude - u.BodyRadius > b.Radius) continue;
                    if (b.World) WorldBlastHits(b, u);
                    else StrikeNow(new AttackEffect { Source = b.Source, TargetId = u.Id, Definition = b.Definition, IgnoreRange = true, IgnoreCover = true });
                }
            }
        }

        // ------------------------------------------------------------------ tick damage and looks

        /// <summary>Damage with no hit roll (a zone's), credited to 'source': a Damage event and the DamageTaken trigger.</summary>
        /// <summary>
        /// What 'summoner' etched onto 'via' (the ability that brought 'summoned' in: Termoul's Monolith, Recollections of
        /// Self) goes on every ability of the summoned unit (ActionBehavior.ActsThrough): Summation on the summon makes The
        /// Monolith's crash hit 30% harder. Only Etchings on that ability (its UpgradeActionModifiers for it), not the
        /// summoner's unit-wide ones or its statuses.
        /// </summary>
        public void InheritEtchings(Unit summoner, ActionDefinition via, Unit summoned)
        {
            if (summoner == null || via == null || summoned == null || summoner.UpgradeActionModifiers == null) return;
            foreach (var m in summoner.UpgradeActionModifiers)
                if (!string.IsNullOrEmpty(m.ActionId) && m.ActionId == via.Id)
                    summoned.UpgradeActionModifiers.Add(new ActionModifier("", m.Stat, m.Op, m.Value));
        }

        /// <param name="actionId">What the damage is called with no ability behind it (a world blast: Temperance's).</param>
        public void DealTickDamage(Unit source, Unit target, float amount, ActionDefinition def, string actionId = null)
        {
            if (target == null || !target.IsAlive || amount <= 0f || target.HasFlag(StatusFlags.NegatesDamage)) return;
            amount -= AbsorbWithShields(target, amount);
            if (amount <= 1e-4f) return;
            float asStrain;
            float lost = TakeAsStrain(target, amount, out asStrain);   // Strange Axis
            target.Health -= lost;
            if (source != null) target.LastAttackerId = source.Id;
            target.LastHitAction = def;
            target.LastHitTag = null;
            Emit(EventType.Damage, source != null ? source.Id : null, target.Id, new DamageInfo { ActionId = def != null ? def.Id : actionId, Amount = lost, Impact = ImpactType.Light, RemainingHealth = target.Health, AsStrain = asStrain });
            Triggers.Fire(new TriggerContext { Type = TriggerType.DamageTaken, Sim = this, Subject = target, Other = source, Amount = amount, Impact = ImpactType.Light, Action = def });
        }

        /// <summary>
        /// Shields on 'u' (StatusEffect.Shield) soak up 'amount' of damage, oldest first; one used up ends its status. Returns
        /// how much they took.
        /// </summary>
        public float AbsorbWithShields(Unit u, float amount)
        {
            if (u == null || amount <= 0f) return 0f;
            float took = 0f;
            List<string> spent = null;
            foreach (var s in u.Statuses)
            {
                if (s.Shield <= 0f || took >= amount) continue;
                float t = Mathf.Min(s.Shield, amount - took);
                s.Shield -= t; took += t;
                if (s.Shield <= 1e-4f) (spent ?? (spent = new List<string>())).Add(s.Id);
            }
            if (spent != null) foreach (var id in spent) RemoveStatus(u, id);
            return took;
        }

        private readonly Dictionary<string, float> shieldsShown = new Dictionary<string, float>();

        /// <summary>Every tick: a ShieldChanged for each unit whose shields (Unit.ShieldTotal) are not what was last told.</summary>
        private void TickShields()
        {
            foreach (var u in State.Units)
            {
                float now = u.IsAlive ? u.ShieldTotal() : 0f, was;
                if (!shieldsShown.TryGetValue(u.Id, out was)) was = 0f;
                if (Mathf.Abs(now - was) < 0.01f) continue;
                shieldsShown[u.Id] = now;
                Emit(EventType.ShieldChanged, u.Id, null, now);
            }
        }

        /// <summary>Ask the views for a look of an ability's own (AbilityFx): 'kind' names it ("rift", "unmade"...).</summary>
        public void EmitFx(Unit actor, Unit target, string kind, ActionDefinition def, Vector3 center, float radius = 0f, Vector3? from = null)
        {
            Emit(EventType.AbilityFx, actor != null ? actor.Id : null, target != null ? target.Id : null,
                new FxInfo { Kind = kind, ActionId = def != null ? def.Id : null, Center = center, Radius = radius, From = from ?? (actor != null ? actor.Position : center) });
        }
    }
}
