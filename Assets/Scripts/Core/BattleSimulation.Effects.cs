using System.Collections.Generic;
using UnityEngine;

namespace TacticsGame.Core
{
    /// <summary>
    /// BattleSimulation: rules any status or ability can use through its data or a behavior.
    ///   Heal over time   StatusEffect.HealPerSecond (Regeneration Seal), in StrainRecoveryTicks steps.
    ///   Seal breaks      a BreaksOnDamage status with BreakStun stuns whichever enemy broke it (see DealDamage).
    ///   Strain inverted  StatusFlags.InvertsStrainGain: strain the unit would gain, it loses instead (True Inversion; AddStrain).
    ///   Strike now       a hit outside an action resolving (a channel's flurry: A Million Cuts).
    ///   Stop a glide     a pull or slide cut short where it is (Death Sentence's broken channel).
    ///   Slam down        an airborne unit thrown to the ground, faster and harder than a fall (Death Sentence).
    /// </summary>
    public partial class BattleSimulation
    {
        /// <summary>Statuses that heal their holder (HealPerSecond) do it here, a step at a time, with a Heal event each.</summary>
        private void TickHealOverTime()
        {
            if (tick % StrainRecoveryTicks != 0) return;
            float step = StrainRecoveryTicks * TickLength;
            foreach (var u in State.Units)
            {
                if (!u.IsAlive) continue;
                float heal = 0f, strain = 0f;
                foreach (var s in u.Statuses) { if (s.HealPerSecond > 0f) heal += s.HealPerSecond * step; strain += s.StrainPerSecond * step; }
                if (strain != 0f) AddStrain(u, strain);   // REDUCTION builds it; EQUIPOSE sheds it
                heal = Mathf.Min(heal, u.MaxHealth - u.Health);
                if (heal <= 1e-4f) continue;
                u.Health += heal;
                Emit(EventType.Heal, u.Id, u.Id, heal);
            }
        }

        /// <summary>A delayed heal comes due (StatusEffect.HealOnExpire): the flat part plus a share of what its holder is missing now.</summary>
        private void HealOnExpiry(Unit u, StatusEffect s)
        {
            if (!u.IsAlive) return;
            float heal = Mathf.Min(s.HealOnExpire + s.HealMissingOnExpire * (u.MaxHealth - u.Health), u.MaxHealth - u.Health);
            if (heal <= 1e-4f) return;
            u.Health += heal;
            Emit(EventType.Heal, s.SourceId, u.Id, heal);
        }

        /// <summary>A unit pays health for something it did (Suffer Beyond Your Limits): a self-inflicted Damage event; it can die of it.</summary>
        public void LoseHealth(Unit u, float amount, string actionId)
        {
            if (u == null || !u.IsAlive || amount <= 0f) return;
            u.Health -= amount;
            Emit(EventType.Damage, u.Id, u.Id, new DamageInfo { ActionId = actionId, Amount = amount, Impact = ImpactType.Light, RemainingHealth = u.Health });
        }

        /// <summary>Land one hit now (not as part of an action resolving): the full hit pipeline, against the target as it is.</summary>
        public void StrikeNow(AttackEffect attack)
        {
            var t = GetUnit(attack.TargetId);
            if (t == null || !t.IsAlive || attack.Source == null || !attack.Source.IsAlive) return;
            var snapshot = new Dictionary<string, CombatSnapshot> { { t.Id, new CombatSnapshot { Dodge = t.EffectiveDodge(), Block = t.EffectiveBlock() } } };
            ApplyEffect(attack, snapshot);
        }

        /// <summary>A guard broken by an ability (Stutter): the Block up now comes down, and Block can't be used for 'seconds'.</summary>
        public void BreakGuard(Unit u, float seconds)
        {
            if (u == null || !u.IsAlive) return;
            if (u.HasStatus(StatusIds.Blocking)) RemoveStatus(u, StatusIds.Blocking);
            BlockDownFor(u, seconds);
        }

        /// <summary>Block on cooldown for 'seconds' (a longer cooldown it already has stays), shown as Guard Broken.</summary>
        public void BlockDownFor(Unit u, float seconds)
        {
            foreach (var a in u.Actions)
            {
                if (a == null || a.BehaviorId != "block") continue;
                float at;
                float until = Mathf.Max(Now + seconds, u.CooldownReadyAt.TryGetValue(a.Id, out at) ? at : 0f);
                u.CooldownReadyAt[a.Id] = until;
                // No effect of its own: it shows (on the HUD, over the unit) that Block is down until then.
                ApplyStatus(u, new StatusEffect { Id = StatusIds.GuardBroken, SourceId = u.Id, ExpiresAt = until }, u);
            }
        }

        /// <summary>A random number in [0, 1) from the battle's seeded generator (so a replay with the same seed matches).</summary>
        public float RandomValue() { return (float)rng.NextDouble(); }

        /// <summary>Stun a unit for a behavior (Death Sentence holds what it pulls), through the usual stun rules.</summary>
        public void StunUnit(Unit target, float seconds, Unit source)
        {
            if (target != null && target.IsAlive && seconds > 0f) ApplyStun(target, seconds, source);
        }

        /// <summary>A channel whose work is done early (a pull arrived, the strikes are all in) ends at 'endsAt' instead, with its
        /// recovery after, as if it had run its course.</summary>
        public void CutChannel(Unit u, float endsAt)
        {
            var c = u != null ? u.Channel : null;
            if (c == null || endsAt >= c.EndsAt) return;
            c.EndsAt = Mathf.Max(Now, endsAt);
            u.PhaseEndsAt = c.EndsAt;
        }

        /// <summary>A unit sliding (a pull, a push, a dash) stops where it is now. Falls carry on.</summary>
        public void StopGlide(Unit u)
        {
            if (u == null || u.GlideEnd < 0f || u.FallLandAt >= 0f) return;
            u.GlideEnd = -1f;
            Emit(EventType.MoveEnded, u.Id, null, new MoveEndedInfo { Position = u.Position });
        }

        /// <summary>In the air: lifted, hovering, jumping, falling or otherwise above the ground under it.</summary>
        public bool IsAirborne(Unit u)
        {
            if (u == null || !u.IsAlive) return false;
            if (u.IsLifted || u.Hovering || u.IsJumping || u.FallLandAt >= 0f) return true;
            return u.Position.y - Terrain.HeightAt(u.Position) > TerrainMap.StepHeight;
        }

        /// <summary>
        /// Throw an airborne unit straight down: out of whatever holds it up (a lift, a hover, a jump), then a fall
        /// 'speed' times as fast as a normal one, its fall damage times 'damageMult', credited to 'source'. False if it
        /// wasn't in the air.
        /// </summary>
        public bool SlamToGround(Unit target, Unit source, float speed, float damageMult)
        {
            if (!IsAirborne(target) || !CanDisplace(target, source)) return false;
            if (target.HeldById != null) ReleaseHeld(target);
            ClearHover(target);
            target.RemoveState<LiftState>();
            if (target.IsJumping) { target.JumpEnd = -1f; if (target.Phase == UnitPhase.Moving) EndMove(target, false); }
            target.GlideEnd = -1f;
            target.FallLandAt = -1f;
            Vector3 landing = Terrain.OnGround(target.Position);
            StartFall(target, landing, target.Position.y - landing.y, 0f, source, false, speed, damageMult);
            return true;
        }

        /// <summary>
        /// What an order's windup is scaled by from its user's Etchings (1 = unchanged): Senses of Closure (near targets),
        /// Perception for Violence (stunned enemies), and a Revenge status waiting to be spent (Revenge Maxim).
        /// </summary>
        public float EtchingWindupScale(Unit actor, ActionDefinition def, UnitCommand cmd)
        {
            float scale = ClosingWindupScale(actor, def, cmd);
            var target = GetUnit(cmd.TargetUnitId);
            if (target != null && target.Team != actor.Team) scale *= EtchingTraits.PerceptionScale(actor, target);
            return scale * EtchingTraits.RevengeScale(actor);
        }
    }
}
