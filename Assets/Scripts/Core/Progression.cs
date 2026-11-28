using System.Collections.Generic;
using UnityEngine;

namespace TacticsGame.Core
{
    /// <summary>
    /// Growing the squad between missions, paid in Grasp. A member holds as much as its max Grasp (MaxGrasp: what missions
    /// gave it, plus what Amputations gave, less what destroying things cost) and every upgrade it keeps uses some of it
    /// (UsedGrasp). Past the max it is Overburdened: strain builds up in battle. Upgrades come in three kinds:
    ///   Form       stat rituals (ProgressionConfig.Rituals): Graft, Weave, Amputate, Perpetuate (DoRitual).
    ///   Cognition  Etchings, permanent passives (EtchingDefinition): a few offered; taking one draws a fresh offer (TakeEtching).
    ///   Knowledge  class abilities: a handful offered each phase, each pricier for every one already learned (Learn).
    /// Any learned ability or Etching can be destroyed (Destroy...): its Grasp is freed but max Grasp drops.
    /// All of it is kept on the roster as ids; Growth turns it into what a unit fights with (Unit.FromDefinition).
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

        // ------------------------------------------------------------------ what a member has

        /// <summary>The class abilities a member hasn't unlocked yet.</summary>
        public static List<ActionDefinition> Locked(RosterMember m, UnitDefinition def)
        {
            return def.Actions.FindAll(a => a != null && a != def.BasicAttack && !m.Unlocked.Contains(a.Id));
        }

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
            if (e.Scope == EtchingScope.Unit) return new List<ActionDefinition>();
            var list = Abilities(m, def).FindAll(e.CanEtch);
            // A granted Transcend only for an ability no other Etching has given one.
            if (e.Grant != TranscendGrant.None) list.RemoveAll(a => m.Etchings.Exists(p => p.ActionId == a.Id && p.GrantsTranscend));
            // A granted follow-up only for an ability no other Etching has given one (it has one now).
            if (e.GrantsFollowup != FollowupGrant.None) list.RemoveAll(a => m.Etchings.Exists(p => (p.ActionId == a.Id || p.SecondActionId == a.Id) && p.GrantsFollowup));
            return list;
        }

        /// <summary>Has 'm' what 'e' needs to go on: nothing for a unit one, an ability for a One Ability one, two for a pair.</summary>
        public static bool CanBeEtched(RosterMember m, UnitDefinition def, EtchingDefinition e)
        {
            if (def != null && def.ExcludedEtchings != null && def.ExcludedEtchings.Contains(e.Id)) return false;   // the Forsaken: none that Unravel or spend past max
            if (e.Scope == EtchingScope.Unit) return true;
            return Etchable(m, def, e).Count >= (e.Scope == EtchingScope.TwoAbilities ? 2 : 1);
        }

        private static ActionDefinition AbilityById(UnitDefinition def, string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var a in new[] { def.BasicAttack, def.Universal, def.Dash }) if (a != null && a.Id == id) return a;
            return def.Actions.Find(a => a != null && a.Id == id);
        }


        // ------------------------------------------------------------------ Grasp

        /// <summary>The Grasp 'm' can hold: the campaign's start, what missions gave, what Amputations gave, less what destroying took.</summary>
        public static int MaxGrasp(RosterMember m, ProgressionConfig config)
        {
            int bonus = 0;
            foreach (var r in m.Rituals) bonus += r.GraspBonus;
            return (config != null ? config.StartingGrasp : 0) + m.GraspEarned + bonus - m.GraspLost;
        }

        /// <summary>The Grasp 'm''s upgrades use: its learned abilities, its Etchings, its rituals.</summary>
        public static int UsedGrasp(RosterMember m)
        {
            int used = 0;
            foreach (var a in m.AbilityGrasp) used += a.Grasp;
            foreach (var e in m.Etchings) used += e.Grasp;
            foreach (var r in m.Rituals) used += r.Grasp;
            return used;
        }

        /// <summary>Grasp 'm' uses past its max (0 if it isn't Overburdened).</summary>
        public static int OverGrasp(RosterMember m, ProgressionConfig config) { return Mathf.Max(0, UsedGrasp(m) - MaxGrasp(m, config)); }

        /// <summary>Overburdened: the strain per second it gains in battle (a share of the Grasp over).</summary>
        public static float OverburdenStrain(RosterMember m, ProgressionConfig config)
        {
            return config != null ? OverGrasp(m, config) * config.OverburdenStrainShare : 0f;
        }

        /// <summary>A mission was won: every member's max Grasp grows by 'grasp' (its EncounterRewards.Grasp). Returns it.</summary>
        public static int AwardGrasp(SquadRoster roster, int grasp)
        {
            foreach (var m in roster.Members) m.GraspEarned += grasp;
            return grasp;
        }

        // ------------------------------------------------------------------ the upgrade phase

        /// <summary>
        /// Start an upgrade phase: every progressing member is offered EtchingsOffered different Etchings (drawn by weight from
        /// the pool, a One Ability one only if it has an ability to go on) and AbilitiesOffered of its locked abilities. The
        /// draws are fixed by the roster's seed and the mission, so reloading shows the same offers.
        /// </summary>
        public static void BeginUpgrades(SquadRoster roster, ProgressionConfig config, ContentCatalog catalog)
        {
            if (config == null) return;
            for (int i = 0; i < roster.Members.Count; i++)
            {
                var m = roster.Members[i];
                var def = catalog.Unit(m.Class);
                m.Offer.Clear(); m.AbilityOffer.Clear(); m.EtchingDraws = 0;
                if (!m.Progressing || def == null) continue;
                DrawAbilityOffer(m, def, config, new System.Random(Seed(roster, i) + 17));
                DrawEtchingOffer(m, def, config, new System.Random(Seed(roster, i)), config.EtchingsOffered, null);
            }
        }

        /// <summary>The upgrade phase is over: the offers go.</summary>
        public static void EndUpgrades(SquadRoster roster)
        {
            foreach (var m in roster.Members) { m.Offer.Clear(); m.AbilityOffer.Clear(); m.EtchingDraws = 0; }
        }

        private static int Seed(SquadRoster roster, int memberIndex) { return roster.Seed * 7919 + roster.MissionIndex * 104729 + memberIndex * 31; }

        /// <summary>A fresh Etching offer for 'm' (different ones from the last, where the pool allows): after a take, or a reroll.</summary>
        public static void RedrawEtchings(SquadRoster roster, RosterMember m, ProgressionConfig config, UnitDefinition def)
        {
            if (config == null || def == null) return;
            var old = new List<string>(m.Offer);
            m.Offer.Clear();
            m.EtchingDraws++;
            DrawEtchingOffer(m, def, config, new System.Random(Seed(roster, roster.Members.IndexOf(m)) + 7001 * m.EtchingDraws), config.EtchingsOffered, old);
        }

        /// <summary>Fill 'm''s Etching offer up to 'count', drawn by weight from the ones it can take, leaving out 'not' (the
        /// ones a reroll is replacing) while there are enough others.</summary>
        private static void DrawEtchingOffer(RosterMember m, UnitDefinition def, ProgressionConfig config, System.Random rng, int count, List<string> not)
        {
            var pool = config.Etchings.FindAll(e => e != null && e.Weight > 0f && !m.Offer.Contains(e.Id) && CanBeEtched(m, def, e));
            if (not != null && pool.Count - pool.FindAll(e => not.Contains(e.Id)).Count >= count - m.Offer.Count) pool.RemoveAll(e => not.Contains(e.Id));
            while (m.Offer.Count < count && pool.Count > 0)
            {
                float total = 0f;
                foreach (var e in pool) total += e.Weight;
                float roll = (float)rng.NextDouble() * total;
                int k = 0;
                while (k < pool.Count - 1 && roll >= pool[k].Weight) { roll -= pool[k].Weight; k++; }
                m.Offer.Add(pool[k].Id);
                pool.RemoveAt(k);
            }
        }

        /// <summary>Which locked class abilities a member may learn this phase: AbilitiesOffered of them at random (all if 0).</summary>
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


        // ------------------------------------------------------------------ Cognition (Etchings)

        /// <param name="secondId">Two Abilities: the other ability (not 'actionId').</param>
        public static bool CanTake(RosterMember m, UnitDefinition def, EtchingDefinition e, string actionId, out string why, string secondId = null)
        {
            why = null;
            if (e == null || !m.Offer.Contains(e.Id)) { why = "not offered"; return false; }
            if (e.Scope != EtchingScope.Unit && !Etchable(m, def, e).Exists(a => a.Id == actionId)) { why = "pick an ability it can go on"; return false; }
            if (e.Scope == EtchingScope.TwoAbilities && (secondId == actionId || !Etchable(m, def, e).Exists(a => a.Id == secondId))) { why = "pick a second, different ability it can go on"; return false; }
            return true;
        }

        /// <summary>What taking another Etching costs 'm' now: EtchingGrasp, plus EtchingGraspStep for each it has.</summary>
        public static int EtchingPrice(RosterMember m, ProgressionConfig config)
        {
            return config == null ? 0 : config.EtchingGrasp + config.EtchingGraspStep * m.Etchings.Count;
        }

        /// <summary>Take an offered Etching (One Ability: onto 'actionId'; Two Abilities: onto it and 'secondId'). It uses
        /// today's EtchingPrice, kept for as long as it is held (going past max Grasp is allowed: Overburdened), and a fresh
        /// set is offered.</summary>
        public static bool TakeEtching(SquadRoster roster, RosterMember m, UnitDefinition def, EtchingDefinition e, ProgressionConfig config, string actionId = null, string secondId = null)
        {
            string why;
            if (config == null || !CanTake(m, def, e, actionId, out why, secondId)) return false;
            GiveEtching(m, e, actionId, secondId, EtchingPrice(m, config));
            RedrawEtchings(roster, m, config, def);
            return true;
        }

        /// <summary>'m' gains Etching 'e' (One Ability: on 'actionId'), using 'grasp'.</summary>
        private static void GiveEtching(RosterMember m, EtchingDefinition e, string actionId, string secondId, int grasp)
        {
            bool one = e.Scope != EtchingScope.Unit;
            m.Etchings.Add(new EtchingPick
            {
                Id = e.Id, ActionId = one ? actionId : "", SecondActionId = e.Scope == EtchingScope.TwoAbilities ? secondId : "",
                GrantsTranscend = one && e.Grant != TranscendGrant.None, GrantsFollowup = one && e.GrantsFollowup != FollowupGrant.None, Grasp = grasp,
            });
        }

        // ------------------------------------------------------------------ Knowledge (abilities)

        /// <summary>What learning another ability costs 'm' now: AbilityGrasp, plus AbilityGraspStep for each it has learned.</summary>
        public static int AbilityPrice(RosterMember m, ProgressionConfig config)
        {
            if (config == null) return 0;
            int learned = config.StartersRaisePrice ? m.Unlocked.Count : m.AbilityGrasp.Count;
            return config.AbilityGrasp + config.AbilityGraspStep * learned;
        }

        /// <summary>'m' learns 'a' at today's price, which it keeps using (it may go past its max Grasp: Overburdened).</summary>
        public static bool Learn(RosterMember m, ActionDefinition a, ProgressionConfig config)
        {
            if (a == null || m.Unlocked.Contains(a.Id)) return false;
            int price = AbilityPrice(m, config);
            m.Unlocked.Add(a.Id);
            m.AbilityGrasp.Add(new GraspUse { Id = a.Id, Grasp = price });
            m.AbilityOffer.Remove(a.Id);
            return true;
        }

        // ------------------------------------------------------------------ Form (stat rituals)

        /// <summary>A stat's base on 'def' (what a ritual's share is of), at least its FormStat.BaseFloor.</summary>
        public static float StatBase(UnitDefinition def, StatId stat, ProgressionConfig config)
        {
            float v;
            switch (stat)
            {
                case StatId.MaxHealth: v = def.MaxHealth; break;
                case StatId.MaxStrain: v = def.MaxStrain; break;
                case StatId.StrainRecovery: v = def.StrainRecoveryPerSecond; break;
                case StatId.MoveSpeed: v = def.MoveSpeed; break;
                case StatId.Dodge: v = def.BaseDodge; break;
                case StatId.ActionSpeed: v = def.ActionSpeed; break;
                case StatId.DashDistance: v = def.DashDistance; break;
                case StatId.Block: v = def.BaseBlock; break;
                default: v = 0f; break;
            }
            var f = config != null ? config.Form(stat) : null;
            return f != null ? Mathf.Max(v, f.BaseFloor) : v;
        }

        /// <summary>Has 'm' Perpetuated 'stat' (kept at its base for good)?</summary>
        public static bool IsLocked(RosterMember m, StatId stat) { return m.Rituals.Exists(r => r.Locks && r.Stat == stat); }

        /// <summary>How far 'm''s rituals have moved 'stat', as a share of its base (never below -0.9: a stat keeps a tenth).</summary>
        public static float RitualShare(RosterMember m, StatId stat)
        {
            if (IsLocked(m, stat)) return 0f;
            float share = 0f;
            foreach (var r in m.Rituals) if (r.Stat == stat && !r.Locks) share += r.Share;
            return Mathf.Max(-0.9f, share);
        }

        public static bool CanDoRitual(RosterMember m, RitualDefinition ritual, StatId stat, ProgressionConfig config, out string why)
        {
            why = null;
            if (ritual == null || config == null || config.Form(stat) == null) { why = "not a Form stat"; return false; }
            if (IsLocked(m, stat)) { why = "Perpetuated: it can't change"; return false; }
            return true;
        }

        /// <summary>
        /// The Grasp 'm' would use, and hold, after 'ritual' on 'stat': its cost and Amputation bonus, and for Perpetuate the
        /// rituals on that stat undone (their Grasp freed, their bonuses gone).
        /// </summary>
        public static void GraspAfterRitual(RosterMember m, RitualDefinition ritual, StatId stat, ProgressionConfig config, out int used, out int max)
        {
            used = UsedGrasp(m) + ritual.GraspCost;
            max = MaxGrasp(m, config) + ritual.GraspBonus;
            if (ritual.Kind != RitualKind.Perpetuate) return;
            foreach (var r in m.Rituals) if (r.Stat == stat) { used -= r.Grasp; max -= r.GraspBonus; }
        }

        /// <summary>Would 'ritual' on 'stat' leave 'm' using more Grasp than it holds?</summary>
        public static bool RitualOverburdens(RosterMember m, RitualDefinition ritual, StatId stat, ProgressionConfig config)
        {
            int used, max;
            GraspAfterRitual(m, ritual, stat, config, out used, out max);
            return used > max;
        }

        /// <summary>
        /// Do 'ritualId' on 'm''s 'stat': Raise or Lower it by a share of its base (rolled now with 'rng'), or Perpetuate it
        /// (the rituals already on it are undone, freeing their Grasp and taking back what Amputations gave, and it is locked).
        /// Returns what was done (null if it can't be).
        /// </summary>
        public static RitualPick DoRitual(RosterMember m, string ritualId, StatId stat, ProgressionConfig config, System.Random rng)
        {
            var ritual = config != null ? config.Ritual(ritualId) : null;
            string why;
            if (!CanDoRitual(m, ritual, stat, config, out why)) return null;
            var pick = new RitualPick { Ritual = ritual.Id, Stat = stat, Grasp = ritual.GraspCost, GraspBonus = ritual.GraspBonus };
            if (ritual.Kind == RitualKind.Perpetuate)
            {
                m.Rituals.RemoveAll(r => r.Stat == stat);
                pick.Locks = true;
            }
            else
            {
                float lo = Mathf.Min(ritual.MinShare, ritual.MaxShare), hi = Mathf.Max(ritual.MinShare, ritual.MaxShare);
                float share = Mathf.Round((lo + (float)rng.NextDouble() * (hi - lo)) * 100f) / 100f;
                pick.Share = ritual.Kind == RitualKind.Lower ? -share : share;
            }
            m.Rituals.Add(pick);
            return pick;
        }

        /// <summary>The rituals' stat changes on the unit (and the Perpetuated stats, locked against every other change).</summary>
        private static void AddForm(UnitGrowth g, RosterMember m, ProgressionConfig config, UnitDefinition def)
        {
            if (m.Rituals.Count == 0) return;
            var done = new List<StatId>();
            foreach (var r in m.Rituals)
            {
                if (done.Contains(r.Stat)) continue;
                done.Add(r.Stat);
                if (IsLocked(m, r.Stat)) { g.LockedStats.Add(r.Stat); continue; }
                if (def == null) continue;
                float share = RitualShare(m, r.Stat);
                if (Mathf.Abs(share) > 1e-4f) g.UnitModifiers.Add(Modifier.Add(r.Stat, share * StatBase(def, r.Stat, config)));
            }
        }

        // ------------------------------------------------------------------ destroying (hold to destroy)

        /// <summary>
        /// Destroy a learned ability: 'm' forgets it, the Grasp it used is freed (and the Etchings on it go too, theirs freed),
        /// and max Grasp drops by DestroyGraspLoss. The basic attack, Block and Dash aren't learned and can't be.
        /// </summary>
        public static bool DestroyAbility(RosterMember m, string actionId, ProgressionConfig config)
        {
            if (string.IsNullOrEmpty(actionId) || !m.Unlocked.Remove(actionId)) return false;
            m.Loadout.Remove(actionId);
            m.AbilityGrasp.RemoveAll(a => a.Id == actionId);
            m.Etchings.RemoveAll(p => p.ActionId == actionId || p.SecondActionId == actionId);
            m.GraspLost += config != null ? config.DestroyGraspLoss : 0;
            return true;
        }

        /// <summary>Destroy one of 'm''s Etchings (by its place in m.Etchings): its Grasp freed, max Grasp down DestroyGraspLoss.</summary>
        public static bool DestroyEtching(RosterMember m, int index, ProgressionConfig config)
        {
            if (index < 0 || index >= m.Etchings.Count) return false;
            m.Etchings.RemoveAt(index);
            m.GraspLost += config != null ? config.DestroyGraspLoss : 0;
            return true;
        }

        // ------------------------------------------------------------------ catching a member up (the dev menu)

        /// <summary>
        /// Bring 'm' up to where the campaign says it would be: it gets the Grasp it is short of 'earned' (all a member would
        /// have been given by now), then spends what it has free on random abilities and Etchings until it can't afford more.
        /// Only tops up: run again, it adds nothing.
        /// </summary>
        /// <returns>How many abilities it learned and how many Etchings it gained.</returns>
        public static KeyValuePair<int, int> CatchUp(RosterMember m, UnitDefinition def, ProgressionConfig config, int earned, System.Random rng)
        {
            int learned = 0, etched = 0;
            if (m == null || def == null || config == null || !m.Progressing) return new KeyValuePair<int, int>(0, 0);
            m.GraspEarned = Mathf.Max(m.GraspEarned, earned);
            while (true)
            {
                int free = MaxGrasp(m, config) - UsedGrasp(m);
                var abilities = AbilityPrice(m, config) <= free ? Locked(m, def) : new List<ActionDefinition>();
                var pool = EtchingPrice(m, config) <= free ? config.Etchings.FindAll(e => e != null && e.Weight > 0f && CanBeEtched(m, def, e)) : new List<EtchingDefinition>();
                if (abilities.Count == 0 && pool.Count == 0) break;
                if (abilities.Count > 0 && (pool.Count == 0 || rng.Next(2) == 0))
                {
                    Learn(m, abilities[rng.Next(abilities.Count)], config);
                    learned++;
                    continue;
                }
                float total = 0f;
                foreach (var e in pool) total += e.Weight;
                float roll = (float)rng.NextDouble() * total;
                int k = 0;
                while (k < pool.Count - 1 && roll >= pool[k].Weight) { roll -= pool[k].Weight; k++; }
                var pick = pool[k];
                string on = null, second = null;
                if (pick.Scope != EtchingScope.Unit)
                {
                    var options = Etchable(m, def, pick);
                    on = options[rng.Next(options.Count)].Id;
                    if (pick.Scope == EtchingScope.TwoAbilities) { options.RemoveAll(a => a.Id == on); second = options[rng.Next(options.Count)].Id; }
                }
                GiveEtching(m, pick, on, second, EtchingPrice(m, config));
                etched++;
            }
            return new KeyValuePair<int, int>(learned, etched);
        }

        /// <summary>A member's upgrades as what its unit fights with: its rituals' stat changes (and Perpetuated stats), its Etchings'
        /// modifiers (a One Ability one's on its ability), traits and notes, and Overburdened if it holds too much.</summary>
        /// <param name="def">The member's class: One Ability Etchings with a fallback look at the ability to pick their changes.</param>
        public static UnitGrowth Growth(RosterMember m, ProgressionConfig config, UnitDefinition def = null)
        {
            var g = new UnitGrowth();
            if (config == null) return g;
            AddForm(g, m, config, def);
            float over = OverburdenStrain(m, config);
            if (over > 0f) g.Traits.Add(new UnitTrait { Id = EtchingTraits.Overburdened, Params = new List<BehaviorParam> { new BehaviorParam("per_second", over) } });
            foreach (var p in m.Etchings)
            {
                var e = config.Etching(p.Id);
                if (e == null) continue;
                bool one = e.Scope != EtchingScope.Unit;
                if (one && string.IsNullOrEmpty(p.ActionId)) continue;
                bool two = e.Scope == EtchingScope.TwoAbilities;
                if (two && string.IsNullOrEmpty(p.SecondActionId)) continue;
                g.UnitModifiers.AddRange(e.UnitModifiers);
                if (!string.IsNullOrEmpty(e.Trait)) g.Traits.Add(new UnitTrait { Id = e.Trait, Params = e.Params });
                if (e.RemovesBehaviors != null) g.RemovedBehaviors.AddRange(e.RemovesBehaviors);
                if (!one)
                {
                    foreach (var am in e.ActionModifiers) g.ActionModifiers.Add(am);
                    g.Notes.Add(new EtchingNote
                    {
                        Name = e.Name, Description = e.Description, ActionId = "",
                        ChangesAbilities = e.ActionModifiers.Exists(am => string.IsNullOrEmpty(am.ActionId)) || e.Trait == EtchingTraits.ClosingWindup,
                    });
                    continue;
                }
                // On one ability, or on each of a pair (a Linked follow-up points each one at the other).
                var ids = two ? new[] { p.ActionId, p.SecondActionId } : new[] { p.ActionId };
                for (int i = 0; i < ids.Length; i++)
                {
                    string id = ids[i], partner = two ? ids[1 - i] : null;
                    var on = def != null ? AbilityById(def, id) : null;
                    foreach (var am in e.ModifiersFor(on)) g.ActionModifiers.Add(new ActionModifier(id, am.Stat, am.Op, am.Value));
                    if (e.Grant != TranscendGrant.None) g.Transcends[id] = e.GrantFor();
                    if (e.GrantsFollowup != FollowupGrant.None) g.Followups[id] = e.FollowupFor(partner);
                    g.Notes.Add(new EtchingNote { Name = e.Name, Description = e.Description, ActionId = id });
                }
            }
            return g;
        }
    }
}
