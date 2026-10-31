using System.Collections.Generic;
using UnityEngine;

namespace TacticsGame.Core
{
    /// <summary>A timed modifier (buff/debuff). Expand with whatever modifiers abilities need.</summary>
    public class StatusEffect
    {
        public string Id;
        public string SourceId;
        public float ExpiresAt;
        public float DodgeBonus;
        public float BlockBonus;
        public float IncomingAccuracyBonus; // e.g. "Marked": attacks against this unit hit more easily
        public float DodgeDistanceBonus;
        public float ActionSpeedMult = 1f;  // haste > 1, slow < 1: scales windup/recovery speed
        public float MoveSpeedMult = 1f;
        public bool Rooted;                 // can't move or dash (Trap)
        public bool Untargetable;           // enemies can't target or hit this unit (Marking Shot's stealth)
        public bool BreaksOnDamage;         // removed the moment the unit takes damage
        public bool StunImmune;             // stuns (and Unravel) don't land (Block)
        public bool KeepsBlock;             // its BlockBonus isn't worn down by hits (Brace)
        public bool NegatesDamage;          // hits that land deal no damage and no strain (Transcended Block)
        public float DamageDealtMult = 1f;  // scales the damage of this unit's attacks (Wrath)
        public float StrainCostMult = 1f;   // scales what this unit's abilities cost in strain (Wrath: 0)
        public List<string> ForbidsActions; // action ids this unit can't use while it has the status (Wrath: Block, Battle Trance)
        public string TargetId;            // whom a follow-up chain is aimed at (Rapid Slices, Steady Strikes)
        public int Stacks;                  // free counter for behaviors (Rapid Slices follow-ups used)
        public ActionDefinition LinkedAction; // the action a stance/reaction fires (Take Aim's Snipe, Retaliate's shot)

        public StatusEffect Copy() { return (StatusEffect)MemberwiseClone(); }
    }

    /// <summary>Author-time description of a status an action applies. Turned into a StatusEffect when it lands.</summary>
    [System.Serializable]
    public class StatusSpec
    {
        public string Id = "status";
        public float Duration = 3f;
        public float DodgeBonus;
        public float BlockBonus;
        public float IncomingAccuracyBonus;
        public float DodgeDistanceBonus;
        public float ActionSpeedMult = 1f;
        public float MoveSpeedMult = 1f;
        public bool Rooted;
        public bool Untargetable;
        public bool BreaksOnDamage;
        public bool StunImmune;
        public bool KeepsBlock;
        [UnityEngine.Tooltip("Scales the damage of the unit's attacks (Wrath).")]
        public float DamageDealtMult = 1f;
        [UnityEngine.Tooltip("Scales what the unit's abilities cost in strain (Wrath: 0 = free).")]
        public float StrainCostMult = 1f;
        [UnityEngine.Tooltip("Action ids the unit can't use while it has this status (Wrath: block, battle_trance).")]
        public List<string> ForbidsActions = new List<string>();
        [UnityEngine.Tooltip("In a list of statuses an ability puts on its target: whom this one lands on.")]
        public StatusTarget AppliesTo = StatusTarget.Any;

        public StatusEffect Create(float now, string sourceId, ActionDefinition linkedAction = null)
        {
            return new StatusEffect
            {
                Id = Id, SourceId = sourceId, ExpiresAt = now + Duration,
                DodgeBonus = DodgeBonus, BlockBonus = BlockBonus, IncomingAccuracyBonus = IncomingAccuracyBonus,
                DodgeDistanceBonus = DodgeDistanceBonus, ActionSpeedMult = ActionSpeedMult, MoveSpeedMult = MoveSpeedMult,
                Rooted = Rooted, Untargetable = Untargetable, BreaksOnDamage = BreaksOnDamage, StunImmune = StunImmune, KeepsBlock = KeepsBlock, LinkedAction = linkedAction,
                DamageDealtMult = DamageDealtMult, StrainCostMult = StrainCostMult,
                ForbidsActions = ForbidsActions != null && ForbidsActions.Count > 0 ? new List<string>(ForbidsActions) : null,
            };
        }

        /// <summary>Does this status land on 'target' when 'source' puts it there (AppliesTo)?</summary>
        public bool AppliesToUnit(Unit source, Unit target)
        {
            if (AppliesTo == StatusTarget.Any || source == null) return true;
            return (target.Team == source.Team) == (AppliesTo == StatusTarget.Allies);
        }
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
        // Subjugate: what this unit is holding (null = nothing), and who is holding this one (null = nobody).
        public GrappleState Grapple;
        public string HeldById;
        public float ChargeDecisionAt;       // charging: when the unit next has to choose (keep charging or release)
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
        public bool IsRooted { get { return Statuses.Exists(s => s.Rooted); } }
        public bool IsHidden { get { return Statuses.Exists(s => s.Untargetable); } }
        public bool IsStunImmune { get { return Statuses.Exists(s => s.StunImmune); } }
        public bool IsJumping { get { return JumpEnd >= 0f; } }
        public bool IsLifted { get { return LiftDropAt >= 0f; } }

        /// <summary>Product of the statuses' DamageDealtMult: what this unit's attacks are scaled by.</summary>
        public float DamageDealtMult()
        {
            float m = 1f;
            foreach (var s in Statuses) m *= s.DamageDealtMult;
            return m;
        }

        /// <summary>Product of the statuses' StrainCostMult: what this unit's strain costs are scaled by.</summary>
        public float StrainCostMult()
        {
            float m = 1f;
            foreach (var s in Statuses) m *= s.StrainCostMult;
            return Mathf.Max(0f, m);
        }

        /// <summary>The status that forbids this action right now (ForbidsActions), or null.</summary>
        public StatusEffect StatusForbidding(string actionId)
        {
            foreach (var s in Statuses) if (s.ForbidsActions != null && s.ForbidsActions.Contains(actionId)) return s;
            return null;
        }

        /// <summary>Stunned and mid-jump units cannot dodge.</summary>
        public float EffectiveDodge() { return Phase == UnitPhase.Stunned || IsJumping ? 0f : UnstunnedDodge(); }

        /// <summary>Dodge once any stun has worn off (previews of hits that land after the stun ends).</summary>
        public float UnstunnedDodge()
        {
            float d = BaseDodge;
            foreach (var s in Statuses) d += s.DodgeBonus;
            return Mathf.Clamp01(d);
        }

        public float EffectiveBlock()
        {
            float b = Block;
            foreach (var s in Statuses) b += s.BlockBonus;
            return Mathf.Clamp01(b);
        }

        /// <summary>Base ActionSpeed times all haste/slow statuses. Higher = shorter windup/recovery.</summary>
        public float EffectiveActionSpeed()
        {
            float s = ActionSpeed;
            foreach (var st in Statuses) s *= st.ActionSpeedMult;
            return Mathf.Max(0.1f, s);
        }

        public float EffectiveMoveSpeed()
        {
            float s = MoveSpeed;
            foreach (var st in Statuses) s *= st.MoveSpeedMult;
            return Mathf.Max(0.1f, s);
        }

        public float DashDistanceNow()
        {
            float d = DashDistance;
            foreach (var s in Statuses) d += s.DodgeDistanceBonus;
            return d;
        }

        /// <summary>Block is shed from temporary status bonuses first (newest first), then the base value.</summary>
        public void ReduceBlock(float amount)
        {
            for (int i = Statuses.Count - 1; i >= 0 && amount > 0f; i--)
            {
                if (Statuses[i].KeepsBlock) continue;
                float take = Mathf.Min(Statuses[i].BlockBonus, amount);
                if (take <= 0f) continue;
                Statuses[i].BlockBonus -= take;
                amount -= take;
            }
            if (amount > 0f) Block = Mathf.Max(0f, Block - amount);
        }

        public static Unit FromDefinition(UnitDefinition def, string id, Team team, Vector3 position)
        {
            var actions = new List<ActionDefinition>(def.Actions);
            // Every unit can always fall back on its basic attack; it goes last so AI tries abilities first.
            if (def.BasicAttack != null && !actions.Contains(def.BasicAttack)) actions.Add(def.BasicAttack);
            if (def.Universal != null && !actions.Contains(def.Universal)) actions.Add(def.Universal);   // Block
            if (def.Dash != null && !actions.Contains(def.Dash)) actions.Add(def.Dash);
            actions.RemoveAll(a => a == null);
            return new Unit
            {
                Id = id,
                DisplayName = def.DisplayName,
                Team = team,
                MaxHealth = def.MaxHealth,
                Health = def.MaxHealth,
                BaseDodge = def.BaseDodge,
                Block = def.BaseBlock,
                MaxStrain = def.MaxStrain, StrainRecoveryPerSecond = def.StrainRecoveryPerSecond, StrainRecoveryDelay = def.StrainRecoveryDelay,
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
            };
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
                Position = Position, Actions = Actions, Passives = Passives,
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
