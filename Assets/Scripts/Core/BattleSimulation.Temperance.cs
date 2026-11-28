using System.Collections.Generic;
using UnityEngine;

namespace TacticsGame.Core
{
    /// <summary>
    /// BattleSimulation: Temperance, the ship that comes over the map (StartTemperance: an encounter's EncounterSpec.Temperance).
    /// It arrives TemperanceArrivesAt (60s in by default; an Omen's CONVERGE REALITIES brings it sooner: HastenTemperance).
    /// From then on:
    ///   every TemperanceBlastEvery seconds an explosion is placed: centred on a random living unit (any side), moved
    ///   0..TemperanceBlastSpread metres in a random direction; it goes off TemperanceBlastDelay later, hitting everyone in
    ///   TemperanceBlastRadius (TemperanceBlastDamage, less Block; TemperanceBlastStun). It can be dodged like any blast.
    ///   a squad unit that takes damage below the execution threshold (TemperanceExecuteThreshold of its max health at arrival,
    ///   TemperanceExecuteGrowth more each second, up to all of it) is marked by a beam: TemperanceExecuteWindup later it dies,
    ///   whatever happens meanwhile (no dodge, no Block, healed or not).
    /// What the screen shows comes from the TemperanceTimer / TemperanceArrived / TemperanceExecution events.
    /// </summary>
    public partial class BattleSimulation
    {
        public float TemperanceBlastEvery = 1f, TemperanceBlastDelay = 0.75f, TemperanceBlastRadius = 9f, TemperanceBlastSpread = 10f;
        public float TemperanceBlastDamage = 20f, TemperanceBlastStun = 1f;
        /// <summary>Seconds before it arrives that the warning (the screen brightening, TEMPERANCE APPROACHES) starts.</summary>
        public float TemperanceWarningSeconds = 15f;
        /// <summary>Execution threshold: this share of max health at arrival, plus this much more every second after.</summary>
        public float TemperanceExecuteThreshold = 0.15f, TemperanceExecuteGrowth = 0.01f;
        public float TemperanceExecuteWindup = 1f;

        public const string TemperanceBlastId = "temperance_blast", TemperanceExecutionId = "temperance_execution";

        /// <summary>Temperance is coming (or here) this battle.</summary>
        public bool TemperanceComing { get; private set; }
        public bool TemperanceArrived { get; private set; }
        public float TemperanceArrivesAt { get; private set; } = -1f;
        private float temperanceStartedAt, nextTemperanceBlastAt;
        private bool temperanceWatching;
        private readonly Dictionary<string, float> executions = new Dictionary<string, float>();

        /// <summary>Temperance arrives 'arriveAfter' seconds from now.</summary>
        public void StartTemperance(float arriveAfter)
        {
            TemperanceComing = true;
            TemperanceArrived = false;
            temperanceStartedAt = Now;
            TemperanceArrivesAt = Now + Mathf.Max(0f, arriveAfter);
            EmitTemperance(EventType.TemperanceTimer, null);
            if (temperanceWatching) return;
            temperanceWatching = true;
            Triggers.Subscribe(TriggerType.DamageTaken, ctx => MarkForExecution(ctx.Subject));
        }

        /// <summary>CONVERGE REALITIES: Temperance comes 'seconds' sooner (not before now). Nothing once it is here.</summary>
        public void HastenTemperance(Unit by, float seconds)
        {
            if (!TemperanceComing || TemperanceArrived || seconds <= 0f) return;
            TemperanceArrivesAt = Mathf.Max(Now, TemperanceArrivesAt - seconds);
            EmitTemperance(EventType.TemperanceTimer, by);
        }

        /// <summary>The share of max health below which damage gets a squad unit executed now (0 before Temperance arrives).</summary>
        public float ExecutionThreshold
        {
            get { return TemperanceArrived ? Mathf.Clamp01(TemperanceExecuteThreshold + TemperanceExecuteGrowth * (Now - TemperanceArrivesAt)) : 0f; }
        }

        /// <summary>Is a beam already on its way to 'u'?</summary>
        public bool MarkedForExecution(Unit u) { return u != null && executions.ContainsKey(u.Id); }

        private void EmitTemperance(EventType type, Unit by)
        {
            Emit(type, by != null ? by.Id : null, null, new TemperanceInfo
            {
                StartedAt = temperanceStartedAt, ArrivesAt = TemperanceArrivesAt, WarningSeconds = TemperanceWarningSeconds,
                ThresholdStart = TemperanceExecuteThreshold, ThresholdPerSecond = TemperanceExecuteGrowth,
            });
        }

        private void TickTemperance()
        {
            if (!TemperanceComing || Outcome != BattleOutcome.InProgress) return;
            if (!TemperanceArrived)
            {
                if (Now + 1e-4f < TemperanceArrivesAt) return;
                TemperanceArrived = true;
                TemperanceArrivesAt = Now;
                nextTemperanceBlastAt = Now;
                EmitTemperance(EventType.TemperanceArrived, null);
            }
            if (Now + 1e-4f >= nextTemperanceBlastAt)
            {
                nextTemperanceBlastAt = Now + Mathf.Max(TickLength, TemperanceBlastEvery);
                PlaceTemperanceBlast();
            }
            if (executions.Count == 0) return;
            var due = new List<string>();
            foreach (var kv in executions) if (Now + 1e-4f >= kv.Value) due.Add(kv.Key);
            foreach (var id in due) { executions.Remove(id); Execute(GetUnit(id)); }
        }

        /// <summary>An explosion around a random living unit (any side), up to TemperanceBlastSpread off it.</summary>
        private void PlaceTemperanceBlast()
        {
            var alive = State.Units.FindAll(u => u.IsAlive && !IsVoided(u));
            if (alive.Count == 0) return;
            var on = alive[rng.Next(alive.Count)];
            float angle = (float)rng.NextDouble() * Mathf.PI * 2f, off = (float)rng.NextDouble() * TemperanceBlastSpread;
            Vector3 at = on.Position + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * off;
            at = WalkablePointNear(at, on.Position);   // never off the map
            var b = new Blast
            {
                Id = "blast_" + (++blastCounter), World = true, ActionId = TemperanceBlastId, Center = Terrain.OnGround(at), Radius = TemperanceBlastRadius,
                At = Now + TemperanceBlastDelay, Damage = TemperanceBlastDamage, Stun = TemperanceBlastStun,
            };
            blasts.Add(b);
            Emit(EventType.BlastPending, null, null, new BlastInfo { Id = b.Id, ActionId = b.ActionId, Center = b.Center, Radius = b.Radius, At = b.At });
        }

        /// <summary>A world blast (Temperance's) lands on 'u': its damage less Block (none through a Transcended Block), and its stun.</summary>
        private void WorldBlastHits(Blast b, Unit u)
        {
            if (!u.HasFlag(StatusFlags.NegatesDamage)) DealTickDamage(null, u, b.Damage * (1f - u.EffectiveBlock()), null, b.ActionId);
            if (u.IsAlive) StunUnit(u, b.Stun, null);
        }

        /// <summary>Damage below the threshold marks a squad unit for execution (once: the beam that is coming is enough).</summary>
        private void MarkForExecution(Unit u)
        {
            if (!TemperanceArrived || u == null || !u.IsAlive || u.Health <= 0f || executions.ContainsKey(u.Id)) return;
            if (u.Team != Team.Player && u.HomeTeam != Team.Player) return;
            if (u.Health >= u.MaxHealth * ExecutionThreshold) return;
            executions[u.Id] = Now + TemperanceExecuteWindup;
            Emit(EventType.TemperanceExecution, null, u.Id, new ExecutionInfo { At = Now + TemperanceExecuteWindup });
        }

        /// <summary>The beam lands: the unit dies, whatever it is doing (only out of the world, VOIDED, is it spared).</summary>
        private void Execute(Unit u)
        {
            if (u == null || !u.IsAlive || IsVoided(u)) return;
            u.Statuses.RemoveAll(s => s.Has(StatusFlags.Unkillable));
            float amount = Mathf.Max(0f, u.Health);
            u.Health = 0f;
            u.LastAttackerId = null;
            u.LastHitAction = null;
            u.LastHitTag = TemperanceExecutionId;
            Emit(EventType.Damage, null, u.Id, new DamageInfo { ActionId = TemperanceExecutionId, Amount = amount, Impact = ImpactType.Heavy, RemainingHealth = 0f });
        }
    }
}
