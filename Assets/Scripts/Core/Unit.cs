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
        public string TargetId;             // whom a follow-up chain is aimed at (Rapid Slices, Steady Strikes)
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

        public StatusEffect Create(float now, string sourceId, ActionDefinition linkedAction = null)
        {
            return new StatusEffect
            {
                Id = Id, SourceId = sourceId, ExpiresAt = now + Duration,
                DodgeBonus = DodgeBonus, BlockBonus = BlockBonus, IncomingAccuracyBonus = IncomingAccuracyBonus,
                DodgeDistanceBonus = DodgeDistanceBonus, ActionSpeedMult = ActionSpeedMult, MoveSpeedMult = MoveSpeedMult,
                Rooted = Rooted, Untargetable = Untargetable, BreaksOnDamage = BreaksOnDamage, StunImmune = StunImmune, KeepsBlock = KeepsBlock, LinkedAction = linkedAction,
            };
        }
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
        public bool Waiting;                  // in the recovery of a Wait order (a queued ranged ability may fire through it)
        // Dash / shove / pull in progress: the unit slides From -> To over [GlideStart, GlideEnd) and can't start anything.
        public Vector3 GlideFrom, GlideTo;
        public float GlideStart, GlideEnd = -1f;
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

        /// <summary>Stunned units cannot dodge.</summary>
        public float EffectiveDodge() { return Phase == UnitPhase.Stunned ? 0f : UnstunnedDodge(); }

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
            if (def.Universal != null && !actions.Contains(def.Universal)) actions.Add(def.Universal);
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

        public BattleState Clone()
        {
            var clone = new BattleState();
            foreach (var u in Units) clone.Units.Add(u.Snapshot());
            foreach (var o in Occluders) clone.Occluders.Add(o.Clone());
            foreach (var t in Traps) clone.Traps.Add(t.Clone());
            return clone;
        }

        public Unit GetUnit(string id) { return Units.Find(u => u.Id == id); }
    }
}
