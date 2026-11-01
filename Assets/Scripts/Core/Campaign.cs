using System.Collections.Generic;
using UnityEngine;

namespace TacticsGame.Core
{
    // ------------------------------------------------------------------ encounters

    /// <summary>What wins an encounter. New values go at the end: assets store the number.</summary>
    public enum EncounterObjective { DefeatAllEnemies }

    /// <summary>Some enemies of one kind in an encounter, with any upgrades they fight with.</summary>
    [System.Serializable]
    public class EnemyEntry
    {
        public UnitDefinition Unit;
        [Min(1)] public int Count = 1;
        public List<UpgradeDefinition> Upgrades = new List<UpgradeDefinition>();
    }

    /// <summary>What winning an encounter earns.</summary>
    [System.Serializable]
    public class EncounterRewards
    {
        public int Currency;
        [Tooltip("Upgrades added to the squad's stock, to give to members later.")]
        public List<UpgradeDefinition> Upgrades = new List<UpgradeDefinition>();
    }

    /// <summary>A battle to fight, without the scene parts (the map lives on EncounterDefinition): who the enemies are, what
    /// wins it, what it pays, and whether wounds carry over to the squad afterwards.</summary>
    [System.Serializable]
    public class EncounterSpec
    {
        [Tooltip("Stable id for saves (completed encounters). Empty: the encounter asset's name.")]
        public string Id = "";
        public List<EnemyEntry> Enemies = new List<EnemyEntry>();
        public EncounterObjective Objective = EncounterObjective.DefeatAllEnemies;
        public EncounterRewards Rewards = new EncounterRewards();
        [Tooltip("The squad keeps its wounds afterwards (members brought to 0 health are down until they rest).")]
        public bool CarryOverWounds = true;
    }

    // ------------------------------------------------------------------ the squad between battles

    /// <summary>One squad member between battles, by ids (so a roster saves as plain data): its class, the class abilities it
    /// brings, its upgrades, and how hurt it is.</summary>
    [System.Serializable]
    public class RosterMember
    {
        public string Id;                 // stable within the roster ("m1")
        public string Class;              // the UnitDefinition's id (ContentCatalog)
        public string Name;               // shown name; empty = the class's
        public bool HasLoadout;           // false: brings all its class abilities
        public List<string> Loadout = new List<string>();     // ActionDefinition ids
        public List<string> Upgrades = new List<string>();    // UpgradeDefinition ids
        [Range(0f, 1f)] public float Wounds;                  // share of max health missing; 1 = down (can't deploy until it rests)

        public bool Down { get { return Wounds >= 1f - 1e-4f; } }
    }

    /// <summary>The player's squad between battles: members, currency, upgrades not yet given out, encounters won.</summary>
    [System.Serializable]
    public class SquadRoster
    {
        public List<RosterMember> Members = new List<RosterMember>();
        public int Currency;
        public List<string> OwnedUpgrades = new List<string>();      // earned or bought, not given to anyone yet
        public List<string> CompletedEncounters = new List<string>();
        public int NextMemberNumber = 1;

        public RosterMember Find(string memberId) { return Members.Find(m => m.Id == memberId); }

        /// <summary>A new member of class 'unit' (bringing all its abilities, unhurt).</summary>
        public RosterMember Add(UnitDefinition unit)
        {
            var m = new RosterMember { Id = "m" + (NextMemberNumber++), Class = ContentCatalog.IdOf(unit) };
            Members.Add(m);
            return m;
        }

        /// <summary>A fresh squad of these classes.</summary>
        public static SquadRoster New(IEnumerable<UnitDefinition> classes)
        {
            var r = new SquadRoster();
            foreach (var c in classes) if (c != null) r.Add(c);
            return r;
        }

        /// <summary>Rest: every member's wounds heal by 'share' of max health (1 = fully; the down get back up).</summary>
        public void Rest(float share = 1f)
        {
            foreach (var m in Members) m.Wounds = Mathf.Clamp01(m.Wounds - share);
        }

        /// <summary>Give an owned upgrade to a member (it leaves the stock). False if not owned or already theirs.</summary>
        public bool GiveUpgrade(string memberId, string upgradeId)
        {
            var m = Find(memberId);
            if (m == null || !OwnedUpgrades.Contains(upgradeId) || m.Upgrades.Contains(upgradeId)) return false;
            OwnedUpgrades.Remove(upgradeId);
            m.Upgrades.Add(upgradeId);
            return true;
        }
    }

    // ------------------------------------------------------------------ between the roster and a battle

    /// <summary>A squad member ready to deploy: its class, the abilities it brings (null = all) and its upgrades.</summary>
    public class SquadSlot
    {
        public RosterMember Member;
        public UnitDefinition Definition;
        public List<ActionDefinition> Loadout;
        public List<UpgradeDefinition> Upgrades = new List<UpgradeDefinition>();
    }

    /// <summary>One enemy to deploy.</summary>
    public class EnemySlot
    {
        public UnitDefinition Definition;
        public List<UpgradeDefinition> Upgrades = new List<UpgradeDefinition>();
    }

    /// <summary>How an encounter went, for the results screen.</summary>
    public class EncounterResult
    {
        public BattleOutcome Outcome;
        public bool Won { get { return Outcome == BattleOutcome.PlayerVictory; } }
        public int CurrencyEarned;
        public List<string> UpgradesEarned = new List<string>();
        public List<string> MembersDown = new List<string>();
    }

    /// <summary>Turning a roster and an encounter into a battle's units, and a finished battle back into the roster.</summary>
    public static class Campaign
    {
        /// <summary>The members who can deploy (not down, class known), with their loadouts and upgrades resolved.</summary>
        public static List<SquadSlot> Squad(SquadRoster roster, ContentCatalog catalog)
        {
            var slots = new List<SquadSlot>();
            foreach (var m in roster.Members)
            {
                var def = catalog.Unit(m.Class);
                if (def == null || m.Down) continue;
                var slot = new SquadSlot { Member = m, Definition = def };
                if (m.HasLoadout) slot.Loadout = def.Actions.FindAll(a => a != null && m.Loadout.Contains(a.Id));
                foreach (var id in m.Upgrades) { var up = catalog.Upgrade(id); if (up != null) slot.Upgrades.Add(up); }
                slots.Add(slot);
            }
            return slots;
        }

        /// <summary>The encounter's enemies, one slot each (Count expanded).</summary>
        public static List<EnemySlot> Enemies(EncounterSpec spec)
        {
            var slots = new List<EnemySlot>();
            foreach (var e in spec.Enemies)
            {
                if (e == null || e.Unit == null) continue;
                for (int i = 0; i < Mathf.Max(1, e.Count); i++) slots.Add(new EnemySlot { Definition = e.Unit, Upgrades = new List<UpgradeDefinition>(e.Upgrades ?? new List<UpgradeDefinition>()) });
            }
            return slots;
        }

        /// <summary>A squad member's unit for the battle: built from its class with its loadout and upgrades, carrying its wounds.</summary>
        public static Unit MakeUnit(SquadSlot slot, string id, Vector3 position)
        {
            var u = Unit.FromDefinition(slot.Definition, id, Team.Player, position, slot.Loadout, slot.Upgrades);
            ApplyWounds(u, slot.Member != null ? slot.Member.Wounds : 0f);
            return u;
        }

        /// <summary>Start a unit 'wounds' (share of max health) down. Never kills: a member that deploys has some health.</summary>
        public static void ApplyWounds(Unit u, float wounds)
        {
            if (wounds <= 0f) return;
            u.Health = Mathf.Max(1f, u.MaxHealth * (1f - Mathf.Clamp01(wounds)));
        }

        /// <summary>
        /// The battle is over: write it back into the roster. Each deployed member ('unitOf': member id -> its unit id in the
        /// battle) keeps its wounds (if the encounter carries them over; downed if killed), and a win pays the rewards and
        /// marks the encounter done. Members who sat it out are untouched.
        /// </summary>
        public static EncounterResult Finish(SquadRoster roster, EncounterSpec spec, string encounterId, BattleSimulation sim, IDictionary<string, string> unitOf)
        {
            var result = new EncounterResult { Outcome = sim.Outcome };
            foreach (var kv in unitOf)
            {
                var m = roster.Find(kv.Key);
                var u = sim.GetUnit(kv.Value);
                if (m == null || u == null) continue;
                if (spec.CarryOverWounds) m.Wounds = u.IsAlive ? Mathf.Clamp01(1f - u.Health / Mathf.Max(1f, u.MaxHealth)) : 1f;
                if (m.Down) result.MembersDown.Add(m.Id);
            }
            if (!result.Won) return result;
            var rewards = spec.Rewards ?? new EncounterRewards();
            roster.Currency += rewards.Currency;
            result.CurrencyEarned = rewards.Currency;
            if (rewards.Upgrades != null)
                foreach (var up in rewards.Upgrades) if (up != null) { roster.OwnedUpgrades.Add(up.Id); result.UpgradesEarned.Add(up.Id); }
            if (!string.IsNullOrEmpty(encounterId) && !roster.CompletedEncounters.Contains(encounterId)) roster.CompletedEncounters.Add(encounterId);
            return result;
        }
    }
}
