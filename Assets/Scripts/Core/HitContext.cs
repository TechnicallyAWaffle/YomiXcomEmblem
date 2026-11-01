namespace TacticsGame.Core
{
    /// <summary>When a hit rule runs, in the order an attack resolves (BattleSimulation.ApplyAttack).</summary>
    public enum HitStage
    {
        Aiming,   // before the hit roll: change AccuracyBonus
        Damage,   // the hit landed and wasn't dodged, before block: change DamageMultiplier
        Landed,   // damage, strain and the Damage event are done, before reactions (interrupts, statuses, stun); the target is alive
    }

    /// <summary>A rule that reads or changes a hit as it resolves (see BattleSimulation.AddHitRule).</summary>
    public delegate void HitRule(BattleSimulation sim, HitContext hit);

    /// <summary>
    /// One attack on one unit as it resolves, passed through the hit steps and to every HitRule: who hit whom with what,
    /// and the numbers worked out so far. Rules change the fields their stage names; the steps after read them.
    /// </summary>
    public class HitContext
    {
        public Unit Source, Target;
        public ActionDefinition Definition;
        public AttackEffect Effect;

        // Aiming
        public float Exposure = 1f;        // how much of the target is open to a ranged shot (cover)
        public float AccuracyBonus;        // added to the hit chance
        public float HitChance;

        // Damage
        public float Dodge, Block;         // the target's, as the tick began (simultaneous hits all see the same)
        public float DamageMultiplier = 1f;// rules multiply this (Lethal Prescience on a stunned target)
        public float Raw, Dealt;           // before and after block
        public bool Negated;               // a Transcended Block: no damage, no strain, no wear

        // Landed
        public bool Unravel;               // a Heavy hit at max strain: unravels, whatever the target is immune to
        public float StunFor;              // seconds of stun this hit gives (0 = none)
        public bool WillStun { get { return Unravel || StunFor > 0f; } }
    }
}
