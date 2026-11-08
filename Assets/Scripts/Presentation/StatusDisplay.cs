using System.Collections.Generic;
using UnityEngine;
using TacticsGame.Core;

namespace TacticsGame.Presentation
{
    /// <summary>
    /// How each status reads on screen, shared by the HUD (name, duration bar) and CombatVfx (ring at the feet, the word
    /// that pops when it lands). An ability's status can carry its own look (StatusSpec.Look, set in the Inspector or the
    /// Tuning & Text window): whatever parts of it are set replace the built-in look below when a battle loads (Register).
    /// The built-in table is the fallback, and the only look for statuses the game makes in code (Focus, Grappling...).
    /// A status with neither still works everywhere: a name made from its id and a neutral colour.
    /// </summary>
    public static class StatusDisplay
    {
        public struct Look
        {
            public Color Color;
            public string Word;      // pops over the unit when applied, and gives it a ring at the feet; null = neither
            public string Name;      // HUD label; null = made from the id ("battle_trance" -> "battle trance")
            public bool CountsUp;    // the duration bar fills toward the end instead of draining (a countdown to something)
            public bool Important;   // a duration bar under the unit's health and strain while it lasts (BattleHUD)

            public Look(Color color, string word, string name = null, bool countsUp = false)
            {
                Color = color; Word = word; Name = name; CountsUp = countsUp; Important = false;
            }
        }

        private static readonly Dictionary<string, Look> Looks = new Dictionary<string, Look>
        {
            { StatusIds.Marked, new Look(new Color(1f, 0.3f, 0.85f), "MARKED") },
            { StatusIds.Hidden, new Look(new Color(0.6f, 0.65f, 0.75f), "HIDDEN") },
            { StatusIds.Rooted, new Look(new Color(0.65f, 0.45f, 0.25f), "ROOTED") },
            { StatusIds.TakeAim, new Look(new Color(1f, 0.3f, 0.25f), "TAKING AIM", "taking aim") },
            { StatusIds.Retaliate, new Look(new Color(1f, 0.6f, 0.2f), "RETALIATE") },
            { StatusIds.Trance, new Look(new Color(0.75f, 0.35f, 1f), "TRANCE") { Important = true } },
            { StatusIds.Evasive, new Look(new Color(0.4f, 1f, 0.95f), "EVASIVE") },
            { StatusIds.Braced, new Look(new Color(0.4f, 0.65f, 1f), "BRACED") },
            { StatusIds.Blocking, new Look(new Color(0.55f, 0.85f, 1f), "BLOCK", "guard") },
            { StatusIds.Mesmerized, new Look(new Color(1f, 0.75f, 0.95f), "MESMERIZED") },
            { StatusIds.ClearMind, new Look(new Color(0.6f, 1f, 0.7f), "CLEAR MIND", "clear mind") },
            // Focus under way: a bar that fills up to the moment strain starts draining.
            { StatusIds.Focusing, new Look(new Color(0.6f, 1f, 0.7f), null, "focus", true) },
            { StatusIds.Wrath, new Look(new Color(1f, 0.2f, 0.15f), "WRATH") { Important = true } },
            { StatusIds.Fated, new Look(new Color(0.55f, 0.15f, 0.35f), "FATED") { Important = true } },
            { StatusIds.Quickened, new Look(new Color(0.45f, 1f, 1f), "QUICKENED") },
            { StatusIds.Slowed, new Look(new Color(0.45f, 0.5f, 0.85f), "SLOWED") },
            { StatusIds.Grappling, new Look(new Color(0.85f, 0.6f, 0.35f), "GRAPPLE", "grappling") },
            { StatusIds.Grappled, new Look(new Color(0.85f, 0.6f, 0.35f), null, "held") },
            { StatusIds.MentalCurrents, new Look(new Color(0.45f, 0.95f, 0.85f), "CURRENTS", "mental currents") },
            { StatusIds.Flow, new Look(new Color(1f, 0.45f, 0.35f), null, "flow") },
            { StatusIds.RavageReady, new Look(new Color(1f, 0.5f, 0.2f), null, "follow-up") },
            { StatusIds.Skyward, new Look(new Color(0.6f, 0.9f, 1f), "SKYWARD", "skyward") },
            { StatusIds.GuardBroken, new Look(new Color(1f, 0.5f, 0.2f), "GUARD BROKEN", "block down") },
            { StatusIds.Unstoppable, new Look(new Color(1f, 0.35f, 0.35f), "UNSTOPPABLE", "unstoppable") { Important = true } },
            { StatusIds.Sanctity, new Look(new Color(1f, 0.9f, 0.5f), "SANCTITY", "sanctity") },
            { StatusIds.Sheltered, new Look(new Color(1f, 0.9f, 0.5f), "SHELTERED", "sheltered") },
            { StatusIds.TimeSlowed, new Look(new Color(0.55f, 0.6f, 1f), "TIME SLOWED", "time slowed") },
            { StatusIds.Burdened, new Look(new Color(0.85f, 0.55f, 1f), "BURDENED", "burdened") { Important = true } },
            { StatusIds.TrueInversion, new Look(new Color(0.4f, 1f, 0.85f), "TRUE INVERSION", "true inversion") { Important = true } },
            { StatusIds.RegenerationSeal, new Look(new Color(0.45f, 1f, 0.5f), "SEALED", "regeneration seal") },
            { StatusIds.Revenge, new Look(new Color(1f, 0.25f, 0.2f), "REVENGE", "revenge") { Important = true } },
            { StatusIds.Flurry, new Look(new Color(1f, 0.85f, 0.3f), "FLURRY", "flurry") { Important = true } },
            { EtchingTraits.StrideStatus, new Look(new Color(1f, 0.35f, 0.35f), "UNSTOPPABLE", "unstoppable") { Important = true } },
            { EtchingTraits.FracturedStatus, new Look(new Color(1f, 0.55f, 0.35f), "FRACTURED", "unstoppable") { Important = true } },
            { EtchingTraits.DesecratedStatus, new Look(new Color(0.85f, 0.15f, 0.25f), "DESECRATED", "desecrated") { Important = true } },
            { DeliveranceParts.SeeBeforeItself, new Look(new Color(0.7f, 0.85f, 1f), "SEE BEFORE ITSELF", "foreseen") { Important = true } },
            { DeliveranceParts.EquiposeStatus, new Look(new Color(0.55f, 1f, 0.45f), "EQUIPOSE", "equipose") { Important = true } },
            { DeliveranceParts.AttachmentsStatus, new Look(new Color(0.9f, 0.8f, 1f), "LAST ATTACHMENTS", "attached") },
            { "strife", new Look(new Color(0.55f, 1f, 0.45f), "STRIFE", "slowed") },
            { StatusIds.Ghost, new Look(new Color(1f, 0.55f, 0.3f), "GHOST", "ghost") },
            { StatusIds.Perplexity, new Look(new Color(1f, 0.3f, 0.85f), "PERPLEXITY", "perplexity") { Important = true } },
            { StatusIds.Immobilized, new Look(new Color(0.8f, 0.6f, 0.3f), "IMMOBILIZED", "immobilized") { Important = true } },
            { StatusIds.Festival, new Look(new Color(1f, 0.6f, 0.9f), "FESTIVAL OF COLORS", "festival") { Important = true } },
            { StatusIds.Reduction, new Look(new Color(0.55f, 0.75f, 0.3f), "REDUCTION", "reduction") { Important = true } },
            { StatusIds.Melded, new Look(new Color(0.3f, 1f, 0.9f), "MELDED", "melded") { Important = true } },
            { StatusIds.Unkillable, new Look(new Color(1f, 0.9f, 0.5f), "UNKILLABLE", "unkillable") { Important = true } },
            { StatusIds.SaviorsGuard, new Look(new Color(0.95f, 0.85f, 0.55f), "GUARDED", "guarded") },
            { StatusIds.Chained, new Look(new Color(0.75f, 0.75f, 0.8f), "CHAINED", "chained") { Important = true } },
            { EtchingTraits.FlowStateStatus, new Look(new Color(0.5f, 0.9f, 1f), "FLOW STATE", "flow state") { Important = true } },
            { StatusIds.Unmake, new Look(new Color(0.75f, 0.2f, 0.9f), "UNMAKE", "unmake") },
            { StatusIds.Withering, new Look(new Color(0.55f, 0.75f, 0.3f), "WITHERING", "withering") },
            { StatusIds.Madness, new Look(new Color(1f, 0.25f, 0.35f), "MADNESS", "madness") { Important = true } },
            { StatusIds.Splinter, new Look(new Color(0.85f, 0.75f, 1f), "SPLINTER", "splinter") },
            { StatusIds.CripplingRounds, new Look(new Color(1f, 0.55f, 0.2f), "CRIPPLING ROUNDS", "crippling rounds") { Important = true } },
            { StatusIds.Crippled, new Look(new Color(0.8f, 0.45f, 0.25f), "CRIPPLED", "crippled") },
            { StatusIds.MentalWeapon, new Look(new Color(0.55f, 0.95f, 1f), "MENTAL WEAPON", "mental weapon") { Important = true } },
        };

        /// <summary>Statuses that are bookkeeping, or AI state, not something the player needs to see (a last stand is the AI's
        /// secret). Follow-up chain windows ("..._chain", see ChainAttackBehavior) are hidden too.</summary>
        private static readonly HashSet<string> Hidden = new HashSet<string> { DeliverancePassives.SplittingStatus, StatusIds.RapidChain, StatusIds.LastStand, StatusIds.RevengePrimed, EtchingTraits.MomentumStatus, EtchingTraits.MarchStatus };   // Avidous March: a sub-second burst, seen in the walk itself

        // Looks set on the loaded battle's statuses (Register), over the built-in ones.
        private static readonly Dictionary<string, Look> FromData = new Dictionary<string, Look>();
        private static readonly HashSet<string> HiddenByData = new HashSet<string>();

        /// <summary>
        /// Take the looks set on these abilities' statuses (every list: on use, on hit, windup, dome; linked actions too).
        /// Each set part (colour, word, name, counts up, hidden) replaces that part of the built-in look.
        /// </summary>
        public static void Register(IEnumerable<ActionDefinition> actions)
        {
            FromData.Clear();
            HiddenByData.Clear();
            var seen = new HashSet<ActionDefinition>();
            foreach (var a in actions)
                for (var d = a; d != null && seen.Add(d); d = d.LinkedAction)
                    foreach (var list in new[] { d.SelfStatuses, d.TargetStatusesOnHit, d.TargetStatusesDuringWindup, d.SelfStatusesDuringWindup, d.AuraStatuses })
                        if (list != null) foreach (var s in list) if (s != null) Register(s);
        }

        private static void Register(StatusSpec s)
        {
            var l = s.Look;
            if (l == null || !l.IsSet || string.IsNullOrEmpty(s.Id)) return;
            Look look;
            if (!FromData.TryGetValue(s.Id, out look) && !Looks.TryGetValue(s.Id, out look)) look = new Look(Neutral, null);
            if (l.Color.a > 0f) look.Color = l.Color;
            if (!string.IsNullOrEmpty(l.Word)) look.Word = l.Word;
            if (!string.IsNullOrEmpty(l.Name)) look.Name = l.Name;
            if (l.CountsUp) look.CountsUp = true;
            if (l.Important) look.Important = true;
            if (l.Hidden) HiddenByData.Add(s.Id);
            FromData[s.Id] = look;
        }

        private static readonly Color Neutral = new Color(0.85f, 0.85f, 0.85f);

        public static bool IsHidden(string id) { return id == null || Hidden.Contains(id) || HiddenByData.Contains(id) || id.EndsWith("_chain"); }

        /// <summary>The look for a status: from the battle's data, its built-in entry, or a neutral default.</summary>
        public static Look Get(string id)
        {
            Look look;
            if (TryGet(id, out look)) return look;
            return new Look(Neutral, null);
        }

        /// <summary>The look for a status, if it has one from data or the built-in table (CombatVfx only draws rings and words for those).</summary>
        public static bool TryGet(string id, out Look look) { return FromData.TryGetValue(id ?? "", out look) || Looks.TryGetValue(id ?? "", out look); }

        public static string NameOf(string id)
        {
            var look = Get(id);
            return look.Name ?? (id ?? "").Replace('_', ' ');
        }
    }
}
