using System.Collections.Generic;
using TacticsGame.Core;

namespace TacticsGame.Presentation
{
    /// <summary>
    /// How each passive (UnitDefinition.Passives) reads on screen: its name and what it does, for the unit details panel
    /// (BattleHUD shows an enemy's passives when it is hovered). A passive missing here still shows, by a name made from
    /// its id and no description.
    /// </summary>
    public static class PassiveDisplay
    {
        private static readonly Dictionary<string, KeyValuePair<string, string>> Table = new Dictionary<string, KeyValuePair<string, string>>
        {
            { Passives.StrainReliefOnDodge, P("Strain Relief", "Dodging an attack sheds strain.") },
            { Passives.Ascension, P("Ascension", "Below 30% health it shakes off every stun, becomes Unstoppable for good and takes to the air.") },
            { DeliverancePassives.UnravelResistance, P("Unravel Resistance", "Unravel stuns it for half as long.") },
            { DeliverancePassives.FreedomsEmbrace, P("Freedom's Embrace", "While it lives, orders must be given within 20 seconds. When the time runs out it stuns every other unit on the map, drops the squad's orders and acts at once, Unstoppable.") },
            { DeliverancePassives.SplitTheMinds, P("Split The Minds", "The blow that would kill it leaves it at 1 health: it rises into the air, stunning every other unit, and splits into its four parts. Every other enemy on the field loses 75% of its maximum health, and every unit is pushed 9m away.") },
            { DeliverancePassives.UnboundTime, P("Unbound Time", "While it lives, time doesn't stop for orders: it creeps on, faster the longer they take.") },
            { DeliveranceParts.PathToVictory, P("Path To Victory", "Copied abilities have 75% more range. A stack of SEE BEFORE ITSELF every 5s (up to 3): while it has one it has 100% Dodge, and each dodge spends one.") },
            { DeliveranceParts.PlightUponStructure, P("Plight Upon Structure", "Copied abilities cost 50% less strain and have 50% shorter cooldowns. Transcending one that costs strain summons a ghost that casts it again, at a different target if it can.") },
            { DeliveranceParts.SingleMindedPerfection, P("Singleminded Perfection", "Copied abilities have 30% shorter windup and recovery. At max strain it doesn't Unravel: it casts EQUIPOSE instead.") },
            { DeliveranceParts.LastAttachments, P("Last Attachments", "Block and Transcended Block cost 75% less strain. Allies within 15m have +50% Block.") },
        };

        private static KeyValuePair<string, string> P(string name, string description) { return new KeyValuePair<string, string>(name, description); }

        public static string NameOf(string id)
        {
            KeyValuePair<string, string> p;
            if (Table.TryGetValue(id ?? "", out p)) return p.Key;
            return string.IsNullOrEmpty(id) ? "" : char.ToUpperInvariant(id[0]) + id.Substring(1).Replace('_', ' ');
        }

        /// <summary>What the passive does, or null if there's no text for it.</summary>
        public static string DescriptionOf(string id)
        {
            KeyValuePair<string, string> p;
            return Table.TryGetValue(id ?? "", out p) ? p.Value : null;
        }
    }
}
