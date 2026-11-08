using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TacticsGame.Core
{
    /// <summary>
    /// Deliverance's four parts (phase two). Each starts with one ability, Imprint Upon Itself (copy a random ability from a
    /// squad unit's loadout, BattleSimulation.Imprint), and a passive of its own. A passive's changes to ability numbers apply only to
    /// the abilities the part has imprinted (ImprintModifiers), except where it names an ability outright (Wex's Block).
    ///   path_to_victory (Syne)           - imprinted abilities get PathRangeBonus (+75%) range; a stack of SEE BEFORE ITSELF
    ///                                      every SeeEvery (5s), up to SeeMax (3); with any, Dodge is 100%, and a dodge spends one.
    ///   plight_upon_structure (Alio)     - imprinted abilities cost PlightStrain (50%) of their strain and have PlightCooldown
    ///                                      (50%) of their cooldown; one that costs strain can be Transcended into a ghost that
    ///                                      casts it again (BattleSimulation.SummonGhost).
    ///   singleminded_perfection (Revl)   - imprinted abilities wind up and recover RevlSpeed (70%) as long; at max strain it
    ///                                      never Unravels: it casts EQUIPOSE at once (behavior equipose).
    ///   last_attachments (Wex)           - its Block and Transcended Block cost WexBlockStrain (25%) of their strain; allies
    ///                                      within WexAuraRadius (15m) have WexAuraBlock (+50%) Block.
    /// </summary>
    public static class DeliveranceParts
    {
        public const string PathToVictory = "path_to_victory";
        public const string PlightUponStructure = "plight_upon_structure";
        public const string SingleMindedPerfection = "singleminded_perfection";
        public const string LastAttachments = "last_attachments";

        public const string SeeBeforeItself = "see_before_itself";
        public const string AttachmentsStatus = "last_attachments";
        public const string EquiposeStatus = "equipose";

        /// <summary>Behaviors an Imprint never copies: the universal guard and dash, and the scripted moves.</summary>
        private static readonly HashSet<string> NotCopied = new HashSet<string> { "block", "dash", "imprint", "split_the_minds", "equipose" };

        /// <summary>Can Imprint copy 'def'? Not Block or Dash, not an always-on one, nor a follow-up that needs another first.</summary>
        public static bool Copyable(ActionDefinition def)
        {
            return def != null && !def.InnatePassive && string.IsNullOrEmpty(def.RequiredStatus) && !NotCopied.Contains(def.BehaviorId ?? "");
        }

        public static void Attach(BattleSimulation sim)
        {
            // SEE BEFORE ITSELF: a dodge spends a stack.
            sim.Triggers.Subscribe(TriggerType.Dodged, ctx =>
            {
                var u = ctx.Subject;
                var s = u != null ? u.GetStatus(SeeBeforeItself) : null;
                if (s == null) return;
                if (s.Stacks <= 1) sim.RemoveStatus(u, SeeBeforeItself);
                else sim.SetStacks(u, s, s.Stacks - 1);
            });
        }
    }

    public partial class BattleSimulation
    {
        // ------------------------------------------------------------------ the parts' numbers (tunable)

        public float PathRangeBonus = 0.75f, SeeEvery = 5f;
        public int SeeMax = 3;
        public float PlightStrain = 0.5f, PlightCooldown = 0.5f;
        public float RevlSpeed = 0.7f;
        public float WexBlockStrain = 0.25f, WexAuraRadius = 15f, WexAuraBlock = 0.5f;

        // ------------------------------------------------------------------ Imprint Upon Itself

        /// <summary>
        /// 'u' copies an ability from 'from''s loadout ('copy', or one at random that it doesn't have yet) into its own, usable
        /// as its own from now on, with its passives' changes on it (ImprintModifiers). Each Imprint makes the next slower,
        /// shorter and longer to come back: 'imprint''s windup, recovery and cooldown grow by its steps, its range shrinks by
        /// 'range_step'. False if there is nothing new to copy.
        /// </summary>
        public bool Imprint(Unit u, Unit from, ActionDefinition imprint, float windupStep, float recoveryStep, float rangeStep, float cooldownStep = 0f, ActionDefinition copy = null)
        {
            var def = copy ?? RandomImprintable(u, from);
            if (u == null || def == null || !CanImprint(u, def)) return false;
            u.Actions = new List<ActionDefinition>(u.Actions) { def };   // its own list (the opening snapshot keeps the old one)
            u.Imprinted = new HashSet<string>(u.Imprinted) { def.Id };
            var mods = new List<ActionModifier>(u.UpgradeActionModifiers);
            mods.AddRange(ImprintModifiers(u, def.Id));
            if (imprint != null)
            {
                mods.Add(new ActionModifier(imprint.Id, ActionStat.Windup, ModOp.Add, windupStep));
                mods.Add(new ActionModifier(imprint.Id, ActionStat.Recovery, ModOp.Add, recoveryStep));
                mods.Add(new ActionModifier(imprint.Id, ActionStat.Range, ModOp.Add, -rangeStep));
                mods.Add(new ActionModifier(imprint.Id, ActionStat.Cooldown, ModOp.Add, cooldownStep));
            }
            u.UpgradeActionModifiers = mods;
            AttachBehaviors(u);
            EmitFx(u, from, "imprint", def, u.Position, 0f, from.Position);
            return true;
        }

        /// <summary>Has 'u' room for 'def' (not already its own, by asset or id)?</summary>
        public bool CanImprint(Unit u, ActionDefinition def)
        {
            return def != null && DeliveranceParts.Copyable(def) && !u.Actions.Exists(a => a == def || (a != null && a.Id == def.Id));
        }

        /// <summary>Does 'from' have anything in its loadout 'u' could copy?</summary>
        public bool HasImprintable(Unit u, Unit from)
        {
            return u != null && from != null && from.Actions.Exists(a => CanImprint(u, a));
        }

        /// <summary>One of the abilities in 'from''s loadout that 'u' could copy, at random (the battle's seeded draw); null if none.</summary>
        private ActionDefinition RandomImprintable(Unit u, Unit from)
        {
            if (u == null || from == null) return null;
            var options = from.Actions.FindAll(a => CanImprint(u, a));
            return options.Count > 0 ? options[Mathf.Min(options.Count - 1, (int)(RandomValue() * options.Count))] : null;
        }

        /// <summary>What 'u''s passives change on an ability it imprinted ('actionId'): the copy-only modifiers.</summary>
        public List<ActionModifier> ImprintModifiers(Unit u, string actionId)
        {
            var list = new List<ActionModifier>();
            if (u.Passives.Contains(DeliveranceParts.PathToVictory)) list.Add(new ActionModifier(actionId, ActionStat.Range, ModOp.Multiply, 1f + PathRangeBonus));
            if (u.Passives.Contains(DeliveranceParts.PlightUponStructure))
            {
                list.Add(new ActionModifier(actionId, ActionStat.StrainOnSelf, ModOp.Multiply, PlightStrain));
                list.Add(new ActionModifier(actionId, ActionStat.TranscendStrain, ModOp.Multiply, PlightStrain));
                list.Add(new ActionModifier(actionId, ActionStat.Cooldown, ModOp.Multiply, PlightCooldown));
            }
            if (u.Passives.Contains(DeliveranceParts.SingleMindedPerfection))
            {
                list.Add(new ActionModifier(actionId, ActionStat.Windup, ModOp.Multiply, RevlSpeed));
                list.Add(new ActionModifier(actionId, ActionStat.Recovery, ModOp.Multiply, RevlSpeed));
            }
            return list;
        }


        // ------------------------------------------------------------------ Alio's ghosts

        private int ghostCounter;

        /// <summary>
        /// Plight Upon Structure, Transcended: a ghost of 'u' (1 health) appears beside it and casts 'cmd''s ability again, at
        /// a different target where there is one, for free; it is gone once its recovery ends (ProcessReadyUnits).
        /// </summary>
        public Unit SummonGhost(Unit u, UnitCommand cmd)
        {
            var def = cmd.Action;
            if (u == null || def == null) return null;
            var g = u.Snapshot();
            g.Id = u.Id + "_ghost_" + (++ghostCounter);
            g.DisplayName = u.DisplayName + " (ghost)";
            g.Health = g.MaxHealth = 1f;
            g.Strain = 0f;
            g.Passives = new List<string>();
            g.IsBoss = false; g.IsGhost = true;
            g.Statuses = new List<StatusEffect>();
            g.CooldownReadyAt = new Dictionary<string, float>();
            g.Phase = UnitPhase.Ready; g.ActiveAction = null; g.Move = null; g.Chase = g.Queued = null; g.GlideEnd = -1f;
            float ang = RandomValue() * Mathf.PI * 2f;
            g.Position = Terrain.OnGround(FreeSpotNear(g, WalkablePointNear(u.Position + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * 1.6f, u.Position), u.Position, false));
            State.Units.Add(g);
            AttachBehaviors(g);
            Emit(EventType.UnitSpawned, u.Id, g.Id, new SpawnInfo { Unit = g.Snapshot(), Definition = u.Definition });
            ApplyStatus(g, new StatusEffect { Id = StatusIds.Ghost, SourceId = u.Id, ExpiresAt = float.MaxValue }, u);

            var again = GhostCommand(g, cmd);
            var behavior = ActionBehaviors.Get(def.BehaviorId);
            string why;
            if (!behavior.CanStart(this, g, again, out why)) again = new UnitCommand { Type = cmd.Type, UnitId = g.Id, Action = def, TargetUnitId = cmd.TargetUnitId, TargetPos = cmd.TargetPos };
            StartAction(g, again, behavior, WindupOf(g, def));   // a "resumed" start: the ghost pays nothing
            return g;
        }

        /// <summary>The ghost's cast: at another unit than 'cmd''s (or its point moved to another enemy) where it can be.</summary>
        private UnitCommand GhostCommand(Unit g, UnitCommand cmd)
        {
            var def = cmd.Action;
            var c = new UnitCommand { Type = CommandType.Action, UnitId = g.Id, Action = def, TargetUnitId = cmd.TargetUnitId, TargetPos = cmd.TargetPos };
            string why;
            if (def.Targeting == TargetType.Self) { c.TargetUnitId = g.Id; c.TargetPos = g.Position; return c; }
            var pool = def.Targeting == TargetType.Ally ? State.Units.Where(o => o.IsAlive && o.Team == g.Team && o != g).ToList() : EnemiesOf(g).ToList();
            var other = pool.Where(o => o.Id != cmd.TargetUnitId && CanUse(g, def, o, out why, false)).OrderBy(o => Vector3.Distance(o.Position, g.Position)).FirstOrDefault();
            if (def.Targeting == TargetType.Position)
            {
                var near = EnemiesOf(g).Where(o => o.Id != cmd.TargetUnitId).OrderBy(o => Vector3.Distance(o.Position, cmd.TargetPos)).FirstOrDefault();
                if (near != null && Vector3.Distance(near.Position, g.Position) <= g.ActionStat(def, ActionStat.Range) + 0.5f) c.TargetPos = near.Position;
                return c;
            }
            if (other != null) { c.TargetUnitId = other.Id; c.TargetPos = other.Position; }
            return c;
        }

        // ------------------------------------------------------------------ each tick: SEE BEFORE ITSELF, EQUIPOSE, Wex's aura

        private void TickParts()
        {
            bool auraStep = IsStep(StrainRecoveryTicks);
            foreach (var u in State.Units.ToArray())
            {
                if (!u.IsAlive || u.Passives.Count == 0) continue;
                if (u.Passives.Contains(DeliveranceParts.PathToVictory)) TickSeeBeforeItself(u);
                if (u.Passives.Contains(DeliveranceParts.SingleMindedPerfection) && u.Strain >= u.MaxStrain - 1e-3f) CastEquipose(u);
                if (u.Passives.Contains(DeliveranceParts.LastAttachments))
                {
                    if (auraStep) TickAttachments(u);
                }
            }
            if (auraStep) DropStaleAttachments();
        }

        private void TickSeeBeforeItself(Unit u)
        {
            float next;
            if (!u.Timers.TryGetValue(DeliveranceParts.SeeBeforeItself, out next)) { u.Timers[DeliveranceParts.SeeBeforeItself] = Now + SeeEvery; return; }
            if (Now + 1e-4f < next) return;
            u.Timers[DeliveranceParts.SeeBeforeItself] = Now + SeeEvery;
            var s = u.GetStatus(DeliveranceParts.SeeBeforeItself);
            if (s == null)
            {
                s = new StatusEffect { Id = DeliveranceParts.SeeBeforeItself, SourceId = u.Id, ExpiresAt = float.MaxValue, Modifiers = new List<Modifier> { Modifier.Add(StatId.Dodge, 1f) } };
                ApplyStatus(u, s, u);
                SetStacks(u, s, 1);
            }
            else if (s.Stacks < SeeMax) SetStacks(u, s, s.Stacks + 1);
        }

        /// <summary>Singleminded Perfection: EQUIPOSE now (its Equipose ability), unless it is already under way.</summary>
        private bool CastEquipose(Unit u)
        {
            if (u.ActiveAction != null && u.ActiveAction.Definition.BehaviorId == "equipose") return true;
            var def = u.Actions.FirstOrDefault(a => a != null && a.BehaviorId == "equipose");
            if (def == null) return false;
            ForceAction(u, def, u.Id);
            return true;
        }

        /// <summary>Last Attachments: its Block (and Transcended Block) costs WexBlockStrain of the strain, once per battle set up.</summary>
        private void CheapenGuard(Unit u)
        {
            u.SpentOnce.Add(DeliveranceParts.LastAttachments);
            var mods = new List<ActionModifier>(u.UpgradeActionModifiers);
            foreach (var a in u.Actions)
            {
                if (a == null || a.BehaviorId != "block") continue;
                mods.Add(new ActionModifier(a.Id, ActionStat.StrainOnSelf, ModOp.Multiply, WexBlockStrain));
                mods.Add(new ActionModifier(a.Id, ActionStat.TranscendStrain, ModOp.Multiply, WexBlockStrain));
            }
            u.UpgradeActionModifiers = mods;
        }

        /// <summary>Allies within WexAuraRadius of 'wex' have its Block (+WexAuraBlock, not worn down by hits).</summary>
        private void TickAttachments(Unit wex)
        {
            foreach (var o in State.Units)
            {
                if (!o.IsAlive || o == wex || o.Team != wex.Team) continue;
                Vector3 d = o.Position - wex.Position; d.y = 0f;
                if (d.magnitude > WexAuraRadius || o.HasStatus(DeliveranceParts.AttachmentsStatus)) continue;
                ApplyStatus(o, new StatusEffect
                {
                    Id = DeliveranceParts.AttachmentsStatus, SourceId = wex.Id, ExpiresAt = float.MaxValue, Flags = StatusFlags.KeepsBlock,
                    Modifiers = new List<Modifier> { Modifier.Add(StatId.Block, WexAuraBlock) },
                }, wex);
            }
        }

        /// <summary>The aura comes off whoever is out of reach of every living Wex (or whose Wex is gone).</summary>
        private void DropStaleAttachments()
        {
            foreach (var o in State.Units)
            {
                if (!o.IsAlive || !o.HasStatus(DeliveranceParts.AttachmentsStatus)) continue;
                bool held = State.Units.Any(w => w.IsAlive && w != o && w.Team == o.Team && w.Passives.Contains(DeliveranceParts.LastAttachments) &&
                                                 Vector3.Distance(new Vector3(w.Position.x, 0f, w.Position.z), new Vector3(o.Position.x, 0f, o.Position.z)) <= WexAuraRadius);
                if (!held) RemoveStatus(o, DeliveranceParts.AttachmentsStatus);
            }
        }
    }
}
