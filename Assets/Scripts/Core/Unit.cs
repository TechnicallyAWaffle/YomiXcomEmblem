using System.Collections.Generic;
using UnityEngine;

namespace TacticsGame.Core
{
    /// <summary>
    /// A timed status on a unit (buff, debuff, stance): stat modifiers, ability-number modifiers and on/off rules (Flags),
    /// plus fields behaviors keep their bookkeeping in. Its modifiers are its own copy, so it can be changed in place (Block
    /// wears a status's Block bonus down).
    /// </summary>
    public class StatusEffect
    {
        public string Id;
        public string SourceId;
        public float ExpiresAt;
        public List<Modifier> Modifiers;          // null = none
        public List<ActionModifier> ActionModifiers;
        public StatusFlags Flags;
        public List<string> ForbidsActions; // action ids this unit can't use while it has the status (Wrath: Block, Battle Trance)
        public string TargetId;            // whom a follow-up chain is aimed at (Rapid Slices, Steady Strikes)
        public int Stacks;                  // free counter for behaviors (Rapid Slices follow-ups used)
        public ActionDefinition LinkedAction; // the action a stance/reaction fires (Take Aim's Snipe, Retaliate's shot)

        public bool Has(StatusFlags flag) { return (Flags & flag) != 0; }

        /// <summary>A copy whose modifier lists are its own.</summary>
        public StatusEffect Copy()
        {
            var c = (StatusEffect)MemberwiseClone();
            if (Modifiers != null) c.Modifiers = new List<Modifier>(Modifiers);
            if (ActionModifiers != null) c.ActionModifiers = new List<ActionModifier>(ActionModifiers);
            return c;
        }
    }

    /// <summary>
    /// Author-time description of a status an action applies, turned into a StatusEffect when it lands: what it changes
    /// (Modifiers on the unit's stats, ActionModifiers on its abilities' numbers) and the rules it gives (Flags).
    /// Assets saved before statuses had modifier lists (DataVersion 14 and earlier) stored one field per bonus; those are
    /// read once on load and folded into Modifiers and Flags (OnAfterDeserialize).
    /// </summary>
    [System.Serializable]
    public class StatusSpec : ISerializationCallbackReceiver
    {
        public string Id = "status";
        public float Duration = 3f;
        public List<Modifier> Modifiers = new List<Modifier>();
        [Tooltip("Changes to the holder's ability numbers while it lasts (empty Action Id = every ability).")]
        public List<ActionModifier> ActionModifiers = new List<ActionModifier>();
        public StatusFlags Flags;
        [Tooltip("Action ids the unit can't use while it has this status (Wrath: block, battle_trance).")]
        public List<string> ForbidsActions = new List<string>();
        [Tooltip("In a list of statuses an ability puts on its target: whom this one lands on.")]
        public StatusTarget AppliesTo = StatusTarget.Any;
        [Tooltip("How it reads on screen. Left empty, the game's built-in look for this id is used (or a plain one).")]
        public StatusLook Look = new StatusLook();

        // ---- authoring helpers: new StatusSpec { Id = ..., Duration = 3f }.Add(StatId.Dodge, 0.3f).With(StatusFlags.StunImmune)
        public StatusSpec Add(StatId stat, float value) { Modifiers.Add(Modifier.Add(stat, value)); return this; }
        public StatusSpec Mult(StatId stat, float value) { Modifiers.Add(Modifier.Mult(stat, value)); return this; }
        public StatusSpec With(StatusFlags flags) { Flags |= flags; return this; }
        public StatusSpec Forbids(params string[] actionIds) { ForbidsActions.AddRange(actionIds); return this; }
        public bool Has(StatusFlags flag) { return (Flags & flag) != 0; }

        /// <summary>The value this status gives 'stat', as (0 + adds) for an additive stat or (1 x mults) for a multiplier.</summary>
        public float Total(StatId stat, bool multiplier)
        {
            var f = StatFold.Start;
            f.Take(Modifiers, stat);
            return multiplier ? f.MultProduct : f.AddSum;
        }

        public StatusEffect Create(float now, string sourceId, ActionDefinition linkedAction = null)
        {
            return new StatusEffect
            {
                Id = Id, SourceId = sourceId, ExpiresAt = now + Duration, LinkedAction = linkedAction, Flags = Flags,
                Modifiers = Modifiers != null && Modifiers.Count > 0 ? new List<Modifier>(Modifiers) : null,
                ActionModifiers = ActionModifiers != null && ActionModifiers.Count > 0 ? new List<ActionModifier>(ActionModifiers) : null,
                ForbidsActions = ForbidsActions != null && ForbidsActions.Count > 0 ? new List<string>(ForbidsActions) : null,
            };
        }

        /// <summary>Does this status land on 'target' when 'source' puts it there (AppliesTo)?</summary>
        public bool AppliesToUnit(Unit source, Unit target)
        {
            if (AppliesTo == StatusTarget.Any || source == null) return true;
            return (target.Team == source.Team) == (AppliesTo == StatusTarget.Allies);
        }

        // ---- the old one-field-per-bonus layout, kept only to read assets saved with it (see the class summary)
#pragma warning disable 0649
        [SerializeField, HideInInspector] private float DodgeBonus, BlockBonus, IncomingAccuracyBonus, DodgeDistanceBonus;
        [SerializeField, HideInInspector] private float ActionSpeedMult = 1f, MoveSpeedMult = 1f, DamageDealtMult = 1f, StrainCostMult = 1f;
        [SerializeField, HideInInspector] private bool Rooted, Untargetable, BreaksOnDamage, StunImmune, KeepsBlock, Unstoppable, RedirectsHits;
#pragma warning restore 0649

        public void OnBeforeSerialize() { }

        public void OnAfterDeserialize()
        {
            if (Modifiers == null) Modifiers = new List<Modifier>();
            if (ActionModifiers == null) ActionModifiers = new List<ActionModifier>();
            if (ForbidsActions == null) ForbidsActions = new List<string>();
            MigrateAdd(ref DodgeBonus, StatId.Dodge);
            MigrateAdd(ref BlockBonus, StatId.Block);
            MigrateAdd(ref IncomingAccuracyBonus, StatId.IncomingAccuracy);
            MigrateAdd(ref DodgeDistanceBonus, StatId.DashDistance);
            MigrateMult(ref ActionSpeedMult, StatId.ActionSpeed);
            MigrateMult(ref MoveSpeedMult, StatId.MoveSpeed);
            MigrateMult(ref DamageDealtMult, StatId.DamageDealt);
            MigrateMult(ref StrainCostMult, StatId.StrainCost);
            MigrateFlag(ref Rooted, StatusFlags.Rooted);
            MigrateFlag(ref Untargetable, StatusFlags.Untargetable);
            MigrateFlag(ref BreaksOnDamage, StatusFlags.BreaksOnDamage);
            MigrateFlag(ref StunImmune, StatusFlags.StunImmune);
            MigrateFlag(ref KeepsBlock, StatusFlags.KeepsBlock);
            MigrateFlag(ref Unstoppable, StatusFlags.Unstoppable);
            MigrateFlag(ref RedirectsHits, StatusFlags.RedirectsHits);
        }

        // Each moves a non-default old value into the new layout and resets it, so a saved asset converts once.
        private void MigrateAdd(ref float old, StatId stat) { if (Mathf.Abs(old) > 1e-6f) Modifiers.Add(Modifier.Add(stat, old)); old = 0f; }
        private void MigrateMult(ref float old, StatId stat) { if (Mathf.Abs(old - 1f) > 1e-6f) Modifiers.Add(Modifier.Mult(stat, old)); old = 1f; }
        private void MigrateFlag(ref bool old, StatusFlags flag) { if (old) Flags |= flag; old = false; }
    }

    /// <summary>How a status reads on screen (StatusDisplay): only the parts that are set replace the built-in look.</summary>
    [System.Serializable]
    public class StatusLook
    {
        [Tooltip("HUD name; empty = the built-in one, or made from the id.")]
        public string Name;
        [Tooltip("Pops over the unit when it lands, and gives it a ring at the feet; empty = the built-in one, if any.")]
        public string Word;
        [Tooltip("Ring and bar colour; transparent = the built-in one.")]
        public Color Color = new Color(0f, 0f, 0f, 0f);
        [Tooltip("The duration bar fills toward the end instead of draining (a countdown to something).")]
        public bool CountsUp;
        [Tooltip("Never shown (bookkeeping).")]
        public bool Hidden;

        public bool IsSet { get { return Color.a > 0f || !string.IsNullOrEmpty(Name) || !string.IsNullOrEmpty(Word) || CountsUp || Hidden; } }
    }

    /// <summary>Subjugate in progress, kept on the grappler (see BattleSimulation.TickGrapples).</summary>
    public class GrappleState
    {
        public string TargetId;
        public ActionDefinition Definition;   // Subjugate: its LinkedAction is the headbutt
        public float NextHeadbuttAt;
        public float EndsAt;                  // the grab's stun runs out: the throw starts
        public Vector3 Facing;                // the held unit is carried this way from the grappler
        public bool Throwing;                 // the throw is winding up
        public bool AwaitingThrow;            // a player's grab ran out: it is asked which way to throw (any order throws)
    }

    /// <summary>A channeled ability running (UnitPhase.Channeling; see BattleSimulation.StartChannel).</summary>
    public class ChannelState
    {
        public ActionInstance Action;   // what was channeled (its target, behavior)
        public float EndsAt;            // runs out here, then the recovery
    }

    /// <summary>Heretic's Ascent in progress, kept on the one carrying (see BattleSimulation.StartAerialSlam).</summary>
    public class AerialSlamState
    {
        public string HeldId;
        public ActionDefinition Definition;
        public AerialSlamInfo Path;     // the scripted flight both follow (AerialSlamPath)
    }

    public class ActiveMove
    {
        public List<Vector3> Path;
        public int NextIndex;
        public float Speed;
    }

    /// <summary>
    /// Pure-data combatant used by the simulation. Never a MonoBehaviour;
    /// the scene representation is UnitView, linked only by Id.
    /// </summary>
    public class Unit
    {
        public string Id;
        public string DisplayName;
        public Team Team;

        // Stats
        public float MaxHealth = 100f;
        public float Health = 100f;
        public float Block;               // 0..1 incoming damage reduction (base; statuses add to it)
        public float BaseDodge;           // 0..1
        public float Strain;
        public float MaxStrain = 100f;
        public float StrainRecoveryPerSecond;
        public float StrainRecoveryDelay;
        public float LastStrainGainAt = -999f;   // sim time strain last went up (recovery waits StrainRecoveryDelay after it)
        public float Energy;              // class resource (Melee energy); 0 MaxEnergy = the unit doesn't use it
        public float MaxEnergy;
        public float ActionSpeed = 1f;
        public float MoveSpeed = 4f;
        public float DashDistance = 3f;
        public float EyeHeight = 1.6f;
        public float BodyHeight = 1.8f;
        public float BodyRadius = 0.4f;

        public Vector3 Position;
        public List<ActionDefinition> Actions = new List<ActionDefinition>();
        public List<string> Passives = new List<string>();
        // From upgrades (UpgradeDefinition): modifiers on its stats (fixed ones are already baked into the fields above) and on
        // its abilities' numbers. Read through Stat / ActionStat.
        public List<Modifier> UpgradeModifiers = new List<Modifier>();
        public List<ActionModifier> UpgradeActionModifiers = new List<ActionModifier>();
        public string AIProfile = "";   // how the AI plays this unit (UnitAIs); empty = BasicEnemyAI

        // Runtime state
        public UnitPhase Phase = UnitPhase.Ready;
        public float PhaseEndsAt;
        public ActionInstance ActiveAction;
        public ActionInstance ResumeAction;   // windup cut short by a dodge dash; retried when the dash ends
        public float ResumeWindupLeft;
        // Out-of-range orders (UnitCommand.WaitForRange). Melee: walk at the target and strike once in reach (Chase).
        // Ranged: a standing order that fires once the target is in range while the unit moves or waits (Queued).
        public UnitCommand Chase;
        public float ChaseUntil, ChaseRepathAt;
        public UnitCommand Queued;
        public float QueuedUntil;
        public bool Waiting;                  // in the recovery of a Wait (Focus) order (a queued ranged ability may fire through it)
        public bool Idling;                   // ...and that Wait is an Idle pause: it doesn't count toward Focus
        public float FocusSince = -1f;        // sim time an unbroken run of Focus orders began (-1 = not focusing); see BattleSimulation.TickFocus
        public bool FocusTranscended;         // the Focus under way was Transcended: it runs until the unit is Focused
        // Dash / shove / pull in progress: the unit slides From -> To over [GlideStart, GlideEnd) and can't start anything.
        public Vector3 GlideFrom, GlideTo;
        public float GlideStart, GlideEnd = -1f;
        // Falling (shoved off a ledge, or knocked out of a jump): the glide above ends in a landing at FallLandAt that
        // deals FallDamage, credited to FallSourceId. -1 = not falling.
        public float FallLandAt = -1f;
        public float FallDamage;
        public string FallSourceId;
        // Jump in progress while walking across a height change: From -> To over [JumpStart, JumpEnd). -1 = not jumping.
        public Vector3 JumpFrom, JumpTo;
        public float JumpStart, JumpEnd = -1f;
        // Lifted (Amblivate): raised From -> To over [LiftStart, LiftRiseEnd), held there, dropped at LiftDropAt (a fall). -1 = not lifted.
        public Vector3 LiftFrom, LiftTo;
        public float LiftStart, LiftRiseEnd, LiftDropAt = -1f;
        public string LiftSourceId;
        // Hovering (Skyward Demise): a lift the unit made itself; it can act, not move. The ability it starts up there ends the
        // hover once it lands (HoverEndsAfterAction); HoverStatuses come off when the hover ends.
        public bool Hovering, HoverEndsAfterAction;
        public List<string> HoverStatuses;
        // Subjugate: what this unit is holding (null = nothing), and who is holding this one (null = nobody).
        public GrappleState Grapple;
        public string HeldById;
        public float ChargeDecisionAt;       // charging: when the unit next has to choose (keep charging or release)
        public ChannelState Channel;         // channeling (UnitPhase.Channeling), else null
        // Heretic's Ascent: the slam this unit is carrying out (null = none), and who is carrying this one up (null = nobody).
        public AerialSlamState Slam;
        public string SlammedById;
        // Mesmerize counter in progress: rapid strikes left at MesmerTargetId (each is a decision; anything else ends it).
        public string MesmerTargetId;
        public int MesmerStrikesLeft;
        public ActionDefinition MesmerStrike;
        // The last ability this unit started (not moves or waits): a follow-up ability can be re-cast from it (see ActionDefinition.IsFollowup).
        public string LastActionId;
        public string LastActionTargetId;
        public Vector3 LastActionPos;
        public ActiveMove Move;
        public List<StatusEffect> Statuses = new List<StatusEffect>();
        public Dictionary<string, float> CooldownReadyAt = new Dictionary<string, float>();
        public string LastAttackerId;
        public ActionDefinition LastHitAction;   // what last damaged this unit (Killed triggers report it)
        public string LastHitTag;

        public bool IsAlive { get { return Health > 0f && Phase != UnitPhase.Dead; } }

        public StatusEffect GetStatus(string id) { return Statuses.Find(s => s.Id == id); }
        public bool HasStatus(string id) { return GetStatus(id) != null; }
        /// <summary>Does any status give this rule?</summary>
        public bool HasFlag(StatusFlags flag) { return Statuses.Exists(s => s.Has(flag)); }
        public bool IsRooted { get { return HasFlag(StatusFlags.Rooted); } }
        public bool IsHidden { get { return HasFlag(StatusFlags.Untargetable); } }
        /// <summary>Stuns don't land (Unravel still does: ApplyStun's piercesImmunity).</summary>
        public bool IsStunImmune { get { return HasFlag(StatusFlags.StunImmune | StatusFlags.Unstoppable); } }
        /// <summary>Nothing but Unravel interrupts what it is doing (StatusFlags.Unstoppable).</summary>
        public bool IsUnstoppable { get { return HasFlag(StatusFlags.Unstoppable); } }
        public bool IsJumping { get { return JumpEnd >= 0f; } }
        /// <summary>Up in the air and held there: lifted (Amblivate), hovering (Skyward Demise) or in an aerial slam.</summary>
        public bool IsLifted { get { return LiftDropAt >= 0f || Slam != null || SlammedById != null; } }
        public bool InAerialSlam { get { return Slam != null || SlammedById != null; } }

        // ------------------------------------------------------------------ stats

        /// <summary>
        /// A stat now: 'baseValue' with every modifier from the unit's upgrades (UpgradeModifiers) and statuses folded in, as
        /// (base + adds) x mults. The one place stats are worked out; the named helpers below just pick the base and the limits.
        /// </summary>
        public float Stat(StatId stat, float baseValue)
        {
            var f = StatFold.Start;
            f.Take(UpgradeModifiers, stat);
            foreach (var s in Statuses) f.Take(s.Modifiers, stat);
            return f.Of(baseValue);
        }

        /// <summary>The statuses' Multiply modifiers on 'stat' alone (action speed statuses also scale walking).</summary>
        public float StatusMultiplier(StatId stat)
        {
            var f = StatFold.Start;
            foreach (var s in Statuses) f.Take(s.Modifiers, stat);
            return f.MultProduct;
        }

        /// <summary>
        /// One of an ability's numbers for this unit: the definition's value (or 'baseValue', for behaviors that work it out
        /// themselves) with the unit's ability modifiers (from upgrades and statuses) folded in. Never below zero, except a
        /// strain cost (negative relieves strain).
        /// </summary>
        public float ActionStat(ActionDefinition def, ActionStat stat, float baseValue)
        {
            var f = StatFold.Start;
            f.Take(UpgradeActionModifiers, def, stat);
            foreach (var s in Statuses) f.Take(s.ActionModifiers, def, stat);
            float v = f.Of(baseValue);
            return Stats.AllowsNegative(stat) ? v : Mathf.Max(0f, v);
        }

        public float ActionStat(ActionDefinition def, ActionStat stat) { return ActionStat(def, stat, Stats.Base(def, stat)); }

        /// <summary>What this unit's attacks are scaled by (Wrath).</summary>
        public float DamageDealtMult() { return Mathf.Max(0f, Stat(StatId.DamageDealt, 1f)); }

        /// <summary>What this unit's strain costs are scaled by (Wrath: free).</summary>
        public float StrainCostMult() { return Mathf.Max(0f, Stat(StatId.StrainCost, 1f)); }

        /// <summary>Added to the hit chance of attacks on this unit (Marked).</summary>
        public float IncomingAccuracyBonus() { return Stat(StatId.IncomingAccuracy, 0f); }

        /// <summary>The status that forbids this action right now (ForbidsActions), or null.</summary>
        public StatusEffect StatusForbidding(string actionId)
        {
            foreach (var s in Statuses) if (s.ForbidsActions != null && s.ForbidsActions.Contains(actionId)) return s;
            return null;
        }

        /// <summary>Stunned and mid-jump units cannot dodge.</summary>
        public float EffectiveDodge() { return Phase == UnitPhase.Stunned || IsJumping ? 0f : UnstunnedDodge(); }

        /// <summary>Dodge once any stun has worn off (previews of hits that land after the stun ends).</summary>
        public float UnstunnedDodge() { return Mathf.Clamp01(Stat(StatId.Dodge, BaseDodge)); }

        public float EffectiveBlock() { return Mathf.Clamp01(Stat(StatId.Block, Block)); }

        /// <summary>Base ActionSpeed with every haste and slow. Higher = shorter windup/recovery.</summary>
        public float EffectiveActionSpeed() { return Mathf.Max(0.1f, Stat(StatId.ActionSpeed, ActionSpeed)); }

        /// <summary>Walking speed: action speed statuses (Trance, Second Mind, Fatebreak, Chronosphere) scale walking too.</summary>
        public float EffectiveMoveSpeed() { return Mathf.Max(0.1f, Stat(StatId.MoveSpeed, MoveSpeed) * StatusMultiplier(StatId.ActionSpeed)); }

        public float DashDistanceNow() { return Mathf.Max(0f, Stat(StatId.DashDistance, DashDistance)); }

        /// <summary>Block is shed from temporary status bonuses first (newest first), then the base value. Upgrades' bonuses aren't worn.</summary>
        public void ReduceBlock(float amount)
        {
            for (int i = Statuses.Count - 1; i >= 0 && amount > 0f; i--)
            {
                var mods = Statuses[i].Modifiers;
                if (mods == null || Statuses[i].Has(StatusFlags.KeepsBlock)) continue;
                for (int j = 0; j < mods.Count && amount > 0f; j++)
                {
                    if (mods[j].Stat != StatId.Block || mods[j].Op != ModOp.Add || mods[j].Value <= 0f) continue;
                    float take = Mathf.Min(mods[j].Value, amount);
                    mods[j] = Modifier.Add(StatId.Block, mods[j].Value - take);
                    amount -= take;
                }
            }
            if (amount > 0f) Block = Mathf.Max(0f, Block - amount);
        }

        /// <summary>
        /// A unit for the battle from its definition. 'loadout': the class abilities it brings (null = all of them; the basic
        /// attack, Block and Dash always come). 'upgrades': its permanent improvements: abilities they grant are added, fixed
        /// stats (Stats.IsFixed) are baked in here, and every other modifier is kept on the unit (UpgradeModifiers,
        /// UpgradeActionModifiers) and read through Stat / ActionStat. The definition is never changed.
        /// </summary>
        public static Unit FromDefinition(UnitDefinition def, string id, Team team, Vector3 position,
                                          IList<ActionDefinition> loadout = null, IList<UpgradeDefinition> upgrades = null)
        {
            var actions = new List<ActionDefinition>(loadout ?? def.Actions);
            var unitMods = new List<Modifier>();
            var actionMods = new List<ActionModifier>();
            if (upgrades != null)
                foreach (var up in upgrades)
                {
                    if (up == null) continue;
                    if (up.GrantsActions != null) foreach (var a in up.GrantsActions) if (a != null && !actions.Contains(a)) actions.Add(a);
                    if (up.UnitModifiers != null) unitMods.AddRange(up.UnitModifiers);
                    if (up.ActionModifiers != null) actionMods.AddRange(up.ActionModifiers);
                }
            // Every unit can always fall back on its basic attack; it goes last so AI tries abilities first.
            if (def.BasicAttack != null && !actions.Contains(def.BasicAttack)) actions.Add(def.BasicAttack);
            if (def.Universal != null && !actions.Contains(def.Universal)) actions.Add(def.Universal);   // Block
            if (def.Dash != null && !actions.Contains(def.Dash)) actions.Add(def.Dash);
            actions.RemoveAll(a => a == null);
            float maxHealth = Fixed(unitMods, StatId.MaxHealth, def.MaxHealth);
            return new Unit
            {
                Id = id,
                DisplayName = def.DisplayName,
                Team = team,
                MaxHealth = maxHealth,
                Health = maxHealth,
                BaseDodge = def.BaseDodge,
                Block = def.BaseBlock,
                MaxStrain = Fixed(unitMods, StatId.MaxStrain, def.MaxStrain),
                StrainRecoveryPerSecond = Fixed(unitMods, StatId.StrainRecovery, def.StrainRecoveryPerSecond),
                StrainRecoveryDelay = Fixed(unitMods, StatId.StrainRecoveryDelay, def.StrainRecoveryDelay),
                MaxEnergy = def.MaxEnergy,
                Energy = Mathf.Min(def.StartingEnergy, def.MaxEnergy),
                ActionSpeed = def.ActionSpeed,
                MoveSpeed = def.MoveSpeed,
                DashDistance = def.DashDistance,
                EyeHeight = def.EyeHeight,
                BodyHeight = def.BodyHeight,
                BodyRadius = def.BodyRadius,
                Position = position,
                Actions = actions,
                Passives = new List<string>(def.Passives),
                AIProfile = def.AIProfile ?? "",
                UpgradeModifiers = unitMods.FindAll(m => !Stats.IsFixed(m.Stat)),
                UpgradeActionModifiers = actionMods,
            };
        }

        private static float Fixed(List<Modifier> mods, StatId stat, float baseValue)
        {
            var f = StatFold.Start;
            f.Take(mods, stat);
            return Mathf.Max(0f, f.Of(baseValue));
        }

        /// <summary>Copies stats and position; transient combat state starts fresh.</summary>
        public Unit Snapshot()
        {
            return new Unit
            {
                Id = Id, DisplayName = DisplayName, Team = Team,
                MaxHealth = MaxHealth, Health = Health, Block = Block, BaseDodge = BaseDodge,
                Strain = Strain, MaxStrain = MaxStrain, StrainRecoveryPerSecond = StrainRecoveryPerSecond, StrainRecoveryDelay = StrainRecoveryDelay, LastStrainGainAt = LastStrainGainAt, Energy = Energy, MaxEnergy = MaxEnergy,
                ActionSpeed = ActionSpeed, MoveSpeed = MoveSpeed, DashDistance = DashDistance,
                EyeHeight = EyeHeight, BodyHeight = BodyHeight, BodyRadius = BodyRadius,
                Position = Position, Actions = Actions, Passives = Passives, AIProfile = AIProfile,
                UpgradeModifiers = UpgradeModifiers, UpgradeActionModifiers = UpgradeActionModifiers,
            };
        }
    }

    public class BattleState
    {
        public List<Unit> Units = new List<Unit>();
        public List<Occluder> Occluders = new List<Occluder>();   // deployables/smoke/walls that block sight
        public List<Trap> Traps = new List<Trap>();
        public TerrainMap Terrain = new TerrainMap();   // fixed for the whole battle: clones share it

        public BattleState Clone()
        {
            var clone = new BattleState { Terrain = Terrain };
            foreach (var u in Units) clone.Units.Add(u.Snapshot());
            foreach (var o in Occluders) clone.Occluders.Add(o.Clone());
            foreach (var t in Traps) clone.Traps.Add(t.Clone());
            return clone;
        }

        public Unit GetUnit(string id) { return Units.Find(u => u.Id == id); }
    }
}
