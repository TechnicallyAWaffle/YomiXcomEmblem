using System.Collections.Generic;
using UnityEngine;

namespace TacticsGame.Core
{
    /// <summary>
    /// The Forsaken: a class with two forms. Its Strain works as Strain in every way, except that it never Unravels: when it reaches maximum, Entropy Beast interrupts what the Forsaken is doing
    /// and casts its DivergeAction (NEUROSIS: UNSTOPPABLE over the windup, then Diverged). Diverged (StatusIds.Diverged,
    /// NEUROSIS's SelfStatuses: faster, more Block, no Dash or Block), its Entropy drains 'drain' (15) a second and it gains
    /// HYSTERIA every 'hysteria_every' (1) second; at 'hysteria_limit' (20) stacks it drains 'frenzied_drain' (30) a second
    /// instead, and as soon as it can act its InsanityAction is cast (SHATTERED MIND: UNSTOPPABLE over the windup, then
    /// INSANE until it leaves the form: BecomeInsane). At 0 Entropy its RegressAction is cast (REGRESSION:
    /// UNSTOPPABLE over the windup, then Composed). Each ability with a DivergedVersion is that one while Diverged; the
    /// numbers above are NEUROSIS's Params.
    /// </summary>
    public static class ForsakenRules
    {
        /// <summary>The Forsaken's innate passive.</summary>
        public const string EntropyBeast = "entropy_beast";

        /// <summary>
        /// The Forsaken can always Block and Dash (Transcended too), whatever its Entropy: a cost that would take it past
        /// maximum just leaves it at maximum (AddStrain stops there), and NEUROSIS follows as usual.
        /// </summary>
        public static bool CapsEntropy(Unit u, ActionDefinition def)
        {
            return u != null && def != null && (def.BehaviorId == "block" || def.BehaviorId == "dash") && u.Passives.Contains(EntropyBeast);
        }
    }

    /// <summary>NEUROSIS / REGRESSION (forsaken_form): the change of form, as it lands ('diverge' 1: to Diverged, 0: to Composed).</summary>
    public class FormShiftBehavior : ActionBehavior
    {
        public override IEnumerable<BehaviorParam> Parameters
        {
            get
            {
                yield return new BehaviorParam("diverge", 1f);
                yield return new BehaviorParam("drain", 15f);
                yield return new BehaviorParam("frenzied_drain", 30f);
                yield return new BehaviorParam("hysteria_every", 1f);
                yield return new BehaviorParam("hysteria_limit", 20f);
                yield return new BehaviorParam("hysteria_damage", 0.5f);   // per stack per second, straight off its health
            }
        }

        public override bool RollsToHit(ActionDefinition def) { return false; }
        public override bool ApproachesTarget(ActionDefinition def) { return false; }

        public override void Describe(AbilityTip tip)
        {
            bool diverge = Param(tip.Def, "diverge") > 0.5f;
            tip.Add("Form", diverge ? "Diverged" : "Composed");
            if (diverge)
            {
                tip.Add("Strain", string.Format("-{0:0}/s; -{1:0}/s at {2:0} HYSTERIA", Param(tip.Def, "drain"), Param(tip.Def, "frenzied_drain"), Param(tip.Def, "hysteria_limit")));
                tip.Add("HYSTERIA", "a stack every " + AbilityTip.Secs(Param(tip.Def, "hysteria_every")) + "; at the limit, INSANITY");
                if (Param(tip.Def, "hysteria_damage") > 0f) tip.Add("HYSTERIA damage", string.Format("{0:0.#}/s per stack, unblockable", Param(tip.Def, "hysteria_damage")));
            }
        }

        public override void OnWindupStart(BattleSimulation sim, ActionInstance a)
        {
            sim.EmitFx(a.Actor, null, Param(a.Definition, "diverge") > 0.5f ? "neurosis" : "regression", a.Definition, a.Actor.Position, a.ResolveAt - sim.Now);
        }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            sim.SetForm(a.Actor, Param(a.Definition, "diverge") > 0.5f);
        }

        public float Drain(ActionDefinition def, bool frenzied) { return Param(def, frenzied ? "frenzied_drain" : "drain"); }
        public float HysteriaEvery(ActionDefinition def) { return Mathf.Max(0.05f, Param(def, "hysteria_every")); }
        public int HysteriaLimit(ActionDefinition def) { return Mathf.Max(1, Mathf.RoundToInt(Param(def, "hysteria_limit"))); }
        public float HysteriaDamage(ActionDefinition def) { return Mathf.Max(0f, Param(def, "hysteria_damage")); }
    }

    /// <summary>SHATTERED MIND (shattered_mind): cast for the Forsaken once its HYSTERIA is full; as it lands, INSANITY.</summary>
    public class ShatteredMindBehavior : ActionBehavior
    {
        public override bool RollsToHit(ActionDefinition def) { return false; }
        public override bool ApproachesTarget(ActionDefinition def) { return false; }

        public override void Describe(AbilityTip tip) { tip.Add("Then", "INSANITY, until Composed again"); }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects) { sim.BecomeInsane(a.Actor); }
    }

    /// <summary>
    /// A single straight shot (projectile_shot: Divulge): one of the ability's projectiles fired the way it was aimed (a
    /// direction, like a line ability; given a unit instead, as the AI may, toward that unit), flying straight until it hits
    /// someone or its ProjectileLifetime runs out.
    /// </summary>
    public class ProjectileShotBehavior : ProjectileBehavior
    {
        public override AbilityRole DefaultRoles(ActionDefinition def) { return base.DefaultRoles(def) | AbilityRole.Strike; }

        public override void Describe(AbilityTip tip)
        {
            DescribeHit(tip, "Damage", "Stun", true);
            DescribeFlight(tip);
        }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            FireAlong(sim, a, this);
        }

        /// <summary>Fire one of 'a's projectiles straight the way it was aimed (its target unit if it has one, else its point); returns the direction.</summary>
        public static Vector3 FireAlong(BattleSimulation sim, ActionInstance a, ProjectileBehavior shooter)
        {
            var u = a.Actor; var def = a.Definition;
            var target = sim.GetUnit(a.TargetUnitId);
            Vector3 aim = target != null && target != u && target.IsAlive ? target.Position : a.TargetPos;
            Vector3 dir = aim - u.Position; dir.y = 0f;
            dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.forward;
            Vector3 from = u.Position + Vector3.up * (u.BodyHeight * 0.7f) + dir * u.BodyRadius;
            sim.LaunchProjectile(u, def, from, dir, shooter.SpecFor(def), target != null && target != u ? target.Id : null, a.Tag);
            return dir;
        }
    }

    /// <summary>
    /// Fray The Mind (fray): one of the ability's projectiles toward the aimed point, flying straight, while the user is
    /// knocked DisplaceDistance (5m) the opposite way.
    /// </summary>
    public class FrayBehavior : ProjectileBehavior
    {
        public override AbilityRole DefaultRoles(ActionDefinition def) { return base.DefaultRoles(def) | AbilityRole.Strike | AbilityRole.Escape; }
        public override bool MovesUser { get { return true; } }

        public override void Describe(AbilityTip tip)
        {
            DescribeHit(tip, "Damage", "Stun", true);
            DescribeFlight(tip);
            tip.Add("You", AbilityTip.Metres(tip.V(ActionStat.DisplaceDistance)) + " back the other way");
        }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            var u = a.Actor;
            Vector3 dir = ProjectileShotBehavior.FireAlong(sim, a, this);
            float back = a.Stat(ActionStat.DisplaceDistance);
            if (back > 0.01f && !u.IsRooted) sim.PushUnit(u, sim.Paths.ClampDash(u.Position, u.Position - dir * back), BattleSimulation.DisplaceDuration, u);
        }
    }

    /// <summary>
    /// Rampage (rampage): Gouge's dash down the aimed line (Range long, LineWidth wide), hitting every enemy on it; each one
    /// it strikes gives the user a HYSTERIA stack.
    /// </summary>
    public class RampageBehavior : GougeBehavior
    {
        public override void Describe(AbilityTip tip)
        {
            base.Describe(tip);
            tip.Add("Each enemy struck", "+1 HYSTERIA");
        }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            int before = effects.Count;
            base.OnResolve(sim, a, effects);
            var actor = a.Actor;
            for (int i = before; i < effects.Count; i++)
            {
                var atk = effects[i] as AttackEffect;
                if (atk == null) continue;
                var prev = atk.AfterHit;
                atk.AfterHit = (s, hit) => { if (prev != null) prev(s, hit); s.AddHysteria(actor, 1); };
            }
        }
    }

    public partial class BattleSimulation
    {
        private static FormShiftBehavior FormRules(Unit u)
        {
            var d = u.Definition != null ? u.Definition.DivergeAction : null;
            return d != null ? ActionBehaviors.Get(d.BehaviorId) as FormShiftBehavior : null;
        }

        /// <summary>Is 'u' winding up a change of form right now?</summary>
        private static bool Shifting(Unit u)
        {
            var a = u.ActiveAction; var def = u.Definition;
            return a != null && def != null && (a.Definition == def.DivergeAction || a.Definition == def.RegressAction);
        }

        /// <summary>Is 'u' free to start something of its own: standing ready or walking (not winding up, recovering,
        /// stunned, held, in the air, channeling or folded away)?</summary>
        private bool FreeToAct(Unit u)
        {
            return (u.Phase == UnitPhase.Ready || u.Phase == UnitPhase.Moving) && u.HeldById == null && !u.IsLifted && u.Grapple == null
                   && u.GlideEnd < 0f && u.FallLandAt < 0f && !IsVoided(u);
        }

        /// <summary>The Forsaken's rules each tick: NEUROSIS at max Entropy; Diverged, Entropy draining, HYSTERIA building, REGRESSION at 0.</summary>
        private void TickForsaken()
        {
            foreach (var u in State.Units)
            {
                if (!u.IsAlive || u.Definition == null || u.Definition.DivergeAction == null || !u.Passives.Contains(ForsakenRules.EntropyBeast)) continue;
                var rules = FormRules(u);
                if (rules == null) continue;
                bool shifting = Shifting(u);
                bool waitForVisions = TickPsyche(u);   // Wretched Visions' shockwave lands before NEUROSIS
                if (!u.HasStatus(StatusIds.Diverged))
                {
                    if (!shifting && !waitForVisions && u.Strain >= u.MaxStrain - 1e-3f) Shift(u, u.Definition.DivergeAction);
                    continue;
                }
                if (shifting) continue;   // REGRESSION under way: the form holds still until it lands
                var div = u.Definition.DivergeAction;
                var hysteria = u.GetStatus(StatusIds.Hysteria);
                int limit = rules.HysteriaLimit(div);
                u.FormSeconds += TickLength;
                float every = rules.HysteriaEvery(div);
                while (u.FormSeconds >= every - 1e-4f)
                {
                    u.FormSeconds -= every;
                    // HYSTERIA hurts: its damage per stack per second, for the stacks it had over this stretch, straight off its
                    // health (no Block, dodge or shield), then the next stack.
                    if (hysteria != null && hysteria.Stacks > 0) LoseHealth(u, hysteria.Stacks * rules.HysteriaDamage(div) * every, StatusIds.Hysteria);
                    if (!u.IsAlive) break;
                    AddHysteria(u, 1);
                    hysteria = u.GetStatus(StatusIds.Hysteria);
                }
                if (!u.IsAlive) continue;
                bool frenzied = hysteria != null && hysteria.Stacks >= limit;
                // Full HYSTERIA: SHATTERED MIND, the moment it is free to act (not cut into what it is doing).
                var shatter = u.Definition.InsanityAction;
                if (frenzied && !u.HasStatus(StatusIds.Insanity))
                {
                    if (shatter == null) BecomeInsane(u);
                    else if ((u.ActiveAction == null || u.ActiveAction.Definition != shatter) && FreeToAct(u)) Shift(u, shatter);
                }
                AddStrain(u, -rules.Drain(div, frenzied) * TickLength);
                if (u.Strain <= 1e-3f && u.Definition.RegressAction != null) Shift(u, u.Definition.RegressAction);
            }
        }

        /// <summary>
        /// Entropy Beast casts 'def' (NEUROSIS or REGRESSION) for 'u' now: what it was doing is interrupted (a windup, a walk, a
        /// recovery, its orders). Stunned, held or in the air, it waits for the moment it is free.
        /// </summary>
        private void Shift(Unit u, ActionDefinition def)
        {
            if (def == null || u.Phase == UnitPhase.Stunned || u.Phase == UnitPhase.Channeling || u.HeldById != null || u.IsLifted || u.Grapple != null
                || u.GlideEnd >= 0f || u.FallLandAt >= 0f || u.HasStatus(StatusIds.Voided)) return;
            if (u.Phase == UnitPhase.Windup || u.Phase == UnitPhase.Charging) Interrupt(u, "entropy beast", false);
            if (u.Phase == UnitPhase.Moving) EndMove(u, false);
            if (u.Phase == UnitPhase.Recovery) { u.Waiting = false; u.Phase = UnitPhase.Ready; u.PhaseEndsAt = Now; }
            ClearChase(u);
            ClearQueued(u, null);
            queues.Remove(u.Id);
            StartAction(u, UnitCommand.UseAction(u.Id, def, u.Id), ActionBehaviors.Get(def.BehaviorId));
        }

        /// <summary>Add HYSTERIA stacks to the Forsaken, up to the limit (where TickForsaken casts SHATTERED MIND, Diverged);
        /// once INSANE it builds no more. Stacks gained while Composed (Wretched Visions) do no harm until it is Diverged.</summary>
        public void AddHysteria(Unit u, int n)
        {
            if (u == null || !u.IsAlive || n <= 0 || u.HasStatus(StatusIds.Insanity) || !u.Passives.Contains(ForsakenRules.EntropyBeast)) return;
            var rules = FormRules(u);
            if (rules == null) return;
            var h = u.GetStatus(StatusIds.Hysteria);
            if (h == null) { ApplyStatus(u, h = new StatusEffect { Id = StatusIds.Hysteria, SourceId = u.Id, ExpiresAt = float.MaxValue }, u); }
            int limit = rules.HysteriaLimit(u.Definition.DivergeAction);
            if (h.Stacks < limit) SetStacks(u, h, Mathf.Min(limit, h.Stacks + n));
        }

        /// <summary>
        /// INSANITY: MADNESS's rules (faster and stronger, out of its side's control: it basic-attacks the nearest target), and
        /// for as long as it lasts it is on no side: Team.Rogue, everyone's enemy, so both sides can hit it and the nearest unit
        /// of either side is its target. Its own side is kept (Unit.HomeTeam) and given back with REGRESSION (EndInsanity).
        /// </summary>
        public void BecomeInsane(Unit u)
        {
            if (u == null || !u.IsAlive) return;
            if (!u.HomeTeam.HasValue) { u.HomeTeam = u.Team; u.Team = Team.Rogue; }
            ApplyStatus(u, new StatusEffect
            {
                Id = StatusIds.Insanity, SourceId = u.Id, ExpiresAt = float.MaxValue, Flags = StatusFlags.Maddened,
                Modifiers = new List<Modifier> { Modifier.Mult(StatId.DamageDealt, 1.5f), Modifier.Mult(StatId.ActionSpeed, 1.75f), Modifier.Mult(StatId.MoveSpeed, 1.5f) },
            }, u);
            Emit(EventType.TeamChanged, u.Id, null, u.Team);
        }

        /// <summary>INSANITY ends: the unit is back on its own side.</summary>
        public void EndInsanity(Unit u)
        {
            if (u == null) return;
            if (u.HasStatus(StatusIds.Insanity)) RemoveStatus(u, StatusIds.Insanity);
            if (!u.HomeTeam.HasValue) return;
            u.Team = u.HomeTeam.Value; u.HomeTeam = null;
            Emit(EventType.TeamChanged, u.Id, null, u.Team);
        }

        /// <summary>
        /// The Forsaken changes form: each ability with a DivergedVersion swaps for it (or back), its basic attack included.
        /// Back to Composed, its HYSTERIA, its own MADNESS and the Diverged form's status go. Announced as FormChanged.
        /// </summary>
        public void SetForm(Unit u, bool diverged)
        {
            if (u == null || !u.IsAlive) return;
            for (int i = 0; i < u.Actions.Count; i++)
            {
                var a = u.Actions[i];
                if (a == null) continue;
                if (diverged && a.DivergedVersion != null) u.Actions[i] = a.DivergedVersion;
                else if (!diverged) { var composed = ComposedOf(u, a); if (composed != null) u.Actions[i] = composed; }
            }
            var basic = u.Definition != null ? u.Definition.BasicAttack : null;
            if (basic != null) u.BasicAttackId = diverged && basic.DivergedVersion != null ? basic.DivergedVersion.Id : basic.Id;
            u.FormSeconds = 0f;
            if (!diverged)
            {
                RemoveStatus(u, StatusIds.Diverged);
                RemoveStatus(u, StatusIds.Hysteria);
                EndInsanity(u);
                u.BerserkLeft = 0;
                EndFrenzy(u);
            }
            Emit(EventType.FormChanged, u.Id, null, diverged);
        }

        /// <summary>The Composed ability whose Diverged form 'a' is, among 'u's class's abilities (null: 'a' isn't a Diverged form).</summary>
        private static ActionDefinition ComposedOf(Unit u, ActionDefinition a)
        {
            var def = u.Definition;
            if (def == null) return null;
            if (def.BasicAttack != null && def.BasicAttack.DivergedVersion == a) return def.BasicAttack;
            foreach (var c in def.Actions) if (c != null && c.DivergedVersion == a) return c;
            return null;
        }
    }
}
