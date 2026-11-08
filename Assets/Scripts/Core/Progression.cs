using System.Collections.Generic;
using UnityEngine;

namespace TacticsGame.Core
{
    /// <summary>
    /// Growing the squad between missions: after each win every member is offered Etchings (permanent passives, see
    /// EtchingDefinition) and takes some (BeginUpgrades, Take), and spends ability points (EncounterRewards, some Etchings)
    /// on unlocking class abilities. All of it is kept on the roster as ids; Growth turns it into what a unit fights with
    /// (Unit.FromDefinition), so nothing changes the definitions.
    /// </summary>
    public static class Progression
    {
        /// <summary>A new member of class 'def' with its starting abilities: the class's default set, or 'RandomCount' at random.</summary>
        public static RosterMember AddMember(SquadRoster roster, UnitDefinition def, ProgressionConfig config, LoadoutStart start, System.Random rng)
        {
            var m = roster.Add(def);
            m.Progressing = true;
            var options = def.Actions.FindAll(a => a != null && a != def.BasicAttack);
            var cs = config != null ? config.StartFor(def) : null;
            List<ActionDefinition> chosen;
            if (start == LoadoutStart.Random)
            {
                int n = Mathf.Min(options.Count, cs != null ? cs.RandomCount : 3);
                chosen = new List<ActionDefinition>(options);
                for (int i = chosen.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); var t = chosen[i]; chosen[i] = chosen[j]; chosen[j] = t; }
                chosen = chosen.GetRange(0, n);
            }
            else chosen = cs != null ? cs.DefaultAbilities.FindAll(a => a != null && options.Contains(a)) : options;
            foreach (var a in chosen) m.Unlocked.Add(a.Id);
            return m;
        }

        /// <summary>A fresh campaign squad of these classes.</summary>
        public static SquadRoster NewCampaign(IEnumerable<UnitDefinition> classes, ProgressionConfig config, LoadoutStart start, int seed)
        {
            var roster = new SquadRoster { Seed = seed };
            var rng = new System.Random(seed);
            foreach (var c in classes) if (c != null) AddMember(roster, c, config, start, rng);
            return roster;
        }

        // ------------------------------------------------------------------ abilities (ability points)

        /// <summary>The class abilities a member hasn't unlocked yet.</summary>
        public static List<ActionDefinition> Locked(RosterMember m, UnitDefinition def)
        {
            return def.Actions.FindAll(a => a != null && a != def.BasicAttack && !m.Unlocked.Contains(a.Id));
        }

        public static bool CanUnlock(RosterMember m, ActionDefinition a, ProgressionConfig config, out string why)
        {
            why = null;
            if (m.Unlocked.Contains(a.Id)) { why = "already unlocked"; return false; }
            int cost = config.CostOf(a);
            if (m.AbilityPoints < cost) { why = string.Format("needs {0} ability point{1}", cost, cost == 1 ? "" : "s"); return false; }
            return true;
        }

        public static bool Unlock(RosterMember m, ActionDefinition a, ProgressionConfig config)
        {
            string why;
            if (!CanUnlock(m, a, config, out why)) return false;
            m.AbilityPoints -= config.CostOf(a);
            m.Unlocked.Add(a.Id);
            m.AbilityOffer.Remove(a.Id);
            return true;
        }

        // ------------------------------------------------------------------ Etchings (the upgrade phase)

        /// <summary>What a member fights with and so can Etch: its unlocked abilities, plus the basic attack, Block and Dash.</summary>
        public static List<ActionDefinition> Abilities(RosterMember m, UnitDefinition def)
        {
            var list = def.Actions.FindAll(a => a != null && a != def.BasicAttack && m.Unlocked.Contains(a.Id));
            foreach (var a in new[] { def.BasicAttack, def.Universal, def.Dash }) if (a != null && !list.Contains(a)) list.Add(a);
            return list;
        }

        /// <summary>The member's abilities a One Ability Etching can go on (Stats.Has); empty for a unit one.</summary>
        public static List<ActionDefinition> Etchable(RosterMember m, UnitDefinition def, EtchingDefinition e)
        {
            if (e.Scope != EtchingScope.OneAbility) return new List<ActionDefinition>();
            var list = Abilities(m, def).FindAll(e.CanEtch);
            // A granted Transcend only for an ability no other Etching has given one.
            if (e.Grant != TranscendGrant.None) list.RemoveAll(a => m.Etchings.Exists(p => p.ActionId == a.Id && p.GrantsTranscend));
            return list;
        }

        private static ActionDefinition AbilityById(UnitDefinition def, string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var a in new[] { def.BasicAttack, def.Universal, def.Dash }) if (a != null && a.Id == id) return a;
            return def.Actions.Find(a => a != null && a.Id == id);
        }

        /// <summary>
        /// Start an upgrade phase: every progressing member is offered EtchingsOffered different Etchings, drawn by weight
        /// from the pool (a One Ability one only if it has an ability to go on), and may take EtchingsTaken of them. The draw
        /// is fixed by the roster's seed and the mission, so reloading shows the same offer.
        /// </summary>
        /// <param name="picks">How many each member takes (the campaign's number for the mission just won); below 0, the config's EtchingsTaken.</param>
        public static void BeginUpgrades(SquadRoster roster, ProgressionConfig config, ContentCatalog catalog, int picks = -1)
        {
            if (config == null) return;
            for (int i = 0; i < roster.Members.Count; i++)
            {
                var m = roster.Members[i];
                var def = catalog.Unit(m.Class);
                m.Offer.Clear(); m.PicksLeft = 0; m.AbilityOffer.Clear();
                if (!m.Progressing || def == null) continue;
                DrawAbilityOffer(m, def, config, new System.Random(roster.Seed * 7919 + roster.MissionIndex * 104729 + i * 31 + 17));
                var rng = new System.Random(roster.Seed * 7919 + roster.MissionIndex * 104729 + i * 31);
                var pool = config.Etchings.FindAll(e => e != null && e.Weight > 0f && (e.Scope == EtchingScope.Unit || Etchable(m, def, e).Count > 0));
                while (m.Offer.Count < config.EtchingsOffered && pool.Count > 0)
                {
                    float total = 0f;
                    foreach (var e in pool) total += e.Weight;
                    float roll = (float)rng.NextDouble() * total;
                    int k = 0;
                    while (k < pool.Count - 1 && roll >= pool[k].Weight) { roll -= pool[k].Weight; k++; }
                    m.Offer.Add(pool[k].Id);
                    pool.RemoveAt(k);
                }
                m.PicksLeft = Mathf.Min(picks >= 0 ? picks : config.EtchingsTaken, m.Offer.Count);
            }
        }

        /// <summary>Which locked class abilities a member may unlock this phase: AbilitiesOffered of them at random (all if 0).</summary>
        public static void DrawAbilityOffer(RosterMember m, UnitDefinition def, ProgressionConfig config, System.Random rng)
        {
            m.AbilityOffer.Clear();
            var locked = Locked(m, def);
            int count = config.AbilitiesOffered > 0 ? Mathf.Min(config.AbilitiesOffered, locked.Count) : locked.Count;
            while (m.AbilityOffer.Count < count)
            {
                int k = rng.Next(locked.Count);
                m.AbilityOffer.Add(locked[k].Id);
                locked.RemoveAt(k);
            }
            m.AbilityOffer.Sort((x, y) => def.Actions.FindIndex(a => a != null && a.Id == x).CompareTo(def.Actions.FindIndex(a => a != null && a.Id == y)));   // class order
        }

        /// <summary>The first member still choosing Etchings, or -1 when the phase is done.</summary>
        public static int NextToChoose(SquadRoster roster)
        {
            return roster.Members.FindIndex(m => m.PicksLeft > 0 && m.Offer.Count > 0);
        }

        public static bool CanTake(RosterMember m, UnitDefinition def, EtchingDefinition e, string actionId, out string why)
        {
            why = null;
            if (e == null || !m.Offer.Contains(e.Id)) { why = "not offered"; return false; }
            if (m.PicksLeft <= 0) { why = "no picks left"; return false; }
            if (e.Scope == EtchingScope.OneAbility && !Etchable(m, def, e).Exists(a => a.Id == actionId)) { why = "pick an ability it can go on"; return false; }
            return true;
        }

        /// <summary>Take an offered Etching (One Ability: onto 'actionId'). It leaves the offer, and pays its ability points.</summary>
        public static bool Take(RosterMember m, UnitDefinition def, EtchingDefinition e, string actionId = null)
        {
            string why;
            if (!CanTake(m, def, e, actionId, out why)) return false;
            GiveEtching(m, e, actionId);
            m.Offer.Remove(e.Id);
            m.PicksLeft--;
            return true;
        }

        /// <summary>'m' gains Etching 'e' (One Ability: on 'actionId'), and the ability points it pays.</summary>
        private static void GiveEtching(RosterMember m, EtchingDefinition e, string actionId)
        {
            bool one = e.Scope == EtchingScope.OneAbility;
            m.Etchings.Add(new EtchingPick { Id = e.Id, ActionId = one ? actionId : "", GrantsTranscend = one && e.Grant != TranscendGrant.None });
            m.AbilityPoints += e.AbilityPoints;
        }

        // ------------------------------------------------------------------ catching a member up (the dev menu)

        /// <summary>
        /// Bring 'm' up to where the campaign says it would be: it gets the ability points it is short of 'earnedPoints' (all a
        /// member would have earned by now, before Etchings' own), spends what it has on random abilities, and gains random
        /// Etchings until it has 'etchings' (a targeted one goes on a random ability it can). Only tops up: run again, it adds nothing.
        /// </summary>
        /// <returns>How many abilities it learned and how many Etchings it gained.</returns>
        public static KeyValuePair<int, int> CatchUp(RosterMember m, UnitDefinition def, ProgressionConfig config, int etchings, int earnedPoints, System.Random rng)
        {
            int learned = 0, etched = 0;
            if (m == null || def == null || config == null || !m.Progressing) return new KeyValuePair<int, int>(0, 0);
            m.AbilityPoints += Mathf.Max(0, earnedPoints - PointsAccountedFor(m, def, config));
            learned += SpendAtRandom(m, def, config, rng);
            while (m.Etchings.Count < etchings)
            {
                var pool = config.Etchings.FindAll(e => e != null && e.Weight > 0f && (e.Scope == EtchingScope.Unit || Etchable(m, def, e).Count > 0));
                if (pool.Count == 0) break;
                float total = 0f;
                foreach (var e in pool) total += e.Weight;
                float roll = (float)rng.NextDouble() * total;
                int k = 0;
                while (k < pool.Count - 1 && roll >= pool[k].Weight) { roll -= pool[k].Weight; k++; }
                var pick = pool[k];
                string on = null;
                if (pick.Scope == EtchingScope.OneAbility) { var options = Etchable(m, def, pick); on = options[rng.Next(options.Count)].Id; }
                GiveEtching(m, pick, on);
                etched++;
                learned += SpendAtRandom(m, def, config, rng);   // an Etching may have paid some points
            }
            return new KeyValuePair<int, int>(learned, etched);
        }

        /// <summary>The points 'm' has earned outside Etchings: what it holds and has spent on abilities, less what its Etchings paid.</summary>
        private static int PointsAccountedFor(RosterMember m, UnitDefinition def, ProgressionConfig config)
        {
            int points = m.AbilityPoints;
            int starters = StarterCount(m, def, config);
            for (int i = starters; i < m.Unlocked.Count; i++)
            {
                var a = def.Actions.Find(x => x != null && x.Id == m.Unlocked[i]);
                if (a != null) points += config.CostOf(a);
            }
            foreach (var p in m.Etchings) { var e = config.Etching(p.Id); if (e != null) points -= e.AbilityPoints; }
            return points;
        }

        /// <summary>How many of 'm''s first unlocks it started with (they cost nothing): the class's default set, or its random handful.</summary>
        private static int StarterCount(RosterMember m, UnitDefinition def, ProgressionConfig config)
        {
            var cs = config.StartFor(def);
            if (cs == null) return m.Unlocked.Count;
            var defaults = cs.DefaultAbilities.FindAll(a => a != null && def.Actions.Contains(a) && a != def.BasicAttack);
            bool isDefault = defaults.Count <= m.Unlocked.Count;
            for (int i = 0; i < defaults.Count && isDefault; i++) isDefault = defaults.Exists(a => a.Id == m.Unlocked[i]);
            return Mathf.Min(m.Unlocked.Count, isDefault ? defaults.Count : cs.RandomCount);
        }

        /// <summary>Unlock random affordable abilities until 'm' can afford no more. Returns how many.</summary>
        private static int SpendAtRandom(RosterMember m, UnitDefinition def, ProgressionConfig config, System.Random rng)
        {
            int n = 0;
            while (true)
            {
                var options = Locked(m, def).FindAll(a => config.CostOf(a) <= m.AbilityPoints);
                if (options.Count == 0) return n;
                Unlock(m, options[rng.Next(options.Count)], config);
                n++;
            }
        }

        /// <summary>A member's Etchings as what its unit fights with: modifiers (a One Ability one's on its ability), traits, notes.</summary>
        /// <param name="def">The member's class: One Ability Etchings with a fallback look at the ability to pick their changes.</param>
        public static UnitGrowth Growth(RosterMember m, ProgressionConfig config, UnitDefinition def = null)
        {
            var g = new UnitGrowth();
            if (config == null) return g;
            foreach (var p in m.Etchings)
            {
                var e = config.Etching(p.Id);
                if (e == null) continue;
                bool one = e.Scope == EtchingScope.OneAbility;
                if (one && string.IsNullOrEmpty(p.ActionId)) continue;
                g.UnitModifiers.AddRange(e.UnitModifiers);
                var on = one && def != null ? AbilityById(def, p.ActionId) : null;
                foreach (var am in one ? e.ModifiersFor(on) : e.ActionModifiers) g.ActionModifiers.Add(new ActionModifier(one ? p.ActionId : am.ActionId, am.Stat, am.Op, am.Value));
                if (one && e.Grant != TranscendGrant.None) g.Transcends[p.ActionId] = e.GrantFor();
                if (e.RemovesBehaviors != null) g.RemovedBehaviors.AddRange(e.RemovesBehaviors);
                if (!string.IsNullOrEmpty(e.Trait)) g.Traits.Add(new UnitTrait { Id = e.Trait, Params = e.Params });
                g.Notes.Add(new EtchingNote
                {
                    Name = e.Name, Description = e.Description, ActionId = one ? p.ActionId : "",
                    ChangesAbilities = !one && (e.ActionModifiers.Exists(am => string.IsNullOrEmpty(am.ActionId)) || e.Trait == EtchingTraits.ClosingWindup),
                });
            }
            return g;
        }

        // ------------------------------------------------------------------ rewards

        /// <summary>Every member gains the mission's ability points (and experience, which nothing spends for now).</summary>
        public static void Award(SquadRoster roster, EncounterRewards rewards)
        {
            if (rewards == null) return;
            foreach (var m in roster.Members)
            {
                m.Experience += rewards.Experience;
                m.AbilityPoints += rewards.AbilityPoints;
            }
        }
    }
}
