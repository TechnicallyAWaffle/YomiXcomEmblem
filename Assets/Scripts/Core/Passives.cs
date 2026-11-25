using System.Collections.Generic;
using UnityEngine;

namespace TacticsGame.Core
{
    /// <summary>
    /// Always-on class traits, listed by id on UnitDefinition.Passives. Each rule subscribes once per battle
    /// and checks whether the unit involved has the passive. (An always-on dome is an ability instead: InnatePassive.)
    /// </summary>
    public static class Passives
    {
        public const string StrainReliefOnDodge = "strain_relief_on_dodge";   // Fighter: shed strain whenever you dodge (BattleSimulation.DodgeStrainRelief)
        public const string Ascension = "ascension";   // Monarch: below AscensionHealth, shakes off stuns, is Unstoppable for good and takes to the air
        public const string KarranosSigil = "karranos_sigil";   // Duelist: breaking a weakpoint makes that enemy its NEMESIS (see AttachKarranosSigil)

        /// <summary>Ascension: the share of max health it triggers below, and how high it flies.</summary>
        public static float AscensionHealth = 0.3f, AscensionHeight = 8f;

        /// <summary>Karranos Sigil: how long NEMESIS lasts, and how much more damage the Duelist deals its NEMESIS.</summary>
        public static float NemesisSeconds = 5f, NemesisDamageBonus = 0.3f;

        public static void Attach(BattleSimulation sim)
        {
            AttachKarranosSigil(sim);
            sim.Triggers.Subscribe(TriggerType.Dodged, ctx =>
            {
                if (ctx.Subject.Passives.Contains(StrainReliefOnDodge)) sim.AddStrain(ctx.Subject, -sim.DodgeStrainRelief);
            });
            sim.Triggers.Subscribe(TriggerType.DamageTaken, ctx =>
            {
                var u = ctx.Subject;
                if (!u.IsAlive || !u.Passives.Contains(Ascension) || u.HasStatus(StatusIds.Ascended)) return;
                if (u.Health >= u.MaxHealth * AscensionHealth) return;
                sim.Ascend(u);
            });
        }

        /// <summary>The NEMESIS 'duelist' marked (Karranos Sigil), or null.</summary>
        public static StatusEffect NemesisOf(Unit marked, Unit duelist)
        {
            if (marked == null || duelist == null) return null;
            var s = marked.GetStatus(StatusIds.Nemesis);
            return s != null && s.SourceId == duelist.Id ? s : null;
        }

        /// <summary>
        /// Karranos Sigil (the Duelist's, innate): breaking an enemy's weakpoint makes it the Duelist's NEMESIS for
        /// NemesisSeconds (only one at a time: a new one takes it off the last). The Duelist deals NemesisDamageBonus more
        /// damage to its NEMESIS, and when the NEMESIS attacks the Duelist it is parried at once: the Duelist parries it as
        /// Unsigned Rebuttal does (no damage or stun, and the parry's reward), and the NEMESIS is no longer its NEMESIS.
        /// </summary>
        private static void AttachKarranosSigil(BattleSimulation sim)
        {
            sim.Triggers.Subscribe(TriggerType.WeakpointsBroken, ctx =>
            {
                var duelist = ctx.Subject; var foe = ctx.Other;
                if (duelist == null || foe == null || !foe.IsAlive || foe.Team == duelist.Team || !duelist.Passives.Contains(KarranosSigil)) return;
                foreach (var u in sim.State.Units)
                    if (u != foe && NemesisOf(u, duelist) != null) sim.RemoveStatus(u, StatusIds.Nemesis);   // one NEMESIS at a time
                sim.ApplyStatus(foe, new StatusEffect { Id = StatusIds.Nemesis, SourceId = duelist.Id, ExpiresAt = sim.Now + NemesisSeconds }, duelist);
            });
            sim.AddHitRule(HitStage.Damage, (s, hit) =>
            {
                if (hit.Source != null && hit.Source.Passives.Contains(KarranosSigil) && NemesisOf(hit.Target, hit.Source) != null)
                    hit.DamageMultiplier *= 1f + NemesisDamageBonus;
            });
            // Before the hit roll: the NEMESIS's attack meets a parry, whatever it would have rolled. The parry is put up for
            // that hit alone (taken down again as it lands), unless the Duelist was parrying anyway.
            var autoParried = new HashSet<HitContext>();
            sim.AddHitRule(HitStage.Aiming, (s, hit) =>
            {
                var duelist = hit.Target; var foe = hit.Source;
                if (foe == null || duelist == null || foe == duelist || !duelist.IsAlive || !duelist.Passives.Contains(KarranosSigil) || NemesisOf(foe, duelist) == null) return;
                s.RemoveStatus(foe, StatusIds.Nemesis);
                hit.AccuracyBonus += 10f;   // it reaches the parry
                if (duelist.HasStatus(StatusIds.Parrying)) return;
                s.ApplyStatus(duelist, new StatusEffect
                {
                    Id = StatusIds.Parrying, SourceId = duelist.Id, ExpiresAt = s.Now + BattleSimulation.TickLength,
                    Flags = StatusFlags.NegatesDamage | StatusFlags.StunImmune | StatusFlags.Counters,
                }, duelist);
                autoParried.Add(hit);
            });
            sim.AddHitRule(HitStage.Resolved, (s, hit) =>
            {
                if (autoParried.Remove(hit) && hit.Target.HasStatus(StatusIds.Parrying)) s.RemoveStatus(hit.Target, StatusIds.Parrying);
            });
        }
    }

    public partial class BattleSimulation
    {
        /// <summary>
        /// The Monarch's Ascension: every stun on it is gone at once (a lift or a fall too), it is Unstoppable for the rest
        /// of the battle (so nothing knocks it out of the air), and it flies Passives.AscensionHeight up and stays there.
        /// </summary>
        internal void Ascend(Unit u)
        {
            ApplyStatus(u, new StatusEffect { Id = StatusIds.Ascended, SourceId = u.Id, ExpiresAt = float.MaxValue }, u);
            ApplyStatus(u, new StatusEffect { Id = StatusIds.Unstoppable, SourceId = u.Id, ExpiresAt = float.MaxValue, Flags = StatusFlags.Unstoppable }, u);
            if (u.HeldById != null) ReleaseHeld(u);
            u.RemoveState<LiftState>();   // lifts and hovers alike
            if (u.FallLandAt >= 0f || u.GlideEnd >= 0f) { u.FallLandAt = -1f; u.GlideEnd = -1f; }
            if (u.Phase == UnitPhase.Stunned) u.PhaseEndsAt = Now;   // free at the end of this tick
            Vector3 ground = Terrain.OnGround(u.Position);
            HoverUnit(u, ground + Vector3.up * Passives.AscensionHeight, float.MaxValue, null, 0f, 0f, true);
        }
    }
}
