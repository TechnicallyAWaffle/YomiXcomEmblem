using UnityEngine;

namespace TacticsGame.Core
{
    /// <summary>
    /// Follow-ups Etchings give abilities that have none (Unit.GrantedFollowups; FollowupGrant). Recast (Incessant): an
    /// ability started for real (not resumed) remembers its order (Unit.RecastPending). Once it is over and the unit is
    /// ready again, that order opens for the grant's 'window' (StatusIds.Incessant, Unit.RecastOpen). Using the ability
    /// in that window is the recast: aimed where the first one was, whatever the new order says, for its strain plus the
    /// grant's Strain, ignoring its cooldown (and not restarting it). A recast opens no window of its own, and an
    /// interrupted ability opens none.
    ///
    /// Linked abilities (ActionDefinition.LinkedAbilities, and Related Concepts' partner): once an ability lands, those it
    /// links to open (Unit.LinkedOpen, shown as StatusIds.Linked) and stay open, with no time limit, until the unit starts
    /// any other ability; walking, dashing (a behavior that KeepsQueuedOrder) and waiting don't count. An interrupted ability
    /// opens nothing, and starting anything closes what was open. A linked use can be aimed anywhere (no same-target rule,
    /// unlike follow-ups) and opens its own links in turn; Unit.LinkDepth counts linked uses in a row (Sever grows with it).
    /// One that RequiresLink can only be used that way. Related Concepts' partner, used linked, winds up and recovers 'speed'
    /// (0.5) as long, and opens nothing back.
    /// </summary>
    public partial class BattleSimulation
    {
        /// <summary>Is the recast of 'def' open for 'u' right now?</summary>
        public bool RecastOpenFor(Unit u, ActionDefinition def)
        {
            return u != null && def != null && u.RecastOpen != null && u.RecastOpen.Action == def && u.HasStatus(StatusIds.Incessant);
        }

        /// <summary>The extra strain using 'def' costs 'u' now: the grant's Strain while its recast is open, else 0.</summary>
        public float RecastStrainOf(Unit u, ActionDefinition def)
        {
            GrantedFollowup g;
            return RecastOpenFor(u, def) && u.GrantedFollowups.TryGetValue(def.Id, out g) ? g.Strain : 0f;
        }

        /// <summary>An order for 'def' while its recast is open, aimed where the first cast was. Any other order as it is.</summary>
        public UnitCommand AsRecast(Unit u, UnitCommand cmd)
        {
            if (cmd == null || !RecastOpenFor(u, cmd.Action)) return cmd;
            var r = u.RecastOpen;
            return new UnitCommand
            {
                UnitId = u.Id, Type = CommandType.Action, Action = cmd.Action, TargetUnitId = r.TargetUnitId, SecondTargetId = r.SecondTargetId,
                TargetPos = r.TargetPos, WaitForRange = cmd.WaitForRange, Transcend = cmd.Transcend,
            };
        }

        /// <summary>The order for the open recast, ready to send (null: none open). The order screen re-casts with it.</summary>
        public UnitCommand RecastOrder(Unit u)
        {
            if (u == null || u.RecastOpen == null || !RecastOpenFor(u, u.RecastOpen.Action)) return null;
            return AsRecast(u, UnitCommand.UseAction(u.Id, u.RecastOpen.Action));
        }

        // ------------------------------------------------------------------ linked abilities

        /// <summary>Is 'def' open as a linked ability for 'u' right now?</summary>
        public bool LinkedOpenFor(Unit u, ActionDefinition def)
        {
            return u != null && def != null && u.LinkedOpen.Contains(def.Id);
        }

        /// <summary>Close every linked ability open for 'u' (it started another ability).</summary>
        private void BreakLinks(Unit u)
        {
            if (u.LinkedOpen.Count == 0) return;
            u.LinkedOpen.Clear();
            RemoveStatus(u, StatusIds.Linked);
        }

        /// <summary>An ability landed: what it links to opens (its LinkedAbilities the unit has, and, for a fresh use, its Related
        /// Concepts partner), carrying the run so far as LinkDepth.</summary>
        private void OpenLinks(ActionInstance a)
        {
            var u = a.Actor;
            if (u == null || !u.IsAlive) return;
            u.LinkedOpen.Clear();
            if (a.Definition.LinkedAbilities != null)
                foreach (var l in a.Definition.LinkedAbilities)
                    if (l != null && u.Actions.Contains(l) && !u.LinkedOpen.Contains(l.Id)) u.LinkedOpen.Add(l.Id);
            GrantedFollowup g;
            if (!a.LinkedUse && u.GrantedFollowups.TryGetValue(a.Definition.Id, out g) && g.Kind == FollowupGrant.Linked
                && !string.IsNullOrEmpty(g.PartnerId) && !u.LinkedOpen.Contains(g.PartnerId))
                u.LinkedOpen.Add(g.PartnerId);
            u.LinkDepth = a.LinkDepth;
            if (u.LinkedOpen.Count > 0) ApplyStatus(u, new StatusEffect { Id = StatusIds.Linked, SourceId = u.Id, ExpiresAt = UntimedStatus }, u);
            else RemoveStatus(u, StatusIds.Linked);
        }

        /// <summary>How much faster a linked use of 'def' winds up and recovers: its Related Concepts grant's 'speed' (0.5); 1 when
        /// it isn't open as a linked ability, or isn't one of such a pair.</summary>
        public float LinkedScale(Unit u, ActionDefinition def)
        {
            GrantedFollowup g;
            if (!LinkedOpenFor(u, def) || !u.GrantedFollowups.TryGetValue(def.Id, out g) || g.Kind != FollowupGrant.Linked) return 1f;
            return Mathf.Clamp(g.Param("speed", 0.5f), 0f, 1f);
        }

        // ------------------------------------------------------------------ recasts

        /// <summary>An ability started for real (after its costs): a recast spends its window; one with a granted recast
        /// remembers what to open when it is over.</summary>
        private void NoteRecastStart(Unit u, UnitCommand cmd, bool recast)
        {
            u.RecastPending = null;
            if (recast)
            {
                u.RecastOpen = null;
                RemoveStatus(u, StatusIds.Incessant);
                return;
            }
            GrantedFollowup g;
            if (!u.GrantedFollowups.TryGetValue(cmd.Action.Id, out g) || g.Kind != FollowupGrant.Recast) return;
            u.RecastPending = new UnitCommand
            {
                UnitId = u.Id, Type = CommandType.Action, Action = cmd.Action, TargetUnitId = cmd.TargetUnitId, SecondTargetId = cmd.SecondTargetId,
                TargetPos = cmd.TargetPos,
            };
        }

        /// <summary>The unit is ready again (TriggerType.UnitReady): a remembered order opens its recast.</summary>
        public void OpenRecast(Unit u)
        {
            var p = u != null ? u.RecastPending : null;
            if (p == null) return;
            u.RecastPending = null;
            GrantedFollowup g;
            if (!u.IsAlive || !u.GrantedFollowups.TryGetValue(p.Action.Id, out g)) return;
            u.RecastOpen = p;
            ApplyStatus(u, new StatusEffect { Id = StatusIds.Incessant, SourceId = u.Id, ExpiresAt = Now + Mathf.Max(TickLength, g.Param("window", 1.5f)) }, u);
        }
    }
}
