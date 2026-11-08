using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TacticsGame.Core
{
    /// <summary>
    /// The final boss's always-on rules, as passives (UnitDefinition.Passives) so any unit can carry them:
    ///   unravel_resistance - Unravel stuns it for UnravelResistanceScale (0.5) as long.
    ///   freedoms_embrace   - while it lives, the world held for the player's orders runs on a real-time clock
    ///                        (BattleSimulation.FreedomsEmbraceSeconds); when that runs out it takes the moment for itself
    ///                        (BattleSimulation.UnleashFreedomsEmbrace). The presentation keeps the clock (BattleController).
    ///   split_the_minds    - the blow that would kill it instead leaves it at 1 health, Unkillable and Unstoppable, winding up
    ///                        its Split The Minds ability (behavior split_the_minds): its next phase.
    ///   unbound_time       - while one lives, time no longer stops for orders: it creeps (BattleSimulation.DecisionCreepRate).
    /// </summary>
    public static class DeliverancePassives
    {
        public const string UnravelResistance = "unravel_resistance";
        public const string FreedomsEmbrace = "freedoms_embrace";
        public const string SplitTheMinds = "split_the_minds";
        public const string UnboundTime = "unbound_time";

        /// <summary>Unravel's stun on a unit with UnravelResistance is scaled by this.</summary>
        public static float UnravelResistanceScale = 0.5f;

        /// <summary>The status a splitting unit holds through its windup: Unkillable, Unstoppable, no dodging, no stun lands.</summary>
        public const string SplittingStatus = "splitting";
    }

    /// <summary>
    /// Stitch's meld, on the one who melded (BattleSimulation.TickMelds): inside TargetId, untargetable and taking no damage
    /// (the Melded status), healing, until EndsAt or until the target is no longer up in the air; then it dashes out.
    /// </summary>
    public class MeldState : ControlState
    {
        public string TargetId;
        public ActionDefinition Definition;
        public float EndsAt, ExitDash;

        public override bool BlocksDodgeDash { get { return true; } }
        public override bool BlocksTeleport { get { return true; } }
        public override void OnDeath(BattleSimulation sim) { Unit.RemoveState(this); }
    }

    public partial class BattleSimulation
    {
        // ------------------------------------------------------------------ the boss's rule numbers (tunable)

        /// <summary>Freedom's Embrace: real-time seconds the player has to give orders before Deliverance takes the moment.</summary>
        public float FreedomsEmbraceSeconds = 20f;
        /// <summary>Freedom's Embrace: how long it stuns the squad, and how long its user is Unstoppable.</summary>
        public float FreedomsEmbraceStun = 1.5f, FreedomsEmbraceUnstoppable = 1f;
        /// <summary>Phase two (UnboundTime): while orders are given, time runs at DecisionCreepStart of normal speed, rising to
        /// DecisionCreepEnd over DecisionCreepRamp real-time seconds.</summary>
        public float DecisionCreepStart = 0.005f, DecisionCreepEnd = 0.05f, DecisionCreepRamp = 20f;
        /// <summary>Freedom's Embrace: orders that are only plain moves and run for less than this (sim seconds, from them to the
        /// next call for orders) don't reset its clock, so a string of tiny moves can't hold it off; any other order does. The presentation keeps the clock (BattleController).</summary>
        public float FreedomsEmbraceResetAfter = 0.5f;
        /// <summary>Split The Minds: how far from where it stood the parts appear, and how high it rises over the windup.</summary>
        public float SplitSpread = 2.5f, SplitRiseHeight = 6f;
        /// <summary>Split The Minds: as each part appears it dashes its own way out (ScatterLanes), at least PartScatterDistance
        /// from the body where there is the room, over PartScatterSeconds. The pulse's shove takes as long.</summary>
        public float PartScatterDistance = 20f, PartScatterSeconds = 0.5f;

        // ------------------------------------------------------------------ holding on (Unkillable, Split The Minds)

        /// <summary>
        /// ProcessDeaths asks first: does 'u' (at 0 health or less) hold on? Unkillable: back to 1. A boss with Split The Minds
        /// still to come: back to 1, and the split starts (once).
        /// </summary>
        private bool HoldsOn(Unit u)
        {
            if (u.HasFlag(StatusFlags.Unkillable)) { BackToOne(u); return true; }
            if (u.Passives.Contains(DeliverancePassives.SplitTheMinds) && !u.SpentOnce.Contains(DeliverancePassives.SplitTheMinds))
            {
                var split = u.Actions.FirstOrDefault(a => a != null && a.BehaviorId == "split_the_minds");
                if (split == null) return false;
                u.SpentOnce.Add(DeliverancePassives.SplitTheMinds);
                BackToOne(u);
                BeginSplit(u, split);
                return true;
            }
            return false;
        }

        private void BackToOne(Unit u)
        {
            float back = 1f - u.Health;
            u.Health = 1f;
            if (back > 0f) Emit(EventType.Heal, u.Id, u.Id, back);   // the bar shows it holding at 1
        }

        /// <summary>Split The Minds starts: Unkillable, Unstoppable, unable to dodge, and nothing stops the windup. Every other
        /// unit on the map, either side's, is stunned for the windup (nothing resists it), and it rises SplitRiseHeight into
        /// the air over it.</summary>
        private void BeginSplit(Unit u, ActionDefinition split)
        {
            ApplyStatus(u, new StatusEffect
            {
                Id = DeliverancePassives.SplittingStatus, SourceId = u.Id, ExpiresAt = float.MaxValue,
                Flags = StatusFlags.Unkillable | StatusFlags.Unstoppable | StatusFlags.StunImmune,
                Modifiers = new List<Modifier> { Modifier.Mult(StatId.Dodge, 0f) },
            }, u);
            ApplyStatus(u, new StatusEffect { Id = StatusIds.Unkillable, SourceId = u.Id, ExpiresAt = float.MaxValue }, u);   // shown as UNKILLABLE
            ForceAction(u, split, u.Id);
            float windup = u.ActiveAction != null ? Mathf.Max(TickLength, u.ActiveAction.ResolveAt - Now) : TickLength;
            StunAllBut(u, windup);
            HoverUnit(u, Terrain.OnGround(u.Position) + Vector3.up * SplitRiseHeight, float.MaxValue, null, windup, 0f, true);
        }

        /// <summary>Stun every living unit but 'u' for 'seconds', whatever it is immune to (Unstoppable too): Freedom's Embrace
        /// and Split The Minds. A unit still dropping in from the sky is left to land.</summary>
        private void StunAllBut(Unit u, float seconds)
        {
            foreach (var o in State.Units.ToArray())
            {
                if (o == u || !o.IsAlive || o.GetState<DropState>() != null) continue;
                ApplyStun(o, seconds, u, true, true);
            }
        }

        /// <summary>Is 'u' winding up its split (no stun, not even Unravel, stops it)?</summary>
        public bool IsSplitting(Unit u) { return u != null && u.HasStatus(DeliverancePassives.SplittingStatus); }

        /// <summary>
        /// Start 'def' on 'u' now, whatever it was doing (a windup, a charge, a channel, a walk, a recovery, a stun; up in the
        /// air it is set down), paying nothing: a scripted move (Split The Minds).
        /// </summary>
        public void ForceAction(Unit u, ActionDefinition def, string targetId)
        {
            if (u == null || !u.IsAlive || def == null) return;
            if (u.Phase == UnitPhase.Windup || u.Phase == UnitPhase.Charging) Interrupt(u, "overtaken", false);
            if (u.Phase == UnitPhase.Channeling) EndChannel(u, "overtaken", false);
            if (u.Phase == UnitPhase.Moving) HaltWalk(u);
            ClearChase(u); ClearQueued(u, null);
            if (u.Grapple != null) EndGrapple(u);
            if (u.HeldById != null) ReleaseHeld(u);
            ClearHover(u);
            u.RemoveState<LiftState>();
            if (u.IsJumping) { u.Position = u.JumpTo; u.JumpEnd = -1f; }
            if (u.FallLandAt >= 0f || u.GlideEnd >= 0f) { u.Position = Terrain.OnGround(u.GlideEnd >= 0f ? u.GlideTo : u.Position); u.FallLandAt = -1f; u.GlideEnd = -1f; }
            u.ResumeAction = null;
            pendingDashes.RemoveAll(d => d.UnitId == u.Id);
            u.Phase = UnitPhase.Ready;
            var behavior = ActionBehaviors.Get(def.BehaviorId);
            StartAction(u, UnitCommand.UseAction(u.Id, def, targetId), behavior, WindupOf(u, def));   // a "resumed" start: no costs
        }

        /// <summary>
        /// Split The Minds lands: the parts (the ability's SummonUnits) appear around 'u' on its side, on the ground under it,
        /// and each dashes out its own way (ScatterLanes). A pulse takes 'healthShare' of its maximum health from every unit on
        /// the field that is neither the squad's nor 'u''s, and shoves every other unit, the squad's too, the ability's
        /// DisplaceDistance straight away from 'u'. The endless reinforcements stop, and 'u' itself is gone.
        /// </summary>
        public void SplitTheMinds(Unit u, ActionDefinition def, float healthShare)
        {
            if (u == null) return;
            EmitFx(u, null, "split", def, u.Position, 30f);
            var parts = def.SummonUnits != null ? def.SummonUnits.Where(p => p != null).ToList() : new List<UnitDefinition>();
            Vector3 body = Terrain.OnGround(u.Position);   // it hangs in the air: the parts land below
            var lanes = ScatterLanes(body, parts.Count);
            for (int i = 0; i < parts.Count; i++)
            {
                var dir = lanes[i];
                SpawnUnit(parts[i], u.Team, body + dir * SplitSpread, u, null, p => SlideUnitTo(p, ScatterEnd(body, dir), PartScatterSeconds));
            }
            StopReinforcementWaves(true);
            drops.RemoveAll(d => d.Team != Team.Player && d.Team != u.Team);
            float shove = u.ActionStat(def, ActionStat.DisplaceDistance);
            foreach (var o in State.Units.ToArray())
            {
                if (o == u || !o.IsAlive) continue;
                if (o.Team != Team.Player && o.Team != u.Team) PulseDamage(u, o, def, healthShare);   // every enemy that isn't the squad
                if (!o.IsAlive || o.GetState<DropState>() != null) continue;   // still in the sky: nothing to shove yet
                Vector3 away = o.Position - body; away.y = 0f;
                PushUnit(o, o.Position + (away.sqrMagnitude > 1e-6f ? away.normalized : Vector3.forward) * shove, PartScatterSeconds, u);
            }
            // It is gone into its parts: not a kill anyone made.
            RemoveStatus(u, DeliverancePassives.SplittingStatus);
            RemoveStatus(u, StatusIds.Unkillable);
            u.Health = 0f;
            u.LastAttackerId = u.Id; u.LastHitAction = def; u.LastHitTag = "split";
        }

        /// <summary>The pulse's blow on 'o': 'share' of its maximum health, whatever its guard. It may kill.</summary>
        private void PulseDamage(Unit u, Unit o, ActionDefinition def, float share)
        {
            float amount = Mathf.Min(o.Health, o.MaxHealth * share);
            if (amount >= o.Health) o.RemoveState<DropState>();   // killed on its way down: it never lands
            o.Health -= amount;
            o.LastAttackerId = u.Id; o.LastHitAction = def; o.LastHitTag = "split";
            Emit(EventType.Damage, u.Id, o.Id, new DamageInfo { ActionId = def.Id, Amount = amount, Impact = ImpactType.Heavy, RemainingHealth = o.Health });
        }

        /// <summary>Where a part dashing out of 'body' along 'dir' stops: a little past PartScatterDistance, or at the map's edge.</summary>
        private Vector3 ScatterEnd(Vector3 body, Vector3 dir) { return Paths.ClampDash(body, body + dir * (PartScatterDistance + 2f)); }

        /// <summary>
        /// The way each of 'count' parts dashes out of 'body', no two the same. Each starts from its own share of the circle
        /// and takes the nearest direction with PartScatterDistance of room; where none has, the one with the most room. A
        /// direction taken closes its neighbours, so the parts stay at least 45 degrees apart even in a corner.
        /// </summary>
        private List<Vector3> ScatterLanes(Vector3 body, int count)
        {
            const int Rays = 16;
            var dirs = new Vector3[Rays]; var reach = new float[Rays]; var closed = new bool[Rays];
            for (int k = 0; k < Rays; k++)
            {
                float ang = k * Mathf.PI * 2f / Rays + Mathf.PI / 4f;
                dirs[k] = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                Vector3 off = ScatterEnd(body, dirs[k]) - body; off.y = 0f;
                reach[k] = off.magnitude;
            }
            var lanes = new List<Vector3>(count);
            for (int i = 0; i < count; i++)
            {
                int home = Mathf.RoundToInt(i * (float)Rays / count) % Rays, best = -1;
                for (int t = 0; t < Rays; t++)
                {
                    int k = (home + ((t + 1) / 2) * (t % 2 == 0 ? 1 : -1) + Rays) % Rays;   // home, one to the right, one to the left, ...
                    if (closed[k]) continue;
                    if (reach[k] >= PartScatterDistance) { best = k; break; }
                    if (best < 0 || reach[k] > reach[best]) best = k;
                }
                if (best < 0) best = home;   // more parts than directions
                closed[(best + Rays - 1) % Rays] = closed[best] = closed[(best + 1) % Rays] = true;
                lanes.Add(dirs[best]);
            }
            return lanes;
        }

        // ------------------------------------------------------------------ Freedom's Embrace

        /// <summary>The living unit whose Freedom's Embrace runs the order clock (null: none; the world waits as usual).</summary>
        public Unit FreedomsEmbraceHolder()
        {
            return State.Units.FirstOrDefault(u => u.IsAlive && u.Passives.Contains(DeliverancePassives.FreedomsEmbrace) && !IsSplitting(u));
        }

        /// <summary>
        /// The order clock ran out: 'u' takes the moment. Every other unit on the map, either side's, is stunned
        /// (FreedomsEmbraceStun; nothing resists it, Unstoppable included), the squad's unfinished orders are dropped, and 'u'
        /// is Unstoppable for FreedomsEmbraceUnstoppable and free to act at once.
        /// </summary>
        public void UnleashFreedomsEmbrace(Unit u)
        {
            if (u == null || !u.IsAlive || Outcome != BattleOutcome.InProgress) return;
            EmitFx(u, null, "freedoms_embrace", null, u.Position, 30f);
            foreach (var p in State.Units.ToArray())
            {
                if (p == u || !p.IsAlive) continue;
                queues.Remove(p.Id);
                pendingDashes.RemoveAll(d => d.UnitId == p.Id);
            }
            StunAllBut(u, FreedomsEmbraceStun);
            ApplyStatus(u, new StatusEffect { Id = StatusIds.Unstoppable, SourceId = u.Id, ExpiresAt = Now + FreedomsEmbraceUnstoppable, Flags = StatusFlags.Unstoppable }, u);
            if (u.Phase == UnitPhase.Recovery || u.Phase == UnitPhase.Stunned) { u.PhaseEndsAt = Now; u.Phase = UnitPhase.Ready; u.Waiting = false; }
        }

        // ------------------------------------------------------------------ phase two: time creeps while orders are given

        /// <summary>Does time keep moving while the player gives orders (a living UnboundTime unit)?</summary>
        public bool DecisionCreepActive { get { return State.Units.Any(u => u.IsAlive && u.Passives.Contains(DeliverancePassives.UnboundTime)); } }

        /// <summary>The share of normal speed time runs at, 'heldSeconds' (real time) into giving orders.</summary>
        public float DecisionCreepRate(float heldSeconds)
        {
            return Mathf.Lerp(DecisionCreepStart, DecisionCreepEnd, Mathf.Clamp01(heldSeconds / Mathf.Max(0.01f, DecisionCreepRamp)));
        }

        private float creepCarry;

        /// <summary>
        /// Run the world on for 'simSeconds' while the player's orders are still being given: everyone else carries on (the
        /// AI decides as usual), the squad's units waiting for orders stand where they are, and a dodge one of them makes is
        /// dashed for them (away from the attacker). Their orders, once given, start where the world is then.
        /// </summary>
        public void CreepWhileDeciding(float simSeconds)
        {
            if (Outcome != BattleOutcome.InProgress || simSeconds <= 0f) return;
            creepCarry += simSeconds;
            while (creepCarry >= TickLength && Outcome == BattleOutcome.InProgress)
            {
                creepCarry -= TickLength;
                ProcessReadyUnits(true);
                Step();
            }
        }

        // ------------------------------------------------------------------ after a unit settles

        private readonly List<KeyValuePair<string, System.Action>> whenSettled = new List<KeyValuePair<string, System.Action>>();

        /// <summary>
        /// Run 'act' once 'u' has settled: no dodge direction still to pick and no dash or shove under way (at once if it
        /// already has). For what has to happen where a unit ends up (Close The Loop teleporting to a target that dodged).
        /// Dropped if the unit dies first.
        /// </summary>
        public void WhenSettled(Unit u, System.Action act)
        {
            if (u == null || act == null) return;
            whenSettled.Add(new KeyValuePair<string, System.Action>(u.Id, act));
        }

        private void TickSettled()
        {
            for (int i = 0; i < whenSettled.Count; i++)
            {
                var u = GetUnit(whenSettled[i].Key);
                if (u != null && u.IsAlive && (IsGliding(u) || pendingDashes.Exists(d => d.UnitId == u.Id))) continue;
                var act = whenSettled[i].Value;
                whenSettled.RemoveAt(i--);
                if (u != null && u.IsAlive) act();
            }
        }

        // ------------------------------------------------------------------ Stitch's meld

        /// <summary>
        /// Stitch landed on 'target' and lifted it: 'u' melds into it for 'seconds' (untargetable, taking no damage, healing
        /// 'healPerSecond'), then dashes 'exitDash' out of it.
        /// </summary>
        public void BeginMeld(Unit u, Unit target, ActionDefinition def, float seconds, float healPerSecond, float exitDash)
        {
            if (u == null || target == null || !u.IsAlive || !target.IsAlive || !target.IsLifted || u.GetState<MeldState>() != null) return;
            float ends = Now + Mathf.Max(TickLength, seconds);
            u.AddState(new MeldState { TargetId = target.Id, Definition = def, EndsAt = ends, ExitDash = exitDash });
            ApplyStatus(u, new StatusEffect
            {
                Id = StatusIds.Melded, SourceId = u.Id, ExpiresAt = float.MaxValue, HealPerSecond = healPerSecond,
                Flags = StatusFlags.Untargetable | StatusFlags.NegatesDamage | StatusFlags.StunImmune | StatusFlags.Unstoppable,
            }, u);
            if (u.Phase == UnitPhase.Moving) HaltWalk(u);
            u.Phase = UnitPhase.Recovery;   // nothing else while inside it
            u.PhaseEndsAt = float.MaxValue;
            u.Waiting = false;
            EmitFx(u, target, "meld", def, target.Position);
        }

        private void TickMelds()
        {
            foreach (var u in State.Units.ToArray())
            {
                var meld = u.GetState<MeldState>();
                if (meld == null) continue;
                var target = GetUnit(meld.TargetId);
                bool over = !u.IsAlive || Now + 1e-4f >= meld.EndsAt || target == null || !target.IsAlive || !target.IsLifted;   // knocked out of the air: out early
                if (over) EndMeld(u, meld, target);
            }
        }

        private void EndMeld(Unit u, MeldState meld, Unit target)
        {
            u.RemoveState(meld);
            if (u.HasStatus(StatusIds.Melded)) RemoveStatus(u, StatusIds.Melded);
            if (!u.IsAlive) return;
            u.PhaseEndsAt = Now;   // free again at the end of this tick
            EmitFx(u, target, "unmeld", meld.Definition, u.Position);
            // Out of it, away from the nearest enemy.
            var threat = NearestEnemy(u);
            Vector3 d = threat != null ? u.Position - threat.Position : Vector3.back; d.y = 0f;
            d = d.sqrMagnitude > 1e-6f ? d.normalized : Vector3.back;
            if (meld.ExitDash > 0f) SlideUnitTo(u, WalkablePointNear(u.Position + d * meld.ExitDash, u.Position), DashDuration);
        }
    }
}
