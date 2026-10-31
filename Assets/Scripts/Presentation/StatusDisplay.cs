using System.Collections.Generic;
using UnityEngine;
using TacticsGame.Core;

namespace TacticsGame.Presentation
{
    /// <summary>
    /// How each status reads on screen, shared by the HUD (name, duration bar) and CombatVfx (ring at the feet, the word
    /// that pops when it lands). A status with no entry still works everywhere: it gets a name made from its id and a
    /// neutral colour, so a new ability's status shows up (with a duration bar, if timed and self-applied) without
    /// touching this file; add an entry only to give it its own colour, word or name.
    /// </summary>
    public static class StatusDisplay
    {
        public struct Look
        {
            public Color Color;
            public string Word;      // pops over the unit when applied, and gives it a ring at the feet; null = neither
            public string Name;      // HUD label; null = made from the id ("battle_trance" -> "battle trance")
            public bool CountsUp;    // the duration bar fills toward the end instead of draining (a countdown to something)

            public Look(Color color, string word, string name = null, bool countsUp = false)
            {
                Color = color; Word = word; Name = name; CountsUp = countsUp;
            }
        }

        private static readonly Dictionary<string, Look> Looks = new Dictionary<string, Look>
        {
            { StatusIds.Marked, new Look(new Color(1f, 0.3f, 0.85f), "MARKED") },
            { StatusIds.Hidden, new Look(new Color(0.6f, 0.65f, 0.75f), "HIDDEN") },
            { StatusIds.Rooted, new Look(new Color(0.65f, 0.45f, 0.25f), "ROOTED") },
            { StatusIds.TakeAim, new Look(new Color(1f, 0.3f, 0.25f), "TAKING AIM", "taking aim") },
            { StatusIds.Retaliate, new Look(new Color(1f, 0.6f, 0.2f), "RETALIATE") },
            { StatusIds.Trance, new Look(new Color(0.75f, 0.35f, 1f), "TRANCE") },
            { StatusIds.Evasive, new Look(new Color(0.4f, 1f, 0.95f), "EVASIVE") },
            { StatusIds.Braced, new Look(new Color(0.4f, 0.65f, 1f), "BRACED") },
            { StatusIds.Blocking, new Look(new Color(0.55f, 0.85f, 1f), "BLOCK", "guard") },
            { StatusIds.Mesmerized, new Look(new Color(1f, 0.75f, 0.95f), "MESMERIZED") },
            { StatusIds.Focused, new Look(new Color(0.6f, 1f, 0.7f), "FOCUSED") },
            // Focus under way: a bar that fills up to the moment strain starts draining.
            { StatusIds.Focusing, new Look(new Color(0.6f, 1f, 0.7f), null, "focus", true) },
            { StatusIds.Wrath, new Look(new Color(1f, 0.2f, 0.15f), "WRATH") },
            { StatusIds.Fated, new Look(new Color(0.55f, 0.15f, 0.35f), "FATED") },
            { StatusIds.Quickened, new Look(new Color(0.45f, 1f, 1f), "QUICKENED") },
            { StatusIds.Slowed, new Look(new Color(0.45f, 0.5f, 0.85f), "SLOWED") },
            { StatusIds.Grappling, new Look(new Color(0.85f, 0.6f, 0.35f), "GRAPPLE", "grappling") },
            { StatusIds.Grappled, new Look(new Color(0.85f, 0.6f, 0.35f), null, "held") },
        };

        /// <summary>Statuses that are bookkeeping, not something the player needs to see.</summary>
        private static readonly HashSet<string> Hidden = new HashSet<string> { StatusIds.RapidChain };

        public static bool IsHidden(string id) { return id == null || Hidden.Contains(id); }

        /// <summary>The look for a status: its entry, or a neutral default.</summary>
        public static Look Get(string id)
        {
            Look look;
            if (id != null && Looks.TryGetValue(id, out look)) return look;
            return new Look(new Color(0.85f, 0.85f, 0.85f), null);
        }

        /// <summary>The entry for a status, if it has one (CombatVfx only draws rings and words for those).</summary>
        public static bool TryGet(string id, out Look look) { return Looks.TryGetValue(id ?? "", out look); }

        public static string NameOf(string id)
        {
            var look = Get(id);
            return look.Name ?? (id ?? "").Replace('_', ' ');
        }
    }
}
