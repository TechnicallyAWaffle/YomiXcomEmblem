using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TacticsGame.Core
{
    /// <summary>BattleSimulation: The tick: movement, traps, windups, resolving actions, deaths and timers.</summary>
    public partial class BattleSimulation
    {
        // ------------------------------------------------------------------ the tick

        private void Step()
        {
            tick++;
            TickFocus();
            TickLifts();
            TickAerialSlams();
            AdvanceGlides();
            TickCompositions();   // Compositions of Destruction drifting on, striking, or going off once displaced
            TickSweeps();     // after glides: a thrown unit's front is where it has flown to
            AdvanceMovement();
            TickGrapples();   // after movement, so the held unit is carried to where the grappler walked
            TickWalls();      // after movement: who is inside a wall now (Obstruction)
            TickTethers();    // after everyone has moved: chains pulled past their length snap
            TickEmitters();     // volleys and streams firing (BattleSimulation.Projectiles.cs)
            TickProjectiles();  // after everyone has moved: what flies meets the bodies where they are now
            TickOutOfRangeOrders();
            TickCharges();
            TickAuras();
            TickInnatePassives();   // always-on abilities that act every tick (Imperfect Apotheosis)
            TickChannels();
            CheckTraps();
            TickWindups();
            RollIncomingDodges();   // DodgeLead before an attack lands, the units it would hit roll their dodge (a dash out of its way)
            ResolveDueActions();
            ProcessDeaths();
            ExpireTimers();
            TickForsaken();   // Entropy at max: NEUROSIS; Diverged: Entropy draining, HYSTERIA building, REGRESSION at 0
            RecoverStrain();
            TickHealOverTime();
            TickBlasts();
            TickTemperance();   // Temperance's arrival, explosions and executions
            TickDrops();   // reinforcements arriving (EnemyEntry.ArriveAfter)
            TickReinforcementWaves();   // endless random reinforcements (StartReinforcementWaves)
            TickMelds();   // Stitch
            TickWeakpoints();
            TickVoids();     // VOIDED units coming back, Cross Beyond Thresholds' mark
            TickCompelled(); // Beckon's walks running out
            TickMakina();    // a Makina at max strain breaks
            TickSettled();   // what waits for a unit to land (Close The Loop on a dodger)
            TickParts();     // Deliverance's parts: SEE BEFORE ITSELF, EQUIPOSE at max strain, Wex's aura
            TickWatchers();  // AIs that keep watch between decisions (a pack noticing an opening)
            TickEvasion();   // enemies noticing area attacks and projectiles on them, and waking to get out of the way
            TickShields();   // last of the rules: what a unit's shields came to this tick, for the health bars
            AddSpawns();
            EtchingTraits.Tick(this);
        }

        /// <summary>Is this tick one of every 'ticks' (for rules that run in steps)?</summary>
        public bool IsStep(int ticks) { return ticks <= 1 || tick % ticks == 0; }

        private void AdvanceMovement()
        {
            foreach (var u in State.Units)
            {
                if (!IsWalking(u)) continue;
                if (u.Move.Flying)
                {
                    FlyStep(u);
                    if (u.Move != null && u.Move.NextIndex >= u.Move.Path.Count) EndMove(u, false);   // up there: no bodies to settle out of
                    continue;
                }
                if (u.IsJumping)
                {
                    float k = Mathf.Clamp01((Now - u.JumpStart) / Mathf.Max(1e-4f, u.JumpEnd - u.JumpStart));
                    Vector3 p = Vector3.Lerp(u.JumpFrom, u.JumpTo, k);
                    p.y = TerrainMap.JumpY(u.JumpFrom, u.JumpTo, k);
                    u.Position = p;
                    if (k < 1f) continue;
                    u.Position = u.JumpTo;
                    u.JumpEnd = -1f;
                    if (u.Move.NextIndex < u.Move.Path.Count)
                    {
                        // Landed: the walk carries on from here.
                        var rest = new List<Vector3> { u.Position };
                        for (int i = u.Move.NextIndex; i < u.Move.Path.Count; i++) rest.Add(u.Move.Path[i]);
                        Emit(EventType.MoveStarted, u.Id, null, new MoveInfo { Path = rest, Speed = u.Move.Speed });
                    }
                }
                else WalkStep(u);
                if (!u.IsJumping && u.Move.NextIndex >= u.Move.Path.Count && u.Chase == null) EndMove(u);   // a chaser waits there for its next re-path
            }
        }

        /// <summary>
        /// One tick of walking along the path, on the ground. Where the ground ahead changes height by more than
        /// TerrainMap.StepHeight the unit stops a body's width short of the edge and jumps it (StartJump), landing a
        /// body's width past it; the walk resumes once it lands.
        /// </summary>
        /// <summary>How far from a floating platform's edge a unit jumps up onto it.</summary>
        public const float ClimbReach = 1.5f;

        private void WalkStep(Unit u)
        {
            float remaining = u.Move.Speed * TickLength;
            while (remaining > 1e-6f && u.Move.NextIndex < u.Move.Path.Count)
            {
                Vector3 pos = u.Position;
                Vector3 next = u.Move.Path[u.Move.NextIndex];
                Vector3 d = next - pos; d.y = 0f;
                float dist = d.magnitude;
                if (dist < 1e-5f) { u.Position = Terrain.OnGround(next); u.Move.NextIndex++; continue; }
                d /= dist;
                float step = Mathf.Min(dist, remaining);
                float ground = Terrain.HeightAt(pos);
                // A corner up on a floating platform (a route onto one, TerrainPaths): walking would pass under it, so once
                // beside it the unit jumps up onto it.
                if (next.y > ground + TerrainMap.StepHeight && next.y > Terrain.HeightAt(TerrainMap.Level(next, ground)) + TerrainMap.StepHeight
                    && dist <= ClimbReach)
                {
                    u.Move.NextIndex++;
                    StartJump(u, TerrainMap.Level(pos, ground), Terrain.OnGround(next));
                    return;
                }
                // Look a body radius ahead (within this leg), so the unit takes off before its body meets the ledge.
                float reach = step + Mathf.Min(u.BodyRadius, dist - step);
                float edge = Terrain.FirstEdge(pos, pos + d * reach, ground);
                if (edge >= 0f)
                {
                    float takeoff = Mathf.Clamp(edge - u.BodyRadius, 0f, step);
                    float land = Mathf.Min(edge + u.BodyRadius, dist);
                    if (land >= dist - 1e-4f) u.Move.NextIndex++;
                    Vector3 from = pos + d * takeoff; from.y = ground;
                    StartJump(u, from, Terrain.OnGround(pos + d * land));
                    return;
                }
                u.Position = Terrain.OnGround(pos + d * step);
                remaining -= step;
                if (step >= dist) u.Move.NextIndex++;
            }
        }

        /// <summary>Armed traps spring on the first unit of another team inside them; unsprung ones expire.</summary>
        private void CheckTraps()
        {
            for (int i = State.Traps.Count - 1; i >= 0; i--)
            {
                var t = State.Traps[i];
                if (t.ExpiresAt <= Now)
                {
                    State.Traps.RemoveAt(i);
                    Emit(EventType.TrapRemoved, t.OwnerId, null, t.Id);
                    continue;
                }
                if (Now + 1e-4f < t.ArmsAt) continue;

                Unit victim = null;
                foreach (var u in State.Units)
                {
                    if (!u.IsAlive || u.Team == t.OwnerTeam) continue;
                    Vector3 d = u.Position - t.Position; d.y = 0f;
                    if (d.magnitude <= t.Radius + u.BodyRadius) { victim = u; break; }
                }
                if (victim == null) continue;

                State.Traps.RemoveAt(i);
                Emit(EventType.TrapTriggered, t.OwnerId, victim.Id, t.Id);
                var owner = GetUnit(t.OwnerId);
                foreach (var spec in t.Statuses) ApplyStatus(victim, spec.Create(Now, t.OwnerId, null, t.DurationScale), owner);
            }
        }

        private void EndMove(Unit u, bool settle = true)
        {
            u.Move = null;
            ClearChase(u);   // cancelled, rooted or dashed out of a chase: it is over
            Emit(EventType.MoveEnded, u.Id, null, new MoveEndedInfo { Position = u.Position });
            if (settle) SettleOutOfBodies(u);
            if (u.Phase == UnitPhase.Charging) { u.ChargeDecisionAt = Now; return; }   // walked while charging: keep charging or release, now
            u.Phase = UnitPhase.Ready;
            Emit(EventType.UnitReady, u.Id, null);
            Triggers.Fire(new TriggerContext { Type = TriggerType.UnitReady, Sim = this, Subject = u });
        }

        private void TickWindups()
        {
            foreach (var u in State.Units)
            {
                if (u.Phase != UnitPhase.Windup || u.ActiveAction == null || !u.IsAlive) continue;
                if (!u.ActiveAction.Behavior.OnWindupTick(this, u.ActiveAction)) { Interrupt(u, "condition lost"); continue; }
                StepIntoMelee(u.ActiveAction);   // its target moved: follow
            }
            foreach (var f in freeActions.ToArray())
            {
                if (!f.Actor.IsAlive) { freeActions.Remove(f); continue; }
                if (!f.Behavior.OnWindupTick(this, f)) { freeActions.Remove(f); Emit(EventType.ActionInterrupted, f.Actor.Id, null, new InterruptInfo { Reason = "condition lost" }); }
            }
        }

        private void ResolveDueActions()
        {
            var due = new List<ActionInstance>();
            foreach (var u in State.Units)
                if (u.Phase == UnitPhase.Windup && u.ActiveAction != null && Now + 1e-4f >= u.ActiveAction.ResolveAt)
                    due.Add(u.ActiveAction);
            foreach (var f in freeActions.ToArray())
                if (Now + 1e-4f >= f.ResolveAt) { due.Add(f); freeActions.Remove(f); }
            if (due.Count == 0) return;

            // Snapshot first: simultaneous attacks all see the same dodge/block.
            var snapshot = new Dictionary<string, CombatSnapshot>();
            foreach (var u in State.Units)
                snapshot[u.Id] = new CombatSnapshot { Dodge = u.EffectiveDodge(), Block = u.EffectiveBlock() };

            var effects = new List<SimEffect>();
            foreach (var a in due)
            {
                int first = effects.Count;
                // Freely aimed: its target is whoever is in its cone now; with nobody there it does nothing (but still recovers).
                bool lands = !a.FreeAimed || ResolveFreeAim(a);
                if (lands) a.Behavior.OnResolve(this, a, effects);
                if (lands && a.Definition.WeakpointsOnTarget > 0 && !a.Behavior.WeakpointsOnHit(a.Definition)) AddTargetWeakpoints(a.Actor, GetUnit(a.TargetUnitId), a.Definition);
                // The actor's damage statuses (Wrath) count as of now: baked in before anything below can take them away.
                float dealt = a.Actor.DamageDealtMult();
                for (int i = first; i < effects.Count; i++)
                {
                    var atk = effects[i] as AttackEffect;
                    if (atk != null && atk.Source == a.Actor) atk.DamageMultiplier *= dealt;
                    if (atk != null && a.AimLocked && atk.Source == a.Actor && atk.Definition == a.Definition && atk.TargetId == a.TargetUnitId)
                    { atk.HasLineOfFire = true; atk.LineOfFire = a.AimPoint; }   // a hitscan shot fires where it locked on
                    if (atk != null && atk.Source == a.Actor && atk.CritChance < 0f && atk.Definition == a.Definition)
                        atk.CritChance = CritChanceFor(a.Actor, a.Definition, a.Transcended);   // Transcended or not, as it was used
                    if (atk != null && atk.Source == a.Actor && atk.Definition == a.Definition && a.Transcended)
                    {
                        // A Transcend an Etching gave it, for weakpoints: carried on each of its hits.
                        var grant = a.Actor.GrantOf(a.Definition);
                        if (grant == TranscendGrant.BreakWeakpoints) atk.BreakWeakpoints = Mathf.RoundToInt(a.Actor.GrantParam(a.Definition, "breaks", 2f));
                        if (grant == TranscendGrant.ExposeWeakpoints)
                        {
                            atk.ExtraWeakpoints = Mathf.RoundToInt(a.Actor.GrantParam(a.Definition, "count", 4f));
                            atk.ExtraWeakpointSeconds = a.Actor.GrantParam(a.Definition, "seconds", 3f);
                        }
                    }
                }
                HoldHitsForArrival(a, effects, first);
                if (!a.IsFree) ClearWindupStatuses(a);
                if (a.Definition.ConsumesRequiredStatus && a.Definition.ConsumeOnResolve)
                    foreach (var id in a.Definition.RequiredStatuses()) RemoveStatus(a.Actor, id);
                if (!a.Definition.SelfStatusesOnWindup)
                    foreach (var spec in a.Definition.SelfStatuses)
                        effects.Add(new ApplyStatusEffect { Source = a.Actor, TargetId = a.Actor.Id, Status = SelfStatus(a.Actor, a.Definition, spec) });
                if (a.Definition.EnergyGain > 0f) AddEnergy(a.Actor, a.Definition.EnergyGain);
                float recovery = a.IsFree || a.NoRecovery ? 0f : a.Stat(ActionStat.Recovery) * a.RecoveryScale / a.Actor.EffectiveActionSpeed();
                Emit(EventType.ActionResolved, a.Actor.Id, a.TargetUnitId, new ActionResolvedInfo { ActionId = a.Definition.Id, Recovery = recovery });
                if (!a.IsFree && !a.Behavior.KeepsQueuedOrder) OpenLinks(a);   // what it links to is open now
                if (!a.IsFree)
                {
                    a.Actor.ActiveAction = null;
                    a.Actor.Phase = UnitPhase.Recovery;
                    a.Actor.PhaseEndsAt = Now + recovery;
                    if (a.Definition.ChannelSeconds > 0f && a.Actor.IsAlive) StartChannel(a);   // the recovery waits for the channel
                }
                Triggers.Fire(new TriggerContext { Type = TriggerType.ActionResolved, Sim = this, Subject = a.Actor, Action = a.Definition });
            }

            foreach (var e in effects) ApplyEffect(e, snapshot);

            // After its hit has landed: an over-max Praa's Truth unravels its user; an ability used from a hover brings it down.
            foreach (var a in due)
            {
                if (a.IsFree || !a.Actor.IsAlive) continue;
                if (a.UnravelsActor) UnravelUnit(a.Actor, a.Actor);
                if (a.Actor.HoverEndsAfterAction) EndHover(a.Actor, false, null);
            }
        }

        /// <summary>
        /// An ability that moves its user as it lands (a dash, a crash from the air: ActionBehavior.MovesUser) hits when the
        /// user gets there, not as it sets off: each of its hits (effects from 'first' on) is held back until the user's
        /// glide reaches that target, measured along the glide (a target beside the end: when it arrives), and then lands
        /// as a fresh hit (ScheduleStrike: the target's dodge as it is then). Behaviors that time their own hits opt out
        /// (ActionBehavior.HitsOnArrival).
        /// </summary>
        private void HoldHitsForArrival(ActionInstance a, List<SimEffect> effects, int first)
        {
            var u = a.Actor;
            if (!a.Behavior.MovesUser || !a.Behavior.HitsOnArrival(a.Definition) || !IsGliding(u)) return;
            float left = u.GlideEnd - Now;
            if (left < TickLength * 0.5f) return;
            Vector3 from = u.GlideFrom, path = u.GlideTo - u.GlideFrom;
            float len2 = path.sqrMagnitude;
            for (int i = effects.Count - 1; i >= first; i--)
            {
                var atk = effects[i] as AttackEffect;
                if (atk == null || atk.Source != u) continue;
                var t = GetUnit(atk.TargetId);
                float k = t != null && len2 > 1e-4f ? Mathf.Clamp01(Vector3.Dot(t.Position - from, path) / len2) : 1f;
                float delay = left * k;
                if (delay < TickLength * 0.5f) continue;   // reached as it sets off: lands now
                effects.RemoveAt(i);
                ScheduleStrike(atk, delay);
            }
        }

        /// <summary>When 'u' (gliding) gets where it is going: 'act' then (now if it isn't moving). For a behavior's effects
        /// that belong to the arrival (Crash Down's shockwave).</summary>
        public void OnArrival(Unit u, System.Action act)
        {
            if (act == null) return;
            float left = IsGliding(u) ? u.GlideEnd - Now : 0f;
            if (left < TickLength * 0.5f) act();
            else After(u, left, act);
        }

        private void ProcessDeaths()
        {
            foreach (var u in State.Units.ToArray())
            {
                if (u.Health > 0f || u.Phase == UnitPhase.Dead) continue;
                if (HoldsOn(u)) continue;   // Unkillable, or a boss whose last blow starts its next phase (Split The Minds)
                // Killed in mid-air (jumping, shoved, falling, lifted): drop the body where it was headed.
                bool airborne = u.IsJumping || u.GlideEnd >= 0f || u.IsLifted;
                if (u.IsJumping) u.Position = u.JumpFrom.y <= u.JumpTo.y ? u.JumpFrom : u.JumpTo;
                else if (u.GlideEnd >= 0f) u.Position = u.GlideTo;
                else if (u.IsLifted) u.Position = Terrain.OnGround(u.Position);
                if (airborne) Emit(EventType.MoveEnded, u.Id, null, new MoveEndedInfo { Position = u.Position });
                if (u.Phase == UnitPhase.Windup) ClearWindupStatuses(u.ActiveAction);
                foreach (var s in u.States.ToArray()) s.OnDeath(this);   // lets go of a channel, a grip, a lift (ControlState.OnDeath)
                u.Phase = UnitPhase.Dead;
                u.ActiveAction = null;
                u.Move = null;
                u.Chase = u.Queued = null;
                u.GlideEnd = -1f;
                u.JumpEnd = -1f;
                u.FallLandAt = -1f;
                pendingDashes.RemoveAll(d => d.UnitId == u.Id);
                queues.Remove(u.Id);
                Emit(EventType.Death, u.LastAttackerId, u.Id, new DeathInfo { Team = u.Team });
                Triggers.Fire(new TriggerContext { Type = TriggerType.Killed, Sim = this, Subject = u, Other = GetUnit(u.LastAttackerId), Action = u.LastHitAction, Tag = u.LastHitTag });
            }

            bool playersAlive = State.Units.Any(u => (u.Team == Team.Player || u.HomeTeam == Team.Player) && u.IsAlive && !u.AIControlled && !u.IsMakina);   // AI-played summons and Makina don't hold the field for a fallen squad; an INSANE squad member still does
            // With a boss in the fight, beating the bosses wins it (the endless reinforcements don't count); else every foe must fall.
            bool bossFight = State.Units.Any(u => u.IsBoss) || drops.Exists(d => d.Definition.Boss) || spawns.Exists(s => s.Definition.Boss);
            bool enemiesAlive = bossFight
                ? State.Units.Any(u => u.IsBoss && u.IsAlive) || drops.Exists(d => d.Definition.Boss) || spawns.Exists(s => s.Definition.Boss)   // a boss's next phase on its way
                : State.Units.Any(u => u.Team != Team.Player && u.HomeTeam != Team.Player && u.IsAlive && !u.IsMapObjective)   // map objectives (the Omen) don't count
                  || (!State.Units.Any(u => u.Team != Team.Player) && drops.Exists(d => d.Team != Team.Player))   // every foe down wins, reinforcements still to come or not; with none fielded yet, wait for them
                  || (AwaitReinforcements && drops.Exists(d => d.Team != Team.Player && !d.Anywhere));   // ... unless the encounter says to fight them all
            if (playersAlive && (enemiesAlive || HoldVictory)) return;   // a scripted battle goes on with no enemies left
            Outcome = !playersAlive && !enemiesAlive ? BattleOutcome.Draw
                    : playersAlive ? BattleOutcome.PlayerVictory : BattleOutcome.PlayerDefeat;
            Emit(EventType.BattleEnd, null, null);
        }

        /// <summary>
        /// Has 'u' lost the unit a follow-up of its goes at: still alive, but hidden from it (HIDDEN) or out of the world
        /// (VOIDED)? Then the follow-up is gone (its window closes, the re-cast is off), even if the unit comes back.
        /// </summary>
        public bool FollowupTargetLost(Unit u, string targetId)
        {
            var t = GetUnit(targetId);
            return t != null && t != u && t.IsAlive && (IsVoided(t) || !IsTargetableBy(t, u));
        }

        /// <summary>The re-cast of 'u's last ability (ActionDefinition.IsFollowup, at the unit it last went at) is off once that unit is lost.</summary>
        private void DropLostFollowup(Unit u)
        {
            if (string.IsNullOrEmpty(u.LastActionId) || string.IsNullOrEmpty(u.LastActionTargetId) || !FollowupTargetLost(u, u.LastActionTargetId)) return;
            var last = u.Actions.Find(a => a != null && a.Id == u.LastActionId);
            if (last != null && last.IsFollowup && !ActionBehaviors.Get(last.BehaviorId).FollowupRetargets(last)) u.LastActionId = null;   // as a wait would
        }

        private void ExpireTimers()
        {
            foreach (var u in State.Units)
            {
                if (!u.IsAlive) continue;
                bool repositioning = IsWalking(u) || u.GlideEnd >= 0f;
                DropLostFollowup(u);
                for (int i = u.Statuses.Count - 1; i >= 0; i--)
                {
                    var window = u.Statuses[i];
                    // A follow-up's window at a unit that can't be targeted any more (HIDDEN, VOIDED) closes there and then.
                    if (window.Has(StatusFlags.FollowupWindow) && window.TargetId != null && FollowupTargetLost(u, window.TargetId)) { window.ExpiresAt = Now; window.Held = false; }
                    // A follow-up's window doesn't run while its holder walks or dashes: it picks up where it left off.
                    if (window.Has(StatusFlags.FollowupWindow) && window.ExpiresAt < UntimedStatus)
                    {
                        if (repositioning) { window.ExpiresAt += TickLength; window.Held = true; continue; }
                        if (window.Held) { window.Held = false; RefreshStatusTimer(u, window, u); }
                    }
                    if (u.Statuses[i].ExpiresAt > Now) continue;
                    float oldAction = u.EffectiveActionSpeed(), oldMove = u.EffectiveMoveSpeed();
                    var expired = u.Statuses[i];
                    Emit(EventType.StatusExpired, u.Id, null, expired.Id);
                    u.Statuses.RemoveAt(i);
                    HandleSpeedChange(u, oldAction, oldMove);
                    if (expired.Has(StatusFlags.UnravelsOnExpire)) { UnravelUnit(u, GetUnit(expired.SourceId)); i = Mathf.Min(i, u.Statuses.Count); }
                    if (expired.Has(StatusFlags.StrikesOnExpire) && expired.LinkedAction != null && u.IsAlive)
                    {
                        // RUPTURED (Delayed Crush): the blow it was holding back lands now, from whoever put it there.
                        StrikeNow(new AttackEffect { Source = GetUnit(expired.SourceId), TargetId = u.Id, Definition = expired.LinkedAction, IgnoreRange = true, IgnoreCover = true, Unerring = true }, true);   // even if the Tank has fallen since
                        i = Mathf.Min(i, u.Statuses.Count);
                    }
                    if (expired.HealOnExpire > 0f || expired.HealMissingOnExpire > 0f) HealOnExpiry(u, expired);
                }
                bool locked = u.Phase == UnitPhase.Recovery || u.Phase == UnitPhase.Stunned;
                if (locked && Now + 1e-4f >= u.PhaseEndsAt)
                {
                    u.Phase = UnitPhase.Ready;
                    u.Waiting = false;
                    if (u.ResumeAction != null && TryResume(u)) continue;
                    Emit(EventType.UnitReady, u.Id, null);
                    Triggers.Fire(new TriggerContext { Type = TriggerType.UnitReady, Sim = this, Subject = u });
                }
            }

            for (int i = State.Occluders.Count - 1; i >= 0; i--)
            {
                if (State.Occluders[i].ExpiresAt > Now) continue;
                Emit(EventType.DeployableRemoved, null, null, State.Occluders[i].Id);
                State.Occluders.RemoveAt(i);
            }
        }
    }
}
