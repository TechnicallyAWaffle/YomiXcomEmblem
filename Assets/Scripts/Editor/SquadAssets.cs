using System.Collections.Generic;
using UnityEngine;
using TacticsGame.Core;

/// <summary>
/// Creates any missing squad, enemy and universal assets (abilities under Assets/Actions, units under Assets/Units, wherever
/// those folders have been moved to). The assets are the source of truth: this only writes an asset that doesn't exist
/// yet (from the init code below), and fills a description or a look that is still empty. Change an existing ability by
/// editing its asset (Inspector, or Tools > Combat Test > Tuning &amp; Text), never here. Delete an asset to regenerate it.
/// The DataVersion patches below are frozen history: they only bring an asset saved before version 15 up to date.
/// </summary>
public static class SquadAssets
{
    public struct Squad { public UnitDefinition Sharpshooter, Melee, Tank, Paradigm; }
    public struct Enemies { public UnitDefinition Sniper, Assassin, Skirmisher, Healer, Caster, Brutebeast; }

    /// <summary>
    /// FROZEN at 15: assets are now the source of truth, so changes go into the assets and no new versions are added. New
    /// content is new assets (init code below, or made in the editor). The history, for assets saved before 15:
    /// Each version said: bump when the squad data changes in a way existing assets must pick up, and in the same change:
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
    /// 10: the enemy types (Sniper, Assassin, Skirmisher, Healer): new assets only, nothing existing changes.
    /// 9: Subjugate and Torrent of Strife (Tank), Wrath and Fatebreak (Fighter), Second Mind, Wrinkles of Reality and
    ///    Amblivate (Paradigm User); Brace is stun immune; Trance and Wrath exclude each other.
    /// 11: Final Cut (Certainty) works in Wrath too; the Caster and Brutebeast (new assets, replacing the Gunner and Brawler).
    /// 12: Skyward Demise and Flickershots (Sharpshooter), Etching of Voracity and Praa's Truth (Fighter), Behemoth's Arrival
    ///    (Tank): new assets, handed to existing units by their unit patches.
    /// 13: Lethal Prescience and Swift Engagement (Fighter), Sanctity and Heretic's Ascent (Tank), Chronosphere (Paradigm User):
    ///    new assets, handed out by the unit patches.
    /// 14: Subjugate is Unstoppable while it winds up.
    /// 15: statuses store their bonuses as modifier lists and flags (StatusSpec): no data changes, but every ability asset is
    ///    re-saved so the old one-field-per-bonus layout is converted on disk (StatusSpec.OnAfterDeserialize reads it).
    /// </summary>
    public const int DataVersion = 15;

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
                { "Brace", a => PatchStatus(a.SelfStatuses, StatusIds.Braced, s => s.Flags |= StatusFlags.StunImmune) },
                { "BattleTrance", a => PatchStatus(a.SelfStatuses, StatusIds.Trance, s => s.ForbidsActions = new List<string> { "wrath" }) },
            }
        },
        { 11, new Dictionary<string, System.Action<ActionDefinition>>
            {
                { "FinalCut", a => a.RequiredStatus = StatusIds.Trance + "|" + StatusIds.Wrath },
            }
        },
        { 14, new Dictionary<string, System.Action<ActionDefinition>>
            {
                { "Subjugate", a => a.SelfStatusesDuringWindup = new List<StatusSpec> { UnstoppableWindup() } },
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
            a.TargetStatusesOnHit = new List<StatusSpec> { new StatusSpec { Id = StatusIds.Marked, Duration = 8f }.Add(StatId.IncomingAccuracy, 0.25f) };
            a.SelfStatuses = new List<StatusSpec> { new StatusSpec { Id = StatusIds.Hidden, Duration = 4f }.With(StatusFlags.Untargetable | StatusFlags.BreaksOnDamage) };
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
                new StatusSpec { Id = StatusIds.Retaliate, Duration = 6f }.Add(StatId.Dodge, 0.3f).Add(StatId.DashDistance, 2f));
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
                new StatusSpec { Id = StatusIds.Rooted, Duration = 3f }.With(StatusFlags.Rooted),
                new StatusSpec { Id = StatusIds.Marked, Duration = 8f }.Add(StatId.IncomingAccuracy, 0.25f),
            };
        });

        var skyward = Action(c, "SkywardDemise", a =>
        {
            // Leap 12m across and 8m up, hovering (and hitting 50% harder) for 2.5s: LeapBehavior.
            a.Id = "skyward_demise"; a.DisplayName = "Skyward Demise"; a.BehaviorId = "leap";
            a.Targeting = TargetType.Position; a.Range = 12f; a.IsRanged = false;
            a.Windup = 0.7f; a.Recovery = 0.3f; a.Cooldown = 14f;
            a.Impact = ImpactType.Heavy; a.Damage = 0f; a.StunDuration = 0f;
            a.DisplaceDistance = 8f;   // how high
            a.SelfStatuses = new List<StatusSpec> { new StatusSpec { Id = StatusIds.Skyward, Duration = 2.5f }.Mult(StatId.DamageDealt, 1.5f) };
        });
        var flicker = Action(c, "Flickershots", a =>
        {
            Attack(a, "flickershots", "Flickershots", "chain_attack", range: 10f, windup: 0.3f, recovery: 0.2f, damage: 8f, accuracy: 0.85f, stun: 0.2f, strain: 4f);
            a.Cooldown = 8f;
            a.ChainFollowups = 2; a.ChainCostMultiplier = 1f; a.ChainWindow = 1f;   // two more on the same target, cooldown or not
            a.IsFollowup = true;
        });

        return Unit(c, u =>
        {
            u.DisplayName = "Sharpshooter"; u.MaxHealth = 85f; u.BaseDodge = 0.15f; u.MoveSpeed = 4f;
            u.BasicAttack = rifleShot;
            u.Actions = new List<ActionDefinition> { snipe, tracking, marking, snapshot, flicker, takeAim, retaliate, trap, skyward };
            u.ViewPrefab = viewPrefab;
        }, new Dictionary<int, System.Action<UnitDefinition>> { { 12, u => { AddActions(u, snapshot, flicker); AddActions(u, null, skyward); } } });
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
            a.SelfStatuses = new List<StatusSpec> { new StatusSpec { Id = StatusIds.Evasive, Duration = 3f }.Add(StatId.Dodge, 0.35f) };
        });
        var trance = Action(c, "BattleTrance", a =>
        {
            SelfBuff(a, "battle_trance", "Battle Trance", "self_buff", windup: 0.3f, recovery: 0.3f, cooldown: 18f,
                new StatusSpec { Id = StatusIds.Trance, Duration = 6f, ForbidsActions = new List<string> { "wrath" } }.Mult(StatId.ActionSpeed, 1.2f));
            a.StrainOnSelf = -35f;   // relieves strain
        });
        var wrath = Action(c, "Wrath", a =>
            SelfBuff(a, "wrath", "Wrath", "self_buff", windup: 0.3f, recovery: 0.3f, cooldown: 15f,
                new StatusSpec { Id = StatusIds.Wrath, Duration = 6f, ForbidsActions = new List<string> { "block", "battle_trance" } }.Mult(StatId.StrainCost, 0f).Mult(StatId.DamageDealt, 1.4f)));
        var fatebreak = Action(c, "Fatebreak", a =>
        {
            Attack(a, "fatebreak", "Fatebreak", "fatebreak", range: 2f, windup: 1.1f, recovery: 0.7f, damage: 50f, accuracy: 0.95f, stun: 1.5f, strain: 30f, heavy: true, melee: true);
            a.RequiredStatus = StatusIds.Trance + "|" + StatusIds.Wrath; a.ConsumesRequiredStatus = true; a.ConsumeOnResolve = true;
            a.DisplaceDistance = 6f;   // the knockback (FatebreakBehavior)
            a.TargetStatusesDuringWindup = new List<StatusSpec> { new StatusSpec { Id = StatusIds.Fated }.Mult(StatId.ActionSpeed, 0.35f) };
            a.ExtraCinematic = true;
        });
        var phantom = Action(c, "PhantomStrike", a =>
        {
            // Range is how far the dash reaches; the slash lands once it arrives (PhantomStrikeBehavior).
            Attack(a, "phantom_strike", "Phantom Strike", "phantom_strike", range: 7f, windup: 0.25f, recovery: 0.2f, damage: 10f, accuracy: 0.9f, stun: 0.3f, strain: 5f, melee: true);
            a.Cooldown = 4f;
            a.TargetStatusesOnHit = new List<StatusSpec> { new StatusSpec { Id = StatusIds.Marked, Duration = 6f }.Add(StatId.IncomingAccuracy, 0.25f) };
        });
        var mesmerStrike = Action(c, "MesmerStrike", a =>
            Attack(a, "mesmer_strike", "Mesmer Strike", "basic_attack", range: 2.2f, windup: 0.12f, recovery: 0.08f, damage: 6f, accuracy: 0.95f, stun: 0.2f, strain: 3f, melee: true));
        var mesmerize = Action(c, "Mesmerize", a =>
        {
            SelfBuff(a, "mesmerize", "Mesmerize", "mesmerize", windup: 0.1f, recovery: 0f, cooldown: 7f,
                new StatusSpec { Id = StatusIds.Mesmerized, Duration = 1.5f }.Add(StatId.Dodge, 0.6f));
            a.LinkedAction = mesmerStrike;   // the counter's rapid strikes
        });
        var finalCut = Action(c, "FinalCut", a =>
        {
            Attack(a, "final_cut", "Final Cut", "final_cut", range: 2f, windup: 0.5f, recovery: 0.8f, damage: 30f, accuracy: 0.95f, stun: 1f, strain: 20f, heavy: true, melee: true);
            a.RequiredStatus = StatusIds.Trance + "|" + StatusIds.Wrath; a.ConsumesRequiredStatus = false;   // keeps it; a kill extends it (FinalCutBehavior)
            a.Cooldown = 4f;   // the trance isn't spent any more, so this keeps it from being chained
            a.ExtraCinematic = true;
        });
        var etching = Action(c, "EtchingOfVoracity", a =>
        {
            // Health for strain: pays HealthCost to start, sheds 35 strain.
            a.Id = "etching_of_voracity"; a.DisplayName = "Etching of Voracity"; a.BehaviorId = "self_buff";
            a.Targeting = TargetType.Self; a.IsRanged = false;
            a.Windup = 0.3f; a.Recovery = 0.5f; a.Cooldown = 0f;
            a.Damage = 0f; a.StunDuration = 0f;
            a.HealthCost = 25f; a.StrainOnSelf = -35f;
        });
        var praa = Action(c, "PraasTruth", a =>
        {
            Attack(a, "praas_truth", "Praa's Truth", "basic_attack", range: 2f, windup: 0.7f, recovery: 0.4f, damage: 32f, accuracy: 0.9f, stun: 0.9f, strain: 20f, heavy: true, melee: true);
            a.StrainOnSelf = 30f; a.Cooldown = 3f;
            a.CanTranscend = true; a.TranscendStrain = 30f; a.TranscendWindupMultiplier = 0f;   // Transcended: no windup, double the strain
            a.UnravelsOverMaxStrain = true;   // usable past max strain, but then the Fighter Unravels once it lands
        });

        var prescience = Action(c, "LethalPrescience", a =>
        {
            // Asked for as 9 range: the Fighter's melee reach (as with the enemy types' strikes).
            Attack(a, "lethal_prescience", "Lethal Prescience", "basic_attack", range: 1.8f, windup: 0.9f, recovery: 0.2f, damage: 12f, accuracy: 0.9f, stun: 0.2f, strain: 6f, melee: true);
            a.StunnedDamageMultiplier = 3f;   // triple damage on a stunned target
        });
        var swift = Action(c, "SwiftEngagement", a =>
        {
            Attack(a, "swift_engagement", "Swift Engagement", "basic_attack", range: 1.8f, windup: 0.2f, recovery: 0.1f, damage: 15f, accuracy: 0.9f, stun: 0.7f, strain: 7f, melee: true);
            a.StrainOnSelf = 15f;
        });

        return Unit(c, u =>
        {
            u.DisplayName = "Fighter"; u.MaxHealth = 100f; u.BaseDodge = 0.35f; u.MoveSpeed = 5.5f; u.DashDistance = 3.5f;
            u.MaxEnergy = 0f; u.StartingEnergy = 0f;   // the Fighter runs on strain, not energy
            u.Passives = new List<string> { Passives.StrainReliefOnDodge };
            u.BasicAttack = slash;
            u.Actions = new List<ActionDefinition> { finalCut, fatebreak, praa, prescience, steady, rapid, swift, evasive, trance, wrath, etching, phantom, mesmerize };
            u.ViewPrefab = viewPrefab;
        }, new Dictionary<int, System.Action<UnitDefinition>>
        {
            { 9, u => { AddActions(u, finalCut, fatebreak); AddActions(u, trance, wrath); } },
            { 12, u => { AddActions(u, fatebreak, praa); AddActions(u, wrath, etching); } },
            { 13, u => { AddActions(u, praa, prescience); AddActions(u, rapid, swift); } },
        });
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
                new StatusSpec { Id = StatusIds.Braced, Duration = 5f }.Add(StatId.Block, 0.5f).With(StatusFlags.KeepsBlock | StatusFlags.StunImmune)));
        var headbutt = Action(c, "SubjugateHeadbutt", a =>
            Attack(a, "subjugate_headbutt", "Headbutt", "basic_attack", range: 2.2f, windup: 0f, recovery: 0f, damage: 10f, accuracy: 1f, stun: 0f, strain: 5f, melee: true));
        var subjugate = Action(c, "Subjugate", a =>
        {
            // The grab (a Heavy hit for no damage); Damage is the throw's. Headbutts and the throw: BattleSimulation.StartGrapple.
            Attack(a, "subjugate", "Subjugate", "subjugate", range: 1.8f, windup: 0.9f, recovery: 0.5f, damage: 15f, accuracy: 0.9f, stun: 2.5f, strain: 10f, heavy: true, melee: true);
            a.Cooldown = 8f;
            a.LinkedAction = headbutt;
            a.SelfStatusesDuringWindup = new List<StatusSpec> { UnstoppableWindup() };
        });
        var torrent = Action(c, "TorrentOfStrife", a =>
        {
            a.Id = "torrent_of_strife"; a.DisplayName = "Torrent of Strife"; a.BehaviorId = "area_attack";
            a.Targeting = TargetType.Position; a.Shape = AreaShape.Line; a.Range = 4f; a.LineWidth = 2.5f; a.IsRanged = false;
            a.Windup = 0.5f; a.Recovery = 0.6f; a.Cooldown = 5f;
            a.Impact = ImpactType.Light; a.Damage = 0f; a.BaseAccuracy = 0.95f; a.StunDuration = 0f; a.StrainOnTarget = 40f;
        });
        var arrival = Action(c, "BehemothsArrival", a =>
        {
            // Blink up to 9m, then slam: everyone within 3m (6 wide) is hit, stunned and lifted 3m to fall (ArrivalBehavior).
            a.Id = "behemoths_arrival"; a.DisplayName = "Behemoth's Arrival"; a.BehaviorId = "arrival";
            a.Targeting = TargetType.Position; a.Range = 9f; a.Shape = AreaShape.Circle; a.AreaRadius = 3f; a.IsRanged = false;
            a.Windup = 0.7f; a.Recovery = 0.7f; a.Cooldown = 11f;
            a.Impact = ImpactType.Heavy; a.Damage = 15f; a.BaseAccuracy = 0.95f; a.StunDuration = 0.9f; a.StrainOnTarget = 15f;
            a.DisplaceDistance = 3f;   // lift height
        });

        var sanctity = Action(c, "Sanctity", a =>
        {
            // A 3m x 1.5m dome around the Tank for 1.5s: allies inside are Sheltered, so projectiles and melee hits on them
            // come to the Tank instead (AuraBehavior, RedirectsHits).
            SelfBuff(a, "sanctity", "Sanctity", "aura", windup: 0.2f, recovery: 0.6f, cooldown: 12f,
                new StatusSpec { Id = StatusIds.Sanctity, Duration = 1.5f });
            a.Shape = AreaShape.Circle; a.AreaRadius = 3f; a.AreaHeight = 1.5f; a.AuraSeconds = 1.5f;
            a.AuraStatuses = new List<StatusSpec> { new StatusSpec { Id = StatusIds.Sheltered, Duration = 1.5f, AppliesTo = StatusTarget.Allies }.With(StatusFlags.RedirectsHits) };
        });
        var ascent = Action(c, "HereticsAscent", a =>
        {
            // The grab (a Heavy hit for no damage), then up 5m with the target over 0.75s, 0.75s at the top and a 0.5s slam:
            // fall damage from the top and StunDuration on landing (AerialSlamBehavior). Asked for as 11 range: just past melee reach.
            Attack(a, "heretics_ascent", "Heretic's Ascent", "aerial_slam", range: 2.2f, windup: 0.9f, recovery: 0.4f, damage: 0f, accuracy: 0.9f, stun: 0.5f, strain: 10f, heavy: true, melee: true);
            a.Cooldown = 14f;
            a.DisplaceDistance = 5f;   // how high
            a.AirRiseSeconds = 0.75f; a.AirHoldSeconds = 0.75f; a.AirSlamSeconds = 0.5f;
        });

        return Unit(c, u =>
        {
            u.DisplayName = "Tank"; u.MaxHealth = 180f; u.BaseDodge = 0.05f; u.MaxStrain = 140f; u.MoveSpeed = 3.5f;
            u.BodyRadius = 0.5f;
            u.BasicAttack = mace;
            u.Actions = new List<ActionDefinition> { slam, hook, shockwave, brace, sanctity, bash, subjugate, ascent, torrent, arrival };
            u.ViewPrefab = viewPrefab;
        }, new Dictionary<int, System.Action<UnitDefinition>>
        {
            { 9, u => AddActions(u, null, subjugate, torrent) },
            { 12, u => AddActions(u, null, arrival) },
            { 13, u => { AddActions(u, brace, sanctity); AddActions(u, subjugate, ascent); } },
        });
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
                new StatusSpec { Id = StatusIds.Quickened, Duration = 3f, AppliesTo = StatusTarget.Allies }.Mult(StatId.ActionSpeed, 1.5f),
                new StatusSpec { Id = StatusIds.Slowed, Duration = 3f, AppliesTo = StatusTarget.Enemies }.Mult(StatId.ActionSpeed, 1f / 1.5f),
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

        var chronosphere = Action(c, "Chronosphere", a =>
        {
            // Channeled for up to 2.5s: a 6m x 3m dome around the caster that halves enemies' action speed (AuraBehavior).
            a.Id = "chronosphere"; a.DisplayName = "Chronosphere"; a.BehaviorId = "aura";
            a.Targeting = TargetType.Self; a.IsRanged = false;
            a.Windup = 0.3f; a.Recovery = 1f; a.Cooldown = 18f;
            a.Damage = 0f; a.StunDuration = 0f; a.StrainOnSelf = 40f;
            a.Shape = AreaShape.Circle; a.AreaRadius = 6f; a.AreaHeight = 3f;
            a.ChannelSeconds = 2.5f;
            a.AuraStatuses = new List<StatusSpec> { new StatusSpec { Id = StatusIds.TimeSlowed, Duration = 2.5f, AppliesTo = StatusTarget.Enemies }.Mult(StatId.ActionSpeed, 0.5f) };
        });

        return Unit(c, u =>
        {
            u.DisplayName = "Paradigm User"; u.MaxHealth = 75f; u.BaseDodge = 0.1f; u.MoveSpeed = 4f;
            u.BasicAttack = spark;
            u.Actions = new List<ActionDefinition> { reconstruct, riftLine, cataclysm, stutter, lance, secondMind, chronosphere, wrinkles, amblivate };
            u.ViewPrefab = viewPrefab;
        }, new Dictionary<int, System.Action<UnitDefinition>>
        {
            { 9, u => AddActions(u, null, secondMind, wrinkles, amblivate) },
            { 13, u => AddActions(u, secondMind, chronosphere) },
        });
    }

    // ------------------------------------------------------------------ Enemy types (each with its own AI profile, see UnitAIs)
    // Their abilities are their own assets, even where they copy a squad ability, so each can be tuned on its own.

    public static Enemies CreateEnemies(GameObject viewPrefab)
    {
        return new Enemies
        {
            Sniper = CreateSniper(viewPrefab), Assassin = CreateAssassin(viewPrefab), Skirmisher = CreateSkirmisher(viewPrefab), Healer = CreateHealer(viewPrefab),
            Caster = CreateCaster(viewPrefab), Brutebeast = CreateBrutebeast(viewPrefab),
        };
    }

    private static UnitDefinition CreateSniper(GameObject viewPrefab)
    {
        const string c = "Sniper";
        var rifle = Action(c, "RifleShot", a =>
            Attack(a, "sniper_rifle_shot", "Rifle Shot", "basic_attack", range: 14f, windup: 0.6f, recovery: 0.5f, damage: 14f, accuracy: 0.85f, stun: 0.15f, strain: 5f));
        var snipe = Action(c, "Snipe", a =>
        {
            Attack(a, "sniper_snipe", "Snipe", "snipe", range: 25f, windup: 1.6f, recovery: 0.6f, damage: 45f, accuracy: 0.95f, stun: 1f, strain: 25f, heavy: true);
            a.Cooldown = 5f;
        });
        var marking = Action(c, "MarkingShot", a =>
        {
            Attack(a, "sniper_marking_shot", "Marking Shot", "basic_attack", range: 18f, windup: 0.6f, recovery: 0.4f, damage: 10f, accuracy: 0.85f, stun: 0.2f, strain: 4f);
            a.Cooldown = 10f;
            a.TargetStatusesOnHit = new List<StatusSpec> { new StatusSpec { Id = StatusIds.Marked, Duration = 8f }.Add(StatId.IncomingAccuracy, 0.25f) };
            a.SelfStatuses = new List<StatusSpec> { new StatusSpec { Id = StatusIds.Hidden, Duration = 4f }.With(StatusFlags.Untargetable | StatusFlags.BreaksOnDamage) };
        });
        return Unit(c, u =>
        {
            u.DisplayName = "Sniper"; u.MaxHealth = 85f; u.BaseDodge = 0.15f; u.MoveSpeed = 4f; u.AIProfile = "sniper";
            u.BasicAttack = rifle;
            u.Actions = new List<ActionDefinition> { snipe, marking };
            u.ViewPrefab = viewPrefab;
        });
    }

    private static UnitDefinition CreateAssassin(GameObject viewPrefab)
    {
        const string c = "Assassin";
        var strike = Action(c, "Strike", a =>
            Attack(a, "assassin_strike", "Strike", "basic_attack", range: MeleeStrikeRange, windup: 0.3f, recovery: 0.3f, damage: 15f, accuracy: 0.9f, stun: 0.3f, strain: 7f, melee: true));
        var polystep = Action(c, "Polystep", a =>
        {
            a.Id = "polystep"; a.DisplayName = "Polystep"; a.BehaviorId = "blink";
            a.Targeting = TargetType.Position; a.Range = 12f; a.IsRanged = false;
            a.Windup = 0.5f; a.Recovery = 0.25f; a.Cooldown = 6f;
            a.Damage = 0f; a.StunDuration = 0f;
        });
        var hidden = Action(c, "HiddenStrike", a =>
        {
            // Phantom Strike's dash (Range is its reach), hidden from enemies while it winds up.
            Attack(a, "hidden_strike", "Hidden Strike", "phantom_strike", range: 18f, windup: 0.9f, recovery: 0.5f, damage: 24f, accuracy: 0.9f, stun: 0.5f, strain: 12f, melee: true);
            a.Cooldown = 11f;
            a.SelfStatusesDuringWindup = new List<StatusSpec> { new StatusSpec { Id = StatusIds.Hidden }.With(StatusFlags.Untargetable) };
        });
        return Unit(c, u =>
        {
            u.DisplayName = "Assassin"; u.MaxHealth = 90f; u.BaseDodge = 0.3f; u.MoveSpeed = 5.5f; u.DashDistance = 3.5f; u.AIProfile = "assassin";
            u.BasicAttack = strike;
            u.Actions = new List<ActionDefinition> { hidden, polystep };
            u.ViewPrefab = viewPrefab;
        });
    }

    private static UnitDefinition CreateSkirmisher(GameObject viewPrefab)
    {
        const string c = "Skirmisher";
        var strike = Action(c, "Strike", a =>
            Attack(a, "skirmisher_strike", "Strike", "basic_attack", range: MeleeStrikeRange, windup: 0.3f, recovery: 0.3f, damage: 15f, accuracy: 0.9f, stun: 0.3f, strain: 7f, melee: true));
        var flow = Action(c, "FlowingDeath", a =>
        {
            // Each one in a row winds up 0.1s faster, down to 0.1s (FlowingDeathBehavior).
            Attack(a, "flowing_death", "Flowing Death", "flowing_death", range: MeleeStrikeRange, windup: 0.5f, recovery: 0.1f, damage: 8f, accuracy: 0.9f, stun: 0.2f, strain: 4f, melee: true);
            a.StrainOnSelf = 5f;   // what keeps it from going on forever: the Skirmisher backs off to rest at high strain
            a.IsFollowup = true;
        });
        var currents = Action(c, "MentalCurrents", a =>
        {
            SelfBuff(a, "mental_currents", "Mental Currents", "self_buff", windup: 0.1f, recovery: 0f, cooldown: 6f,
                new StatusSpec { Id = StatusIds.MentalCurrents, Duration = 2f }.Add(StatId.Dodge, 0.5f).Mult(StatId.MoveSpeed, 1.4f));
            a.SelfStatusesOnWindup = true;
        });
        return Unit(c, u =>
        {
            u.DisplayName = "Skirmisher"; u.MaxHealth = 110f; u.BaseDodge = 0.25f; u.MoveSpeed = 5f; u.AIProfile = "skirmisher";
            u.BasicAttack = strike;
            u.Actions = new List<ActionDefinition> { flow, currents };
            u.ViewPrefab = viewPrefab;
        });
    }

    private static UnitDefinition CreateHealer(GameObject viewPrefab)
    {
        const string c = "Healer";
        var repel = Action(c, "Repel", a =>
        {
            a.Id = "repel"; a.DisplayName = "Repel"; a.BehaviorId = "repel";
            a.Targeting = TargetType.Position; a.Shape = AreaShape.Line; a.Range = 15f; a.LineWidth = 6f; a.IsRanged = true;
            a.Windup = 0.5f; a.Recovery = 0.3f;
            a.Impact = ImpactType.Light; a.Damage = 11f; a.BaseAccuracy = 0.9f; a.StunDuration = 0.2f; a.StrainOnTarget = 5f;
            a.DisplaceDistance = 8f;   // the knockback
            a.StrainOnSelf = 10f;      // no cooldown, so strain is what paces it
        });
        var deluge = Action(c, "DelugeOfCreation", a =>
        {
            a.Id = "deluge_of_creation"; a.DisplayName = "Deluge of Creation"; a.BehaviorId = "charged_heal";
            a.Targeting = TargetType.Ally; a.Range = 15f; a.IsRanged = false;
            a.Windup = 0f; a.Recovery = 0.5f; a.Cooldown = 1f;
            a.Damage = 0f; a.StunDuration = 0f; a.Heal = 10f;
            a.IsCharge = true; a.MaxChargeSeconds = 3f; a.ChargeStrainPerSecond = 20f; a.ChargeHealMultiplier = 3f;   // 10 -> 30 at full charge
        });
        return Unit(c, u =>
        {
            u.DisplayName = "Healer"; u.MaxHealth = 80f; u.BaseDodge = 0.1f; u.MoveSpeed = 4f; u.AIProfile = "healer";
            u.BasicAttack = repel;
            u.Actions = new List<ActionDefinition> { deluge };
            u.ViewPrefab = viewPrefab;
        });
    }

    /// <summary>The Caster: the old Gunner reworked. Kinetic Blast is its Rifle Shot with a small knockback.</summary>
    private static UnitDefinition CreateCaster(GameObject viewPrefab)
    {
        const string c = "Caster";
        var blast = Action(c, "KineticBlast", a =>
        {
            Attack(a, "kinetic_blast", "Kinetic Blast", "basic_attack", range: 14f, windup: 0.6f, recovery: 0.5f, damage: 14f, accuracy: 0.85f, stun: 0.15f, strain: 5f);
            a.Displace = DisplaceMode.AwayFromSource; a.DisplaceDistance = 3f;
        });
        var distort = Action(c, "DistortSpace", a =>
        {
            a.Id = "distort_space"; a.DisplayName = "Distort Space"; a.BehaviorId = "area_attack";
            a.Targeting = TargetType.Position; a.Shape = AreaShape.Circle; a.Range = 10f; a.AreaRadius = 1.5f; a.IsRanged = true;   // 3 wide
            a.Windup = 0.6f; a.Recovery = 0.5f; a.Cooldown = 6f;
            a.Impact = ImpactType.Heavy; a.Damage = 17f; a.BaseAccuracy = 0.9f; a.StunDuration = 0.6f; a.StrainOnTarget = 12f;
        });
        return Unit(c, u =>
        {
            u.DisplayName = "Caster"; u.MaxHealth = 80f; u.BaseDodge = 0.15f; u.BaseBlock = 0f; u.MoveSpeed = 4f; u.AIProfile = "caster";
            u.BasicAttack = blast;
            u.Actions = new List<ActionDefinition> { distort };
            u.ViewPrefab = viewPrefab;
        });
    }

    /// <summary>The Brutebeast: the old Brawler reworked. Ravage is its Heavy Strike plus a follow-up; Charge pins enemies on walls.</summary>
    private static UnitDefinition CreateBrutebeast(GameObject viewPrefab)
    {
        const string c = "Brutebeast";
        var followup = Action(c, "RavageFollowup", a =>
        {
            // 50% more windup, recovery and damage than Ravage. Open for a moment after Ravage (RequiredStatus).
            Attack(a, "ravage_followup", "Ravage Follow-up", "basic_attack", range: MeleeStrikeRange, windup: 1.35f, recovery: 1.05f, damage: 42f, accuracy: 0.95f, stun: 0.8f, strain: 25f, heavy: true, melee: true);
            a.RequiredStatus = StatusIds.RavageReady; a.ConsumesRequiredStatus = true;
        });
        var ravage = Action(c, "Ravage", a =>
        {
            Attack(a, "ravage", "Ravage", "basic_attack", range: MeleeStrikeRange, windup: 0.9f, recovery: 0.7f, damage: 28f, accuracy: 0.95f, stun: 0.8f, strain: 25f, heavy: true, melee: true);
            // The follow-up's window: through the recovery and RavageWindow seconds after.
            a.SelfStatuses = new List<StatusSpec> { new StatusSpec { Id = StatusIds.RavageReady, Duration = 0.7f + RavageWindow } };
            a.LinkedAction = followup;
        });
        var charge = Action(c, "Charge", a =>
        {
            a.Id = "charge"; a.DisplayName = "Charge"; a.BehaviorId = "charge";
            a.Targeting = TargetType.Position; a.Range = 9f; a.IsRanged = false;   // Range: how far it runs
            a.Windup = 0.6f; a.Recovery = 0.8f; a.Cooldown = 8f;
            a.Impact = ImpactType.Heavy; a.Damage = 30f; a.BaseAccuracy = 1f; a.StunDuration = 1.25f; a.StrainOnTarget = 25f;   // on a wall hit only
            a.StrainOnSelf = 10f;
        });
        return Unit(c, u =>
        {
            u.DisplayName = "Brutebeast"; u.MaxHealth = 130f; u.BaseDodge = 0.05f; u.BaseBlock = 0.2f; u.MoveSpeed = 5f; u.AIProfile = "brutebeast";
            u.BasicAttack = ravage;
            u.Actions = new List<ActionDefinition> { followup, charge };
            u.ViewPrefab = viewPrefab;
        });
    }

    /// <summary>Seconds after Ravage's recovery its follow-up stays open.</summary>
    private const float RavageWindow = 1f;

    /// <summary>Reach of the enemy types' melee strikes (asked for as 10 and 9 range, which no melee strike here has; see notes).</summary>
    private const float MeleeStrikeRange = 1.8f;

    // ------------------------------------------------------------------ helpers

    /// <summary>Classes whose ability assets are keyed "Class/File" in ActionPatches and Descriptions, since they share file names
    /// with squad abilities they copy (the Sniper's Snipe is not the Sharpshooter's).</summary>
    private static readonly HashSet<string> QualifiedClasses = new HashSet<string> { "Sniper", "Assassin", "Skirmisher", "Healer", "Caster", "Brutebeast" };

    /// <summary>The folder name a class's abilities (under Assets/Actions) and unit (under Assets/Units) are in: the Fighter's
    /// were moved from Melee to Fighter. Folders may sit anywhere below those (Actions/Player Units/Tank): the builder finds them
    /// by name (CombatTestSceneBuilder.GetOrCreate).</summary>
    private static string ActionFolder(string cls) { return cls == "Melee" ? "Fighter" : cls; }

    private static ActionDefinition Action(string cls, string file, System.Action<ActionDefinition> init)
    {
        string key = QualifiedClasses.Contains(cls) ? cls + "/" + file : file;
        var a = CombatTestSceneBuilder.GetOrCreate<ActionDefinition>("Assets/Actions/" + ActionFolder(cls) + "/" + file + ".asset", x => { init(x); x.DataVersion = DataVersion; });
        if (a.DataVersion < DataVersion)
        {
            if (a.DataVersion < PatchBaseline)
            {
                CombatTestSceneBuilder.ResetToDefaults(a);
                init(a);
                a.Description = "";   // refilled below with the current text
            }
            else Upgrade(a, key, a.DataVersion, ActionPatches);
            a.DataVersion = DataVersion;
            CombatTestSceneBuilder.MarkDirty(a);
        }
        ApplyDescription(a, key);
        ApplyVisuals(a);
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
                new StatusSpec { Id = StatusIds.Blocking, Duration = BlockSeconds + BattleSimulation.TickLength }.Add(StatId.Block, 0.5f).With(StatusFlags.StunImmune));
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
        { "SkywardDemise", "Leap forward and high into the air and hover there for 2.5 seconds, dealing 50% more damage. You can act but not move up there. Using an ability from the air ends the hover once it lands; taking damage knocks you down, with fall damage." },
        { "Flickershots", "Fire a quick shot. Follow-up: shoot the same target up to two more times right after, cooldown or not." },
        { "Trap", "Toss out a trap that arms after a short delay. The first enemy to step in is immobilized and marked." },
        // Melee Fighter
        { "Slash", "Basic melee attack. Always available." },
        { "RapidSlices", "Quick melee attack that costs strain. Follow-up: press Space to slice again. Slicing the same target again halves the strain cost, up to 4 more slices." },
        { "SteadyStrikes", "A deliberate, steady melee strike. Transcend (hold its key, 20 strain): an empowered blow that always hits and pierces 25% block." },
        { "EvasiveStrike", "Melee attack. Increases your dodge while you swing and for a short time afterwards." },
        { "BattleTrance", "Briefly enter a trance: sheds strain and speeds up your actions. Enables Final Cut and Fatebreak. Can't be used during Wrath." },
        { "Wrath", "Enter Wrath for 6 seconds: your abilities cost no strain and deal 40% more damage, but you can't Block. Can't be used during Battle Trance, and Battle Trance can't be used during Wrath." },
        { "Fatebreak", "Trance or Wrath only, and ends it. Heavily slows the target while you wind up, then strikes it with a massive blow that knocks it far back." },
        { "PraasTruth", "A powerful melee blow that costs a lot of strain. It can take you past max strain, but then you Unravel as soon as it lands. Transcend (hold its key): no windup, for double the strain." },
        { "EtchingOfVoracity", "Sacrifice 25 health to shed 35 strain." },
        { "LethalPrescience", "A slow, deliberate strike that deals triple damage to a stunned target." },
        { "SwiftEngagement", "An incredibly fast strike that costs 15 strain." },
        { "FinalCut", "Trance or Wrath only. A finishing blow that deals more damage the more health the target is missing. A kill extends your Trance or Wrath by 3 seconds." },
        // Tank
        { "MaceStrike", "Basic melee attack. Always available." },
        { "ShieldSlam", "Heavy shield blow: a long stun that interrupts windups and builds strain." },
        { "ChainHook", "Hook an enemy and drag them to you, stunning them briefly." },
        { "Shockwave", "Slam the ground, stunning every enemy around you and building their strain." },
        { "Bash", "Shove enemies in front of you back and damage them. Anyone driven into a wall takes double damage and stun." },
        { "BehemothsArrival", "Blink to a point and slam down: every enemy within 3m takes a heavy hit, is stunned and is knocked into the air, taking fall damage when it lands." },
        { "Sanctity", "Emit a dome of protection around you for 1.5 seconds. Projectiles headed into it are drawn to you, and melee attacks on allies inside it hit you instead." },
        { "HereticsAscent", "Grab a target, interrupting and stunning it, and carry it up into the air. After a moment at the top, slam it into the ground behind you: it takes fall damage and is stunned. You land unhurt, but taking damage on the way up drops you both, stunned, and you both take fall damage." },
        { "Brace", "Raise your guard for +50% block for 5 seconds and become immune to stun (except Unravel). Hits don't wear it down." },
        { "Subjugate", "Become Unstoppable while you wind up (no stun or interrupt can stop you, except Unravel), then grab a target, stunning it for 2.5s and carrying it with you. While you hold it you are immune to stun and can only move, and you headbutt it every 0.5s for 10 damage; after each headbutt you can let go early. When you let go or the stun ends, pick a direction and wind up a short throw that hurls it away for 15 damage." },
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
        { "Chronosphere", "Channel: a sphere around you that halves enemies' action speed for up to 2.5 seconds. You can't act or cancel it while channeling, and any damage or stun breaks it." },
        { "SecondMind", "Speed up an ally's actions by 1.5x, or slow an enemy's by 1.5x, for 3 seconds. No hit roll." },
        { "WrinklesOfReality", "Pick an ally, an enemy or yourself, then a point within 10m of them: they teleport there. No hit roll." },
        { "Amblivate", "Stun an enemy for 1.5s and lift them 7m into the air, then drop them: they take fall damage." },
        // Universal
        { "Dash", "Key G. Dash toward a point as fast as a dodge, up to your dash distance (costs 20 strain). You can't act until you land. Transcend (hold G, +15 strain): dash 80% further, much faster." },
        { "Block", "Raise your guard for 1 second (costs 20 strain, even past your max): +50% block and immune to stun, except Unravel. Getting hit while guarding lets you act again right away, but a heavy hit puts Block on cooldown for 1s. Follow-up: press Space to block again. Transcend (hold its key, +15 strain): blocks all damage and heavy hits don't put it on cooldown." },
        // Enemies
        { "HeavyStrike", "Slow, heavy melee blow that stuns." },
        { "Sniper/RifleShot", "Basic ranged attack. Always available." },
        { "Sniper/Snipe", "Enter an aiming stance and snipe a target. Cancels if the target leaves line of sight." },
        { "Sniper/MarkingShot", "Fire a shot that marks the target (attacks hit it more easily) and become undetectable for a short time. Taking damage reveals you." },
        { "Assassin/Strike", "A basic melee strike." },
        { "Assassin/Polystep", "Blink to a point." },
        { "Assassin/HiddenStrike", "Become hidden while winding up, then dash to the target and deliver a strong strike." },
        { "Skirmisher/Strike", "A basic melee strike." },
        { "Skirmisher/FlowingDeath", "A melee attack. Each one used in a row winds up 0.1s faster, down to 0.1s." },
        { "Skirmisher/MentalCurrents", "Gain +50% dodge and faster movement for 2 seconds." },
        { "Healer/Repel", "Strike in a wide line in front, damaging enemies and pushing them away." },
        { "Caster/KineticBlast", "Basic ranged attack that knocks the target back a short way." },
        { "Caster/DistortSpace", "Distort space at a point: after a short windup it detonates, a heavy impact that damages and stuns everyone in a small circle." },
        { "Brutebeast/Ravage", "A slow, heavy melee blow that stuns. Opens Ravage Follow-up for a moment afterwards." },
        { "Brutebeast/RavageFollowup", "Right after Ravage only: another strike with 50% more windup, recovery and damage." },
        { "Brutebeast/Charge", "After a windup, charge in a direction, dragging any enemy in the way along. Slam them into a wall for a heavy impact: big damage and a long stun." },
        { "Healer/DelugeOfCreation", "Charge: strain builds while you charge (any damage breaks it), then release it at an ally to heal them; up to 3x the heal at full charge." },
    };

    /// <summary>Gives an ability its look (ActionDefinition.Visuals) if it has none yet: an asset's own, once set (here or in
    /// the Inspector), is never overwritten. Abilities not listed keep Auto, and CombatVfx works a look out from them.</summary>
    public static void ApplyVisuals(ActionDefinition a)
    {
        ActionVisuals v;
        if (a == null || (a.Visuals != null && a.Visuals.IsSet) || !Visuals.TryGetValue(a.Id ?? "", out v)) return;
        a.Visuals = new ActionVisuals(v.Kind, v.Color, v.Width, v.Pierce);
        CombatTestSceneBuilder.MarkDirty(a);
    }

    // Per-ability looks (by ActionDefinition.Id): what each one's Visuals starts as.
    private static readonly Dictionary<string, ActionVisuals> Visuals = new Dictionary<string, ActionVisuals>
    {
        // Sharpshooter
        { "snipe", new ActionVisuals(VfxKind.Tracer, new Color(1f, 0.25f, 0.2f), 0.1f) },
        { "tracking_shot", new ActionVisuals(VfxKind.Tracer, new Color(0.5f, 0.95f, 1f), 0.13f, true) },
        { "snapshot", new ActionVisuals(VfxKind.Tracer, new Color(1f, 0.9f, 0.4f), 0.04f) },
        { "marking_shot", new ActionVisuals(VfxKind.Tracer, new Color(1f, 0.35f, 0.9f), 0.05f) },
        { "take_aim", new ActionVisuals(VfxKind.Buff, new Color(1f, 0.3f, 0.25f)) },
        { "retaliate", new ActionVisuals(VfxKind.Buff, new Color(1f, 0.6f, 0.2f)) },
        { "trap", new ActionVisuals(VfxKind.Lob, new Color(0.4f, 0.85f, 1f)) },
        { "rifle_shot", new ActionVisuals(VfxKind.Tracer, new Color(1f, 0.95f, 0.7f), 0.035f) },
        // Melee Fighter
        { "slash", new ActionVisuals(VfxKind.Slash, new Color(0.95f, 0.95f, 1f), 0.1f) },
        { "rapid_slices", new ActionVisuals(VfxKind.Slash, new Color(1f, 0.9f, 0.35f), 0.06f) },
        { "steady_strikes", new ActionVisuals(VfxKind.Slash, new Color(1f, 0.7f, 0.35f), 0.14f) },
        { "evasive_strike", new ActionVisuals(VfxKind.Slash, new Color(0.4f, 1f, 0.95f), 0.1f) },
        { "battle_trance", new ActionVisuals(VfxKind.Buff, new Color(0.75f, 0.35f, 1f)) },
        { "final_cut", new ActionVisuals(VfxKind.Slash, new Color(1f, 0.1f, 0.2f), 0.28f) },
        { "wrath", new ActionVisuals(VfxKind.Buff, new Color(1f, 0.2f, 0.15f)) },
        { "fatebreak", new ActionVisuals(VfxKind.Slash, new Color(0.75f, 0.1f, 0.45f), 0.36f) },
        // Tank
        { "mace_strike", new ActionVisuals(VfxKind.Slash, new Color(0.9f, 0.8f, 0.5f), 0.16f) },
        { "shield_slam", new ActionVisuals(VfxKind.Shockwave, new Color(0.55f, 0.75f, 1f), 0.12f) },
        { "chain_hook", new ActionVisuals(VfxKind.Chain, new Color(0.75f, 0.75f, 0.8f), 0.06f) },
        { "shockwave", new ActionVisuals(VfxKind.Shockwave, new Color(0.95f, 0.8f, 0.55f), 0.16f) },
        { "brace", new ActionVisuals(VfxKind.Buff, new Color(0.4f, 0.65f, 1f)) },
        { "phantom_strike", new ActionVisuals(VfxKind.Slash, new Color(0.55f, 0.9f, 1f), 0.14f) },
        { "mesmerize", new ActionVisuals(VfxKind.Buff, new Color(0.85f, 0.5f, 1f)) },
        { "mesmer_strike", new ActionVisuals(VfxKind.Slash, new Color(0.85f, 0.5f, 1f), 0.07f) },
        { "bash", new ActionVisuals(VfxKind.Beam, new Color(0.9f, 0.75f, 0.45f), 0.3f) },
        { "subjugate", new ActionVisuals(VfxKind.Slash, new Color(0.85f, 0.6f, 0.35f), 0.2f) },
        { "subjugate_headbutt", new ActionVisuals(VfxKind.Slash, new Color(0.95f, 0.8f, 0.55f), 0.08f) },
        { "torrent_of_strife", new ActionVisuals(VfxKind.Beam, new Color(0.85f, 0.25f, 0.5f), 0.3f) },
        { "stutter", new ActionVisuals(VfxKind.Bolt, new Color(0.95f, 0.95f, 1f), 0.04f) },
        { "lance_of_destiny", new ActionVisuals(VfxKind.Beam, new Color(1f, 0.85f, 0.35f), 0.25f) },
        // Universal
        { "block", new ActionVisuals(VfxKind.Buff, new Color(0.55f, 0.85f, 1f)) },
        // Paradigm User
        { "spark", new ActionVisuals(VfxKind.Bolt, new Color(0.45f, 0.9f, 1f), 0.05f) },
        { "reconstruct", new ActionVisuals(VfxKind.Heal, new Color(0.35f, 1f, 0.5f)) },
        { "rift_line", new ActionVisuals(VfxKind.Beam, new Color(0.7f, 0.35f, 1f), 0.2f) },
        { "cataclysm", new ActionVisuals(VfxKind.Shockwave, new Color(0.65f, 0.3f, 1f), 0.25f) },
        { "second_mind", new ActionVisuals(VfxKind.Bolt, new Color(0.45f, 1f, 1f), 0.05f) },
        { "wrinkles_of_reality", new ActionVisuals(VfxKind.Bolt, new Color(0.75f, 0.45f, 1f), 0.05f) },
        { "amblivate", new ActionVisuals(VfxKind.Bolt, new Color(0.95f, 0.9f, 1f), 0.06f) },
        // Enemy types
        { "sniper_snipe", new ActionVisuals(VfxKind.Tracer, new Color(1f, 0.25f, 0.2f), 0.1f) },
        { "sniper_marking_shot", new ActionVisuals(VfxKind.Tracer, new Color(1f, 0.35f, 0.9f), 0.05f) },
        { "sniper_rifle_shot", new ActionVisuals(VfxKind.Tracer, new Color(1f, 0.95f, 0.7f), 0.035f) },
        { "assassin_strike", new ActionVisuals(VfxKind.Slash, new Color(0.7f, 0.6f, 1f), 0.09f) },
        { "hidden_strike", new ActionVisuals(VfxKind.Slash, new Color(0.45f, 0.3f, 0.9f), 0.18f) },
        { "polystep", new ActionVisuals(VfxKind.Buff, new Color(0.6f, 0.5f, 1f)) },
        { "skirmisher_strike", new ActionVisuals(VfxKind.Slash, new Color(1f, 0.8f, 0.6f), 0.09f) },
        { "flowing_death", new ActionVisuals(VfxKind.Slash, new Color(1f, 0.45f, 0.35f), 0.07f) },
        { "mental_currents", new ActionVisuals(VfxKind.Buff, new Color(0.45f, 0.95f, 0.85f)) },
        { "repel", new ActionVisuals(VfxKind.Beam, new Color(0.6f, 1f, 0.75f), 0.3f) },
        { "deluge_of_creation", new ActionVisuals(VfxKind.Heal, new Color(0.35f, 1f, 0.6f)) },
        { "kinetic_blast", new ActionVisuals(VfxKind.Bolt, new Color(0.6f, 0.85f, 1f), 0.06f) },
        { "distort_space", new ActionVisuals(VfxKind.Shockwave, new Color(0.55f, 0.3f, 1f), 0.18f) },
        { "ravage", new ActionVisuals(VfxKind.Slash, new Color(1f, 0.55f, 0.2f), 0.18f) },
        { "ravage_followup", new ActionVisuals(VfxKind.Slash, new Color(1f, 0.3f, 0.15f), 0.26f) },
        { "charge", new ActionVisuals(VfxKind.Beam, new Color(0.85f, 0.55f, 0.3f), 0.35f) },
        // Round 12
        { "skyward_demise", new ActionVisuals(VfxKind.Buff, new Color(0.6f, 0.9f, 1f)) },
        { "flickershots", new ActionVisuals(VfxKind.Tracer, new Color(0.7f, 1f, 0.95f), 0.03f) },
        { "etching_of_voracity", new ActionVisuals(VfxKind.Buff, new Color(0.85f, 0.1f, 0.2f)) },
        { "praas_truth", new ActionVisuals(VfxKind.Slash, new Color(1f, 0.85f, 0.35f), 0.3f) },
        { "behemoths_arrival", new ActionVisuals(VfxKind.Shockwave, new Color(0.9f, 0.7f, 0.4f), 0.25f) },
        // Enemies
        { "heavy_strike", new ActionVisuals(VfxKind.Slash, new Color(1f, 0.55f, 0.2f), 0.18f) },
        // Round 13
        { "lethal_prescience", new ActionVisuals(VfxKind.Slash, new Color(0.85f, 0.9f, 1f), 0.2f) },
        { "swift_engagement", new ActionVisuals(VfxKind.Slash, new Color(0.6f, 1f, 0.9f), 0.05f) },
        { "sanctity", new ActionVisuals(VfxKind.Buff, new Color(1f, 0.88f, 0.45f)) },
        { "heretics_ascent", new ActionVisuals(VfxKind.Slash, new Color(0.85f, 0.45f, 0.3f), 0.22f) },
        { "chronosphere", new ActionVisuals(VfxKind.Buff, new Color(0.5f, 0.6f, 1f)) },
    };

    // Earlier default texts, replaced by the current ones where an asset still has one of them unedited. When a
    // description above changes, add the text it had here.
    private static readonly Dictionary<string, string[]> OldDescriptions = new Dictionary<string, string[]>
    {
        { "BattleTrance", new[] { "Briefly enter a trance: sheds strain and speeds up your actions. Enables Final Cut." } },
        { "FinalCut", new[] { "Trance only. A finishing blow that deals more damage the more health the target is missing. A kill extends your trance by 3 seconds." } },
        { "Brace", new[] { "Raise your guard for +50% block for 5 seconds. Hits don't wear it down." } },
        { "Subjugate", new[] { "Become immune to stun and grab a target, stunning it for 2.5s and carrying it with you. While you hold it you can only move, and you headbutt it every 0.5s for 10 damage; after each headbutt you can let go early. When you let go or the stun ends, wind up a short throw that hurls it away for 15 damage." } },
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

    /// <summary>Unstoppable for a windup (SelfStatusesDuringWindup: its Duration is ignored): no stun or interrupt but Unravel.</summary>
    private static StatusSpec UnstoppableWindup() { return new StatusSpec { Id = StatusIds.Unstoppable, Duration = 0f }.With(StatusFlags.Unstoppable); }

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
        var u = CombatTestSceneBuilder.GetOrCreate<UnitDefinition>("Assets/Units/" + ActionFolder(cls) + "/" + cls + ".asset", x => { init(x); x.DataVersion = DataVersion; });
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
