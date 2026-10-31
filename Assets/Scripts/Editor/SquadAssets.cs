using System.Collections.Generic;
using UnityEngine;
using TacticsGame.Core;

/// <summary>
/// Creates the four squad classes from the design doc (Sharpshooter, Melee Fighter, Tank, Paradigm User) and their
/// abilities as assets under Assets/Actions/&lt;Class&gt; and Assets/Units/&lt;Class&gt;, plus the universal Block and Dash. The
/// code here writes an asset in full only when it is first created; after that the asset (and your Inspector tweaks) is
/// the source of truth, and a DataVersion bump only patches the fields that version changed (see ActionPatches). Delete
/// an asset to regenerate it from scratch.
/// </summary>
public static class SquadAssets
{
    public struct Squad { public UnitDefinition Sharpshooter, Melee, Tank, Paradigm; }

    /// <summary>
    /// Bump when the squad data changes in a way existing assets must pick up, and in the same change:
    ///   1. edit the init code below (what a freshly created asset gets), and
    ///   2. add an entry for the new version to ActionPatches (and the class's patches passed to Unit) that sets ONLY the fields that changed
    ///      (including clearing a value the new data no longer uses), keyed by asset file name.
    /// An older asset gets each newer version's patch once, in order; every other field keeps its Inspector value.
    /// A description that changed is replaced only if the asset still has an earlier default (add it to OldDescriptions).
    /// Assets older than PatchBaseline predate patches and are still reset to defaults and re-initialised in full.
    /// 2: universal Block, Fighter on strain instead of energy, target strain on every attack, follow-up abilities.
    /// 3: redo 2 with the reset (2 left the Fighter's old energy costs in place).
    /// 4: Take Aim 3s and not cancelled by your own actions, 1s Block, Steady Strikes replaces the Focused Strikes,
    ///    Final Cut keeps the trance (a kill extends it), Brace's block isn't worn down.
    /// 5: Final Cut gets a 4s cooldown.
    /// 6: universal Dash; each unit gets Block OR Dash (ranged dash, melee block).
    /// 7: Bash (Tank), Phantom Strike and Mesmerize (Fighter), Stutter and Lance of Destiny (Paradigm User).
    /// 8: Transcend: Block, Dash and Steady Strikes (no longer a follow-up) can be Transcended; every unit gets Block AND Dash.
    /// 9: Subjugate and Torrent of Strife (Tank), Wrath and Fatebreak (Fighter), Second Mind, Wrinkles of Reality and
    ///    Amblivate (Paradigm User); Brace is stun immune; Trance and Wrath exclude each other.
    /// </summary>
    public const int DataVersion = 9;

    /// <summary>The first version with a patch: assets from before it can't be patched and are rebuilt in full.</summary>
    public const int PatchBaseline = 7;

    /// <summary>Per version, per asset file name: the fields that version changed. Keep init above in step with these.</summary>
    private static readonly Dictionary<int, Dictionary<string, System.Action<ActionDefinition>>> ActionPatches =
        new Dictionary<int, Dictionary<string, System.Action<ActionDefinition>>>
    {
        { 8, new Dictionary<string, System.Action<ActionDefinition>>
            {
                { "SteadyStrikes", a => { a.IsFollowup = false; a.CanTranscend = true; a.TranscendStrain = 20f; } },
                { "Block", a => { a.CanTranscend = true; a.TranscendStrain = 15f; } },
                { "Dash", a => { a.CanTranscend = true; a.TranscendStrain = 15f; } },
            }
        },
        { 9, new Dictionary<string, System.Action<ActionDefinition>>
            {
                { "Brace", a => PatchStatus(a.SelfStatuses, StatusIds.Braced, s => s.StunImmune = true) },
                { "BattleTrance", a => PatchStatus(a.SelfStatuses, StatusIds.Trance, s => s.ForbidsActions = new List<string> { "wrath" }) },
            }
        },
    };

    // Unit patches are passed to Unit() by each Create* method (per version), since they hand out that class's new ability
    // assets. (8 needed none: EnsureUniversal hands out Block and Dash.)

    /// <summary>How long the universal Block guards for, in seconds.</summary>
    public const float BlockSeconds = 1f;

    public static Squad Create(GameObject viewPrefab, ActionDefinition rifleShot)
    {
        return new Squad
        {
            Sharpshooter = CreateSharpshooter(viewPrefab, rifleShot),
            Melee = CreateMelee(viewPrefab),
            Tank = CreateTank(viewPrefab),
            Paradigm = CreateParadigm(viewPrefab),
        };
    }

    // ------------------------------------------------------------------ Sharpshooter: backline sniper, traps

    private static UnitDefinition CreateSharpshooter(GameObject viewPrefab, ActionDefinition rifleShot)
    {
        const string c = "Sharpshooter";
        var snipe = Action(c, "Snipe", a =>
        {
            Attack(a, "snipe", "Snipe", "snipe", range: 25f, windup: 1.6f, recovery: 0.6f, damage: 45f, accuracy: 0.95f, stun: 1f, strain: 25f, heavy: true);
            a.Cooldown = 5f;
        });
        var snapshot = Action(c, "Snapshot", a =>
        {
            Attack(a, "snapshot", "Snapshot", "basic_attack", range: 18f, windup: 0.35f, recovery: 0.4f, damage: 20f, accuracy: 0.6f, stun: 0.25f, strain: 8f);
            a.Cooldown = 3f;
        });
        var takeAim = Action(c, "TakeAim", a =>
        {
            SelfBuff(a, "take_aim", "Take Aim", "take_aim", windup: 0.4f, recovery: 0.2f, cooldown: 12f,
                new StatusSpec { Id = StatusIds.TakeAim, Duration = 3f });
            a.LinkedAction = snipe;
        });
        var marking = Action(c, "MarkingShot", a =>
        {
            Attack(a, "marking_shot", "Marking Shot", "basic_attack", range: 18f, windup: 0.6f, recovery: 0.4f, damage: 10f, accuracy: 0.85f, stun: 0.2f, strain: 4f);
            a.Cooldown = 10f;
            a.TargetStatusesOnHit = new List<StatusSpec> { new StatusSpec { Id = StatusIds.Marked, Duration = 8f, IncomingAccuracyBonus = 0.25f } };
            a.SelfStatuses = new List<StatusSpec> { new StatusSpec { Id = StatusIds.Hidden, Duration = 4f, Untargetable = true, BreaksOnDamage = true } };
        });
        var tracking = Action(c, "TrackingShot", a =>
        {
            Attack(a, "tracking_shot", "Tracking Shot", "basic_attack", range: 22f, windup: 2.2f, recovery: 0.6f, damage: 35f, accuracy: 0.95f, stun: 0.8f, strain: 20f, heavy: true);
            a.Cooldown = 8f;
            a.PiercesTerrain = true; a.BlockPierce = 0.5f;
            a.InterruptedByDamage = true; a.InterruptedByStun = true;
            a.ExtraCinematic = true;
        });
        var retaliate = Action(c, "Retaliate", a =>
        {
            SelfBuff(a, "retaliate", "Retaliate", "retaliate", windup: 0.2f, recovery: 0.2f, cooldown: 14f,
                new StatusSpec { Id = StatusIds.Retaliate, Duration = 6f, DodgeBonus = 0.3f, DodgeDistanceBonus = 2f });
            a.LinkedAction = snapshot;
        });
        var trap = Action(c, "Trap", a =>
        {
            a.Id = "trap"; a.DisplayName = "Trap"; a.BehaviorId = "trap";
            a.Targeting = TargetType.Position; a.Range = 10f; a.IsRanged = false;
            a.Windup = 0.5f; a.Recovery = 0.4f; a.Cooldown = 12f;
            a.Shape = AreaShape.Circle; a.AreaRadius = 1.2f; a.TrapArmDelay = 1.5f; a.TrapLifetime = 30f;
            a.Damage = 0f; a.StunDuration = 0f;
            a.TargetStatusesOnHit = new List<StatusSpec>
            {
                new StatusSpec { Id = StatusIds.Rooted, Duration = 3f, Rooted = true },
                new StatusSpec { Id = StatusIds.Marked, Duration = 8f, IncomingAccuracyBonus = 0.25f },
            };
        });

        return Unit(c, u =>
        {
            u.DisplayName = "Sharpshooter"; u.MaxHealth = 85f; u.BaseDodge = 0.15f; u.MoveSpeed = 4f;
            u.BasicAttack = rifleShot;
            u.Actions = new List<ActionDefinition> { snipe, tracking, marking, snapshot, takeAim, retaliate, trap };
            u.ViewPrefab = viewPrefab;
        });
    }

    // ------------------------------------------------------------------ Melee Fighter: close-range damage, very evasive, energy

    private static UnitDefinition CreateMelee(GameObject viewPrefab)
    {
        const string c = "Melee";
        var slash = Action(c, "Slash", a =>
            Attack(a, "slash", "Slash", "basic_attack", range: 1.8f, windup: 0.35f, recovery: 0.35f, damage: 8f, accuracy: 0.9f, stun: 0.3f, strain: 4f, melee: true));
        var rapid = Action(c, "RapidSlices", a =>
        {
            Attack(a, "rapid_slices", "Rapid Slices", "rapid_slices", range: 1.8f, windup: 0.25f, recovery: 0.2f, damage: 7f, accuracy: 0.9f, stun: 0.25f, strain: 4f, melee: true);
            a.StrainOnSelf = 12f;   // halved while the chain is open (RapidSlicesBehavior)
            a.IsFollowup = true;
        });
        var steady = Action(c, "SteadyStrikes", a =>
        {
            // Focused Strike's stats; Transcended (20 strain) it gets Focused Strike+'s (SteadyStrikesBehavior).
            Attack(a, "steady_strikes", "Steady Strikes", "steady_strikes", range: 1.8f, windup: 0.6f, recovery: 0.5f, damage: 16f, accuracy: 0.8f, stun: 0.5f, strain: 8f, melee: true);
            a.CanTranscend = true; a.TranscendStrain = 20f;   // what the old follow-up cost
        });
        var evasive = Action(c, "EvasiveStrike", a =>
        {
            Attack(a, "evasive_strike", "Evasive Strike", "basic_attack", range: 1.8f, windup: 0.45f, recovery: 0.45f, damage: 10f, accuracy: 0.85f, stun: 0.3f, strain: 5f, melee: true);
            a.Cooldown = 5f;
            a.SelfStatusesOnWindup = true;
            a.SelfStatuses = new List<StatusSpec> { new StatusSpec { Id = StatusIds.Evasive, Duration = 3f, DodgeBonus = 0.35f } };
        });
        var trance = Action(c, "BattleTrance", a =>
        {
            SelfBuff(a, "battle_trance", "Battle Trance", "self_buff", windup: 0.3f, recovery: 0.3f, cooldown: 18f,
                new StatusSpec { Id = StatusIds.Trance, Duration = 6f, ActionSpeedMult = 1.2f, ForbidsActions = new List<string> { "wrath" } });
            a.StrainOnSelf = -35f;   // relieves strain
        });
        var wrath = Action(c, "Wrath", a =>
            SelfBuff(a, "wrath", "Wrath", "self_buff", windup: 0.3f, recovery: 0.3f, cooldown: 15f,
                new StatusSpec { Id = StatusIds.Wrath, Duration = 6f, StrainCostMult = 0f, DamageDealtMult = 1.4f, ForbidsActions = new List<string> { "block", "battle_trance" } }));
        var fatebreak = Action(c, "Fatebreak", a =>
        {
            Attack(a, "fatebreak", "Fatebreak", "fatebreak", range: 2f, windup: 1.1f, recovery: 0.7f, damage: 50f, accuracy: 0.95f, stun: 1.5f, strain: 30f, heavy: true, melee: true);
            a.RequiredStatus = StatusIds.Trance + "|" + StatusIds.Wrath; a.ConsumesRequiredStatus = true; a.ConsumeOnResolve = true;
            a.DisplaceDistance = 6f;   // the knockback (FatebreakBehavior)
            a.TargetStatusesDuringWindup = new List<StatusSpec> { new StatusSpec { Id = StatusIds.Fated, ActionSpeedMult = 0.35f } };
            a.ExtraCinematic = true;
        });
        var phantom = Action(c, "PhantomStrike", a =>
        {
            // Range is how far the dash reaches; the slash lands once it arrives (PhantomStrikeBehavior).
            Attack(a, "phantom_strike", "Phantom Strike", "phantom_strike", range: 7f, windup: 0.25f, recovery: 0.2f, damage: 10f, accuracy: 0.9f, stun: 0.3f, strain: 5f, melee: true);
            a.Cooldown = 4f;
            a.TargetStatusesOnHit = new List<StatusSpec> { new StatusSpec { Id = StatusIds.Marked, Duration = 6f, IncomingAccuracyBonus = 0.25f } };
        });
        var mesmerStrike = Action(c, "MesmerStrike", a =>
            Attack(a, "mesmer_strike", "Mesmer Strike", "basic_attack", range: 2.2f, windup: 0.12f, recovery: 0.08f, damage: 6f, accuracy: 0.95f, stun: 0.2f, strain: 3f, melee: true));
        var mesmerize = Action(c, "Mesmerize", a =>
        {
            SelfBuff(a, "mesmerize", "Mesmerize", "mesmerize", windup: 0.1f, recovery: 0f, cooldown: 7f,
                new StatusSpec { Id = StatusIds.Mesmerized, Duration = 1.5f, DodgeBonus = 0.6f });
            a.LinkedAction = mesmerStrike;   // the counter's rapid strikes
        });
        var finalCut = Action(c, "FinalCut", a =>
        {
            Attack(a, "final_cut", "Final Cut", "final_cut", range: 2f, windup: 0.5f, recovery: 0.8f, damage: 30f, accuracy: 0.95f, stun: 1f, strain: 20f, heavy: true, melee: true);
            a.RequiredStatus = StatusIds.Trance; a.ConsumesRequiredStatus = false;   // keeps the trance; a kill extends it (FinalCutBehavior)
            a.Cooldown = 4f;   // the trance isn't spent any more, so this keeps it from being chained
            a.ExtraCinematic = true;
        });

        return Unit(c, u =>
        {
            u.DisplayName = "Fighter"; u.MaxHealth = 100f; u.BaseDodge = 0.35f; u.MoveSpeed = 5.5f; u.DashDistance = 3.5f;
            u.MaxEnergy = 0f; u.StartingEnergy = 0f;   // the Fighter runs on strain, not energy
            u.Passives = new List<string> { Passives.StrainReliefOnDodge };
            u.BasicAttack = slash;
            u.Actions = new List<ActionDefinition> { finalCut, fatebreak, steady, rapid, evasive, trance, wrath, phantom, mesmerize };
            u.ViewPrefab = viewPrefab;
        }, new Dictionary<int, System.Action<UnitDefinition>> { { 9, u => { AddActions(u, finalCut, fatebreak); AddActions(u, trance, wrath); } } });
    }

    // ------------------------------------------------------------------ Tank: durable, crowd control and disables
    // The design doc lists no Tank abilities yet; these are placeholders built from the same rules.

    private static UnitDefinition CreateTank(GameObject viewPrefab)
    {
        const string c = "Tank";
        var mace = Action(c, "MaceStrike", a =>
            Attack(a, "mace_strike", "Mace Strike", "basic_attack", range: 1.9f, windup: 0.5f, recovery: 0.5f, damage: 10f, accuracy: 0.9f, stun: 0.4f, strain: 5f, melee: true));
        var slam = Action(c, "ShieldSlam", a =>
        {
            Attack(a, "shield_slam", "Shield Slam", "basic_attack", range: 1.9f, windup: 0.7f, recovery: 0.6f, damage: 18f, accuracy: 0.9f, stun: 1.5f, strain: 20f, heavy: true, melee: true);
            a.Cooldown = 6f;
        });
        var hook = Action(c, "ChainHook", a =>
        {
            Attack(a, "chain_hook", "Chain Hook", "basic_attack", range: 9f, windup: 0.6f, recovery: 0.5f, damage: 6f, accuracy: 0.85f, stun: 0.8f, strain: 4f);
            a.Cooldown = 8f; a.Displace = DisplaceMode.TowardSource; a.DisplaceDistance = 7f;
        });
        var shockwave = Action(c, "Shockwave", a =>
        {
            a.Id = "shockwave"; a.DisplayName = "Shockwave"; a.BehaviorId = "area_attack";
            a.Targeting = TargetType.Self; a.Shape = AreaShape.Circle; a.AreaRadius = 3.5f; a.IsRanged = false;
            a.Windup = 0.8f; a.Recovery = 0.6f; a.Cooldown = 10f;
            a.Impact = ImpactType.Light; a.Damage = 8f; a.BaseAccuracy = 0.9f; a.StunDuration = 1.2f; a.StrainOnTarget = 15f;
        });
        var bash = Action(c, "Bash", a =>
        {
            a.Id = "bash"; a.DisplayName = "Bash"; a.BehaviorId = "bash";
            a.Targeting = TargetType.Position; a.Shape = AreaShape.Line; a.Range = 2.4f; a.LineWidth = 2.2f; a.IsRanged = false;
            a.Windup = 0.65f; a.Recovery = 0.4f; a.Cooldown = 5f;
            a.Impact = ImpactType.Heavy; a.Damage = 14f; a.BaseAccuracy = 0.95f; a.StunDuration = 0.6f; a.StrainOnTarget = 15f;
            a.DisplaceDistance = 3f;   // the push; a wall that stops it short doubles damage and stun (BashBehavior)
        });
        var brace = Action(c, "Brace", a =>
            SelfBuff(a, "brace", "Brace", "self_buff", windup: 0.3f, recovery: 0.2f, cooldown: 10f,
                new StatusSpec { Id = StatusIds.Braced, Duration = 5f, BlockBonus = 0.5f, KeepsBlock = true, StunImmune = true }));
        var headbutt = Action(c, "SubjugateHeadbutt", a =>
            Attack(a, "subjugate_headbutt", "Headbutt", "basic_attack", range: 2.2f, windup: 0f, recovery: 0f, damage: 10f, accuracy: 1f, stun: 0f, strain: 5f, melee: true));
        var subjugate = Action(c, "Subjugate", a =>
        {
            // The grab (a Heavy hit for no damage); Damage is the throw's. Headbutts and the throw: BattleSimulation.StartGrapple.
            Attack(a, "subjugate", "Subjugate", "subjugate", range: 1.8f, windup: 0.9f, recovery: 0.5f, damage: 15f, accuracy: 0.9f, stun: 2.5f, strain: 10f, heavy: true, melee: true);
            a.Cooldown = 8f;
            a.LinkedAction = headbutt;
        });
        var torrent = Action(c, "TorrentOfStrife", a =>
        {
            a.Id = "torrent_of_strife"; a.DisplayName = "Torrent of Strife"; a.BehaviorId = "area_attack";
            a.Targeting = TargetType.Position; a.Shape = AreaShape.Line; a.Range = 4f; a.LineWidth = 2.5f; a.IsRanged = false;
            a.Windup = 0.5f; a.Recovery = 0.6f; a.Cooldown = 5f;
            a.Impact = ImpactType.Light; a.Damage = 0f; a.BaseAccuracy = 0.95f; a.StunDuration = 0f; a.StrainOnTarget = 40f;
        });

        return Unit(c, u =>
        {
            u.DisplayName = "Tank"; u.MaxHealth = 180f; u.BaseDodge = 0.05f; u.MaxStrain = 140f; u.MoveSpeed = 3.5f;
            u.BodyRadius = 0.5f;
            u.BasicAttack = mace;
            u.Actions = new List<ActionDefinition> { slam, hook, shockwave, brace, bash, subjugate, torrent };
            u.ViewPrefab = viewPrefab;
        }, new Dictionary<int, System.Action<UnitDefinition>> { { 9, u => AddActions(u, null, subjugate, torrent) } });
    }

    // ------------------------------------------------------------------ Paradigm User: backline caster, AoE and support

    private static UnitDefinition CreateParadigm(GameObject viewPrefab)
    {
        const string c = "Paradigm";
        var spark = Action(c, "Spark", a =>
            Attack(a, "spark", "Spark", "basic_attack", range: 12f, windup: 0.5f, recovery: 0.5f, damage: 8f, accuracy: 0.85f, stun: 0.2f, strain: 4f));
        var reconstruct = Action(c, "Reconstruct", a =>
        {
            a.Id = "reconstruct"; a.DisplayName = "Reconstruct"; a.BehaviorId = "heal_displace";
            a.Targeting = TargetType.Ally; a.Range = 10f; a.IsRanged = false;
            a.Windup = 0.7f; a.Recovery = 0.5f; a.Cooldown = 8f;
            a.Heal = 30f; a.Displace = DisplaceMode.AwayFromEnemies; a.DisplaceDistance = 4f;
            a.Damage = 0f; a.StunDuration = 0f;
        });
        var riftLine = Action(c, "RiftLine", a =>
        {
            a.Id = "rift_line"; a.DisplayName = "Rift Line"; a.BehaviorId = "area_attack";
            a.Targeting = TargetType.Position; a.Shape = AreaShape.Line; a.Range = 14f; a.LineWidth = 1.2f; a.IsRanged = true;
            a.Windup = 0.9f; a.Recovery = 0.6f; a.Cooldown = 6f;
            a.Impact = ImpactType.Light; a.Damage = 20f; a.BaseAccuracy = 0.9f; a.StunDuration = 0.4f; a.StrainOnTarget = 10f;
            a.PiercesTerrain = true; a.BlockPierce = 0.25f;
        });
        var stutter = Action(c, "Stutter", a =>
        {
            a.Id = "stutter"; a.DisplayName = "Stutter"; a.BehaviorId = "stutter";
            a.Targeting = TargetType.Enemy; a.Range = 12f; a.IsRanged = true;
            a.Windup = 0.1f; a.Recovery = 0.4f; a.Cooldown = 5f;
            a.Damage = 0f; a.StunDuration = 0f; a.StrainOnTarget = 0f;
            a.StrainOnSelf = 15f;
        });
        var lance = Action(c, "LanceOfDestiny", a =>
        {
            a.Id = "lance_of_destiny"; a.DisplayName = "Lance of Destiny"; a.BehaviorId = "lance";
            a.Targeting = TargetType.Position; a.Shape = AreaShape.Line; a.Range = 16f; a.LineWidth = 0.8f; a.IsRanged = true;
            a.Windup = 0f; a.Recovery = 1f; a.Cooldown = 0f;
            a.Impact = ImpactType.Light; a.Damage = 10f; a.BaseAccuracy = 0.9f; a.StunDuration = 0.25f; a.StrainOnTarget = 8f;
            a.IsCharge = true; a.MaxChargeSeconds = 2.5f; a.ChargeStrainPerSecond = 18f;
            a.ChargeDamageMultiplier = 4f; a.ChargeStunMultiplier = 6f;   // 10 -> 40 damage, 0.25s -> 1.5s stun at full charge
        });
        var secondMind = Action(c, "SecondMind", a =>
        {
            a.Id = "second_mind"; a.DisplayName = "Second Mind"; a.BehaviorId = "status_spell";
            a.Targeting = TargetType.AnyUnit; a.CannotTargetSelf = true; a.Range = 20f; a.IsRanged = true;
            a.Windup = 0.6f; a.Recovery = 0.3f; a.Cooldown = 12f;
            a.Damage = 0f; a.StunDuration = 0f; a.StrainOnSelf = 15f;
            a.TargetStatusesOnHit = new List<StatusSpec>
            {
                new StatusSpec { Id = StatusIds.Quickened, Duration = 3f, ActionSpeedMult = 1.5f, AppliesTo = StatusTarget.Allies },
                new StatusSpec { Id = StatusIds.Slowed, Duration = 3f, ActionSpeedMult = 1f / 1.5f, AppliesTo = StatusTarget.Enemies },
            };
        });
        var wrinkles = Action(c, "WrinklesOfReality", a =>
        {
            a.Id = "wrinkles_of_reality"; a.DisplayName = "Wrinkles of Reality"; a.BehaviorId = "teleport";
            a.Targeting = TargetType.AnyUnit; a.Range = 18f; a.DestinationRange = 10f; a.IsRanged = true;
            a.Windup = 0.3f; a.Recovery = 0.75f; a.Cooldown = 9f;
            a.Damage = 0f; a.StunDuration = 0f; a.StrainOnSelf = 10f;
        });
        var amblivate = Action(c, "Amblivate", a =>
        {
            a.Id = "amblivate"; a.DisplayName = "Amblivate"; a.BehaviorId = "levitate";
            a.Targeting = TargetType.Enemy; a.Range = 15f; a.IsRanged = true;
            a.Windup = 0.6f; a.Recovery = 0.3f; a.Cooldown = 10f;
            a.Impact = ImpactType.Heavy; a.Damage = 0f; a.BaseAccuracy = 0.9f; a.StunDuration = 1.5f; a.StrainOnSelf = 15f;   // Heavy, so a windup can't shrug off the stun
            a.DisplaceDistance = 7f;   // how high it lifts them: the drop's fall damage grows with it
        });
        var cataclysm = Action(c, "Cataclysm", a =>
        {
            a.Id = "cataclysm"; a.DisplayName = "Cataclysm"; a.BehaviorId = "area_attack";
            a.Targeting = TargetType.Position; a.Shape = AreaShape.Circle; a.Range = 16f; a.AreaRadius = 4.5f; a.IsRanged = true;
            a.Windup = 3f; a.Recovery = 1.2f; a.Cooldown = 20f;
            a.Impact = ImpactType.Heavy; a.Damage = 35f; a.BaseAccuracy = 0.9f; a.StunDuration = 2f; a.StrainOnTarget = 30f;
            a.InterruptedByStun = true;   // a long channel: any stun breaks it
        });

        return Unit(c, u =>
        {
            u.DisplayName = "Paradigm User"; u.MaxHealth = 75f; u.BaseDodge = 0.1f; u.MoveSpeed = 4f;
            u.BasicAttack = spark;
            u.Actions = new List<ActionDefinition> { reconstruct, riftLine, cataclysm, stutter, lance, secondMind, wrinkles, amblivate };
            u.ViewPrefab = viewPrefab;
        }, new Dictionary<int, System.Action<UnitDefinition>> { { 9, u => AddActions(u, null, secondMind, wrinkles, amblivate) } });
    }

    // ------------------------------------------------------------------ helpers

    private static ActionDefinition Action(string cls, string file, System.Action<ActionDefinition> init)
    {
        var a = CombatTestSceneBuilder.GetOrCreate<ActionDefinition>("Assets/Actions/" + cls + "/" + file + ".asset", x => { init(x); x.DataVersion = DataVersion; });
        if (a.DataVersion < DataVersion)
        {
            if (a.DataVersion < PatchBaseline)
            {
                CombatTestSceneBuilder.ResetToDefaults(a);
                init(a);
                a.Description = "";   // refilled below with the current text
            }
            else Upgrade(a, file, a.DataVersion, ActionPatches);
            a.DataVersion = DataVersion;
            CombatTestSceneBuilder.MarkDirty(a);
        }
        ApplyDescription(a, file);
        return a;
    }

    /// <summary>Apply each patch newer than 'from', oldest first, to the asset filed under 'key'.</summary>
    private static void Upgrade<T>(T asset, string key, int from, Dictionary<int, Dictionary<string, System.Action<T>>> patches)
    {
        for (int v = from + 1; v <= DataVersion; v++)
        {
            Dictionary<string, System.Action<T>> forVersion;
            System.Action<T> patch;
            if (patches.TryGetValue(v, out forVersion) && forVersion.TryGetValue(key, out patch)) patch(asset);
        }
    }

    /// <summary>Universal Block (one of the two UnitDefinition.Universal options): a stun-proof guard paid for in strain.</summary>
    public static ActionDefinition UniversalBlock()
    {
        return Action("Universal", "Block", a =>
        {
            // The guard goes up as the order starts and lasts through the recovery; one extra tick covers the tick
            // the instant windup takes to resolve.
            SelfBuff(a, "block", "Block", "block", windup: 0f, recovery: BlockSeconds, cooldown: 0f,
                new StatusSpec { Id = StatusIds.Blocking, Duration = BlockSeconds + BattleSimulation.TickLength, BlockBonus = 0.5f, StunImmune = true });
            a.SelfStatusesOnWindup = true;
            a.StrainOnSelf = 20f;
            a.IsFollowup = true;
            a.CanTranscend = true; a.TranscendStrain = 15f;   // stops all damage, and heavy hits don't put it on cooldown
        });
    }

    /// <summary>Universal Dash (key G, every unit has it): slide toward a point at dodge speed and reach, paid for in strain.</summary>
    public static ActionDefinition UniversalDash()
    {
        return Action("Universal", "Dash", a =>
        {
            a.Id = "dash"; a.DisplayName = "Dash"; a.BehaviorId = "dash";
            a.Targeting = TargetType.Position; a.IsRanged = false;
            a.Range = 1000f;   // any point: the dash goes as far as the unit's dash distance that way (DashDestination)
            a.Windup = 0f; a.Recovery = 0f; a.Cooldown = 0f;
            a.Damage = 0f; a.StunDuration = 0f;
            a.StrainOnSelf = 20f;
            a.CanTranscend = true; a.TranscendStrain = 15f;   // further and much faster
        });
    }

    /// <summary>Gives a unit definition the universal Block and Dash if it lacks them (an older asset may hold Dash as its
    /// one universal option: that becomes Block, and Dash gets its own slot).</summary>
    public static void EnsureUniversal(UnitDefinition u)
    {
        if (u == null) return;
        var block = UniversalBlock();
        var dash = UniversalDash();
        if (u.Universal == block && u.Dash == dash) return;
        u.Universal = block;
        u.Dash = dash;
        CombatTestSceneBuilder.MarkDirty(u);
    }

    /// <summary>Fills in the hover text for an ability if it has none yet, or still has an older default from
    /// OldDescriptions (text edited in the Inspector is never overwritten).</summary>
    public static void ApplyDescription(ActionDefinition a, string key)
    {
        string text; string[] old;
        if (a == null || !Descriptions.TryGetValue(key, out text) || a.Description == text) return;
        bool stale = OldDescriptions.TryGetValue(key, out old) && System.Array.IndexOf(old, a.Description) >= 0;
        if (!string.IsNullOrEmpty(a.Description) && !stale) return;
        a.Description = text;
        CombatTestSceneBuilder.MarkDirty(a);
    }

    private static readonly Dictionary<string, string> Descriptions = new Dictionary<string, string>
    {
        // Sharpshooter
        { "RifleShot", "Basic ranged attack. Always available." },
        { "Snipe", "Enter an aiming stance and snipe a target. Cancels if the target leaves line of sight." },
        { "Snapshot", "Quickly enter a sniping stance and fire a shot. Fast, but less accurate." },
        { "TakeAim", "Take aim for up to 3 seconds. The first enemy that starts an action within Snipe's range and sight gets sniped as a reaction. You can keep acting while aiming." },
        { "MarkingShot", "Fire a shot that marks the target (attacks hit it more easily) and become undetectable for a short time. Taking damage reveals you." },
        { "TrackingShot", "Enter intense focus, tracking your target. If you aren't interrupted by damage or crowd control, fire a shot that pierces terrain and 50% block." },
        { "Retaliate", "Increase dodge chance and dodge distance. Gain a free Snapshot at every attacker you dodge." },
        { "Trap", "Toss out a trap that arms after a short delay. The first enemy to step in is immobilized and marked." },
        // Melee Fighter
        { "Slash", "Basic melee attack. Always available." },
        { "RapidSlices", "Quick melee attack that costs strain. Follow-up: press Space to slice again. Slicing the same target again halves the strain cost, up to 4 more slices." },
        { "SteadyStrikes", "A deliberate, steady melee strike. Transcend (hold its key, 20 strain): an empowered blow that always hits and pierces 25% block." },
        { "EvasiveStrike", "Melee attack. Increases your dodge while you swing and for a short time afterwards." },
        { "BattleTrance", "Briefly enter a trance: sheds strain and speeds up your actions. Enables Final Cut and Fatebreak. Can't be used during Wrath." },
        { "Wrath", "Enter Wrath for 6 seconds: your abilities cost no strain and deal 40% more damage, but you can't Block. Can't be used during Battle Trance, and Battle Trance can't be used during Wrath." },
        { "Fatebreak", "Trance or Wrath only, and ends it. Heavily slows the target while you wind up, then strikes it with a massive blow that knocks it far back." },
        { "FinalCut", "Trance only. A finishing blow that deals more damage the more health the target is missing. A kill extends your trance by 3 seconds." },
        // Tank
        { "MaceStrike", "Basic melee attack. Always available." },
        { "ShieldSlam", "Heavy shield blow: a long stun that interrupts windups and builds strain." },
        { "ChainHook", "Hook an enemy and drag them to you, stunning them briefly." },
        { "Shockwave", "Slam the ground, stunning every enemy around you and building their strain." },
        { "Bash", "Shove enemies in front of you back and damage them. Anyone driven into a wall takes double damage and stun." },
        { "Brace", "Raise your guard for +50% block for 5 seconds and become immune to stun (except Unravel). Hits don't wear it down." },
        { "Subjugate", "Become immune to stun and grab a target, stunning it for 2.5s and carrying it with you. While you hold it you can only move, and you headbutt it every 0.5s for 10 damage; after each headbutt you can let go early. When you let go or the stun ends, wind up a short throw that hurls it away for 15 damage." },
        { "SubjugateHeadbutt", "Subjugate's headbutt at the unit you are holding." },
        { "TorrentOfStrife", "Emit a wave of strife in front of you, adding 40 strain to every enemy it hits." },
        { "PhantomStrike", "Phase to the target, straight through walls and cover, and slash it, marking it (attacks hit it more easily)." },
        { "Mesmerize", "Drastically raise your dodge for a moment. The first attack you dodge blinks you to the attacker for 4 rapid strikes; you can stop after any of them." },
        { "MesmerStrike", "Mesmerize's counter: a rapid strike at the attacker you dodged." },
        // Paradigm User
        { "Spark", "Basic ranged spell. Always available." },
        { "Reconstruct", "Heal an ally and move them away from the nearest enemy." },
        { "RiftLine", "Deal damage in a line that pierces terrain and 25% block." },
        { "Stutter", "A near-instant jolt that interrupts the target's windup or charge. Deals no damage and costs you 15 strain." },
        { "LanceOfDestiny", "Charge: start charging (strain builds while you do; any damage breaks it), then aim and release. The lance hits the first enemy in its path; damage and stun grow with charge time, up to 4x damage and 6x stun." },
        { "Cataclysm", "After a long channel, deal heavy damage in a huge area and stun. Any stun breaks the channel." },
        { "SecondMind", "Speed up an ally's actions by 1.5x, or slow an enemy's by 1.5x, for 3 seconds. No hit roll." },
        { "WrinklesOfReality", "Pick an ally, an enemy or yourself, then a point within 10m of them: they teleport there. No hit roll." },
        { "Amblivate", "Stun an enemy for 1.5s and lift them 7m into the air, then drop them: they take fall damage." },
        // Universal
        { "Dash", "Key G. Dash toward a point as fast as a dodge, up to your dash distance (costs 20 strain). You can't act until you land. Transcend (hold G, +15 strain): dash 80% further, much faster." },
        { "Block", "Raise your guard for 1 second (costs 20 strain, even past your max): +50% block and immune to stun, except Unravel. Getting hit while guarding lets you act again right away, but a heavy hit puts Block on cooldown for 1s. Follow-up: press Space to block again. Transcend (hold its key, +15 strain): blocks all damage and heavy hits don't put it on cooldown." },
        // Enemies
        { "HeavyStrike", "Slow, heavy melee blow that stuns." },
    };

    // Earlier default texts, replaced by the current ones where an asset still has one of them unedited. When a
    // description above changes, add the text it had here.
    private static readonly Dictionary<string, string[]> OldDescriptions = new Dictionary<string, string[]>
    {
        { "BattleTrance", new[] { "Briefly enter a trance: sheds strain and speeds up your actions. Enables Final Cut." } },
        { "Brace", new[] { "Raise your guard for +50% block for 5 seconds. Hits don't wear it down." } },
        { "PhantomStrike", new[] { "Dash to the target and slash it, marking it (attacks hit it more easily). A wall in the way stops the dash short." } },
        { "Block", new[]
            {
                "Raise your guard for a moment (costs 20 strain): +50% block and immune to stun. Getting hit while guarding lets you act again right away. Follow-up: press Space to block again.",
                "Raise your guard for 1 second (costs 20 strain, even past your max): +50% block and immune to stun, except Unravel. Getting hit while guarding lets you act again right away. Follow-up: press Space to block again.",
            }
        },
        { "Dash", new[] { "Dash toward a point as fast as a dodge, up to your dash distance (costs 20 strain). You can't act until you land." } },
        { "SteadyStrikes", new[]
            {
                "A deliberate, steady melee strike. Follow-up: strike the same target again right after (Space) for an empowered blow that costs 20 strain, always hits and pierces 25% block.",
                "A deliberate, steady melee strike. Transcend (hold its key, +12 strain): an empowered blow that always hits and pierces 25% block.",
            }
        },
    };

    /// <summary>A patch helper: change the status 'id' in a list of status specs, if it is there.</summary>
    private static void PatchStatus(List<StatusSpec> specs, string id, System.Action<StatusSpec> patch)
    {
        if (specs == null) return;
        foreach (var s in specs) if (s != null && s.Id == id) patch(s);
    }

    /// <summary>A unit patch helper: add abilities the unit lacks, right after 'after' if it has that one, else at the end.</summary>
    private static void AddActions(UnitDefinition u, ActionDefinition after, params ActionDefinition[] actions)
    {
        int at = after != null ? u.Actions.IndexOf(after) : -1;
        foreach (var a in actions)
        {
            if (a == null || u.Actions.Contains(a)) continue;
            if (at >= 0) u.Actions.Insert(++at, a); else u.Actions.Add(a);
        }
    }

    /// <summary>patches: per version, what that version changed on this class (see DataVersion).</summary>
    private static UnitDefinition Unit(string cls, System.Action<UnitDefinition> init, Dictionary<int, System.Action<UnitDefinition>> patches = null)
    {
        var u = CombatTestSceneBuilder.GetOrCreate<UnitDefinition>("Assets/Units/" + cls + "/" + cls + ".asset", x => { init(x); x.DataVersion = DataVersion; });
        if (u.DataVersion < DataVersion)
        {
            if (u.DataVersion < PatchBaseline)
            {
                CombatTestSceneBuilder.ResetToDefaults(u);
                init(u);
            }
            else if (patches != null)
                for (int v = u.DataVersion + 1; v <= DataVersion; v++) { System.Action<UnitDefinition> p; if (patches.TryGetValue(v, out p)) p(u); }
            u.DataVersion = DataVersion;
            CombatTestSceneBuilder.MarkDirty(u);
        }
        EnsureUniversal(u);
        return u;
    }

    private static void Attack(ActionDefinition a, string id, string name, string behavior, float range, float windup, float recovery,
                               float damage, float accuracy, float stun, float strain, bool heavy = false, bool melee = false)
    {
        a.Id = id; a.DisplayName = name; a.BehaviorId = behavior;
        a.Targeting = TargetType.Enemy; a.Range = range; a.IsRanged = !melee;
        a.Windup = windup; a.Recovery = recovery;
        a.Impact = heavy ? ImpactType.Heavy : ImpactType.Light;
        a.Damage = damage; a.BaseAccuracy = accuracy; a.StunDuration = stun;
        a.StrainOnTarget = strain;   // every attack builds strain on what it hits: roughly half its damage, more for heavy blows
    }

    private static void SelfBuff(ActionDefinition a, string id, string name, string behavior, float windup, float recovery, float cooldown, StatusSpec status)
    {
        a.Id = id; a.DisplayName = name; a.BehaviorId = behavior;
        a.Targeting = TargetType.Self; a.IsRanged = false;
        a.Windup = windup; a.Recovery = recovery; a.Cooldown = cooldown;
        a.Damage = 0f; a.StunDuration = 0f;
        a.SelfStatuses = new List<StatusSpec> { status };
    }
}
