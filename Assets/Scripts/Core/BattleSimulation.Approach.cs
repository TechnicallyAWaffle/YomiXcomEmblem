using System.Collections.Generic;
using UnityEngine;

namespace TacticsGame.Core
{
    /// <summary>BattleSimulation: closing in behind a melee windup, and follow-up windows.</summary>
    public partial class BattleSimulation
    {
        /// <summary>A melee windup carries its user toward the target until their bodies are this far apart.</summary>
        public float MeleeApproachGap = 0.5f;
        /// <summary>Only abilities with at most this much range step in (a "melee" ability that reaches further stays put).</summary>
        public float MeleeApproachMaxRange = 4f;
        /// <summary>The target has to move this far from where it was aimed at before the step is aimed again.</summary>
        public float MeleeApproachRetarget = 0.25f;

        /// <summary>
        /// A melee ability winding up at an enemy (ActionBehavior.ApproachesTarget): its user walks toward the target at its
        /// own move speed, as far as the windup allows, stopping MeleeApproachGap from it. Called as the windup starts and
        /// every tick after, so it follows a target that moves. A rooted, lifted or held user stays where it is, and a dodge
        /// or shove under way isn't overridden.
        /// </summary>
        private void StepIntoMelee(ActionInstance a)
        {
            var u = a.Actor; var def = a.Definition;
            if (a.IsFree || u.ActiveAction != a || !a.Behavior.ApproachesTarget(def) || u.ActionStat(def, ActionStat.Range) > MeleeApproachMaxRange) return;
            var t = GetUnit(a.TargetUnitId);
            if (t == null || t == u || !t.IsAlive || t.Team == u.Team) return;
            if (u.IsRooted || u.IsLifted || u.IsJumping || u.FallLandAt >= 0f || u.AnyState(s => s.BlocksDodgeDash)) return;
            if (u.GlideEnd >= 0f && Mathf.Abs(u.GlideEnd - a.ApproachGlideEnd) > 1e-4f) return;   // someone else's glide
            float left = a.ResolveAt - Now;
            if (left < TickLength * 0.5f) return;
            Vector3 d = t.Position - u.Position; d.y = 0f;
            float dist = d.magnitude, stop = u.BodyRadius + t.BodyRadius + MeleeApproachGap;
            if (dist <= stop + 0.05f) return;
            if (u.GlideEnd >= 0f && a.ApproachAim.HasValue)
            {
                Vector3 moved = t.Position - a.ApproachAim.Value; moved.y = 0f;
                if (moved.magnitude < MeleeApproachRetarget) return;   // still heading the right way
            }
            float speed = Mathf.Max(0.1f, u.EffectiveMoveSpeed());
            float travel = Mathf.Min(dist - stop, speed * left);
            if (travel < 0.05f) return;
            a.ApproachAim = t.Position;
            Relocate(u, u.Position + d / dist * travel, travel / speed, false, null, null, true);
            a.ApproachGlideEnd = u.GlideEnd;
        }

        /// <summary>
        /// Is 's' on 'u' the window of a follow-up (a status one of its abilities needs and uses up)? Then it gets
        /// StatusFlags.FollowupWindow: walking and dashing don't run it down.
        /// </summary>
        private static void MarkFollowupWindow(Unit u, StatusEffect s)
        {
            if (u == null || s == null) return;
            foreach (var a in u.Actions)
            {
                if (a == null || !a.ConsumesRequiredStatus) continue;
                if (System.Array.IndexOf(a.RequiredStatuses(), s.Id) >= 0) { s.Flags |= StatusFlags.FollowupWindow; return; }
            }
        }
    }
}
