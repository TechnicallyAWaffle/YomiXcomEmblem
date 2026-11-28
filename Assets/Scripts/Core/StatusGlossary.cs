using System.Collections.Generic;

namespace TacticsGame.Core
{
    /// <summary>
    /// What each status does, in a sentence, for the STATUS EFFECTS section of an ability's popup. Every ability that puts a
    /// status on someone shares its description; editing it is done in Tools > Combat Test > Tuning &amp; Text > Statuses, which
    /// keeps the edited wording in the GameText asset under "status.desc.&lt;id&gt;" (an empty edit hides it). One ability can
    /// word it its own way instead: its StatusSpec's Look.Description.
    /// The built-in wording below is the default; a status with none (and no edit) has no description.
    /// </summary>
    public static class StatusGlossary
    {
        /// <summary>The GameText key a status's shared description is edited under.</summary>
        public static string Key(string id) { return "status.desc." + (id ?? ""); }

        /// <summary>The built-in description of 'id', or null.</summary>
        public static string Default(string id)
        {
            string text;
            return id != null && Defaults.TryGetValue(id, out text) ? text : null;
        }

        /// <summary>
        /// What 'id' does, as an ability's popup shows it: 'spec's own wording (Look.Description), else the shared one (edited,
        /// else built in). Null or empty: nothing to show.
        /// </summary>
        public static string Describe(string id, StatusSpec spec = null)
        {
            if (spec != null && spec.Look != null && !string.IsNullOrEmpty(spec.Look.Description)) return spec.Look.Description.Trim();
            return GameText.Get(Key(id), Default(id));
        }

        /// <summary>
        /// Every status 'def' puts on anyone (its StatusSpec lists, then what its behavior applies in code: StatusesApplied),
        /// once each, with what it does: (id, description). Statuses with nothing to say, or hidden by their look, are left out.
        /// </summary>
        public static List<KeyValuePair<string, string>> For(ActionDefinition def)
        {
            var list = new List<KeyValuePair<string, string>>();
            if (def == null) return list;
            var seen = new HashSet<string>();
            System.Action<string, StatusSpec> add = (id, spec) =>
            {
                if (string.IsNullOrEmpty(id) || !seen.Add(id) || (spec != null && spec.Look != null && spec.Look.Hidden)) return;
                string text = Describe(id, spec);
                if (!string.IsNullOrEmpty(text)) list.Add(new KeyValuePair<string, string>(id, text));
            };
            foreach (var specs in SpecLists(def)) if (specs != null) foreach (var s in specs) if (s != null) add(s.Id, s);
            foreach (var id in ActionBehaviors.Get(def.BehaviorId).StatusesApplied(def)) add(id, null);
            return list;
        }

        /// <summary>The ability's lists of statuses it puts on units.</summary>
        public static IEnumerable<List<StatusSpec>> SpecLists(ActionDefinition def)
        {
            yield return def.SelfStatuses;
            yield return def.SelfStatusesDuringWindup;
            yield return def.TargetStatusesDuringWindup;
            yield return def.TargetStatusesOnHit;
            yield return def.SelfStatusesOnHit;
            yield return def.AuraStatuses;
            yield return def.RandomStatusesOnHit;
        }

        /// <summary>The ids with a built-in description (the Statuses tab lists these beside the ones assets use).</summary>
        public static IEnumerable<string> Known { get { return Defaults.Keys; } }

        private static readonly Dictionary<string, string> Defaults = new Dictionary<string, string>
        {
            { StatusIds.Marked, "Attacks against it hit more easily, and it dodges less." },
            { StatusIds.Hidden, "Enemies can't target it. Taking damage reveals it." },
            { StatusIds.Rooted, "Can't move or dash." },
            { StatusIds.Immobilized, "Can't move or dash." },
            { StatusIds.Crippled, "Walks slower and dodges a shorter distance. Can't use dashes." },
            { StatusIds.CripplingRounds, "Deals less damage, but its hits CRIPPLE what they hit." },
            { StatusIds.Fated, "Its actions are heavily slowed." },
            { StatusIds.Slowed, "Its actions are slower." },
            { StatusIds.Quickened, "Its actions are faster." },
            { StatusIds.Hastened, "A burst of action speed." },
            { StatusIds.TimeSlowed, "Its actions are slower while it stays inside the Chronosphere." },
            { StatusIds.Decayed, "Slowed while it stays inside the decay." },
            { StatusIds.Dereliction, "Walks slower and wears down while it stays inside the Dereliction." },
            { StatusIds.Quelled, "Walks much slower." },
            { StatusIds.Withering, "Slowed while the decay is channeled into it." },
            { StatusIds.Reduction, "Slowed, and strain builds up while it lasts." },
            { StatusIds.GuardBroken, "Its Block is down and can't be used until this runs out." },
            { "stutter_block_disable", "Its Block is down and can't be used until this runs out." },
            { StatusIds.Unstoppable, "Stuns don't land, nothing interrupts it and nothing moves it. Unravel still does." },
            { StatusIds.Braced, "More Block, and UNSTOPPABLE." },
            { StatusIds.Blocking, "Guarding: takes less damage and can't be stunned." },
            { StatusIds.Parrying, "Takes no damage or stun from hits for a moment." },
            { StatusIds.Rebuttal, "The next Unsigned Rebuttal has no windup and ignores its cooldown." },
            { StatusIds.Nemesis, "The Duelist's NEMESIS: takes more damage from the Duelist, and its attacks on the Duelist are parried." },
            { StatusIds.Shielded, "A shield soaks up damage before health. It ends when used up." },
            { StatusIds.Sanctity, "A dome of protection: hits on allies sheltering inside go to its owner." },
            { StatusIds.Sheltered, "Inside an ally's Sanctity: hits on it go to the dome's owner instead." },
            { StatusIds.SaviorsGuard, "More Block while the Tank comes to its aid." },
            { StatusIds.Counterpoise, "Double Block while it stays inside the zone." },
            { StatusIds.Trance, "Final Cut can be used." },
            { StatusIds.Wrath, "Abilities cost no strain and hit harder, but it can't Block." },
            { StatusIds.Evasive, "More dodge." },
            { StatusIds.Mesmerized, "Much more dodge; the first attack on it is countered." },
            { StatusIds.Retaliate, "A free shot at the attacker every time it dodges." },
            { StatusIds.TakeAim, "Snipes the next enemy that acts in sight." },
            { StatusIds.Flurry, "Much faster actions and more dodge, but only the basic attack can be used." },
            { StatusIds.Skyward, "Hovering in the air and hitting harder." },
            { StatusIds.MentalCurrents, "More dodge and faster walking." },
            { StatusIds.Monostep, "A burst of walking speed." },
            { StatusIds.Festival, "Faster actions and walking, and Joyous Dance can be used." },
            { StatusIds.Madness, "Faster and stronger, but out of control: it only basic-attacks the nearest enemy." },
            { StatusIds.RegenerationSeal, "Heals over time. A hit breaks it and stuns the attacker." },
            { "unmaking_harm", "Heals when it runs out, more the more health is missing then." },
            { StatusIds.TrueInversion, "Strain it would gain, it loses instead." },
            { StatusIds.Ruptured, "A heavy blow lands when it runs out." },
            { StatusIds.Unkillable, "Health can't fall below 1." },
            { StatusIds.MentalWeapon, "Its next single-target damage fires the stored bullet at that target." },
            { StatusIds.Perplexity, "Deliverance can step behind it at once." },
            { StatusIds.BeyondThresholds, "Faster actions; taking damage or dashing sends it back to where it started." },
            { StatusIds.Frenzied, "Much faster actions while striking its Madness Spills Forth target." },
            { "paranoia", "Gaining strain while the Forsaken channels Paranoia on it." },
            { "paranoia_relief", "Shedding strain while channelling Paranoia." },
            { "exort", "Shedding strain while its Exort aura strains nearby enemies." },
            { "withdrawal", "Shedding strain while channelling Withdrawal, its weakpoints open." },
            { "simplicity", "Faster actions and shedding strain, but only its basic attack can be used." },
            { StatusIds.ConjectureReady, "Conjecture can be used." },
            { StatusIds.RavageReady, "Its follow-up strike can be used." },
            { "pyre_ready", "Light the Pyre can be used." },
            { "amahaltel_poise", "Much more dodge while it winds up." },
            { "carapace", "A shield and faster walking while barbs stream out." },
            { "strife", "Slowed by the wave of strife." },
            { StatusIds.ClearAndSteady, "The next ability used while it lasts has no windup." },
            { StatusIds.Intention, "The next two abilities used while it lasts have no recovery." },
            { StatusIds.Countersign, "The next time it damages one of the Duelist's allies, the Duelist blinks in front of that ally and takes the hit instead (against the Duelist's NEMESIS: parried)." },
            { StatusIds.WrathfulReflection, "A shield. While it holds, half of the damage and stun taken from each attack goes back at the attacker a moment later." },
            { StatusIds.Diverged, "The Forsaken's Diverged form: much faster walking and more Block. Strain drains every second; at 0 it returns to Composed form (REGRESSION)." },
            { StatusIds.Hysteria, "Builds every second in Diverged form, and with abilities that give it (Devour, Rampage, Wretched Visions...). Each stack costs 1 health a second while Diverged, which Block can't stop. At 20 it builds no further, Strain drains twice as fast, and as soon as the Forsaken can act it casts SHATTERED MIND: INSANITY until Composed again." },
            { StatusIds.Insanity, "Faster and stronger, but on no side: both teams can damage it, and it attacks the nearest unit, ally or enemy." },
            { StatusIds.Measured, "Walks much slower." },
            { StatusIds.StrangeAxis, "Damage taken costs no health: 75% of it is added as strain instead (the hit's own strain still lands)." },
        };
    }
}
