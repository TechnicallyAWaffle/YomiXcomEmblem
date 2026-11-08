using UnityEngine;

namespace TacticsGame.Core
{
    /// <summary>
    /// A channeled ability whose effect can be sent on to a new target while it lasts (Soulseeker's bullet). The behavior
    /// sets Unit.RedirectPending when it is ready to go on; a player's unit is then asked for a Redirect decision (the battle
    /// holds), an AI's picks its own (PickRedirect). CanRedirect says which targets are allowed; Redirect sends it there.
    /// </summary>
    public interface IRedirectable
    {
        bool CanRedirect(BattleSimulation sim, ActionInstance a, Unit target, out string reason);
        void Redirect(BattleSimulation sim, ActionInstance a, Unit target);
        /// <summary>How many more times it can go on.</summary>
        int RedirectsLeft(ActionInstance a);
    }

    /// <summary>BattleSimulation: redirecting a channel (IRedirectable) to its next target.</summary>
    public partial class BattleSimulation
    {
        private IRedirectable RedirectableOf(Unit unit, out ActionInstance a)
        {
            a = unit != null && unit.Phase == UnitPhase.Channeling && unit.Channel != null ? unit.Channel.Action : null;   // the channel holds its action
            return a != null ? a.Behavior as IRedirectable : null;
        }

        /// <summary>Can 'unit's channel be sent on to 'target' now?</summary>
        public bool CanRedirect(Unit unit, Unit target, out string reason)
        {
            ActionInstance a;
            var r = RedirectableOf(unit, out a);
            if (r == null) { reason = "nothing to redirect"; return false; }
            if (target == null) { reason = "no target"; return false; }
            return r.CanRedirect(this, a, target, out reason);
        }

        /// <summary>How many more redirects 'unit's channel has (0 when it has none).</summary>
        public int RedirectsLeft(Unit unit)
        {
            ActionInstance a;
            var r = RedirectableOf(unit, out a);
            return r != null ? r.RedirectsLeft(a) : 0;
        }

        /// <summary>A Redirect order the way an AI would give it: on to the nearest enemy it can reach, or stop (auto-play).</summary>
        public UnitCommand AutoRedirect(Unit unit)
        {
            ActionInstance a;
            var r = RedirectableOf(unit, out a);
            Unit best = null; float bestD = float.MaxValue; string why;
            if (r != null)
                foreach (var e in EnemiesOf(unit))
                {
                    if (!r.CanRedirect(this, a, e, out why)) continue;
                    float d = Vector3.Distance(e.Position, a.TargetPos);
                    if (d < bestD) { bestD = d; best = e; }
                }
            return UnitCommand.Redirect(unit.Id, best != null ? best.Id : null);
        }

        /// <summary>A Redirect order: on to its target, or (no target) the channel ends here.</summary>
        private bool Redirect(Unit unit, UnitCommand cmd, out string reason)
        {
            reason = null;
            ActionInstance a;
            var r = RedirectableOf(unit, out a);
            if (r == null || cmd.Type != CommandType.Redirect) { reason = "redirect it or stop"; return false; }
            unit.RedirectPending = false;
            if (string.IsNullOrEmpty(cmd.TargetUnitId)) { EndChannel(unit, null); return true; }   // stopped: it ends as if run out (recovery follows)
            var target = GetUnit(cmd.TargetUnitId);
            if (!r.CanRedirect(this, a, target, out reason)) { unit.RedirectPending = true; return false; }
            r.Redirect(this, a, target);
            return true;
        }
    }
}
