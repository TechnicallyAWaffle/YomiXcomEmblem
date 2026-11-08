using System.Collections.Generic;
using TacticsGame.Core;

namespace TacticsGame.Presentation
{
    /// <summary>
    /// The battle log's line for each timeline event (BattleHUD draws them). Kept apart from the HUD, with no GUI in it, so
    /// the lines can be checked outside Unity. Lines are rich text: a Heavy impact is red and in capitals, a Light one blue.
    /// A unit the log can't name is written "?", and Unknown hears which case it was (see the case names in Who).
    /// </summary>
    public class BattleLogText
    {
        /// <summary>A unit's display name by id; null if the log doesn't know it.</summary>
        public System.Func<string, string> UnitName;
        /// <summary>An ability by id; null if unknown.</summary>
        public System.Func<string, ActionDefinition> Action;
        /// <summary>Is the unit hanging in the air from a lift (a fall then reads "is dropped")?</summary>
        public System.Func<string, bool> Suspended;
        /// <summary>A "?" was written: the case it was written for.</summary>
        public System.Action<string> Unknown;

        public const string HeavyColor = "#ff5a45", LightColor = "#7ec8ff";

        public string ActionName(string id)
        {
            var def = id != null && Action != null ? Action(id) : null;
            return def != null && !string.IsNullOrEmpty(def.DisplayName) ? def.DisplayName : id;
        }

        private bool Known(string id) { return id != null && UnitName != null && UnitName(id) != null; }

        /// <summary>The unit's name, or "?" (reported to Unknown as 'situation').</summary>
        private string Who(string id, string situation)
        {
            string name = id != null && UnitName != null ? UnitName(id) : null;
            if (name != null) return name;
            if (Unknown != null) Unknown(situation);
            return "?";
        }

        /// <summary>An impact's line in its colour: Heavy red and in capitals, Light blue.</summary>
        public static string Impact(string line, ImpactType impact)
        {
            return impact == ImpactType.Heavy ? "<color=" + HeavyColor + ">" + line.ToUpperInvariant() + "</color>" : "<color=" + LightColor + ">" + line + "</color>";
        }

        /// <summary>
        /// The line for 'evt', or null for none. Called before the HUD updates its readout for the event (a fall asks whether
        /// the unit was hanging in the air).
        /// </summary>
        public string Line(TimelineEvent evt)
        {
            switch (evt.Type)
            {
                case EventType.ActionStarted:
                {
                    var a = (ActionStartedInfo)evt.Payload;
                    if (!Known(evt.ActorId) || a.ActionId == "wait" || a.ActionId == "idle") return null;
                    string actor = UnitName(evt.ActorId), name = ActionName(a.ActionId);
                    var def = Action != null ? Action(a.ActionId) : null;
                    bool self = evt.TargetId == evt.ActorId || (evt.TargetId == null && def != null && (def.Targeting == TargetType.Self || def.Targeting == TargetType.None));
                    if (self) return string.Format("{0} uses {1}{2}", actor, a.Transcended ? "TRANSCENDED " : "", name);
                    if (evt.TargetId == null) return string.Format("{0} prepares {1}{2}", actor, a.Transcended ? "TRANSCENDED " : "", name);   // aimed at a point
                    return string.Format("{0} {1}{2} on {3}", actor, a.Transcended ? "TRANSCENDS " : "winds up ", name,
                        Who(evt.TargetId, "an ability started on a unit the log doesn't know"));
                }
                case EventType.ActionInterrupted:
                    if (!Known(evt.ActorId)) return null;
                    return string.Format("{0}'s action is interrupted ({1})", UnitName(evt.ActorId), ((InterruptInfo)evt.Payload).Reason);
                case EventType.Damage:
                {
                    var d = (DamageInfo)evt.Payload;
                    if (d.ActionId == "fall")
                        return Impact(string.Format("{0} hits the ground for {1:0}", Who(evt.TargetId, "fall damage on an unknown unit"), d.Amount), d.Impact);
                    if (evt.ActorId == evt.TargetId) return string.Format("{0} pays {1:0} health for {2}", Who(evt.TargetId, "health paid by an unknown unit"), d.Amount, ActionName(d.ActionId));
                    string by = Who(evt.ActorId, d.ActionId == "dev" ? "damage from the dev menu (kill, set health)" : evt.ActorId == null ? "damage with no source (a zone or effect with no owner)" : "damage from a unit the log doesn't know");
                    string on = Who(evt.TargetId, "damage on an unknown unit");
                    // A hit that does no damage by design (Subjugate's grab, Amblivate, Torrent of Strife): it still landed.
                    if (d.Amount < 0.5f && d.BlockedAmount < 0.5f) return Impact(string.Format("{0} catches {1} with {2}", by, on, ActionName(d.ActionId)), d.Impact);
                    return Impact(string.Format("{0} hits {1} with {2} for {3:0}{4}", by, on, ActionName(d.ActionId), d.Amount,
                        d.BlockedAmount > 0.5f ? string.Format(" ({0:0} blocked)", d.BlockedAmount) : ""), d.Impact);
                }
                case EventType.Miss:
                {
                    var m = (MissInfo)evt.Payload;
                    string with = m.ActionId != null ? " with " + ActionName(m.ActionId) : "";
                    string why = m.Reason == "cover" ? (m.HitCover ? "hits cover" : "cover") : m.Reason == "out_of_range" ? "out of range" : m.Reason;
                    return string.Format("{0} misses {1}{2} ({3})", Who(evt.ActorId, "a miss with no attacker"), Who(evt.TargetId, "a miss with no target (" + m.Reason + ")"), with, why);
                }
                case EventType.Dodge:
                    return string.Format("{0} dodges {1}", Who(evt.TargetId, "a dodge by an unknown unit"), Who(evt.ActorId, "a dodge of an attack with no attacker"));
                case EventType.Carry:
                    if (!((CarryInfo)evt.Payload).Carrying) return null;
                    return string.Format("{0} grabs {1}", Who(evt.ActorId, "a grab by an unknown unit"), Who(evt.TargetId, "a grab of an unknown unit"));
                case EventType.ChannelStarted:
                    if (!Known(evt.ActorId)) return null;
                    return string.Format("{0} channels {1}", UnitName(evt.ActorId), ActionName(((ChannelInfo)evt.Payload).ActionId));
                case EventType.ChannelEnded:
                {
                    var c = (ChannelInfo)evt.Payload;
                    if (!Known(evt.ActorId) || !c.Broken || c.Reason == "death") return null;
                    return string.Format("{0}'s {1} is broken ({2})", UnitName(evt.ActorId), ActionName(c.ActionId), c.Reason);
                }
                case EventType.HitRedirected:
                {
                    var r = (RedirectInfo)evt.Payload;
                    return string.Format("{0} takes {1}'s {2} meant for {3}", Who(evt.ActorId, "a hit taken for another by an unknown unit"),
                        Who(r.AttackerId, "a hit taken for another, from no attacker"), ActionName(r.ActionId), Who(evt.TargetId, "a hit taken for an unknown unit"));
                }
                case EventType.AerialSlam:
                    return string.Format("{0} carries {1} into the air", Who(evt.ActorId, "an aerial slam by an unknown unit"), Who(evt.TargetId, "an aerial slam on an unknown unit"));
                case EventType.AerialSlamEnded:
                    return (bool)evt.Payload
                        ? string.Format("{0} is knocked out of the air and drops {1}", Who(evt.ActorId, "an aerial slam broken, by an unknown carrier"), Who(evt.TargetId, "an aerial slam broken, dropping no one"))
                        : string.Format("{0} slams {1} into the ground", Who(evt.ActorId, "an aerial slam landing, by an unknown unit"), Who(evt.TargetId, "an aerial slam landing on an unknown unit"));
                case EventType.DropStarted:
                    return string.Format("{0} drops in from the sky", Who(evt.TargetId, "a drop-in by an unknown unit"));
                case EventType.Lift:
                    if (((LiftInfo)evt.Payload).Hover) return string.Format("{0} leaps into the air", Who(evt.TargetId, "a leap by an unknown unit"));
                    return string.Format("{0} lifts {1} into the air", Who(evt.ActorId, "a lift with no one lifting (the source is gone)"), Who(evt.TargetId, "a lift of an unknown unit"));
                case EventType.Dash:
                {
                    var dash = (DashInfo)evt.Payload;
                    if (!dash.Teleport || !Known(evt.ActorId)) return null;
                    return string.Format("{0} is teleported", UnitName(evt.ActorId));
                }
                case EventType.Fall:
                {
                    var f = (FallInfo)evt.Payload;
                    bool dropped = Suspended != null && evt.TargetId != null && Suspended(evt.TargetId);
                    return string.Format("{0} {1} and falls {2:0.0}m", Who(evt.TargetId, "a fall by an unknown unit"), f.FromJump ? "is knocked out of a jump" : dropped ? "is dropped" : "is knocked off a ledge", f.Height);
                }
                case EventType.Stun:
                    return string.Format("{0} is stunned for {1:0.00}s", Who(evt.TargetId, "a stun on an unknown unit"), ((StunInfo)evt.Payload).Duration);
                case EventType.Unravel:
                    return string.Format("{0} UNRAVELS", Who(evt.TargetId, "an Unravel of an unknown unit"));
                case EventType.Heal:
                    return string.Format("{0} heals {1} for {2:0}", Who(evt.ActorId, evt.ActorId == null ? "a heal with no healer (the dev menu's heal)" : "a heal from a healer the log doesn't know"), Who(evt.TargetId, "a heal on an unknown unit"), (float)evt.Payload);
                case EventType.StatusApplied:
                {
                    var id = (string)evt.Payload;
                    if (StatusDisplay.IsHidden(id) || id == StatusIds.Focusing) return null;
                    return string.Format("{0} is {1}", Who(evt.TargetId, "a status on an unknown unit"), StatusDisplay.NameOf(id));
                }
                case EventType.TrapPlaced:
                    return string.Format("{0} sets a trap", Who(evt.ActorId, "a trap set by an unknown unit"));
                case EventType.TrapTriggered:
                    return string.Format("{0} springs {1}'s trap", Who(evt.TargetId, "a trap sprung by an unknown unit"), Who(evt.ActorId, "a trap sprung whose owner is unknown"));
                case EventType.ChargeStarted:
                    if (!Known(evt.ActorId)) return null;
                    return string.Format("{0} starts charging {1}", UnitName(evt.ActorId), ActionName(((ChargeInfo)evt.Payload).ActionId));
                case EventType.QueuedActionChanged:
                {
                    var q = (QueuedActionInfo)evt.Payload;
                    if (!Known(evt.ActorId) || evt.TargetId == null) return null;
                    return string.Format("{0} {1} {2} for {3}", UnitName(evt.ActorId), q.Chase ? "closes in to" : "queues", ActionName(q.ActionId), Who(evt.TargetId, "an order queued on an unknown unit"));
                }
                case EventType.UnitSpawned:
                {
                    var s = (SpawnInfo)evt.Payload;
                    if (s.Unit == null || evt.ActorId == null) return null;   // no summoner (reinforcements: their drop has its own line)
                    return string.Format("{0} summons a {1}", Who(evt.ActorId, "a summon by an unknown unit"), s.Unit.DisplayName);
                }
                case EventType.Death:
                    return string.Format("{0} is down", Who(evt.TargetId, "the death of an unknown unit"));
            }
            return null;
        }
    }
}
