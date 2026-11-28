using System.Collections.Generic;
using UnityEngine;

namespace TacticsGame.Core
{
    // The Forsaken's second set of abilities (SquadAssets.CreateForsaken). Pairs swap with the form (DivergedVersion);
    // the others are one form's only (ActionDefinition.Form). The rules that run between casts (Berserk's rampages,
    // Madness Spills Forth's strikes, Miserable Cacophony's auto-cast, Wretched Visions' charge) are in
    // BattleSimulation.TickPsyche, called from TickForsaken.

    /// <summary>
    /// A spread of homing shots (burst_volley: Controlled Burst): as it lands, 'count' (5) of its projectiles leave at once,
    /// each turned off the aimed direction by a random 0..'spread' (60) degrees in any direction (never into the ground),
    /// then homing as the definition says (the nearest enemy). Damage and stun are per projectile.
    /// </summary>
    public class BurstVolleyBehavior : ProjectileBehavior
    {
        public override IEnumerable<BehaviorParam> Parameters
        {
            get { yield return new BehaviorParam("count", 5f); yield return new BehaviorParam("spread", 60f); }
        }

        public override AbilityRole DefaultRoles(ActionDefinition def) { return base.DefaultRoles(def) | AbilityRole.Strike; }

        public override void Describe(AbilityTip tip)
        {
            DescribeHit(tip, "Damage per shot", "Stun per shot", true);
            tip.Add("Shots", string.Format("{0:0}, up to {1:0} degrees off your aim, homing", Param(tip.Def, "count"), Param(tip.Def, "spread")));
            DescribeFlight(tip);
        }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            var u = a.Actor; var def = a.Definition;
            var target = sim.GetUnit(a.TargetUnitId);
            Vector3 aim = target != null && target != u && target.IsAlive ? target.Position : a.TargetPos;
            Vector3 dir = aim - u.Position; dir.y = 0f;
            dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.forward;
            Vector3 from = u.Position + Vector3.up * (u.BodyHeight * 0.7f) + dir * u.BodyRadius;
            int count = Mathf.Max(1, Mathf.RoundToInt(Param(def, "count")));
            float spread = Mathf.Clamp(Param(def, "spread"), 0f, 89f) * Mathf.Deg2Rad;
            for (int i = 0; i < count; i++)
                sim.LaunchProjectile(u, def, from, Deviate(dir, sim.RandomValue() * spread, sim.RandomValue() * Mathf.PI * 2f), SpecFor(def), null, a.Tag);
        }

        /// <summary>'dir' (level) turned 'angle' radians off itself toward 'around' (0: to its right, PI/2: up), never below level.</summary>
        public static Vector3 Deviate(Vector3 dir, float angle, float around)
        {
            Vector3 right = new Vector3(dir.z, 0f, -dir.x);
            Vector3 off = right * Mathf.Cos(around) + Vector3.up * Mathf.Abs(Mathf.Sin(around));   // the lower half folded up: into the air, not the ground
            return (dir * Mathf.Cos(angle) + off * Mathf.Sin(angle)).normalized;
        }
    }

    /// <summary>
    /// A channelled barrage (channel_barrage): as it lands the user channels (ChannelSeconds), firing one of its projectiles
    /// every 'every' (0.2) seconds. 'aim' 0 (Porcupine): each leaves in a random direction around the user and homes as the
    /// definition says; 'aim' 1 (Agitation): each flies straight at where its target unit stands as it is fired, not
    /// leading it (the channel ends if the target is lost). Damage and stun are per projectile.
    /// </summary>
    public class ChannelBarrageBehavior : ProjectileBehavior
    {
        public override IEnumerable<BehaviorParam> Parameters
        {
            get { yield return new BehaviorParam("every", 0.2f); yield return new BehaviorParam("aim", 0f); }
        }

        public override AbilityRole DefaultRoles(ActionDefinition def) { return base.DefaultRoles(def) | AbilityRole.Strike; }

        public override void Describe(AbilityTip tip)
        {
            DescribeHit(tip, "Damage per shot", "Stun per shot", true);
            tip.Add("Channel", string.Format("up to {0}: a shot every {1}{2}", AbilityTip.Secs(tip.Def.ChannelSeconds), AbilityTip.Secs(Param(tip.Def, "every")),
                Param(tip.Def, "aim") > 0.5f ? ", straight at the target" : ", homing"));
            DescribeFlight(tip);
        }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            a.NextPulseAt = sim.Now;   // the first shot as the channel starts
        }

        public override bool OnChannelTick(BattleSimulation sim, ActionInstance a)
        {
            var u = a.Actor; var def = a.Definition;
            bool atTarget = Param(def, "aim") > 0.5f;
            var target = sim.GetUnit(a.TargetUnitId);
            if (atTarget && (target == null || !target.IsAlive || target == u)) return false;
            if (sim.Now + 1e-4f < a.NextPulseAt) return true;
            a.NextPulseAt += Mathf.Max(BattleSimulation.TickLength, Param(def, "every"));
            Vector3 dir;
            if (atTarget) { dir = target.Position - u.Position; dir.y = 0f; }
            else { float t = sim.RandomValue() * Mathf.PI * 2f; dir = new Vector3(Mathf.Cos(t), 0f, Mathf.Sin(t)); }
            dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.forward;
            Vector3 from = u.Position + Vector3.up * (u.BodyHeight * 0.7f) + dir * u.BodyRadius;
            sim.LaunchProjectile(u, def, from, dir, SpecFor(def), atTarget ? target.Id : null, a.Tag);
            return true;
        }
    }

    /// <summary>
    /// A charged blast down a line (howling_end: Howling End): released toward a point, it hits every enemy on the line
    /// (LineWidth wide) and knocks each one back DisplaceDistance. Its range, damage, stun and knockback all grow with the
    /// charge, from x1 to x'max_scale' (3: +200%) at full charge.
    /// </summary>
    public class HowlingEndBehavior : ActionBehavior
    {
        public override IEnumerable<BehaviorParam> Parameters { get { yield return new BehaviorParam("max_scale", 3f); } }

        public override AbilityRole DefaultRoles(ActionDefinition def) { return base.DefaultRoles(def) | AbilityRole.Strike; }
        public override bool RollsToHit(ActionDefinition def) { return true; }
        public override bool ApproachesTarget(ActionDefinition def) { return false; }

        public override void Describe(AbilityTip tip)
        {
            base.Describe(tip);
            tip.Add("Knockback", AbilityTip.Metres(tip.V(ActionStat.DisplaceDistance)));
            tip.Add("Full charge", string.Format("x{0:0.#} range, damage, stun and knockback", Param(tip.Def, "max_scale")));
        }

        public static float Scale(ActionInstance a) { return Mathf.Lerp(1f, Mathf.Max(1f, a.Behavior.Param(a.Definition, "max_scale")), Mathf.Clamp01(a.Charge)); }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            var u = a.Actor; var def = a.Definition;
            float scale = Scale(a);
            Vector3 dir = a.TargetPos - u.Position; dir.y = 0f;
            dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.forward;
            float length = a.Stat(ActionStat.Range) * scale, half = def.LineWidth * 0.5f, push = a.Stat(ActionStat.DisplaceDistance) * scale;
            foreach (var t in sim.State.Units)
            {
                if (!t.IsAlive || t == u || t.Team == u.Team || !sim.IsTargetableBy(t, u)) continue;
                Vector3 v = t.Position - u.Position; v.y = 0f;
                float along = Vector3.Dot(v, dir);
                if (along < -t.BodyRadius || along > length + t.BodyRadius) continue;
                if ((v - dir * along).magnitude > half + t.BodyRadius) continue;
                var atk = new AttackEffect { Source = u, TargetId = t.Id, Definition = def, Tag = a.Tag, DamageMultiplier = a.DamageMultiplier * scale, StunMultiplier = scale, IgnoreRange = true };
                if (push > 0.01f) { atk.HasPush = true; atk.PushTo = sim.Paths.ClampDash(t.Position, t.Position + dir * push); }
                effects.Add(atk);
            }
        }
    }

    /// <summary>
    /// Berserk (berserk): UNSTOPPABLE through the windup (SelfStatusesDuringWindup); as it lands the user is set on 'casts'
    /// (5) uses of its LinkedAction (Blind Rampage), each aimed toward where Berserk was cast from, turned a random -90..90
    /// degrees. They follow one another as soon as it is free to act (BattleSimulation.TickPsyche): a cast cut short
    /// still counts, and one it can't start yet (stunned) waits.
    /// </summary>
    public class BerserkBehavior : ActionBehavior
    {
        public override IEnumerable<BehaviorParam> Parameters { get { yield return new BehaviorParam("casts", 5f); } }

        public override AbilityRole DefaultRoles(ActionDefinition def) { return base.DefaultRoles(def) | AbilityRole.Strike; }
        public override bool RollsToHit(ActionDefinition def) { return false; }
        public override bool ApproachesTarget(ActionDefinition def) { return false; }
        public override IEnumerable<ActionDefinition> ActsThrough(ActionDefinition def) { if (def.LinkedAction != null) yield return def.LinkedAction; }

        public override void Describe(AbilityTip tip)
        {
            tip.Add("Then", string.Format("{0:0}x {1} within {2} of here, at random", Param(tip.Def, "casts"),
                tip.Def.LinkedAction != null ? tip.Def.LinkedAction.DisplayName : "a rampage", AbilityTip.Metres(tip.Def.AreaRadius)));
        }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            var u = a.Actor;
            if (a.Definition.LinkedAction == null) return;
            u.BerserkLeft = Mathf.Max(0, Mathf.RoundToInt(Param(a.Definition, "casts")));
            u.BerserkOrigin = u.Position;
            u.BerserkAction = a.Definition.LinkedAction;
            u.BerserkRadius = a.Definition.AreaRadius;
        }
    }

    /// <summary>
    /// Blind Rampage (blind_rampage): Rampage's dash down the aimed line, but blind: it strikes everyone on the line, allies
    /// as well as enemies, and each unit struck gives the user a HYSTERIA stack.
    /// </summary>
    public class BlindRampageBehavior : GougeBehavior
    {
        public override void Describe(AbilityTip tip)
        {
            base.Describe(tip);
            tip.Add("Strikes", "allies and enemies alike; +1 HYSTERIA each");
        }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            var actor = a.Actor; var def = a.Definition;
            if (actor.IsRooted) return;
            Vector3 dir = a.TargetPos - actor.Position; dir.y = 0f;
            dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.forward;
            float range = a.Stat(ActionStat.Range), half = def.LineWidth * 0.5f;
            Vector3 end = sim.PhaseLanding(actor.Position, actor.Position + dir * range);
            foreach (var t in sim.State.Units)
            {
                if (!t.IsAlive || t == actor) continue;
                Vector3 v = t.Position - actor.Position; v.y = 0f;
                float along = Vector3.Dot(v, dir);
                if (along < -t.BodyRadius || along > range + t.BodyRadius || (v - dir * along).magnitude > half + t.BodyRadius) continue;
                effects.Add(new AttackEffect
                {
                    Source = actor, TargetId = t.Id, Definition = def, DamageMultiplier = a.DamageMultiplier, Tag = a.Tag, IgnoreRange = true, IgnoreCover = true,
                    AfterHit = (s, hit) => s.AddHysteria(actor, 1),
                });
            }
            sim.SlideUnitTo(actor, end, BattleSimulation.DashDuration, end);
        }
    }

    /// <summary>
    /// A channel on the user (withdrawal: Withdrawal): it channels (ChannelSeconds) with 'weakpoints' (4) of its own
    /// weakpoints open, the first behind it, for as long as the channel lasts; its SelfStatuses (the strain it sheds) last
    /// the channel too, and come off when it ends however it ends.
    /// </summary>
    public class WithdrawalBehavior : ActionBehavior
    {
        public override IEnumerable<BehaviorParam> Parameters { get { yield return new BehaviorParam("weakpoints", 4f); } }

        public override bool RollsToHit(ActionDefinition def) { return false; }
        public override bool ApproachesTarget(ActionDefinition def) { return false; }

        public override void Describe(AbilityTip tip)
        {
            base.Describe(tip);
            tip.Add("Channel", string.Format("up to {0}, {1:0} weakpoints open on you; damage breaks it", AbilityTip.Secs(tip.Def.ChannelSeconds), Param(tip.Def, "weakpoints")));
        }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            var u = a.Actor;
            int n = Mathf.RoundToInt(Param(a.Definition, "weakpoints"));
            if (n > 0) sim.AddWeakpoints(u, n, sim.BehindOf(a), a.Definition.ChannelSeconds, u, a);   // bound to the channel (TickWeakpoints)
        }

        public override void OnChannelEnd(BattleSimulation sim, ActionInstance a, bool broken) { ChannelSelf.EndSelfStatuses(sim, a); }
    }

    /// <summary>The SelfStatuses a channel holds up come off when it ends (Withdrawal, Paranoia).</summary>
    public static class ChannelSelf
    {
        public static void EndSelfStatuses(BattleSimulation sim, ActionInstance a)
        {
            foreach (var spec in a.Definition.SelfStatuses)
            {
                if (spec == null) continue;
                var s = a.Actor.GetStatus(spec.Id);
                if (s != null && s.SourceId == a.Actor.Id) sim.RemoveStatus(a.Actor, spec.Id);
            }
        }
    }

    /// <summary>
    /// Paranoia (strain_siphon): a channel on an enemy (ChanneledStatus: its TargetStatusesOnHit, the strain it gains and
    /// CRIPPLED, last the channel) while the user's SelfStatuses (the strain it sheds) last it too.
    /// </summary>
    public class StrainSiphonBehavior : ChanneledStatusBehavior
    {
        public override void OnChannelEnd(BattleSimulation sim, ActionInstance a, bool broken)
        {
            base.OnChannelEnd(sim, a, broken);
            ChannelSelf.EndSelfStatuses(sim, a);
        }
    }

    /// <summary>
    /// Madness Spills Forth (madness_spills): 'hysteria' (5) HYSTERIA as the windup starts; as it lands, the user dashes
    /// at the nearest unit within Range, ally or enemy, and sets on it: 'strikes' (3) basic attacks with its SelfStatuses
    /// (the burst of action speed), chasing it as it goes (BattleSimulation.TickPsyche), until they are done or it is lost.
    /// </summary>
    public class MadnessSpillsBehavior : ActionBehavior
    {
        public override IEnumerable<BehaviorParam> Parameters
        {
            get { yield return new BehaviorParam("hysteria", 5f); yield return new BehaviorParam("strikes", 3f); }
        }

        public override AbilityRole DefaultRoles(ActionDefinition def) { return base.DefaultRoles(def) | AbilityRole.Gapcloser | AbilityRole.Strike; }
        public override bool RollsToHit(ActionDefinition def) { return false; }
        public override bool ApproachesTarget(ActionDefinition def) { return false; }
        public override bool MovesUser { get { return true; } }
        public override bool Dashes { get { return true; } }

        public override void Describe(AbilityTip tip)
        {
            tip.Add("Windup", string.Format("+{0:0} HYSTERIA", Param(tip.Def, "hysteria")));
            tip.Add("Then", string.Format("dash at the nearest unit within {0}, ally or enemy, and strike it {1:0} times", AbilityTip.Metres(tip.V(ActionStat.Range)), Param(tip.Def, "strikes")));
        }

        public override bool CanStart(BattleSimulation sim, Unit actor, UnitCommand cmd, out string reason)
        {
            reason = actor.IsRooted ? "immobilized" : null;
            return reason == null;
        }

        public override void OnWindupStart(BattleSimulation sim, ActionInstance a)
        {
            sim.AddHysteria(a.Actor, Mathf.RoundToInt(Param(a.Definition, "hysteria")));
        }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            var u = a.Actor;
            Unit nearest = null; float best = a.Stat(ActionStat.Range);
            foreach (var t in sim.State.Units)
            {
                if (!t.IsAlive || t == u || !sim.IsTargetableBy(t, u) || t.IsMakina) continue;
                float d = Vector3.Distance(u.Position, t.Position);
                if (d <= best) { best = d; nearest = t; }
            }
            if (nearest == null) return;
            if (!u.IsRooted) sim.PhaseDashBeside(u, nearest, BattleSimulation.DashDuration);
            u.FrenzyTargetId = nearest.Id;
            u.FrenzyStrikes = Mathf.Max(0, Mathf.RoundToInt(Param(a.Definition, "strikes")));
            u.FrenzyCounted = null;
        }
    }

    /// <summary>
    /// Feast (feast): a bite (the basic attack's hit) dealing its Damage plus 'missing_share' (0.25) of the target's missing
    /// health. A bite that kills heals the user 'heal_on_kill' (35) and gives it 'hysteria_on_kill' (5) HYSTERIA.
    /// </summary>
    public class FeastBehavior : BasicAttackBehavior
    {
        public override IEnumerable<BehaviorParam> Parameters
        {
            get { yield return new BehaviorParam("missing_share", 0.25f); yield return new BehaviorParam("heal_on_kill", 35f); yield return new BehaviorParam("hysteria_on_kill", 5f); }
        }

        public override void Describe(AbilityTip tip)
        {
            base.Describe(tip);
            tip.Add("Plus", string.Format("{0:0}% of the target's missing health", Param(tip.Def, "missing_share") * 100f));
            tip.Add("On a kill", string.Format("heal {0:0}, +{1:0} HYSTERIA", Param(tip.Def, "heal_on_kill"), Param(tip.Def, "hysteria_on_kill")));
        }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            int first = effects.Count;
            base.OnResolve(sim, a, effects);
            var actor = a.Actor; var def = a.Definition;
            float baseDamage = Mathf.Max(0.01f, a.Stat(ActionStat.Damage));
            for (int i = first; i < effects.Count; i++)
            {
                var atk = effects[i] as AttackEffect;
                var t = atk != null ? sim.GetUnit(atk.TargetId) : null;
                if (t == null) continue;
                float bonus = Param(def, "missing_share") * Mathf.Max(0f, t.MaxHealth - t.Health);
                atk.DamageMultiplier *= (baseDamage + bonus) / baseDamage;
                var prev = atk.AfterHit;
                atk.AfterHit = (s, hit) =>
                {
                    if (prev != null) prev(s, hit);
                    if (hit.Target == null || hit.Target.IsAlive) return;
                    s.RestoreHealth(actor, Param(def, "heal_on_kill"));
                    s.AddHysteria(actor, Mathf.RoundToInt(Param(def, "hysteria_on_kill")));
                };
            }
        }
    }

    /// <summary>
    /// Shatter The Foundations (shatter_foundations): a heavy blow (the basic attack's hit) that gives the user 'hysteria'
    /// (3) HYSTERIA; Transcended, it deals 'transcend_damage' (30) more and gives 'transcend_hysteria' (6) instead.
    /// </summary>
    public class ShatterFoundationsBehavior : BasicAttackBehavior
    {
        public override IEnumerable<BehaviorParam> Parameters
        {
            get { yield return new BehaviorParam("hysteria", 3f); yield return new BehaviorParam("transcend_damage", 30f); yield return new BehaviorParam("transcend_hysteria", 6f); }
        }

        public override void Describe(AbilityTip tip)
        {
            base.Describe(tip);
            tip.Add("You", string.Format("+{0:0} HYSTERIA", Param(tip.Def, "hysteria")));
            tip.Add("Transcended", string.Format("+{0:0} damage, +{1:0} HYSTERIA", Param(tip.Def, "transcend_damage"), Param(tip.Def, "transcend_hysteria")));
        }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            int first = effects.Count;
            base.OnResolve(sim, a, effects);
            var def = a.Definition;
            if (a.Transcended)
            {
                float baseDamage = Mathf.Max(0.01f, a.Stat(ActionStat.Damage));
                for (int i = first; i < effects.Count; i++)
                {
                    var atk = effects[i] as AttackEffect;
                    if (atk != null) atk.DamageMultiplier *= (baseDamage + Param(def, "transcend_damage")) / baseDamage;
                }
            }
            sim.AddHysteria(a.Actor, Mathf.RoundToInt(Param(def, a.Transcended ? "transcend_hysteria" : "hysteria")));
        }
    }

    /// <summary>
    /// Miserable Cacophony (cacophony): heals the user its Heal and puts 'ally_strain' (15) strain on every ally within
    /// AreaRadius (its own strain is its StrainOnSelf). It casts itself when the user takes damage and it is off cooldown,
    /// as soon as the user is free to act (BattleSimulation.TickPsyche).
    /// </summary>
    public class CacophonyBehavior : ActionBehavior
    {
        public override IEnumerable<BehaviorParam> Parameters { get { yield return new BehaviorParam("ally_strain", 15f); } }

        public override bool RollsToHit(ActionDefinition def) { return false; }
        public override bool ApproachesTarget(ActionDefinition def) { return false; }

        public override void Describe(AbilityTip tip)
        {
            tip.Add("Heal", tip.V(ActionStat.Heal).ToString("0"));
            tip.Add("Allies within " + AbilityTip.Metres(tip.V(ActionStat.AreaRadius)), string.Format("+{0:0} strain", Param(tip.Def, "ally_strain")));
            tip.Add("Casts itself", "when you take damage, if off cooldown");
        }

        public override void Attach(BattleSimulation sim)
        {
            sim.Triggers.Subscribe(TriggerType.DamageTaken, ctx =>
            {
                var u = ctx.Subject;
                if (u != null && u.IsAlive && ctx.Amount > 0f && u.Actions.Exists(x => x != null && x.BehaviorId == "cacophony")) u.CacophonyPending = true;
            });
        }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            var u = a.Actor; var def = a.Definition;
            float heal = a.Stat(ActionStat.Heal);
            if (heal > 0f) sim.RestoreHealth(u, heal);
            float radius = a.Stat(ActionStat.AreaRadius), strain = Param(def, "ally_strain");
            foreach (var t in sim.State.Units)
                if (t.IsAlive && t != u && t.Team == u.Team && Vector3.Distance(t.Position, u.Position) <= radius) sim.AddStrain(t, strain);
        }
    }

    /// <summary>
    /// Wretched Visions (wretched_visions): a charge (ChargeStrainPerSecond: strain building) that gives the user
    /// 'hysteria_per_second' (3) HYSTERIA while it charges (harmless until Diverged). Released, a shockwave of AreaRadius
    /// around the user stuns every enemy in it (AreaAttack). It releases itself at MaxChargeSeconds, and at maximum strain,
    /// after which NEUROSIS follows (BattleSimulation.TickPsyche).
    /// </summary>
    public class WretchedVisionsBehavior : AreaAttackBehavior
    {
        public override IEnumerable<BehaviorParam> Parameters { get { yield return new BehaviorParam("hysteria_per_second", 3f); } }

        public override void Describe(AbilityTip tip)
        {
            base.Describe(tip);
            tip.Add("Charging", string.Format("+{0:0} strain and +{1:0} HYSTERIA a second, up to {2}; at maximum strain it releases", tip.Def.ChargeStrainPerSecond, Param(tip.Def, "hysteria_per_second"), AbilityTip.Secs(tip.Def.MaxChargeSeconds)));
        }
    }

    public partial class BattleSimulation
    {
        /// <summary>The side behind 'a's user as it acts (the first of its own weakpoints opens there).</summary>
        public Vector3 BehindOf(ActionInstance a) { return -FacingFor(a); }

        /// <summary>Heal 'u' by 'amount' (up to its max), shown as a Heal.</summary>
        public void RestoreHealth(Unit u, float amount)
        {
            if (u == null || !u.IsAlive || amount <= 0f) return;
            float heal = Mathf.Min(amount, u.MaxHealth - u.Health);
            if (heal <= 1e-4f) return;
            u.Health += heal;
            Emit(EventType.Heal, u.Id, u.Id, heal);
        }

        /// <summary>
        /// The Forsaken's abilities that go on between casts, each tick: Wretched Visions' charge (its HYSTERIA, releasing at
        /// its limit or maximum strain), Berserk's rampages, Madness Spills Forth's strikes, Miserable Cacophony's auto-cast.
        /// Returns true if it released Wretched Visions this tick (NEUROSIS waits for the shockwave to land).
        /// </summary>
        private bool TickPsyche(Unit u)
        {
            // Wretched Visions charging.
            if (u.Phase == UnitPhase.Charging && u.ActiveAction != null && u.ActiveAction.Behavior is WretchedVisionsBehavior)
            {
                var a = u.ActiveAction;
                u.PsycheHysteria += a.Behavior.Param(a.Definition, "hysteria_per_second") * TickLength;
                if (u.PsycheHysteria >= 1f) { int n = Mathf.FloorToInt(u.PsycheHysteria); u.PsycheHysteria -= n; AddHysteria(u, n); }
                bool full = Now - a.StartedAt >= a.Definition.MaxChargeSeconds - 1e-4f;
                if (full || u.Strain >= u.MaxStrain - 1e-3f)
                {
                    u.PsycheHysteria = 0f;
                    string why;
                    ReleaseCharge(u, UnitCommand.UseAction(u.Id, a.Definition, u.Id), out why);
                    return true;
                }
                return false;
            }
            if (u.ActiveAction != null && u.ActiveAction.Behavior is WretchedVisionsBehavior) return true;   // the shockwave is about to land

            // Berserk: the next rampage as soon as it is free (a cast cut short counts; Composed again, the rest are off).
            if (u.BerserkLeft > 0)
            {
                if (!u.HasStatus(StatusIds.Diverged) || u.BerserkAction == null) u.BerserkLeft = 0;
                else if (FreeToAct(u))
                {
                    Vector3 to = u.BerserkOrigin - u.Position; to.y = 0f;
                    float turn = (RandomValue() * 2f - 1f) * Mathf.PI * 0.5f;
                    float baseAngle = to.sqrMagnitude > 0.25f ? Mathf.Atan2(to.z, to.x) : RandomValue() * Mathf.PI * 2f;
                    Vector3 dir = new Vector3(Mathf.Cos(baseAngle + turn), 0f, Mathf.Sin(baseAngle + turn));
                    var rampage = u.BerserkAction;
                    u.BerserkLeft--;
                    ForceStart(u, UnitCommand.UseActionAt(u.Id, rampage, u.Position + dir * u.ActionStat(rampage, ActionStat.Range)));
                    return false;
                }
            }

            // Madness Spills Forth: strike the target with the basic attack until the strikes are done or it is lost.
            if (u.FrenzyStrikes > 0 || u.FrenzyTargetId != null)
            {
                var t = GetUnit(u.FrenzyTargetId);
                if (u.Phase == UnitPhase.Windup && u.ActiveAction != null && u.ActiveAction.Definition.Id == u.BasicAttackId && u.ActiveAction != u.FrenzyCounted)
                {
                    u.FrenzyCounted = u.ActiveAction;
                    u.FrenzyStrikes--;
                }
                bool lost = t == null || !t.IsAlive || !IsTargetableBy(t, u) || !u.HasStatus(StatusIds.Diverged);
                bool striking = u.ActiveAction != null && u.ActiveAction == u.FrenzyCounted;
                if (lost || (u.FrenzyStrikes <= 0 && !striking)) EndFrenzy(u);
                else if (u.FrenzyStrikes > 0 && u.Phase == UnitPhase.Ready && u.HeldById == null && !u.IsLifted)
                {
                    var basic = u.Actions.Find(x => x != null && x.Id == u.BasicAttackId);
                    string why;
                    if (basic != null) { ClearQueued(u, null); queues.Remove(u.Id); TryStartCommand(u, UnitCommand.UseAction(u.Id, basic, t.Id), out why); }
                }
            }

            // Miserable Cacophony: cast for it once it is free, if still off cooldown.
            if (u.CacophonyPending)
            {
                var cac = u.Actions.Find(x => x != null && x.BehaviorId == "cacophony");
                if (cac == null || !FormAllows(u, cac) || u.CooldownReadyAt.ContainsKey(cac.Id) && u.CooldownReadyAt[cac.Id] > Now + 1e-4f) u.CacophonyPending = false;
                else if (FreeToAct(u) && u.FrenzyStrikes <= 0 && u.BerserkLeft <= 0)
                {
                    u.CacophonyPending = false;
                    ForceStart(u, UnitCommand.UseAction(u.Id, cac, u.Id));
                }
            }
            return false;
        }

        private void EndFrenzy(Unit u)
        {
            u.FrenzyStrikes = 0; u.FrenzyTargetId = null; u.FrenzyCounted = null;
            foreach (var s in u.Statuses.ToArray()) if (s.Id == StatusIds.Frenzied) RemoveStatus(u, s.Id);
        }

        /// <summary>Does 'u's form let it use 'def' (ActionDefinition.Form)?</summary>
        private static bool FormAllows(Unit u, ActionDefinition def)
        {
            bool diverged = u.HasStatus(StatusIds.Diverged);
            return def.Form == FormLock.Any || (def.Form == FormLock.Diverged) == diverged;
        }

        /// <summary>Start 'cmd' for 'u' at once, as its own rules cast it (a walk is cut short; queued orders dropped).</summary>
        private void ForceStart(Unit u, UnitCommand cmd)
        {
            if (u.Phase == UnitPhase.Moving) EndMove(u, false);
            ClearChase(u);
            ClearQueued(u, null);
            queues.Remove(u.Id);
            StartAction(u, cmd, ActionBehaviors.Get(cmd.Action.BehaviorId));
        }
    }
}
