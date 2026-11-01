using System.Collections.Generic;
using UnityEngine;

namespace TacticsGame.Core
{
    /// <summary>
    /// Domes around a unit (auras: Sanctity, Chronosphere), channeled abilities, hits redirected by a dome, and aerial slams
    /// (Heretic's Ascent). Any ability can use them through its definition's Aura / Channel / Aerial slam fields.
    /// </summary>
    public partial class BattleSimulation
    {
        // ------------------------------------------------------------------ auras

        /// <summary>A dome around Source: every unit inside holds the definition's AuraStatuses while it is inside.</summary>
        private class Aura
        {
            public string Id;
            public Unit Source;
            public ActionDefinition Definition;
            public float EndsAt;
            public bool Channeled;   // ends with Source's channel
            public readonly HashSet<string> Inside = new HashSet<string>();
        }

        private readonly List<Aura> auras = new List<Aura>();
        private int auraCounter;

        /// <summary>
        /// Raise a dome of def.AreaRadius by def.AreaHeight around 'source' for 'seconds' (it moves with the unit). Units that
        /// step in get def.AuraStatuses (each one's AppliesTo picks allies or enemies; 'source' itself is left out), and lose them
        /// when they step out or the dome comes down. 'channeled': it also comes down when the source's channel ends.
        /// </summary>
        public void StartAura(Unit source, ActionDefinition def, float seconds, bool channeled)
        {
            if (source == null || !source.IsAlive || seconds <= 0f) return;
            var aura = new Aura { Id = "aura_" + (++auraCounter), Source = source, Definition = def, EndsAt = Now + seconds, Channeled = channeled };
            auras.Add(aura);
            Emit(EventType.AuraStarted, source.Id, null, new AuraInfo { Id = aura.Id, ActionId = def.Id, Radius = source.ActionStat(def, ActionStat.AreaRadius), Height = def.AreaHeight, EndsAt = aura.EndsAt });
            UpdateAura(aura);
        }

        /// <summary>Is any of u's body inside the dome of 'radius' by 'height' centred on 'center''s feet? (height 0: a cylinder of any height)</summary>
        public static bool InDome(Unit center, float radius, float height, Unit u)
        {
            Vector3 d = u.Position - center.Position;
            float up = d.y; d.y = 0f;
            float flat = Mathf.Max(0f, d.magnitude - u.BodyRadius);
            if (flat > radius + 1e-4f) return false;
            if (height <= 0f) return true;
            if (up + u.BodyHeight < 0f) return false;   // wholly below the dome's floor
            float r = flat / Mathf.Max(1e-4f, radius), h = Mathf.Max(0f, up) / height;
            return r * r + h * h <= 1f + 1e-4f;
        }

        /// <summary>The domes up around 'u' right now, as AuraInfo (for previews and tests).</summary>
        public List<AuraInfo> AurasOf(Unit u)
        {
            var list = new List<AuraInfo>();
            foreach (var a in auras)
                if (a.Source == u) list.Add(new AuraInfo { Id = a.Id, ActionId = a.Definition.Id, Radius = a.Source.ActionStat(a.Definition, ActionStat.AreaRadius), Height = a.Definition.AreaHeight, EndsAt = a.EndsAt });
            return list;
        }

        private void TickAuras()
        {
            for (int i = auras.Count - 1; i >= 0; i--)
            {
                var a = auras[i];
                if (!a.Source.IsAlive || Now + 1e-4f >= a.EndsAt) { EndAura(a); continue; }
                UpdateAura(a);
            }
        }

        private void UpdateAura(Aura a)
        {
            foreach (var u in State.Units)
            {
                if (u == a.Source) continue;
                bool inside = u.IsAlive && InDome(a.Source, a.Source.ActionStat(a.Definition, ActionStat.AreaRadius), a.Definition.AreaHeight, u);
                if (inside == a.Inside.Contains(u.Id)) continue;
                if (!inside)
                {
                    a.Inside.Remove(u.Id);
                    if (u.IsAlive) LeaveAura(a, u);
                    continue;
                }
                a.Inside.Add(u.Id);
                foreach (var spec in a.Definition.AuraStatuses)
                {
                    if (spec == null || !spec.AppliesToUnit(a.Source, u)) continue;
                    var st = spec.Create(Now, a.Source.Id);
                    st.ExpiresAt = a.EndsAt;   // it lasts as long as the dome (or until the unit steps out)
                    ApplyStatus(u, st, a.Source);
                }
            }
        }

        private void LeaveAura(Aura a, Unit u)
        {
            foreach (var spec in a.Definition.AuraStatuses)
            {
                if (spec == null) continue;
                var st = u.GetStatus(spec.Id);
                if (st != null && st.SourceId == a.Source.Id) RemoveStatus(u, spec.Id);   // not one another dome put there
            }
        }

        private void EndAura(Aura a)
        {
            auras.Remove(a);
            foreach (var id in a.Inside)
            {
                var u = GetUnit(id);
                if (u != null && u.IsAlive) LeaveAura(a, u);
            }
            a.Inside.Clear();
            Emit(EventType.AuraEnded, a.Source.Id, null, a.Id);
        }

        // ------------------------------------------------------------------ redirected hits

        /// <summary>
        /// Sanctity's shelter: a projectile (a ranged attack at a unit, or a ranged line) or a single-target melee hit aimed at a
        /// unit holding a RedirectsHits status goes to whoever gave it that status, if that one is alive and an enemy of the
        /// attacker. Returns the unit the hit really goes to, or null if the dome soaks it up: one attack (a piercing line
        /// through several sheltered allies) only ever reaches the dome's owner once that way.
        /// </summary>
        private long redirectTick = -1;
        private readonly HashSet<string> redirectedHits = new HashSet<string>();   // attacks a dome already took this tick

        private Unit RedirectTarget(Unit src, ActionDefinition def, Unit target)
        {
            bool projectile = def.IsRanged && (def.Targeting == TargetType.Enemy || def.Shape == AreaShape.Line);
            bool melee = !def.IsRanged && def.Targeting == TargetType.Enemy;
            if (!projectile && !melee) return target;
            foreach (var s in target.Statuses)
            {
                if (!s.Has(StatusFlags.RedirectsHits) || s.SourceId == target.Id) continue;
                var guard = GetUnit(s.SourceId);
                if (guard == null || !guard.IsAlive || guard == src || guard.Team == src.Team) continue;
                if (redirectTick != tick) { redirectTick = tick; redirectedHits.Clear(); }
                return redirectedHits.Add(src.Id + "|" + def.Id + "|" + guard.Id) ? guard : null;
            }
            return target;
        }

        // ------------------------------------------------------------------ channels

        /// <summary>
        /// A channeled action (ActionDefinition.ChannelSeconds) has landed: the user channels it, for up to ChannelSeconds,
        /// then recovers. Until then it can't be given orders (the channel can't be cancelled) or do anything else, not even
        /// a reaction; any damage or stun breaks it, and so does losing sight of its target if ChannelNeedsSight.
        /// </summary>
        private void StartChannel(ActionInstance a)
        {
            var u = a.Actor;
            var channel = u.AddState(new ChannelState { Action = a, EndsAt = Now + a.Definition.ChannelSeconds });
            u.Phase = UnitPhase.Channeling;
            u.PhaseEndsAt = channel.EndsAt;
            Emit(EventType.ChannelStarted, u.Id, a.TargetUnitId, new ChannelInfo { ActionId = a.Definition.Id, Seconds = a.Definition.ChannelSeconds });
        }

        public bool IsChanneling(Unit u) { return u != null && u.Phase == UnitPhase.Channeling && u.Channel != null; }

        private void TickChannels()
        {
            foreach (var u in State.Units)
            {
                var c = u.Channel;
                if (c == null) continue;
                if (!u.IsAlive || u.Phase != UnitPhase.Channeling) { EndChannel(u, "ended", false); continue; }
                var def = c.Action.Definition;
                string broken = null;
                if (def.ChannelNeedsSight)
                {
                    var t = GetUnit(c.Action.TargetUnitId);
                    if (t == null || !t.IsAlive) broken = "target lost";
                    else if (!HasSightOf(u, def, t)) broken = "sight lost";
                    else if (Vector3.Distance(u.Position, t.Position) > u.ActionStat(def, ActionStat.Range) * RangeTolerance) broken = "out of range";
                }
                if (broken == null && !c.Action.Behavior.OnChannelTick(this, c.Action)) broken = "condition lost";
                if (broken != null) EndChannel(u, broken);
                else if (Now + 1e-4f >= c.EndsAt) EndChannel(u, null);
            }
        }

        /// <summary>
        /// End a unit's channel. reason null: it ran its course, and the action's Recovery follows. Otherwise it broke: the
        /// unit is ready again at once (a stun breaking it takes over instead; emitReady false leaves the announcing to that).
        /// Its channeled domes come down either way.
        /// </summary>
        internal void EndChannel(Unit u, string reason, bool emitReady = true)
        {
            var c = u.Channel;
            if (c == null) return;
            u.RemoveState(c);
            foreach (var a in auras.ToArray()) if (a.Source == u && a.Channeled) EndAura(a);
            c.Action.Behavior.OnChannelEnd(this, c.Action, reason != null);
            var def = c.Action.Definition;
            float recovery = reason == null ? c.Action.Stat(ActionStat.Recovery) / u.EffectiveActionSpeed() : 0f;
            Emit(EventType.ChannelEnded, u.Id, null, new ChannelInfo { ActionId = def.Id, Recovery = recovery, Broken = reason != null, Reason = reason });
            if (u.Phase != UnitPhase.Channeling) return;
            if (reason == null)
            {
                u.Phase = UnitPhase.Recovery;
                u.PhaseEndsAt = Now + recovery;
                return;
            }
            u.Phase = UnitPhase.Ready;
            u.PhaseEndsAt = Now;
            if (!emitReady) return;
            Emit(EventType.UnitReady, u.Id, null);
            Triggers.Fire(new TriggerContext { Type = TriggerType.UnitReady, Sim = this, Subject = u });
        }

        // ------------------------------------------------------------------ aerial slams (Heretic's Ascent)

        /// <summary>
        /// Carry a stunned 'target' up into the air and slam it down (Heretic's Ascent): along AerialSlamPath, rising
        /// def.DisplaceDistance metres over AirRiseSeconds, hanging for AirHoldSeconds and slamming down over AirSlamSeconds, the
        /// target swung over the carrier's head to land behind it. It lands for fall damage from that height and def's stun;
        /// the carrier lands where it took off, unhurt, and then recovers. Both are held in the air meanwhile (nothing moves
        /// them, melee can't reach them). Damage to the carrier before the slam starts knocks both out of the air (BreakAerialSlam).
        /// </summary>
        public void StartAerialSlam(Unit carrier, Unit target, ActionDefinition def)
        {
            if (carrier == null || target == null || !carrier.IsAlive || !target.IsAlive || carrier == target) return;
            if (target.Phase != UnitPhase.Stunned || def.DisplaceDistance <= 0f) return;   // a stun-immune target slipped the grab
            if (carrier.IsLifted || target.IsLifted || carrier.FallLandAt >= 0f || target.FallLandAt >= 0f || carrier.Phase == UnitPhase.Stunned) return;
            if (target.HeldById != null) ReleaseHeld(target);
            if (target.Grapple != null) EndGrapple(target);
            if (carrier.Grapple != null) EndGrapple(carrier);
            if (carrier.Channel != null) EndChannel(carrier, "slam", false);
            HaltWalk(carrier);
            carrier.GlideEnd = target.GlideEnd = -1f;
            carrier.Position = Terrain.OnGround(carrier.Position);

            Vector3 f = target.Position - carrier.Position; f.y = 0f;
            f = f.sqrMagnitude > 1e-6f ? f.normalized : Vector3.forward;
            float radius = CarryOffset(carrier, target);
            Vector3 behind = Paths.ClampDash(carrier.Position, carrier.Position - f * radius);
            Vector3 landing = Terrain.OnGround(FreeSpotNear(target, Terrain.OnGround(behind), carrier.Position, false));
            var path = new AerialSlamInfo
            {
                CarrierStart = carrier.Position, HeldStart = target.Position, Landing = landing, Facing = f,
                Height = carrier.ActionStat(def, ActionStat.DisplaceDistance), Radius = radius, PivotHeight = carrier.BodyHeight * 0.55f, HeldHalfHeight = target.BodyHeight * 0.5f,
                Start = Now, Rise = Mathf.Max(TickLength, def.AirRiseSeconds), Hold = Mathf.Max(0f, def.AirHoldSeconds), Slam = Mathf.Max(TickLength, def.AirSlamSeconds),
            };
            carrier.AddState(new AerialSlamState { HeldId = target.Id, Definition = def, Path = path });
            target.AddState(new SlamCarriedState { CarrierId = carrier.Id });
            HoldForSlam(carrier, target, path);
            Emit(EventType.AerialSlam, carrier.Id, target.Id, path);
        }

        /// <summary>Both stay out of action until the slam lands (a slow mustn't end the carrier's recovery up there).</summary>
        private void HoldForSlam(Unit carrier, Unit held, AerialSlamInfo path)
        {
            float end = AerialSlamPath.End(path) + TickLength;
            if (carrier.Phase == UnitPhase.Ready || carrier.Phase == UnitPhase.Windup) carrier.Phase = UnitPhase.Recovery;
            carrier.PhaseEndsAt = Mathf.Max(carrier.PhaseEndsAt, end);
            held.PhaseEndsAt = Mathf.Max(held.PhaseEndsAt, end);
        }

        private void TickAerialSlams()
        {
            foreach (var u in State.Units)
            {
                var s = u.Slam;
                if (s == null) continue;
                var held = GetUnit(s.HeldId);
                if (!u.IsAlive || held == null || !held.IsAlive) { BreakAerialSlam(u, null); continue; }
                u.Position = AerialSlamPath.Carrier(s.Path, Now);
                held.Position = AerialSlamPath.HeldFeet(s.Path, Now);
                if (Now + 1e-4f < AerialSlamPath.End(s.Path)) { HoldForSlam(u, held, s.Path); continue; }
                FinishAerialSlam(u, held, s);
            }
        }

        private void FinishAerialSlam(Unit carrier, Unit held, AerialSlamState s)
        {
            carrier.RemoveState(s);
            held.RemoveState<SlamCarriedState>();
            carrier.Position = s.Path.CarrierStart;
            held.Position = s.Path.Landing;
            Emit(EventType.AerialSlamEnded, carrier.Id, held.Id, false);
            Emit(EventType.MoveEnded, carrier.Id, null, new MoveEndedInfo { Position = carrier.Position });
            Emit(EventType.MoveEnded, held.Id, null, new MoveEndedInfo { Position = held.Position });
            // The carrier lands unhurt and recovers; the one it slammed takes the fall from the top and is stunned.
            carrier.PhaseEndsAt = Now + carrier.ActionStat(s.Definition, ActionStat.Recovery) / carrier.EffectiveActionSpeed();
            held.PhaseEndsAt = Now;
            TakeFallDamage(held, FallDamageBase + FallDamagePerMeter * s.Path.Height, carrier);
            float stun = carrier.ActionStat(s.Definition, ActionStat.StunDuration);
            if (held.IsAlive && stun > 0f) ApplyStun(held, stun, carrier, true);
            SettleOutOfBodies(held);
        }

        /// <summary>
        /// The slam fails in mid-air: the one carried is let go, and both fall from where they are, stunned for the fall and
        /// taking fall damage (credited to 'by', who knocked them down). With a carrier or target gone, the other just falls.
        /// </summary>
        internal void BreakAerialSlam(Unit carrier, Unit by)
        {
            var s = carrier.Slam;
            if (s == null) return;
            var held = GetUnit(s.HeldId);
            carrier.RemoveState(s);
            if (held != null) held.RemoveState<SlamCarriedState>();
            Emit(EventType.AerialSlamEnded, carrier.Id, held != null ? held.Id : null, true);
            foreach (var u in new[] { carrier, held })
            {
                if (u == null || !u.IsAlive) continue;
                u.PhaseEndsAt = Now;   // the fall's stun replaces what the slam held them for
                Vector3 ground = Terrain.OnGround(u.Position);
                Vector3 landing = Terrain.OnGround(FreeSpotNear(u, ground, ground, false));
                StartFall(u, landing, u.Position.y - landing.y, 0f, by, false);
            }
        }

        /// <summary>Fall damage dealt at once (a slam's landing), credited to 'source'.</summary>
        private void TakeFallDamage(Unit u, float amount, Unit source)
        {
            if (!u.IsAlive || amount <= 0f) return;
            u.Health -= amount;
            if (source != null) u.LastAttackerId = source.Id;
            u.LastHitAction = null;
            u.LastHitTag = "fall";
            Emit(EventType.Damage, source != null ? source.Id : null, u.Id, new DamageInfo { ActionId = "fall", Amount = amount, Impact = ImpactType.Heavy, RemainingHealth = u.Health });
        }
    }
}
