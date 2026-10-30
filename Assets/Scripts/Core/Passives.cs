namespace TacticsGame.Core
{
    /// <summary>
    /// Always-on class traits, listed by id on UnitDefinition.Passives. Each rule subscribes once per battle
    /// and checks whether the unit involved has the passive.
    /// </summary>
    public static class Passives
    {
        public const string StrainReliefOnDodge = "strain_relief_on_dodge";   // Melee: shed strain whenever you dodge
        public const float StrainReliefOnDodgeAmount = 15f;

        public static void Attach(BattleSimulation sim)
        {
            sim.Triggers.Subscribe(TriggerType.Dodged, ctx =>
            {
                if (ctx.Subject.Passives.Contains(StrainReliefOnDodge)) sim.AddStrain(ctx.Subject, -StrainReliefOnDodgeAmount);
            });
        }
    }
}
