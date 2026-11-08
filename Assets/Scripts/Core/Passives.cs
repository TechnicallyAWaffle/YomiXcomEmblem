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

        /// <summary>Ascension: the share of max health it triggers below, and how high it flies.</summary>
        public static float AscensionHealth = 0.3f, AscensionHeight = 8f;

        public static void Attach(BattleSimulation sim)
        {
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
