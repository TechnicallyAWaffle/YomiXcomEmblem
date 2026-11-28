using System.Collections.Generic;
using UnityEngine;

namespace TacticsGame.Core
{
    /// <summary>
    /// A behavior that fires projectiles (BattleSimulation.Projectiles.cs). Every ability using one sets their speed, size and
    /// lifetime in its own fields (ActionDefinition.ProjectileSpeed, ProjectileAcceleration, ProjectileMaxSpeed,
    /// ProjectileRadius, ProjectileLifetime) and how they steer (ProjectileHoming, ProjectileHomingDelay, ProjectileTurnRate,
    /// ProjectileTurnGrowth); its Params say how it leaves: launch_spread (0: straight at its target;
    /// 1: any way at all) and launch_min_pitch (degrees above level it must at least leave at: 0 never toward the floor).
    /// Each projectile lands the ability's own hit (damage, impact, stun, statuses) on the first enemy it touches.
    /// Squad abilities can use it as well as enemies': subclass it, or use "volley" as it is.
    /// </summary>
    public abstract class ProjectileBehavior : ActionBehavior
    {
        public override bool IsHitscan(ActionDefinition def) { return false; }   // it flies: its projectiles meet bodies on their own

        public override IEnumerable<BehaviorParam> Parameters
        {
            get
            {
                yield return new BehaviorParam("launch_spread", 1f);
                yield return new BehaviorParam("launch_min_pitch", 5f);
                foreach (var p in MoreParameters) yield return p;
            }
        }

        /// <summary>A subclass's own numbers, after the flight's.</summary>
        protected virtual IEnumerable<BehaviorParam> MoreParameters { get { yield break; } }

        public override bool RollsToHit(ActionDefinition def) { return true; }
        public override bool ApproachesTarget(ActionDefinition def) { return false; }

        /// <summary>How 'def's projectiles fly.</summary>
        public ProjectileSpec SpecFor(ActionDefinition def)
        {
            return new ProjectileSpec
            {
                Speed = def.ProjectileSpeed, Accel = def.ProjectileAcceleration, MaxSpeed = Mathf.Max(def.ProjectileSpeed, def.ProjectileMaxSpeed),
                Homing = def.ProjectileHoming,
                HomingDelay = def.ProjectileHomingDelay, TurnRate = def.ProjectileTurnRate, TurnGrowth = def.ProjectileTurnGrowth,
                HitRadius = def.ProjectileRadius, Lifetime = def.ProjectileLifetime,
            };
        }

        /// <summary>Fire one of 'def's projectiles from 'actor' (at 'target' if given): from its chest, outward.</summary>
        public void Fire(BattleSimulation sim, Unit actor, ActionDefinition def, Unit target = null, string tag = null)
        {
            Vector3 aim = target != null ? target.Position + Vector3.up * (target.BodyHeight * 0.55f) - Chest(actor) : Forward(actor, sim);
            Vector3 dir = LaunchDirection(sim, aim, Param(def, "launch_spread"), Param(def, "launch_min_pitch"));
            Vector3 from = Chest(actor) + new Vector3(dir.x, 0f, dir.z) * actor.BodyRadius;
            sim.LaunchProjectile(actor, def, from, dir, SpecFor(def), target != null ? target.Id : null, tag);
        }

        private static Vector3 Chest(Unit u) { return u.Position + Vector3.up * (u.BodyHeight * 0.7f); }

        private static Vector3 Forward(Unit u, BattleSimulation sim)
        {
            var e = sim.NearestEnemy(u);
            return e != null ? e.Position - u.Position : Vector3.forward;
        }

        /// <summary>
        /// Which way one leaves: 'aim', turned off it at random by up to 'spread' (1: any direction at all), and never
        /// pointing lower than 'minPitch' degrees above level (picked evenly over what is left of the sky).
        /// </summary>
        public static Vector3 LaunchDirection(BattleSimulation sim, Vector3 aim, float spread, float minPitch)
        {
            if (aim.sqrMagnitude < 1e-6f) aim = Vector3.forward;
            aim.Normalize();
            float minZ = Mathf.Sin(Mathf.Clamp(minPitch, -89f, 89f) * Mathf.Deg2Rad);
            Vector3 d;
            if (spread >= 0.999f)
            {
                // Evenly over the cap of the sphere above minPitch.
                float z = Mathf.Lerp(minZ, 1f, sim.RandomValue());
                float a = sim.RandomValue() * Mathf.PI * 2f, r = Mathf.Sqrt(Mathf.Max(0f, 1f - z * z));
                d = new Vector3(Mathf.Cos(a) * r, z, Mathf.Sin(a) * r);
            }
            else
            {
                Vector3 jitter = new Vector3(sim.RandomValue() * 2f - 1f, sim.RandomValue() * 2f - 1f, sim.RandomValue() * 2f - 1f);
                d = (aim + jitter * (spread * 2f)).normalized;
                if (d.y < minZ) { d.y = minZ; d.Normalize(); }
            }
            return d;
        }

        protected void DescribeFlight(AbilityTip tip)
        {
            var def = tip.Def;
            var homing = def.ProjectileHoming;
            tip.Add("Flight", string.Format("{0:0} m/s, speeding up to {1:0}, for up to {2}", def.ProjectileSpeed, Mathf.Max(def.ProjectileSpeed, def.ProjectileMaxSpeed), AbilityTip.Secs(def.ProjectileLifetime)) +
                (homing == ProjectileHoming.ClosestEnemy ? ", homing on the nearest enemy" : homing == ProjectileHoming.Target ? ", homing on its target" : ""));
        }
    }

    /// <summary>
    /// A volley (volley: Needlestorm): as it lands, 'count' (10) projectiles leave the user one every 'every' (0.1) seconds
    /// (an emitter, so the user may move on while the rest go out). Each is the ability's hit: Damage is per projectile.
    /// </summary>
    public class VolleyBehavior : ProjectileBehavior
    {
        protected override IEnumerable<BehaviorParam> MoreParameters
        {
            get { yield return new BehaviorParam("count", 10f); yield return new BehaviorParam("every", 0.1f); }
        }

        public override AbilityRole DefaultRoles(ActionDefinition def) { return base.DefaultRoles(def) | AbilityRole.Volley; }

        public override void Describe(AbilityTip tip)
        {
            DescribeHit(tip, "Damage each", "Stun", true);
            float every = Param(tip.Def, "every");
            tip.Add("Projectiles", every <= 0f ? string.Format("{0:0}, all at once", Param(tip.Def, "count")) : string.Format("{0:0}, one every {1}", Param(tip.Def, "count"), AbilityTip.Secs(every)));
            DescribeFlight(tip);
        }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            var target = a.Definition.Targeting == TargetType.Enemy ? sim.GetUnit(a.TargetUnitId) : null;
            int count = Mathf.Max(1, Mathf.RoundToInt(Param(a.Definition, "count")));
            string tag = a.Tag;
            sim.StartEmitter(a.Actor, a.Definition, Param(a.Definition, "every"), count, 0f,
                (s, owner, def) => Fire(s, owner, def, target != null && target.IsAlive ? target : null, tag));
        }
    }

    /// <summary>
    /// A guard that streams projectiles (volley_stream: Carapace): its SelfStatuses are the guard (a shield, a burst of
    /// speed); as it lands, the user fires one projectile every 'every' (0.4) seconds for 'seconds' (3), while it does
    /// whatever else. The projectiles are its LinkedAction's (Needlestorm's: same hit and flight), or its own.
    /// </summary>
    public class VolleyStreamBehavior : ProjectileBehavior
    {
        protected override IEnumerable<BehaviorParam> MoreParameters
        {
            get { yield return new BehaviorParam("every", 0.4f); yield return new BehaviorParam("seconds", 3f); }
        }

        public override bool RollsToHit(ActionDefinition def) { return false; }

        public override AbilityRole DefaultRoles(ActionDefinition def) { return base.DefaultRoles(def) | AbilityRole.Bulwark; }

        public override void Describe(AbilityTip tip)
        {
            var src = tip.Def.LinkedAction ?? tip.Def;
            tip.Add("Projectiles", string.Format("one every {0} for {1}, {2}", AbilityTip.Secs(Param(tip.Def, "every")), AbilityTip.Secs(Param(tip.Def, "seconds")),
                src != tip.Def ? "as " + src.DisplayName + "'s" : string.Format("{0:0} damage each", tip.V(ActionStat.Damage))));
        }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            var src = a.Definition.LinkedAction ?? a.Definition;
            var shooter = ActionBehaviors.Get(src.BehaviorId) as ProjectileBehavior ?? this;
            string tag = a.Tag;
            sim.StartEmitter(a.Actor, src, Param(a.Definition, "every"), 0, Param(a.Definition, "seconds") * a.Actor.DurationScale(a.Definition),
                (s, owner, def) => shooter.Fire(s, owner, def, null, tag));
        }
    }

    /// <summary>
    /// Reel (reel: Captivate): as it lands, the target is interrupted and stunned, and the user channels (ChannelSeconds),
    /// dragging it in at 'pull_speed' (6 m/s). One that reaches the user takes the ability's hit (Damage, its Stun), which
    /// can't miss; the channel then ends. A broken channel lets go of it (it is free at once). The windup needs sight.
    /// </summary>
    public class ReelBehavior : ActionBehavior
    {
        public override IEnumerable<BehaviorParam> Parameters { get { yield return new BehaviorParam("pull_speed", 6f); } }

        public override bool RollsToHit(ActionDefinition def) { return false; }
        public override bool WeakpointsOnHit(ActionDefinition def) { return true; }
        public override bool MovesTargets(ActionDefinition def) { return true; }
        public override AbilityRole DefaultRoles(ActionDefinition def) { return base.DefaultRoles(def) | AbilityRole.Reel; }

        public override void Describe(AbilityTip tip)
        {
            tip.Add("On landing", "interrupts and stuns the target");
            tip.Add("Pull", string.Format("{0:0.#} m/s, up to {1}", Param(tip.Def, "pull_speed"), AbilityTip.Secs(tip.Def.ChannelSeconds)));
            DescribeHit(tip, "If it reaches you", "Stun", false);
        }

        public override bool OnWindupTick(BattleSimulation sim, ActionInstance a)
        {
            var target = sim.GetUnit(a.TargetUnitId);
            return target != null && target.IsAlive && sim.HasSightOf(a.Actor, a.Definition, target);
        }

        private static float Gap(Unit actor, Unit target)
        {
            Vector3 d = actor.Position - target.Position; d.y = 0f;
            return d.magnitude - (actor.BodyRadius + target.BodyRadius + 0.3f);
        }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            var actor = a.Actor;
            var target = sim.GetUnit(a.TargetUnitId);
            a.Steps = 0;
            if (target == null || !target.IsAlive || !sim.InReach(actor, a.Definition, target)) { sim.CutChannel(actor, sim.Now); return; }
            sim.InterruptUnit(target, "captivated");
            float gap = Gap(actor, target);
            float speed = Mathf.Max(0.1f, Param(a.Definition, "pull_speed"));
            float channel = Mathf.Max(BattleSimulation.TickLength, a.Definition.ChannelSeconds);
            float dist = Mathf.Clamp(gap, 0f, speed * channel);
            float seconds = Mathf.Max(BattleSimulation.TickLength, dist / speed);
            sim.StunUnit(target, seconds + BattleSimulation.TickLength, actor);   // held while it is dragged
            if (dist > 0.05f)
            {
                Vector3 dir = actor.Position - target.Position; dir.y = 0f;
                sim.PushUnit(target, target.Position + dir.normalized * dist, seconds, actor);
            }
            a.Steps = 1;
            a.NextStepAt = sim.Now + seconds;
        }

        public override bool OnChannelTick(BattleSimulation sim, ActionInstance a)
        {
            if (a.Steps == 0) return false;
            var target = sim.GetUnit(a.TargetUnitId);
            if (target == null || !target.IsAlive) return false;
            if (a.Steps == 1 && sim.Now + 1e-4f >= a.NextStepAt)
            {
                a.Steps = 2;
                if (Gap(a.Actor, target) <= 0.35f)
                {
                    sim.StrikeNow(new AttackEffect { Source = a.Actor, TargetId = target.Id, Definition = a.Definition, DamageMultiplier = a.DamageMultiplier, Tag = a.Tag,
                                                     Unerring = true, IgnoreRange = true, IgnoreCover = true });
                    sim.CutChannel(a.Actor, sim.Now);   // it reached the user: over, recovery after
                }
            }
            return true;
        }

        public override void OnChannelEnd(BattleSimulation sim, ActionInstance a, bool broken)
        {
            if (broken && a.Steps == 1)
            {
                var target = sim.GetUnit(a.TargetUnitId);
                sim.StopGlide(target);   // let go: it stops where it is
                sim.ReleaseStun(target, a.NextStepAt + BattleSimulation.TickLength);
            }
            a.Steps = 0;
        }
    }

    /// <summary>
    /// A pack leader's howl (howl: Howling Lament): as it lands, every unit within AreaRadius of the user gets its
    /// AuraStatuses (each one's AppliesTo picks allies or enemies; a slow on the enemies), and its target unit gets its
    /// TargetStatusesOnHit (FATED). It is the pack's signal (BattleSimulation.Signal): Howlers set on its target.
    /// No hit roll; the howl isn't stopped by walls.
    /// </summary>
    public class HowlBehavior : ActionBehavior
    {
        public override bool RollsToHit(ActionDefinition def) { return false; }
        public override bool WeakpointsOnHit(ActionDefinition def) { return false; }
        public override AbilityRole DefaultRoles(ActionDefinition def) { return base.DefaultRoles(def) | AbilityRole.Signal; }

        public override void Describe(AbilityTip tip)
        {
            tip.Add("Howl", "everyone within " + AbilityTip.Metres(tip.V(ActionStat.AreaRadius)));
            tip.TargetStatusesTitle = "TARGET";
        }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            var actor = a.Actor;
            var def = a.Definition;
            float radius = a.Stat(ActionStat.AreaRadius);
            float scale = actor.DurationScale(def);
            sim.EmitFx(actor, sim.GetUnit(a.TargetUnitId), "howl", def, actor.Position, radius);   // the wave, from the Howler (CombatVfx.PlayHowl)
            foreach (var u in sim.State.Units)
            {
                if (!u.IsAlive || u == actor) continue;
                Vector3 d = u.Position - actor.Position; d.y = 0f;
                if (d.magnitude - u.BodyRadius > radius) continue;
                foreach (var spec in def.AuraStatuses)
                    if (spec != null && spec.AppliesToUnit(actor, u))
                        effects.Add(new ApplyStatusEffect { Source = actor, TargetId = u.Id, Status = spec.Create(sim.Now, actor.Id, null, scale) });
            }
            var target = sim.GetUnit(a.TargetUnitId);
            if (target == null || !target.IsAlive) return;
            foreach (var spec in def.TargetStatusesOnHit)
                if (spec != null && spec.AppliesToUnit(actor, target))
                    effects.Add(new ApplyStatusEffect { Source = actor, TargetId = target.Id, Status = spec.Create(sim.Now, actor.Id, def.LinkedAction, scale) });
            sim.Signal(actor, target);
        }
    }
}
