using System.Collections.Generic;
using UnityEngine;

namespace TacticsGame.Core
{
    // Paradigm User abilities of round AF: Withering End, Splinters, Baneful Domain, Mindshatter. Madness is status_spell
    // (its MADNESS status is Maddened: BattleSimulation.MaddenedAI plays the unit while it lasts).

    /// <summary>
    /// Withering End: a channel of decay on an enemy (ChanneledStatusBehavior: its TargetStatusesOnHit, the slow, last as long
    /// as the channel). Every tick_seconds of it the target takes tick_damage (no hit roll; the ability's damage modifiers and
    /// the user's damage bonuses scale it). It holds while the target stays in sight and range (ChannelNeedsSight); run to its
    /// end, a final burst lands: the ability's own hit (Damage, StunDuration, Heavy), sure to connect.
    /// </summary>
    public class WitheringEndBehavior : ChanneledStatusBehavior
    {
        public override IEnumerable<BehaviorParam> Parameters
        {
            get
            {
                yield return new BehaviorParam("tick_damage", 1f);
                yield return new BehaviorParam("tick_seconds", 0.1f);
            }
        }

        public override AbilityRole DefaultRoles(ActionDefinition def) { return base.DefaultRoles(def) | AbilityRole.Strike; }

        public override void Describe(AbilityTip tip)
        {
            var def = tip.Def;
            float tick = Param(def, "tick_damage"), every = Param(def, "tick_seconds");
            float scaled = tip.Who != null ? tip.Who.ActionStat(def, ActionStat.Damage, tick) : tick;
            tip.Add("Decay", string.Format("{0:0.#} damage every {1}", scaled, AbilityTip.Secs(every)));
            tip.Add("Channel", "up to " + AbilityTip.Secs(def.ChannelSeconds) + " while they stay in sight; damage on you breaks it");
            DescribeHit(tip, "Final burst", "Final stun", false);
        }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            base.OnResolve(sim, a, effects);   // the slow, for the channel's length
            a.NextStepAt = sim.Now + Mathf.Max(BattleSimulation.TickLength, Param(a.Definition, "tick_seconds"));
        }

        public override bool OnChannelTick(BattleSimulation sim, ActionInstance a)
        {
            var target = sim.GetUnit(a.TargetUnitId);
            if (target == null || !target.IsAlive) return true;   // the channel's sight check ends it
            var def = a.Definition;
            float every = Mathf.Max(BattleSimulation.TickLength, Param(def, "tick_seconds"));
            while (sim.Now + 1e-4f >= a.NextStepAt && target.IsAlive)
            {
                float dmg = a.Actor.ActionStat(def, ActionStat.Damage, Param(def, "tick_damage")) * a.Actor.DamageDealtMult();
                sim.DealTickDamage(a.Actor, target, dmg, def);
                a.NextStepAt += every;
            }
            return true;
        }

        public override void OnChannelEnd(BattleSimulation sim, ActionInstance a, bool broken)
        {
            var target = sim.GetUnit(a.TargetUnitId);
            if (!broken && a.Actor.IsAlive && target != null && target.IsAlive)
                sim.StrikeNow(new AttackEffect
                {
                    Source = a.Actor, TargetId = target.Id, Definition = a.Definition, Tag = a.Tag,
                    DamageMultiplier = a.DamageMultiplier * a.Actor.DamageDealtMult(),
                    IgnoreRange = true, IgnoreCover = true, Unerring = true,   // the decay is already in them
                });
            base.OnChannelEnd(sim, a, broken);   // the slow comes off (after the burst, whose hit would put it back on)
        }
    }

    /// <summary>
    /// Splinters: a hit that lodges a SPLINTER in its target (one more stack each time; max_stacks above 0 caps them). They
    /// stay until the target Unravels: then each one deals unravel_damage (the ability's damage modifiers scale it) and makes
    /// the Unravel's stun unravel_stun longer, and they are gone.
    /// </summary>
    public class SplinterBehavior : ActionBehavior
    {
        public override IEnumerable<BehaviorParam> Parameters
        {
            get
            {
                yield return new BehaviorParam("unravel_damage", 15f);
                yield return new BehaviorParam("unravel_stun", 0.2f);
                yield return new BehaviorParam("max_stacks", 0f);
            }
        }

        public override AbilityRole DefaultRoles(ActionDefinition def) { return base.DefaultRoles(def) | AbilityRole.Strike; }

        public override void Attach(BattleSimulation sim)
        {
            sim.Triggers.Subscribe(TriggerType.Unraveled, ctx => Shatter(ctx.Sim, ctx.Subject));
        }

        public override void Describe(AbilityTip tip)
        {
            base.Describe(tip);
            tip.Add("On Unravel", string.Format("{0:0} damage and +{1} stun per SPLINTER", Param(tip.Def, "unravel_damage"), AbilityTip.Secs(Param(tip.Def, "unravel_stun"))));
        }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            var target = sim.GetUnit(a.TargetUnitId);
            if (target == null || !target.IsAlive) return;
            var actor = a.Actor; var def = a.Definition;
            effects.Add(new AttackEffect
            {
                Source = actor, TargetId = target.Id, Definition = def, DamageMultiplier = a.DamageMultiplier, Tag = a.Tag,
                OnLanded = (s, t) => Lodge(s, actor, t, def),
            });
        }

        private void Lodge(BattleSimulation sim, Unit actor, Unit target, ActionDefinition def)
        {
            var st = target.GetStatus(StatusIds.Splinter);
            if (st == null)
            {
                st = new StatusEffect { Id = StatusIds.Splinter, SourceId = actor.Id, ExpiresAt = BattleSimulation.UntimedStatus, LinkedAction = def };
                sim.ApplyStatus(target, st, actor);
            }
            else { st.SourceId = actor.Id; st.LinkedAction = def; }   // the latest Splinters' numbers count
            int max = Mathf.RoundToInt(Param(def, "max_stacks"));
            sim.SetStacks(target, st, max > 0 ? Mathf.Min(max, st.Stacks + 1) : st.Stacks + 1);
        }

        /// <summary>'u' just Unraveled: its splinters burst (damage and a longer stun) and are gone.</summary>
        private void Shatter(BattleSimulation sim, Unit u)
        {
            if (u == null) return;
            var st = u.GetStatus(StatusIds.Splinter);
            if (st == null) return;
            int n = st.Stacks;
            var def = st.LinkedAction;
            var src = sim.GetUnit(st.SourceId);
            sim.RemoveStatus(u, StatusIds.Splinter);
            if (n <= 0 || def == null || !u.IsAlive) return;
            float per = Param(def, "unravel_damage");
            float dmg = n * (src != null ? src.ActionStat(def, ActionStat.Damage, per) * src.DamageDealtMult() : per);
            sim.EmitFx(src, u, "splinters_burst", def, u.Position, n);
            sim.DealTickDamage(src, u, dmg, def);
            sim.ExtendStun(u, n * Param(def, "unravel_stun"), src);
        }
    }

    /// <summary>
    /// Baneful Domain: a zone left at the target point (AuraBehavior with AuraAtTarget), hurting the enemies inside more the
    /// longer it is up (AuraDamageGrowth). Cast again while it is up, it renews the zone instead (same place, its growth
    /// kept) and costs followup_strain: the first cast costs its StrainOnSelf (none).
    /// </summary>
    public class BanefulDomainBehavior : AuraBehavior
    {
        public override IEnumerable<BehaviorParam> Parameters { get { yield return new BehaviorParam("followup_strain", 15f); } }

        public override AbilityRole DefaultRoles(ActionDefinition def) { return base.DefaultRoles(def) | AbilityRole.AreaBlast; }

        public override bool RollsToHit(ActionDefinition def) { return false; }

        // While its zone is up, the ability is a follow-up: no aiming, it renews that zone.
        public override bool FollowupOpen(BattleSimulation sim, Unit actor, ActionDefinition def) { return sim != null && sim.HasAura(actor, def); }

        /// <summary>The zone's centre, or as near it as the ability reaches from 'from' (a zone left far behind can still be renewed).</summary>
        public override Vector3 FollowupPoint(BattleSimulation sim, Unit actor, ActionDefinition def, Vector3 from)
        {
            Vector3 center;
            if (!sim.TryGetAuraCenter(actor, def, out center)) return from;
            float reach = actor.ActionStat(def, ActionStat.Range) * 0.98f;
            return Vector3.Distance(from, center) <= reach ? center : from + (center - from).normalized * reach;
        }

        public override float StrainCost(BattleSimulation sim, Unit actor, ActionDefinition def, string targetId)
        {
            return sim != null && sim.HasAura(actor, def) ? actor.ActionStat(def, ActionStat.StrainOnSelf, Param(def, "followup_strain")) : base.StrainCost(sim, actor, def, targetId);
        }

        public override void Describe(AbilityTip tip)
        {
            base.Describe(tip);
            var def = tip.Def;
            tip.Add("Zone", string.Format("{0} wide for {1}", AbilityTip.Metres(tip.V(ActionStat.AreaRadius)), AbilityTip.Secs(def.AuraSeconds * (tip.Who != null ? tip.Who.DurationScale(def) : 1f))));
            tip.Add("Damage", string.Format("{0:0.#}/s, +{1:0}% for each second it's up", def.AuraDamagePerSecond, def.AuraDamageGrowth * 100f));
            tip.Add("Recast", string.Format("while it's up: renews it, +{0:0} strain", Param(def, "followup_strain")));
        }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            var def = a.Definition;
            if (sim.RefreshAura(a.Actor, def, def.AuraSeconds * a.Actor.DurationScale(def))) return;
            base.OnResolve(sim, a, effects);
        }
    }

    /// <summary>
    /// Mindshatter: one heavy hit. Transcended, it hits harder the more strain its target and its user carry as it lands
    /// (target_strain_bonus and own_strain_bonus per point: 0.01 = +1%), and its user Unravels right after (ActionInstance.UnravelsActor).
    /// </summary>
    public class MindshatterBehavior : ActionBehavior
    {
        public override IEnumerable<BehaviorParam> Parameters
        {
            get
            {
                yield return new BehaviorParam("target_strain_bonus", 0.01f);
                yield return new BehaviorParam("own_strain_bonus", 0.01f);
            }
        }

        public override AbilityRole DefaultRoles(ActionDefinition def) { return base.DefaultRoles(def) | AbilityRole.Strike; }

        public override void Describe(AbilityTip tip)
        {
            base.Describe(tip);
            if (!tip.Def.CanTranscend) return;
            tip.Add("Transcended", string.Format("+{0:0.#}% damage per strain on the target, +{1:0.#}% per strain on you; then you UNRAVEL",
                Param(tip.Def, "target_strain_bonus") * 100f, Param(tip.Def, "own_strain_bonus") * 100f));
        }

        /// <summary>What a Transcended Mindshatter's damage is multiplied by, from 'actor' at 'target' as things stand.</summary>
        public float StrainMultiplier(ActionDefinition def, Unit actor, Unit target)
        {
            return 1f + Mathf.Max(0f, target.Strain) * Param(def, "target_strain_bonus") + Mathf.Max(0f, actor.Strain) * Param(def, "own_strain_bonus");
        }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            var target = sim.GetUnit(a.TargetUnitId);
            if (target == null || !target.IsAlive) return;
            float mult = a.DamageMultiplier;
            if (a.Transcended)
            {
                mult *= StrainMultiplier(a.Definition, a.Actor, target);
                a.UnravelsActor = true;   // once the hit has landed
            }
            effects.Add(new AttackEffect { Source = a.Actor, TargetId = target.Id, Definition = a.Definition, DamageMultiplier = mult, Tag = a.Tag });
        }
    }
}
