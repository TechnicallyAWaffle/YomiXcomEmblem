using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TacticsGame.Core
{
    // The Sharpshooter's later abilities that need rules beyond data. Refraction Cloak is data (self_buff: Hidden, broken by
    // damage). Fate Encumbers Its Prey and Witness To The End are sky_shot follow-ups opened through SelfStatusesOnHit,
    // RequiredStatusOnTarget and ActionDefinition.FollowUps; Witness's missing-health damage is MissingHealthDamage.

    /// <summary>
    /// Soulseeker: a shot (rolled, cover and all) at the target, then a channel (ChannelSeconds at most) in which the bullet
    /// can be sent on up to 'max_redirects' times, each 'redirect_delay' seconds after its last hit, to an enemy within
    /// 'redirect_range' of the bullet that the bullet can see, other than the one it just hit (IRedirectable). Each redirect
    /// hits for the ability's damage and stun. A player's unit picks each target (a Redirect decision, the battle held); an
    /// AI's goes for the nearest. It ends when the redirects are spent, nothing is left to hit, the player stops it, or damage
    /// breaks the channel.
    /// </summary>
    public class SoulseekerBehavior : ActionBehavior, IRedirectable
    {
        public override IEnumerable<BehaviorParam> Parameters
        {
            get
            {
                yield return new BehaviorParam("redirect_range", 15f);
                yield return new BehaviorParam("redirect_delay", 0.25f);
                yield return new BehaviorParam("max_redirects", 4f);
            }
        }

        public override void Describe(AbilityTip tip)
        {
            base.Describe(tip);
            tip.Add("Redirects", string.Format("up to {0:0}, {1} from the bullet, every {2}", Param(tip.Def, "max_redirects"),
                AbilityTip.Metres(Param(tip.Def, "redirect_range")), AbilityTip.Secs(Param(tip.Def, "redirect_delay"))));
        }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            a.Steps = 0;
            a.NextStepAt = sim.Now + Param(a.Definition, "redirect_delay");
            var target = sim.GetUnit(a.TargetUnitId);
            if (target == null || !target.IsAlive) return;
            a.TargetPos = target.Position;   // where the bullet is
            effects.Add(new AttackEffect { Source = a.Actor, TargetId = target.Id, Definition = a.Definition, DamageMultiplier = a.DamageMultiplier, Tag = a.Tag });
        }

        public override bool OnChannelTick(BattleSimulation sim, ActionInstance a)
        {
            var actor = a.Actor;
            if (RedirectsLeft(a) <= 0) { sim.EndChannel(actor, null); return true; }
            if (actor.RedirectPending || sim.Now + 1e-4f < a.NextStepAt) return true;
            var last = sim.GetUnit(a.TargetUnitId);
            if (last != null && last.IsAlive) a.TargetPos = last.Position;   // the bullet rides in whoever it hit
            string why;
            var options = sim.EnemiesOf(actor).Where(e => CanRedirect(sim, a, e, out why)).ToList();
            if (options.Count == 0) { sim.EndChannel(actor, null); return true; }
            if (actor.Team == Team.Player) { actor.RedirectPending = true; return true; }   // the player picks (DecisionKind.Redirect)
            Redirect(sim, a, options.OrderBy(e => Flat(e.Position, a.TargetPos)).ThenBy(e => e.Id).First());
            return true;
        }

        public override void OnChannelEnd(BattleSimulation sim, ActionInstance a, bool broken) { a.Actor.RedirectPending = false; }

        public int RedirectsLeft(ActionInstance a) { return Mathf.Max(0, Mathf.RoundToInt(Param(a.Definition, "max_redirects")) - a.Steps); }

        public bool CanRedirect(BattleSimulation sim, ActionInstance a, Unit target, out string reason)
        {
            reason = null;
            if (target == null || !target.IsAlive || target.Team == a.Actor.Team) { reason = "not an enemy"; return false; }
            if (target.Id == a.TargetUnitId) { reason = "it just hit them"; return false; }
            if (!sim.IsTargetableBy(target, a.Actor)) { reason = "target hidden"; return false; }
            if (Flat(target.Position, a.TargetPos) > Param(a.Definition, "redirect_range")) { reason = "too far from the bullet"; return false; }
            if (!sim.Visibility.HasLineOfSight(Bullet(sim, a), target)) { reason = "the bullet can't see them"; return false; }
            return true;
        }

        public void Redirect(BattleSimulation sim, ActionInstance a, Unit target)
        {
            Vector3 from = a.TargetPos + Vector3.up * 1f;
            a.Steps++;
            a.TargetUnitId = target.Id;
            a.TargetPos = target.Position;
            a.NextStepAt = sim.Now + Param(a.Definition, "redirect_delay");
            a.Actor.RedirectPending = false;
            sim.EmitFx(a.Actor, target, "redirect", a.Definition, target.Position, 0f, from);
            sim.StrikeNow(new AttackEffect { Source = a.Actor, TargetId = target.Id, Definition = a.Definition, DamageMultiplier = a.DamageMultiplier, Tag = a.Tag, IgnoreRange = true, IgnoreCover = true });
        }

        /// <summary>Who the bullet "sees" from: the unit it last hit (its own body aside), or, if it is gone, a stand-in there.</summary>
        private static Unit Bullet(BattleSimulation sim, ActionInstance a)
        {
            var last = sim.GetUnit(a.TargetUnitId);
            if (last != null && last.IsAlive && Flat(last.Position, a.TargetPos) < 0.01f) return last;
            var ghost = a.Actor.Snapshot();
            ghost.Position = a.TargetPos;
            return ghost;
        }

        private static float Flat(Vector3 a, Vector3 b) { a.y = b.y = 0f; return Vector3.Distance(a, b); }
    }

    /// <summary>
    /// A shot from the air (Destiny Closes In and its follow-ups): the user rises DisplaceDistance over the windup (or, already
    /// up from the shot before it, stays there) and fires an ordinary ranged shot. An ability with FollowUps keeps the user up
    /// through its recovery, and for 'window' seconds more once the shot lands (its follow-up opening: SelfStatusesOnHit), so
    /// the next shot of the chain can be fired from the air; otherwise, and after the last one, it comes back down over the
    /// recovery. Struck while up there, it falls (a hover).
    /// </summary>
    public class SkyShotBehavior : FlyingBehavior
    {
        public override IEnumerable<BehaviorParam> Parameters { get { yield return new BehaviorParam("window", 1f); } }

        public override void Describe(AbilityTip tip)
        {
            base.Describe(tip);
            if (tip.V(ActionStat.DisplaceDistance) > 0f) tip.Add("Flies", AbilityTip.Metres(tip.V(ActionStat.DisplaceDistance)) + " up");
            if (tip.Def.MissingHealthDamage > 0f) tip.Add("Missing health", "+" + (tip.Def.MissingHealthDamage * 100f).ToString("0") + "% as damage");
            if (tip.Def.FollowUps != null && tip.Def.FollowUps.Count > 0)
                tip.Add("Follow-up", string.Join(", ", tip.Def.FollowUps.Where(f => f != null).Select(f => f.DisplayName).ToArray()) + " (if it lands)");
        }

        public override void Attach(BattleSimulation sim)
        {
            // The shot landed (blocked or not): the follow-up is open, so stay up for it.
            sim.Triggers.Subscribe(TriggerType.DamageTaken, ctx =>
            {
                var def = ctx.Action; var shooter = ctx.Other;
                if (def == null || def.BehaviorId != "sky_shot" || def.FollowUps == null || def.FollowUps.Count == 0 || shooter == null || !shooter.Hovering) return;
                float recovery = def.Recovery / Mathf.Max(0.1f, shooter.EffectiveActionSpeed());
                shooter.Hover.DropAt = Mathf.Max(shooter.Hover.DropAt, sim.Now + recovery + Param(def, "window"));
            });
        }

        public override void OnWindupStart(BattleSimulation sim, ActionInstance a)
        {
            var actor = a.Actor;
            float recovery = Mathf.Max(BattleSimulation.TickLength, a.Stat(ActionStat.Recovery) / actor.EffectiveActionSpeed());
            if (actor.Hovering)
            {
                // Up from the shot before it: hold the hover for this one, and come down over this one's recovery.
                a.OwnsHover = true;
                actor.Hover.DropAt = float.MaxValue;
                actor.Hover.EndsAfterAction = false;   // not Skyward Demise's one-ability-from-the-air: this chain lands itself
                actor.Hover.DescendSeconds = recovery;
                return;
            }
            float h = a.Stat(ActionStat.DisplaceDistance);
            if (h > 0f) TakeOff(sim, a, actor.Position + Vector3.up * h);
        }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            var target = sim.GetUnit(a.TargetUnitId);
            if (target != null && target.IsAlive)
                effects.Add(new AttackEffect { Source = a.Actor, TargetId = target.Id, Definition = a.Definition, DamageMultiplier = a.DamageMultiplier, Tag = a.Tag });
            bool chain = a.Definition.FollowUps != null && a.Definition.FollowUps.Count > 0;
            if (chain && a.OwnsHover && a.Actor.Hovering)
            {
                // Stay up through the recovery; a landed shot extends it for the follow-up (Attach). Then down by itself.
                a.OwnsHover = false;
                a.Actor.Hover.DropAt = sim.Now + Mathf.Max(BattleSimulation.TickLength, a.Stat(ActionStat.Recovery) / a.Actor.EffectiveActionSpeed());
                return;
            }
            Land(sim, a);
        }
    }
}
