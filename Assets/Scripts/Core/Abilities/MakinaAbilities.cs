using System.Collections.Generic;
using UnityEngine;

namespace TacticsGame.Core
{
    // Makina: the Strategist's constructs (BattleSimulation, Makina.cs). summon_makina brings one in and has it act at once;
    // crash_down, massacre and omen are what they do.

    /// <summary>
    /// Termoul's Monolith, Karraerm's Hedron (summon_makina): bring in SummonUnit (a Makina) on the user's side and have it
    /// use LinkedAction (one of its own abilities) at once, with no windup, aimed at the target point. 'spawn_ahead' 0: it
    /// appears at the point itself (The Monolith drops onto it); more: that far in front of the user, toward the point (The
    /// Hedron, then charging that way).
    /// </summary>
    public class SummonMakinaBehavior : ActionBehavior
    {
        public override IEnumerable<BehaviorParam> Parameters { get { yield return new BehaviorParam("spawn_ahead", 0f); } }

        public override bool RollsToHit(ActionDefinition def) { return false; }

        public override bool CanStart(BattleSimulation sim, Unit actor, UnitCommand cmd, out string reason)
        {
            reason = cmd.Action.SummonUnit == null ? "nothing to summon" : null;
            return reason == null;
        }

        public override void Describe(AbilityTip tip)
        {
            var d = tip.Def;
            if (d.SummonUnit != null) tip.Add("Summons", d.SummonUnit.DisplayName + (d.LinkedAction != null ? ", which uses " + d.LinkedAction.DisplayName + " at once" : ""));
        }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            var actor = a.Actor; var def = a.Definition;
            if (def.SummonUnit == null) return;
            Vector3 aim = a.TargetPos;
            Vector3 dir = aim - actor.Position; dir.y = 0f;
            dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.forward;
            float ahead = Param(def, "spawn_ahead");
            Vector3 spot = ahead > 0f ? sim.Paths.ClampDash(actor.Position, actor.Position + dir * ahead) : aim;
            var act = def.LinkedAction;
            // The Hedron charges the way it was aimed, from where it appears: its aim point is that far along the line.
            Vector3 entranceAim = ahead > 0f ? spot + dir * Mathf.Max(1f, (aim - actor.Position).magnitude) : aim;
            sim.SpawnUnit(def.SummonUnit, actor.Team, spot, actor, null, m =>
            {
                if (act != null) sim.StartEntrance(m, act, entranceAim);
            });
            sim.EmitFx(actor, null, "makina", def, spot);
        }
    }

    /// <summary>
    /// Crash Down (crash_down): rise diagonally over the windup to 'height' (5m) above the target point, then come straight down hard onto it in 'fall_seconds'
    /// (0.2s), hitting every enemy within AreaRadius of it and knocking each one it stuns DisplaceDistance (1.5m) into the air
    /// for the stun, to drop and take fall damage (BattleSimulation.LiftUnit, as Behemoth's Arrival). Damage while it is up
    /// knocks it out of the air (a hover's rule), and the crash with it. With no windup (its entrance) it drops onto the point
    /// from the sky.
    /// </summary>
    public class CrashDownBehavior : FlyingBehavior
    {
        public override IEnumerable<BehaviorParam> Parameters
        {
            get { yield return new BehaviorParam("height", 5f); yield return new BehaviorParam("fall_seconds", 0.2f); yield return new BehaviorParam("drop_from", 10f); }
        }

        public override bool MovesUser { get { return true; } }
        public override bool RollsToHit(ActionDefinition def) { return true; }

        protected override void DescribeMovement(AbilityTip tip)
        {
            tip.Add("Rises", AbilityTip.Metres(Param(tip.Def, "height")) + " toward the point, then crashes straight down onto it");
            if (tip.V(ActionStat.DisplaceDistance) > 0f) tip.Add("Knocks up", AbilityTip.Metres(tip.V(ActionStat.DisplaceDistance)));
        }

        public override void OnWindupStart(BattleSimulation sim, ActionInstance a)
        {
            // A diagonal rise to 'height' above the target point; the crash then comes straight down onto it.
            if (a.ResolveAt - sim.Now > BattleSimulation.TickLength * 0.5f) TakeOff(sim, a, sim.Terrain.OnGround(a.TargetPos) + Vector3.up * Param(a.Definition, "height"));
        }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            var actor = a.Actor; var def = a.Definition;
            a.OwnsHover = false;   // the crash takes it down itself
            Vector3 landed = sim.CrashTo(actor, a.TargetPos, Param(def, "fall_seconds"), Param(def, "drop_from"));
            float up = a.Stat(ActionStat.DisplaceDistance);
            foreach (var u in sim.UnitsInArea(actor, def, landed, true))
                effects.Add(new AttackEffect
                {
                    Source = actor, TargetId = u.Id, Definition = def, DamageMultiplier = a.DamageMultiplier, Tag = a.Tag, IgnoreRange = true, IgnoreCover = true,
                    OnLanded = up > 0f ? (s, t) => s.LiftUnit(t, up, actor) : (System.Action<BattleSimulation, Unit>)null,
                });
            float crashRadius = a.Stat(ActionStat.AreaRadius);
            sim.OnArrival(actor, () => sim.EmitFx(actor, null, "crash", def, landed, crashRadius));   // as it hits the ground, with its hits
        }
    }

    /// <summary>
    /// Massacre (massacre): dash Range along the aimed line, hitting every enemy on it (LineWidth) and dragging each one hit
    /// along to where the dash ends, just ahead of the user.
    /// </summary>
    public class MassacreBehavior : ActionBehavior
    {
        public override IEnumerable<BehaviorParam> Parameters { get { yield return new BehaviorParam("dash_seconds", 0.25f); } }

        public override AbilityRole DefaultRoles(ActionDefinition def) { return base.DefaultRoles(def) | AbilityRole.Gapcloser; }
        public override bool MovesUser { get { return true; } }
        public override bool MovesTargets(ActionDefinition def) { return true; }
        public override bool RollsToHit(ActionDefinition def) { return true; }

        protected override void DescribeMovement(AbilityTip tip) { tip.Add("Dash", AbilityTip.Metres(tip.V(ActionStat.Range)) + ", dragging enemies hit along"); }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            var actor = a.Actor; var def = a.Definition;
            Vector3 dir = a.TargetPos - actor.Position; dir.y = 0f;
            dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.forward;
            float range = a.Stat(ActionStat.Range);
            Vector3 end = sim.PhaseLanding(actor.Position, actor.Position + dir * range);   // through walls and cover (who it drags stops at them)
            foreach (var u in sim.UnitsInArea(actor, def, actor.Position + dir * range))
            {
                Vector3 dragTo = sim.Paths.ClampDash(u.Position, end + dir * (actor.BodyRadius + u.BodyRadius + 0.2f));
                effects.Add(new AttackEffect
                {
                    Source = actor, TargetId = u.Id, Definition = def, DamageMultiplier = a.DamageMultiplier, Tag = a.Tag,
                    IgnoreRange = true, IgnoreCover = true, HasPush = true, PushTo = dragTo,
                });
            }
            sim.SlideUnitTo(actor, end, Mathf.Max(BattleSimulation.TickLength, Param(def, "dash_seconds")), end);
        }
    }

    /// <summary>
    /// Omen (omen): rise 'height' (3m) over the windup, then blink to the target point and pull every enemy within AreaRadius
    /// DisplaceDistance toward it (its hit: Displace TowardSource), stunning them.
    /// </summary>
    public class OmenBehavior : FlyingBehavior
    {
        public override IEnumerable<BehaviorParam> Parameters { get { yield return new BehaviorParam("height", 3f); } }

        public override bool MovesUser { get { return true; } }
        public override bool RollsToHit(ActionDefinition def) { return true; }

        protected override void DescribeMovement(AbilityTip tip)
        {
            tip.Add("Blink", "to the point");
            if (tip.V(ActionStat.DisplaceDistance) > 0f) tip.Add("Pulls", string.Format("enemies within {0} in by {1}", AbilityTip.Metres(tip.V(ActionStat.AreaRadius)), AbilityTip.Metres(tip.V(ActionStat.DisplaceDistance))));
        }

        public override void OnWindupStart(BattleSimulation sim, ActionInstance a)
        {
            if (a.ResolveAt - sim.Now > BattleSimulation.TickLength * 0.5f) TakeOff(sim, a, a.Actor.Position + Vector3.up * Param(a.Definition, "height"));
        }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            var actor = a.Actor; var def = a.Definition;
            a.OwnsHover = false;
            sim.TeleportUnit(actor, a.TargetPos, actor);   // out of the air and there
            foreach (var u in sim.UnitsInArea(actor, def, actor.Position, true))
                effects.Add(new AttackEffect { Source = actor, TargetId = u.Id, Definition = def, DamageMultiplier = a.DamageMultiplier, Tag = a.Tag, IgnoreRange = true, IgnoreCover = true, Unerring = true });
            sim.EmitFx(actor, null, "omen", def, actor.Position, a.Stat(ActionStat.AreaRadius));
        }
    }

    /// <summary>
    /// Outburst (outburst): dash up to Range to the target point, then a burst of AreaRadius around where it lands that
    /// always connects (its hit: the ability's damage, stun and TargetStatusesOnHit, IMMOBILIZED). The Stellation's
    /// entrance uses it where it appears, so it bursts on the spot.
    /// </summary>
    public class OutburstBehavior : ActionBehavior
    {
        public override AbilityRole DefaultRoles(ActionDefinition def) { return base.DefaultRoles(def) | AbilityRole.Gapcloser; }
        public override bool MovesUser { get { return true; } }
        public override bool Dashes { get { return true; } }
        public override bool RollsToHit(ActionDefinition def) { return false; }

        protected override void DescribeMovement(AbilityTip tip) { tip.Add("Dash", "up to " + AbilityTip.Metres(tip.V(ActionStat.Range)) + ", then a burst " + AbilityTip.Metres(tip.V(ActionStat.AreaRadius)) + " wide"); }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            var actor = a.Actor; var def = a.Definition;
            Vector3 d = a.TargetPos - actor.Position; d.y = 0f;
            float reach = a.Stat(ActionStat.Range);
            if (d.magnitude > reach) d = d.normalized * reach;
            Vector3 at = actor.Position;
            if (!actor.IsRooted && d.sqrMagnitude > 1e-4f)
            {
                at = sim.PhaseLanding(actor.Position, actor.Position + d);   // through walls and cover
                sim.SlideUnitTo(actor, at, BattleSimulation.DashDuration, at);
                at = actor.GlideEnd > sim.Now ? actor.GlideTo : at;   // where it really lands (a crowded spot moves it a little)
            }
            foreach (var u in sim.UnitsInArea(actor, def, at, true))
                effects.Add(new AttackEffect { Source = actor, TargetId = u.Id, Definition = def, DamageMultiplier = a.DamageMultiplier, Tag = a.Tag, IgnoreRange = true, IgnoreCover = true, Unerring = true });
            float burstRadius = a.Stat(ActionStat.AreaRadius); Vector3 burstAt = at;
            sim.OnArrival(actor, () => sim.EmitFx(actor, null, "outburst", def, burstAt, burstRadius));   // where it lands, with its hits
        }
    }

    /// <summary>
    /// Imperfect Apotheosis (apotheosis): The Infinite's always-on passive (InnatePassive, acting through OnInnateTick). An
    /// enemy whose body comes within Range (0.5m) of its body is violently pulled into it over 'pull_seconds' (0.15s),
    /// VOIDED for 'void_seconds' (1s), then tossed out DisplaceDistance (4m) in a random direction, taking the ability's
    /// hit as it goes (its Damage, heavy, and its stun; it always connects). Unstoppable, airborne or already caught
    /// enemies are left alone.
    /// </summary>
    public class ApotheosisBehavior : ActionBehavior
    {
        public override IEnumerable<BehaviorParam> Parameters
        {
            get { yield return new BehaviorParam("pull_seconds", 0.15f); yield return new BehaviorParam("void_seconds", 1f); }
        }

        public override bool RollsToHit(ActionDefinition def) { return true; }   // its toss is a hit (always connecting)

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects) { }   // never cast: always on

        public override void OnInnateTick(BattleSimulation sim, Unit owner, ActionDefinition def)
        {
            float touch = owner.ActionStat(def, ActionStat.Range);
            foreach (var e in new List<Unit>(sim.EnemiesOf(owner)))
            {
                if (!e.IsAlive || e.HasStatus(StatusIds.Apotheosis) || sim.IsVoided(e) || e.IsUnstoppable || e.IsLifted || e.Hovering) continue;
                Vector3 d = e.Position - owner.Position; d.y = 0f;
                if (d.magnitude - owner.BodyRadius - e.BodyRadius > touch + 1e-3f) continue;
                Catch(sim, owner, def, e);
            }
        }

        private void Catch(BattleSimulation sim, Unit owner, ActionDefinition def, Unit e)
        {
            float pull = Mathf.Max(BattleSimulation.TickLength, Param(def, "pull_seconds"));
            float gone = Mathf.Max(BattleSimulation.TickLength, Param(def, "void_seconds") * owner.DurationScale(def));
            sim.ApplyStatus(e, new StatusEffect { Id = StatusIds.Apotheosis, SourceId = owner.Id, ExpiresAt = sim.Now + pull + gone + 0.3f }, owner);
            sim.PushUnit(e, owner.Position, pull, owner);   // violently pulled into it
            sim.VoidAfter(e, pull, gone, owner);
            sim.EmitFx(owner, e, "apotheosis", def, owner.Position);
            sim.After(e, pull + gone, () => Toss(sim, owner, def, e));
        }

        /// <summary>Out of the void and thrown clear, in a random direction, taking the hit.</summary>
        private static void Toss(BattleSimulation sim, Unit owner, ActionDefinition def, Unit e)
        {
            sim.EndVoid(e);
            if (!e.IsAlive) return;
            float ang = sim.RandomValue() * Mathf.PI * 2f;
            Vector3 dir = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
            Vector3 to = sim.Paths.ClampDash(e.Position, e.Position + dir * owner.ActionStat(def, ActionStat.DisplaceDistance));
            sim.ScheduleStrike(new AttackEffect
            {
                Source = owner, TargetId = e.Id, Definition = def, IgnoreRange = true, IgnoreCover = true, Unerring = true, HasPush = true, PushTo = to,
            }, 0f);
        }
    }

    /// <summary>
    /// Beckon (beckon): mark an enemy so that the next time it can act it walks toward the user (The Infinite) for
    /// 'walk_seconds' (1.5s) instead (BattleSimulation.CompelNextAct). No hit roll; fizzles if the target left range.
    /// </summary>
    public class BeckonBehavior : ActionBehavior
    {
        public override IEnumerable<BehaviorParam> Parameters { get { yield return new BehaviorParam("walk_seconds", 1.5f); } }

        public override bool RollsToHit(ActionDefinition def) { return false; }
        public override bool WeakpointsOnHit(ActionDefinition def) { return false; }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            var target = sim.GetUnit(a.TargetUnitId);
            if (target == null || !target.IsAlive || !sim.InReach(a.Actor, a.Definition, target)) return;
            sim.CompelNextAct(target, a.Actor, Param(a.Definition, "walk_seconds") * a.Actor.DurationScale(a.Definition), a.Definition);
            sim.EmitFx(a.Actor, target, "beckon", a.Definition, target.Position);
        }
    }
}
