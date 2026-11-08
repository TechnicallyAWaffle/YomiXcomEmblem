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
    public struct Enemies { public UnitDefinition Sniper, Assassin, Skirmisher, Healer, Caster, Brutebeast, Wraith, Monarch, Zola, Primavaeran, Exemplar, Deliverance, Syne, Alio, Revl, Wex; }

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
            Attack(a, "snipe", "Snipe", "snipe", range: 25f, windup: 1.6f, recovery: 0.6f, damage: 45f, accuracy: 1f, stun: 1f, strain: 25f, heavy: true);
            a.Cooldown = 5f;
        });
        var snapshot = Action(c, "Snapshot", a =>
        {
            Attack(a, "snapshot", "Snapshot", "basic_attack", range: 18f, windup: 0.35f, recovery: 0.4f, damage: 20f, accuracy: 1f, stun: 0.25f, strain: 8f);
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
            Attack(a, "marking_shot", "Marking Shot", "basic_attack", range: 18f, windup: 0.6f, recovery: 0.4f, damage: 10f, accuracy: 1f, stun: 0.2f, strain: 4f);
            a.Cooldown = 10f;
            a.TargetStatusesOnHit = new List<StatusSpec> { new StatusSpec { Id = StatusIds.Marked, Duration = 8f }.Add(StatId.IncomingAccuracy, 0.25f).Add(StatId.Dodge, -0.25f) };
            a.SelfStatuses = new List<StatusSpec> { new StatusSpec { Id = StatusIds.Hidden, Duration = 4f }.With(StatusFlags.Untargetable | StatusFlags.BreaksOnDamage) };
        });
        var tracking = Action(c, "TrackingShot", a =>
        {
            Attack(a, "tracking_shot", "Tracking Shot", "basic_attack", range: 22f, windup: 2.2f, recovery: 0.6f, damage: 35f, accuracy: 1f, stun: 0.8f, strain: 20f, heavy: true);
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
                new StatusSpec { Id = StatusIds.Marked, Duration = 8f }.Add(StatId.IncomingAccuracy, 0.25f).Add(StatId.Dodge, -0.25f),
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
            Attack(a, "flickershots", "Flickershots", "chain_attack", range: 10f, windup: 0.3f, recovery: 0.2f, damage: 8f, accuracy: 1f, stun: 0.2f, strain: 4f);
            a.Cooldown = 8f;
            a.ChainFollowups = 2; a.ChainCostMultiplier = 1f; a.ChainWindow = 1f;   // two more on the same target, cooldown or not
            a.IsFollowup = true;
        });

        var flurry = Action(c, "Flurry", a =>
        {
            // 4s of 2.5x action speed and +50% dodge, but only the basic attack can be used (BasicAttackOnly).
            SelfBuff(a, "flurry", "Flurry", "self_buff", windup: 0.7f, recovery: 0f, cooldown: 17f,
                new StatusSpec { Id = StatusIds.Flurry, Duration = 4f }.Mult(StatId.ActionSpeed, 2.5f).Add(StatId.Dodge, 0.5f).With(StatusFlags.BasicAttackOnly));
            a.StrainOnSelf = 30f;
        });
        var pierce = Action(c, "PierceTheWound", a =>
        {
            Attack(a, "pierce_the_wound", "Pierce The Wound", "basic_attack", range: 11f, windup: 0.3f, recovery: 0.3f, damage: 12f, accuracy: 1f, stun: 0.3f, strain: 6f);
            a.Cooldown = 9f;
            a.StunnedDamageMultiplier = 2f;   // double damage on a stunned target
        });
        var extricate = Action(c, "Extricate", a =>
        {
            // A heavy shot whose recoil blasts the Sharpshooter 5m back (RecoilShotBehavior).
            Attack(a, "extricate", "Extricate", "recoil_shot", range: 11f, windup: 0.4f, recovery: 0.7f, damage: 16f, accuracy: 1f, stun: 0.7f, strain: 10f, heavy: true);
            a.Cooldown = 11f;
            a.Params = new List<BehaviorParam> { new BehaviorParam("recoil", 5f) };
        });
        var unmaker = Action(c, "MarkOfTheUnmaker", a =>
        {
            // Stacks Unmake; a target already at 3 is Fated for 1s, then Unravels (UnmakerBehavior). Transcended: hidden while winding up.
            Attack(a, "mark_of_the_unmaker", "Mark of the Unmaker", "unmaker", range: 10f, windup: 1f, recovery: 1f, damage: 5f, accuracy: 1f, stun: 0f, strain: 3f);
            a.Cooldown = 0f;
            a.CanTranscend = true; a.TranscendStrain = 15f; a.TranscendWindupMultiplier = 1f;
        });

        var sharpshooter = Unit(c, u =>
        {
            u.DisplayName = "Sharpshooter"; u.MaxHealth = 85f; u.BaseDodge = 0.15f; u.MoveSpeed = 4f;
            u.BasicAttack = rifleShot;
            u.Actions = new List<ActionDefinition> { snipe, tracking, marking, snapshot, flicker, takeAim, retaliate, trap, skyward };
            u.ViewPrefab = viewPrefab;
        }, new Dictionary<int, System.Action<UnitDefinition>> { { 12, u => { AddActions(u, snapshot, flicker); AddActions(u, null, skyward); } } });
        // Soulseeker: a shot, then a channel in which the bullet goes on to new targets (SoulseekerBehavior, IRedirectable).
        var soulseeker = Action(c, "Soulseeker", a =>
        {
            Attack(a, "soulseeker", "Soulseeker", "soulseeker", range: 20f, windup: 0.75f, recovery: 0.3f, damage: 15f, accuracy: 1f, stun: 0.5f, strain: 7f);
            a.Cooldown = 11f;
            a.ChannelSeconds = 4f;   // at most: it ends once the redirects are spent or nothing is left to hit
            a.Params = new List<BehaviorParam> { new BehaviorParam("redirect_range", 15f), new BehaviorParam("redirect_delay", 0.25f), new BehaviorParam("max_redirects", 4f) };
        });
        var cloak = Action(c, "RefractionCloak", a =>
            SelfBuff(a, "refraction_cloak", "Refraction Cloak", "self_buff", windup: 0.4f, recovery: 0.1f, cooldown: 16f,
                new StatusSpec { Id = StatusIds.Hidden, Duration = 5f }.With(StatusFlags.Untargetable | StatusFlags.BreaksOnDamage)));
        // Destiny Closes In -> Fate Encumbers Its Prey -> Witness To The End: shots from the air (SkyShotBehavior). Each one that
        // lands on its target opens the next at that target (SelfStatusesOnHit + RequiredStatusOnTarget); FollowUps brings them along.
        var witness = Action(c, "WitnessToTheEnd", a =>
        {
            Attack(a, "witness_to_the_end", "Witness To The End", "sky_shot", range: 16f, windup: 1f, recovery: 0.5f, damage: 30f, accuracy: 1f, stun: 1f, strain: 15f, heavy: true);
            a.MissingHealthDamage = 0.2f;
            a.RequiredStatus = WitnessReady; a.ConsumesRequiredStatus = true; a.RequiredStatusOnTarget = true;
        });
        var fate = Action(c, "FateEncumbersItsPrey", a =>
        {
            Attack(a, "fate_encumbers_its_prey", "Fate Encumbers Its Prey", "sky_shot", range: 16f, windup: 0.4f, recovery: 0.4f, damage: 15f, accuracy: 1f, stun: 0.5f, strain: 7f);
            a.TargetStatusesOnHit = new List<StatusSpec> { new StatusSpec { Id = StatusIds.Fated, Duration = 1.5f }.Mult(StatId.ActionSpeed, 0.35f) };
            a.SelfStatusesOnHit = new List<StatusSpec> { ChainOpen(WitnessReady, 0.4f) };
            a.RequiredStatus = FateReady; a.ConsumesRequiredStatus = true; a.RequiredStatusOnTarget = true;
            a.FollowUps = new List<ActionDefinition> { witness };
        });
        var destiny = Action(c, "DestinyClosesIn", a =>
        {
            Attack(a, "destiny_closes_in", "Destiny Closes In", "sky_shot", range: 16f, windup: 0.4f, recovery: 0.4f, damage: 10f, accuracy: 1f, stun: 0.5f, strain: 5f);
            a.Cooldown = 14f;
            a.DisplaceDistance = 5f;   // flight height
            a.TargetStatusesOnHit = new List<StatusSpec> { new StatusSpec { Id = StatusIds.Marked, Duration = 2.5f }.Add(StatId.IncomingAccuracy, 0.25f).Add(StatId.Dodge, -0.25f) };
            a.SelfStatusesOnHit = new List<StatusSpec> { ChainOpen(FateReady, 0.4f) };
            a.FollowUps = new List<ActionDefinition> { fate };
        });
        Grant(sharpshooter, null, flurry, pierce, extricate, unmaker, soulseeker, cloak, destiny);

        // Round AF.
        var fixation = Action(c, "DreadfulFixation", a =>
        {
            // A status spell: ROOTED for 2s, no hit roll.
            a.Id = "dreadful_fixation"; a.DisplayName = "Dreadful Fixation"; a.BehaviorId = "status_spell";
            a.Targeting = TargetType.Enemy; a.Range = 17f; a.IsRanged = true;
            a.Windup = 0.4f; a.Recovery = 1f; a.Cooldown = 10f;
            a.Damage = 0f; a.StunDuration = 0f; a.StrainOnSelf = 10f;
            a.TargetStatusesOnHit = new List<StatusSpec> { new StatusSpec { Id = StatusIds.Rooted, Duration = 2f, AppliesTo = StatusTarget.Enemies }.With(StatusFlags.Rooted) };
        });
        var pursuit = Action(c, "CripplingPursuit", a =>
        {
            // A dash (no range was given: 6m, twice the Sharpshooter's dash), then 2s of special rounds: 30% less damage, but
            // every hit CRIPPLES for 1s (ArmsHits puts TargetStatusesOnHit on them: CripplingPursuitBehavior).
            a.Id = "crippling_pursuit"; a.DisplayName = "Crippling Pursuit"; a.BehaviorId = "crippling_pursuit";
            a.Targeting = TargetType.Position; a.Range = 6f; a.IsRanged = false;
            a.Windup = 0.3f; a.Recovery = 0f; a.Cooldown = 7f;
            a.Damage = 0f; a.StunDuration = 0f; a.StrainOnSelf = 10f;
            a.SelfStatuses = new List<StatusSpec> { new StatusSpec { Id = StatusIds.CripplingRounds, Duration = 2f }.Mult(StatId.DamageDealt, 0.7f).With(StatusFlags.ArmsHits) };
            a.TargetStatusesOnHit = new List<StatusSpec>
            {
                new StatusSpec { Id = StatusIds.Crippled, Duration = 1f, AppliesTo = StatusTarget.Enemies }.Mult(StatId.MoveSpeed, 0.5f).Mult(StatId.DashDistance, 0.5f).With(StatusFlags.NoDash),
            };
        });
        var mentalWeapon = Action(c, "MentalWeapon", a =>
        {
            // A shot into an ally (no hit roll): MENTAL WEAPON for 3s. Their next single-target damage fires the bullet 0.5s
            // later at that target: this ability's heavy hit (13, 0.9s stun; MentalWeaponBehavior). No range was given: 15m.
            Attack(a, "mental_weapon", "Mental Weapon", "mental_weapon", range: 15f, windup: 0.7f, recovery: 0.5f, damage: 13f, accuracy: 1f, stun: 0.9f, strain: 7f, heavy: true);
            a.Targeting = TargetType.Ally; a.CannotTargetSelf = true;
            a.Cooldown = 9f; a.StrainOnSelf = 15f;
            a.TargetStatusesOnHit = new List<StatusSpec> { new StatusSpec { Id = StatusIds.MentalWeapon, Duration = 3f, AppliesTo = StatusTarget.Allies } };
            a.Params = new List<BehaviorParam> { new BehaviorParam("fire_delay", 0.5f) };
        });
        Grant(sharpshooter, null, fixation, pursuit, mentalWeapon);
        return sharpshooter;
    }

    // ------------------------------------------------------------------ Melee Fighter: close-range damage, very evasive, energy

    private static UnitDefinition CreateMelee(GameObject viewPrefab)
    {
        const string c = "Melee";
        var slash = Action(c, "Slash", a =>
            Attack(a, "slash", "Slash", "basic_attack", range: 1.8f, windup: 0.35f, recovery: 0.35f, damage: 8f, accuracy: 1f, stun: 0.3f, strain: 4f, melee: true));
        var rapid = Action(c, "RapidSlices", a =>
        {
            Attack(a, "rapid_slices", "Rapid Slices", "rapid_slices", range: 1.8f, windup: 0.25f, recovery: 0.2f, damage: 7f, accuracy: 1f, stun: 0.25f, strain: 4f, melee: true);
            a.StrainOnSelf = 12f;   // halved while the chain is open (RapidSlicesBehavior)
            a.IsFollowup = true;
        });
        var steady = Action(c, "SteadyStrikes", a =>
        {
            // Focused Strike's stats; Transcended (20 strain) it gets Focused Strike+'s (SteadyStrikesBehavior).
            Attack(a, "steady_strikes", "Steady Strikes", "steady_strikes", range: 1.8f, windup: 0.6f, recovery: 0.5f, damage: 16f, accuracy: 1f, stun: 0.5f, strain: 8f, melee: true);
            a.CanTranscend = true; a.TranscendStrain = 20f;   // what the old follow-up cost
        });
        var evasive = Action(c, "EvasiveStrike", a =>
        {
            Attack(a, "evasive_strike", "Evasive Strike", "basic_attack", range: 1.8f, windup: 0.45f, recovery: 0.45f, damage: 10f, accuracy: 1f, stun: 0.3f, strain: 5f, melee: true);
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
            Attack(a, "fatebreak", "Fatebreak", "fatebreak", range: 2f, windup: 1.1f, recovery: 0.7f, damage: 50f, accuracy: 1f, stun: 1.5f, strain: 30f, heavy: true, melee: true);
            a.RequiredStatus = StatusIds.Trance + "|" + StatusIds.Wrath; a.ConsumesRequiredStatus = true; a.ConsumeOnResolve = true;
            a.DisplaceDistance = 6f;   // the knockback (FatebreakBehavior)
            a.TargetStatusesDuringWindup = new List<StatusSpec> { new StatusSpec { Id = StatusIds.Fated }.Mult(StatId.ActionSpeed, 0.35f) };
            a.ExtraCinematic = true;
        });
        var phantom = Action(c, "PhantomStrike", a =>
        {
            // Range is how far the dash reaches; the slash lands once it arrives (PhantomStrikeBehavior).
            Attack(a, "phantom_strike", "Phantom Strike", "phantom_strike", range: 7f, windup: 0.25f, recovery: 0.2f, damage: 10f, accuracy: 1f, stun: 0.3f, strain: 5f, melee: true);
            a.Cooldown = 4f;
            a.TargetStatusesOnHit = new List<StatusSpec> { new StatusSpec { Id = StatusIds.Marked, Duration = 6f }.Add(StatId.IncomingAccuracy, 0.25f).Add(StatId.Dodge, -0.25f) };
        });
        var mesmerStrike = Action(c, "MesmerStrike", a =>
            Attack(a, "mesmer_strike", "Mesmer Strike", "basic_attack", range: 2.2f, windup: 0.12f, recovery: 0.08f, damage: 6f, accuracy: 1f, stun: 0.2f, strain: 3f, melee: true));
        var mesmerize = Action(c, "Mesmerize", a =>
        {
            SelfBuff(a, "mesmerize", "Mesmerize", "mesmerize", windup: 0.1f, recovery: 0f, cooldown: 7f,
                new StatusSpec { Id = StatusIds.Mesmerized, Duration = 1.5f }.Add(StatId.Dodge, 0.6f));
            a.LinkedAction = mesmerStrike;   // the counter's rapid strikes
        });
        var finalCut = Action(c, "FinalCut", a =>
        {
            Attack(a, "final_cut", "Final Cut", "final_cut", range: 2f, windup: 0.5f, recovery: 0.8f, damage: 30f, accuracy: 1f, stun: 1f, strain: 20f, heavy: true, melee: true);
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
            Attack(a, "praas_truth", "Praa's Truth", "basic_attack", range: 2f, windup: 0.7f, recovery: 0.4f, damage: 32f, accuracy: 1f, stun: 0.9f, strain: 20f, heavy: true, melee: true);
            a.StrainOnSelf = 30f; a.Cooldown = 3f;
            a.CanTranscend = true; a.TranscendStrain = 30f; a.TranscendWindupMultiplier = 0f;   // Transcended: no windup, double the strain
            a.UnravelsOverMaxStrain = true;   // usable past max strain, but then the Fighter Unravels once it lands
        });

        var prescience = Action(c, "LethalPrescience", a =>
        {
            // Asked for as 9 range: the Fighter's melee reach (as with the enemy types' strikes).
            Attack(a, "lethal_prescience", "Lethal Prescience", "basic_attack", range: 1.8f, windup: 0.9f, recovery: 0.2f, damage: 12f, accuracy: 1f, stun: 0.2f, strain: 6f, melee: true);
            a.StunnedDamageMultiplier = 3f;   // triple damage on a stunned target
        });
        var swift = Action(c, "SwiftEngagement", a =>
        {
            Attack(a, "swift_engagement", "Swift Engagement", "basic_attack", range: 1.8f, windup: 0.2f, recovery: 0.1f, damage: 15f, accuracy: 1f, stun: 0.7f, strain: 7f, melee: true);
            a.StrainOnSelf = 15f;
        });

        var cuts = Action(c, "AMillionCuts", a =>
        {
            // Phantom Strike's dash (Range is its reach), then a 2s channel of 15 cuts (FlurryBehavior). Channels can't be cancelled.
            Attack(a, "a_million_cuts", "A Million Cuts", "flurry", range: 7f, windup: 0.2f, recovery: 1f, damage: 3f, accuracy: 1f, stun: 0.2f, strain: 1f, melee: true);
            a.Cooldown = 19f; a.StrainOnSelf = 25f;
            a.ChannelSeconds = 2f;
            a.Params = new List<BehaviorParam> { new BehaviorParam("strikes", 15f) };
        });
        var fighterPolystep = Action(c, "Polystep", a =>
        {
            // The Assassin's Polystep, 2s more cooldown.
            a.Id = "fighter_polystep"; a.DisplayName = "Polystep"; a.BehaviorId = "blink";
            a.Targeting = TargetType.Position; a.Range = 12f; a.IsRanged = false;
            a.Windup = 0.5f; a.Recovery = 0.25f; a.Cooldown = 8f;
            a.Damage = 0f; a.StunDuration = 0f;
        });
        var hunt = Action(c, "TheEndlessHunt", a =>
        {
            // Fated on the target through the long windup (as Fatebreak), then a blink to it, sight or not (HuntBehavior).
            a.Id = "the_endless_hunt"; a.DisplayName = "The Endless Hunt"; a.BehaviorId = "hunt";
            a.Targeting = TargetType.Enemy; a.Range = 50f; a.IsRanged = false; a.PiercesTerrain = true;
            a.Windup = 1.25f; a.Recovery = 0.1f; a.Cooldown = 17f;
            a.Damage = 0f; a.StunDuration = 0f; a.StrainOnSelf = 15f;
            a.TargetStatusesDuringWindup = new List<StatusSpec> { new StatusSpec { Id = StatusIds.Fated }.Mult(StatId.ActionSpeed, 0.35f) };
        });
        var you = Action(c, "You", a =>
        {
            // A chain onto an enemy (ChainTetherBehavior): no hit of its own. Pulled more than 5m from the Fighter for 0.1s,
            // it snaps: the enemy is yanked back and takes the damage and stun (sure to hit). Lasts 3s, snaps once.
            Attack(a, "you", "You.", "chain_tether", range: 5f, windup: 0.75f, recovery: 0.1f, damage: 6f, accuracy: 1f, stun: 0.3f, strain: 3f, melee: true);
            a.Cooldown = 14f; a.StrainOnSelf = 20f;
            a.Params = new List<BehaviorParam> { new BehaviorParam("max_length", 5f), new BehaviorParam("snap_delay", 0.1f), new BehaviorParam("duration", 3f), new BehaviorParam("snaps", 1f), new BehaviorParam("yank_to", 0.5f) };
        });

        var fighter = Unit(c, u =>
        {
            u.DisplayName = "Fighter"; u.MaxHealth = 100f; u.BaseDodge = 0.35f; u.MoveSpeed = 5.5f; u.DashDistance = 3.5f;
            u.MaxEnergy = 0f; u.StartingEnergy = 0f;   // the Fighter runs on strain, not energy
            u.Passives = new List<string> { Passives.StrainReliefOnDodge };
            u.BasicAttack = slash;
            u.Actions = new List<ActionDefinition> { finalCut, fatebreak, praa, prescience, steady, rapid, swift, evasive, trance, wrath, etching, phantom, mesmerize };   // newer ones: Grant, below
            u.ViewPrefab = viewPrefab;
        }, new Dictionary<int, System.Action<UnitDefinition>>
        {
            { 9, u => { AddActions(u, finalCut, fatebreak); AddActions(u, trance, wrath); } },
            { 12, u => { AddActions(u, fatebreak, praa); AddActions(u, wrath, etching); } },
            { 13, u => { AddActions(u, praa, prescience); AddActions(u, rapid, swift); } },
        });
        Grant(fighter, null, cuts, fighterPolystep, hunt, you);
        return fighter;
    }

    // ------------------------------------------------------------------ Tank: durable, crowd control and disables
    // The design doc lists no Tank abilities yet; these are placeholders built from the same rules.

    private static UnitDefinition CreateTank(GameObject viewPrefab)
    {
        const string c = "Tank";
        var mace = Action(c, "MaceStrike", a =>
            Attack(a, "mace_strike", "Mace Strike", "basic_attack", range: 1.9f, windup: 0.5f, recovery: 0.5f, damage: 10f, accuracy: 1f, stun: 0.4f, strain: 5f, melee: true));
        var slam = Action(c, "ShieldSlam", a =>
        {
            Attack(a, "shield_slam", "Shield Slam", "basic_attack", range: 1.9f, windup: 0.7f, recovery: 0.6f, damage: 18f, accuracy: 1f, stun: 1.5f, strain: 20f, heavy: true, melee: true);
            a.Cooldown = 6f;
        });
        var hook = Action(c, "ChainHook", a =>
        {
            Attack(a, "chain_hook", "Chain Hook", "basic_attack", range: 9f, windup: 0.6f, recovery: 0.5f, damage: 6f, accuracy: 1f, stun: 0.8f, strain: 4f);
            a.Cooldown = 8f; a.Displace = DisplaceMode.TowardSource; a.DisplaceDistance = 7f;
        });
        var shockwave = Action(c, "Shockwave", a =>
        {
            a.Id = "shockwave"; a.DisplayName = "Shockwave"; a.BehaviorId = "area_attack";
            a.Targeting = TargetType.Self; a.Shape = AreaShape.Circle; a.AreaRadius = 3.5f; a.IsRanged = false;
            a.Windup = 0.8f; a.Recovery = 0.6f; a.Cooldown = 10f;
            a.Impact = ImpactType.Light; a.Damage = 8f; a.BaseAccuracy = 1f; a.StunDuration = 1.2f; a.StrainOnTarget = 15f;
        });
        var bash = Action(c, "Bash", a =>
        {
            a.Id = "bash"; a.DisplayName = "Bash"; a.BehaviorId = "bash";
            a.Targeting = TargetType.Position; a.Shape = AreaShape.Line; a.Range = 2.4f; a.LineWidth = 2.2f; a.IsRanged = false;
            a.Windup = 0.65f; a.Recovery = 0.4f; a.Cooldown = 5f;
            a.Impact = ImpactType.Heavy; a.Damage = 14f; a.BaseAccuracy = 1f; a.StunDuration = 0.6f; a.StrainOnTarget = 15f;
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
            Attack(a, "subjugate", "Subjugate", "subjugate", range: 1.8f, windup: 0.9f, recovery: 0.5f, damage: 15f, accuracy: 1f, stun: 2.5f, strain: 10f, heavy: true, melee: true);
            a.Cooldown = 8f;
            a.LinkedAction = headbutt;
            a.SelfStatusesDuringWindup = new List<StatusSpec> { UnstoppableWindup() };
        });
        var torrent = Action(c, "TorrentOfStrife", a =>
        {
            a.Id = "torrent_of_strife"; a.DisplayName = "Torrent of Strife"; a.BehaviorId = "area_attack";
            a.Targeting = TargetType.Position; a.Shape = AreaShape.Line; a.Range = 4f; a.LineWidth = 2.5f; a.IsRanged = false;
            a.Windup = 0.5f; a.Recovery = 0.6f; a.Cooldown = 5f;
            a.Impact = ImpactType.Light; a.Damage = 0f; a.BaseAccuracy = 1f; a.StunDuration = 0f; a.StrainOnTarget = 40f;
        });
        var arrival = Action(c, "BehemothsArrival", a =>
        {
            // Blink up to 9m, then slam: everyone within 3m (6 wide) is hit, stunned and lifted 3m to fall (ArrivalBehavior).
            a.Id = "behemoths_arrival"; a.DisplayName = "Behemoth's Arrival"; a.BehaviorId = "arrival";
            a.Targeting = TargetType.Position; a.Range = 9f; a.Shape = AreaShape.Circle; a.AreaRadius = 3f; a.IsRanged = false;
            a.Windup = 0.7f; a.Recovery = 0.7f; a.Cooldown = 11f;
            a.Impact = ImpactType.Heavy; a.Damage = 15f; a.BaseAccuracy = 1f; a.StunDuration = 0.9f; a.StrainOnTarget = 15f;
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
            Attack(a, "heretics_ascent", "Heretic's Ascent", "aerial_slam", range: 2.2f, windup: 0.9f, recovery: 0.4f, damage: 0f, accuracy: 1f, stun: 0.5f, strain: 10f, heavy: true, melee: true);
            a.Cooldown = 14f;
            a.DisplaceDistance = 5f;   // how high
            a.AirRiseSeconds = 0.75f; a.AirHoldSeconds = 0.75f; a.AirSlamSeconds = 0.5f;
        });

        var burden = Action(c, "CognitiveBurden", a =>
        {
            // Up to 50 of an ally's strain onto the Tank, then Burdened for 3s: an enemy's hit hands it to them (StrainBurdenBehavior).
            a.Id = "cognitive_burden"; a.DisplayName = "Cognitive Burden"; a.BehaviorId = "strain_burden";
            a.Targeting = TargetType.Ally; a.CannotTargetSelf = true; a.Range = 10f; a.IsRanged = true;
            a.Windup = 0.5f; a.Recovery = 0.5f; a.Cooldown = 8f;
            a.Damage = 0f; a.StunDuration = 0f;
            a.Params = new List<BehaviorParam> { new BehaviorParam("transfer", 50f), new BehaviorParam("seconds", 3f) };
        });
        var banishment = Action(c, "Banishment", a =>
        {
            // A heavy punch that lifts the target 9m (LevitateBehavior): the short stun holds it at the top, then it falls.
            Attack(a, "banishment", "Banishment", "levitate", range: 8f, windup: 0.6f, recovery: 0.3f, damage: 15f, accuracy: 1f, stun: 0.5f, strain: 10f, heavy: true);
            a.Cooldown = 16f; a.StrainOnSelf = 15f;
            a.DisplaceDistance = 9f;   // how high
        });
        var sentence = Action(c, "DeathSentence", a =>
        {
            // Sight through the windup, then a channel of up to 3s dragging the target in at 4 m/s; airborne, slammed down (TelekineticPullBehavior).
            a.Id = "death_sentence"; a.DisplayName = "Death Sentence"; a.BehaviorId = "telekinetic_pull";
            a.Targeting = TargetType.Enemy; a.Range = 20f; a.IsRanged = true;
            a.Windup = 0.5f; a.Recovery = 0.4f; a.Cooldown = 16f;
            a.Impact = ImpactType.Heavy; a.Damage = 0f; a.StunDuration = 0f; a.StrainOnSelf = 20f;
            a.ChannelSeconds = 3f;
            a.Params = new List<BehaviorParam> { new BehaviorParam("pull_speed", 4f), new BehaviorParam("air_speed", 4f), new BehaviorParam("air_fall_damage", 2f) };
        });
        var savior = Action(c, "Savior", a =>
        {
            // Dash to an ally (Range), guarding it (+50% Block for 2s), while winding up a stomp around you that knocks enemies away (SaviorBehavior).
            a.Id = "savior"; a.DisplayName = "Savior"; a.BehaviorId = "savior";
            a.Targeting = TargetType.Ally; a.CannotTargetSelf = true; a.Range = 8f; a.IsRanged = false;
            a.Windup = 0.2f; a.Recovery = 0.3f; a.Cooldown = 12f;
            a.Shape = AreaShape.Circle; a.AreaRadius = 7f;
            a.Damage = 12f; a.BaseAccuracy = 1f; a.StunDuration = 0.5f; a.StrainOnTarget = 6f;
            a.Displace = DisplaceMode.AwayFromSource; a.DisplaceDistance = 5f;
            a.TargetStatusesOnHit = new List<StatusSpec> { new StatusSpec { Id = StatusIds.SaviorsGuard, Duration = 2f }.Add(StatId.Block, 0.5f) };
        });

        var tank = Unit(c, u =>
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
        Grant(tank, null, burden, banishment, sentence, savior);
        return tank;
    }

    // ------------------------------------------------------------------ Paradigm User: backline caster, AoE and support

    private static UnitDefinition CreateParadigm(GameObject viewPrefab)
    {
        const string c = "Paradigm";
        var spark = Action(c, "Spark", a =>
            Attack(a, "spark", "Spark", "basic_attack", range: 12f, windup: 0.5f, recovery: 0.5f, damage: 8f, accuracy: 1f, stun: 0.2f, strain: 4f));
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
            a.Impact = ImpactType.Light; a.Damage = 20f; a.BaseAccuracy = 1f; a.StunDuration = 0.4f; a.StrainOnTarget = 10f;
            a.PiercesTerrain = true; a.BlockPierce = 0.25f;
        });
        var stutter = Action(c, "Stutter", a =>
        {
            a.Id = "stutter"; a.DisplayName = "Stutter"; a.BehaviorId = "stutter";
            a.Targeting = TargetType.Enemy; a.Range = 12f; a.IsRanged = true;
            a.Windup = 0.1f; a.Recovery = 0.4f; a.Cooldown = 5f;
            a.Damage = 0f; a.StunDuration = 0f; a.StrainOnTarget = 0f;
            a.StrainOnSelf = 15f;
            a.GuardBreakSeconds = 2f;   // drops a guard and keeps Block down
        });
        var lance = Action(c, "LanceOfDestiny", a =>
        {
            a.Id = "lance_of_destiny"; a.DisplayName = "Lance of Destiny"; a.BehaviorId = "lance";
            a.Targeting = TargetType.Position; a.Shape = AreaShape.Line; a.Range = 16f; a.LineWidth = 0.8f; a.IsRanged = true;
            a.Windup = 0f; a.Recovery = 1f; a.Cooldown = 0f;
            a.Impact = ImpactType.Light; a.Damage = 10f; a.BaseAccuracy = 1f; a.StunDuration = 0.25f; a.StrainOnTarget = 8f;
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
            a.Impact = ImpactType.Heavy; a.Damage = 0f; a.BaseAccuracy = 1f; a.StunDuration = 1.5f; a.StrainOnSelf = 15f;   // Heavy, so a windup can't shrug off the stun
            a.DisplaceDistance = 7f;   // how high it lifts them: the drop's fall damage grows with it
        });
        var cataclysm = Action(c, "Cataclysm", a =>
        {
            a.Id = "cataclysm"; a.DisplayName = "Cataclysm"; a.BehaviorId = "area_attack";
            a.Targeting = TargetType.Position; a.Shape = AreaShape.Circle; a.Range = 16f; a.AreaRadius = 4.5f; a.IsRanged = true;
            a.Windup = 3f; a.Recovery = 1.2f; a.Cooldown = 20f;
            a.Impact = ImpactType.Heavy; a.Damage = 35f; a.BaseAccuracy = 1f; a.StunDuration = 2f; a.StrainOnTarget = 30f;
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

        var seal = Action(c, "RegenerationSeal", a =>
        {
            // A status spell: 50 health over 4s; a hit on the ally breaks it and stuns the attacker for 1s (BreaksOnDamage, BreakStun).
            a.Id = "regeneration_seal"; a.DisplayName = "Regeneration Seal"; a.BehaviorId = "status_spell";
            a.Targeting = TargetType.Ally; a.Range = 15f; a.IsRanged = true;
            a.Windup = 0.3f; a.Recovery = 0.5f; a.Cooldown = 10f;
            a.Damage = 0f; a.StunDuration = 0f; a.StrainOnSelf = 10f;
            a.TargetStatusesOnHit = new List<StatusSpec>
            {
                new StatusSpec { Id = StatusIds.RegenerationSeal, Duration = 4f, AppliesTo = StatusTarget.Allies, HealPerSecond = 50f / 4f, BreakStun = 1f }.With(StatusFlags.BreaksOnDamage),
            };
        });
        var inversion = Action(c, "TrueInversion", a =>
        {
            // Channeled for up to 2s: the ally is True Inverted while it lasts (strain gains become losses; ChanneledStatusBehavior).
            a.Id = "true_inversion"; a.DisplayName = "True Inversion"; a.BehaviorId = "channeled_status";
            a.Targeting = TargetType.Ally; a.Range = 10f; a.IsRanged = true;
            a.Windup = 0.3f; a.Recovery = 0f; a.Cooldown = 16f;
            a.Damage = 0f; a.StunDuration = 0f; a.StrainOnSelf = 20f;
            a.ChannelSeconds = 2f;
            a.TargetStatusesOnHit = new List<StatusSpec>
            {
                new StatusSpec { Id = StatusIds.TrueInversion, Duration = 2f, AppliesTo = StatusTarget.Allies }.With(StatusFlags.InvertsStrainGain),
            };
        });

        var trammels = Action(c, "Trammels", a =>
        {
            // An area attack whose hits root (TargetStatusesOnHit: Rooted, 2s): nothing new in code (AreaAttackBehavior).
            a.Id = "trammels"; a.DisplayName = "Trammels"; a.BehaviorId = "area_attack";
            a.Targeting = TargetType.Position; a.Shape = AreaShape.Circle; a.Range = 12f; a.AreaRadius = 4f; a.IsRanged = true;
            a.Windup = 0.4f; a.Recovery = 0.5f; a.Cooldown = 8f;
            a.Impact = ImpactType.Light; a.Damage = 14f; a.BaseAccuracy = 1f; a.StunDuration = 0f; a.StrainOnTarget = 7f;   // every attack builds strain: half its damage
            a.TargetStatusesOnHit = new List<StatusSpec>
            {
                new StatusSpec { Id = StatusIds.Rooted, Duration = 2f, AppliesTo = StatusTarget.Enemies }.With(StatusFlags.Rooted),
            };
        });
        var centuries = Action(c, "MinutesTurnToCenturies", a =>
        {
            // A status spell on an ally: its actions run at half speed while this winds up (TargetStatusesDuringWindup),
            // then it sheds 50 strain (a negative StrainOnTarget: StatusSpellBehavior relieves it).
            a.Id = "minutes_turn_to_centuries"; a.DisplayName = "Minutes Turn To Centuries"; a.BehaviorId = "status_spell";
            a.Targeting = TargetType.Ally; a.Range = 15f; a.IsRanged = true;
            a.Windup = 0.9f; a.Recovery = 0.2f; a.Cooldown = 11f;
            a.Damage = 0f; a.StunDuration = 0f; a.StrainOnTarget = -50f;
            a.TargetStatusesDuringWindup = new List<StatusSpec>
            {
                new StatusSpec { Id = StatusIds.TimeSlowed, Duration = 0.9f, AppliesTo = StatusTarget.Allies }.Mult(StatId.ActionSpeed, 0.5f),
            };
        });

        var paradigm = Unit(c, u =>
        {
            u.DisplayName = "Paradigm User"; u.MaxHealth = 75f; u.BaseDodge = 0.1f; u.MoveSpeed = 4f;
            u.BasicAttack = spark;
            u.Actions = new List<ActionDefinition> { reconstruct, riftLine, cataclysm, stutter, lance, secondMind, chronosphere, wrinkles, amblivate };   // newer ones: Grant, below
            u.ViewPrefab = viewPrefab;
        }, new Dictionary<int, System.Action<UnitDefinition>>
        {
            { 9, u => AddActions(u, null, secondMind, wrinkles, amblivate) },
            { 13, u => AddActions(u, secondMind, chronosphere) },
        });
        // Summons two Wraiths beside the caster (SummonBehavior with no flight and no channel): they fight on its side.
        var wraith = CreateWraith(viewPrefab);
        var recollections = Action(c, "RecollectionsOfSelf", a =>
        {
            a.Id = "recollections_of_self"; a.DisplayName = "Recollections of Self"; a.BehaviorId = "summon";
            a.Targeting = TargetType.Self; a.IsRanged = false;
            a.Windup = 1.5f; a.Recovery = 0.5f; a.Cooldown = 13f;
            a.Damage = 0f; a.StunDuration = 0f; a.StrainOnSelf = 30f;
            a.DisplaceDistance = 0f; a.ChannelSeconds = 0f;
            a.SummonUnit = wraith; a.SummonCount = 2;
            a.SummonAIStyle = SeekStrikeAI.Style;   // they fight on their own: the nearest enemy, Rend
        });
        // A status spell on an ally (or the caster): its status heals when it runs out (StatusSpec.HealOnExpire).
        var unmake = Action(c, "UnmakeTheHarm", a =>
        {
            a.Id = "unmake_the_harm"; a.DisplayName = "Unmake The Harm"; a.BehaviorId = "status_spell";
            a.Targeting = TargetType.Ally; a.Range = 10f; a.IsRanged = true;
            a.Windup = 0.2f; a.Recovery = 1f; a.Cooldown = 13f;
            a.Damage = 0f; a.StunDuration = 0f; a.StrainOnSelf = 15f;
            a.TargetStatusesOnHit = new List<StatusSpec>
            {
                new StatusSpec { Id = UnmakingHarm, Duration = 2f, AppliesTo = StatusTarget.Allies, HealOnExpire = 15f, HealMissingOnExpire = 0.3f,
                                 Look = new StatusLook { Name = "Unmaking the Harm", Word = "MENDING", Color = new Color(0.5f, 1f, 0.65f), CountsUp = true } },
            };
        });
        Grant(paradigm, null, seal, inversion, trammels, centuries, recollections, unmake);

        // Round AF.
        var withering = Action(c, "WitheringEnd", a =>
        {
            // A channel of decay while the target stays in sight and range: 1 damage every 0.1s and a slow (WITHERING), then,
            // run to its end, a heavy burst that stuns (sure to connect: WitheringEndBehavior). Damage is the burst's.
            Attack(a, "withering_end", "Withering End", "withering_end", range: 14f, windup: 0.2f, recovery: 0.7f, damage: 15f, accuracy: 1f, stun: 0.9f, strain: 8f, heavy: true);
            a.Cooldown = 10f; a.StrainOnSelf = 10f;
            a.ChannelSeconds = 2.5f; a.ChannelNeedsSight = true;
            a.TargetStatusesOnHit = new List<StatusSpec> { new StatusSpec { Id = StatusIds.Withering, Duration = 2.5f, AppliesTo = StatusTarget.Enemies }.Mult(StatId.MoveSpeed, 0.6f) };
            a.Params = new List<BehaviorParam> { new BehaviorParam("tick_damage", 1f), new BehaviorParam("tick_seconds", 0.1f) };
        });
        var wildSwing = Action(c, "WildSwing", a =>
            Attack(a, "wild_swing", "Wild Swing", "basic_attack", range: 1.8f, windup: 0.45f, recovery: 0.4f, damage: 5f, accuracy: 0.6f, stun: 0f, strain: 3f, melee: true));
        var madness = Action(c, "Madness", a =>
        {
            // A status spell on anyone else: MADNESS makes them stronger and faster, but BattleSimulation.MaddenedAI plays them
            // (Maddened): their basic attack on the nearest enemy, again and again, or LinkedAction lent for as long without one.
            a.Id = "madness"; a.DisplayName = "Madness"; a.BehaviorId = "status_spell";
            a.LinkedAction = wildSwing;
            a.Targeting = TargetType.AnyUnit; a.CannotTargetSelf = true; a.Range = 10f; a.IsRanged = true;
            a.Windup = 0.5f; a.Recovery = 1f; a.Cooldown = 11f;
            a.Damage = 0f; a.StunDuration = 0f; a.StrainOnSelf = 10f;
            a.TargetStatusesOnHit = new List<StatusSpec>
            {
                new StatusSpec { Id = StatusIds.Madness, Duration = 3f }.Mult(StatId.DamageDealt, 1.5f).Mult(StatId.ActionSpeed, 1.75f).Mult(StatId.MoveSpeed, 1.5f).With(StatusFlags.Maddened),
            };
        });
        var splinters = Action(c, "Splinters", a =>
        {
            // A quick light hit that lodges a SPLINTER (stacks, until Unravel): when the target Unravels, each one deals 15 and
            // adds 0.2s to the stun (SplinterBehavior).
            Attack(a, "splinters", "Splinters", "splinter", range: 16f, windup: 0.2f, recovery: 0.1f, damage: 5f, accuracy: 1f, stun: 0.1f, strain: 15f);
            a.StrainOnSelf = 5f;
            a.Params = new List<BehaviorParam> { new BehaviorParam("unravel_damage", 15f), new BehaviorParam("unravel_stun", 0.2f) };
        });
        var domain = Action(c, "BanefulDomain", a =>
        {
            // A 9m zone at a point for 2s: 5 damage a second to enemies inside, +20% for each second it has been up. Cast again
            // while it is up, it renews the zone (same place) for 15 strain (BanefulDomainBehavior).
            a.Id = "baneful_domain"; a.DisplayName = "Baneful Domain"; a.BehaviorId = "baneful_domain";
            a.Targeting = TargetType.Position; a.Range = 17f; a.IsRanged = true;
            a.Windup = 0.7f; a.Recovery = 0.9f; a.Cooldown = 0f;
            a.Damage = 0f; a.StunDuration = 0f; a.StrainOnSelf = 0f;
            a.Shape = AreaShape.Circle; a.AreaRadius = 9f; a.AreaHeight = 0f;
            a.AuraAtTarget = true; a.AuraSeconds = 2f; a.AuraDamagePerSecond = 5f; a.AuraDamageGrowth = 0.2f;
            a.Params = new List<BehaviorParam> { new BehaviorParam("followup_strain", 15f) };
        });
        var mindshatter = Action(c, "Mindshatter", a =>
        {
            // A heavy hit. Transcended (+40 strain): +1% damage per point of strain on the target and on you, then you Unravel
            // (MindshatterBehavior). No range was given: 15m, as Amblivate.
            Attack(a, "mindshatter", "Mindshatter", "mindshatter", range: 15f, windup: 0.9f, recovery: 0.9f, damage: 15f, accuracy: 1f, stun: 0f, strain: 10f, heavy: true);
            a.Cooldown = 12f; a.StrainOnSelf = 10f;
            a.CanTranscend = true; a.TranscendStrain = 40f;
            a.Params = new List<BehaviorParam> { new BehaviorParam("target_strain_bonus", 0.01f), new BehaviorParam("own_strain_bonus", 0.01f) };
        });
        Grant(paradigm, null, withering, madness, splinters, domain, mindshatter);
        return paradigm;
    }

    // ------------------------------------------------------------------ Enemy types (each with its own AI profile, see UnitAIs)
    // Their abilities are their own assets, even where they copy a squad ability, so each can be tuned on its own.

    public static Enemies CreateEnemies(GameObject viewPrefab)
    {
        var wraith = CreateWraith(viewPrefab);   // first: the Monarch summons it
        // Deliverance's four parts first: Split The Minds brings them.
        var syne = CreateDeliverancePart(viewPrefab, "Syne", SyneAI.Style, DeliveranceParts.PathToVictory, health: 75f, strain: 75f, moveSpeed: 6f);
        var alio = CreateDeliverancePart(viewPrefab, "Alio", AlioAI.Style, DeliveranceParts.PlightUponStructure, health: 100f, strain: 125f, moveSpeed: 4f);
        var revl = CreateDeliverancePart(viewPrefab, "Revl", RevlAI.Style, DeliveranceParts.SingleMindedPerfection, health: 125f, strain: 75f, moveSpeed: 8f, dodge: 0.25f, extra: Equipose());
        var wex = CreateDeliverancePart(viewPrefab, "Wex", WexAI.Style, DeliveranceParts.LastAttachments, health: 145f, strain: 125f, moveSpeed: 5f, block: 0.2f);
        return new Enemies
        {
            Sniper = CreateSniper(viewPrefab), Assassin = CreateAssassin(viewPrefab), Skirmisher = CreateSkirmisher(viewPrefab), Healer = CreateHealer(viewPrefab),
            Caster = CreateCaster(viewPrefab), Brutebeast = CreateBrutebeast(viewPrefab),
            Wraith = wraith, Monarch = CreateMonarch(viewPrefab, wraith), Zola = CreateZola(viewPrefab), Primavaeran = CreatePrimavaeran(viewPrefab), Exemplar = CreateExemplar(viewPrefab),
            Deliverance = CreateDeliverance(viewPrefab, syne, alio, revl, wex), Syne = syne, Alio = alio, Revl = revl, Wex = wex,
        };
    }

    // ------------------------------------------------------------------ Deliverance, the final boss, and its parts

    /// <summary>
    /// Deliverance, melded into one (phase one): a boss on a side of its own (the encounter puts it on Team Rogue), fighting
    /// the squad and the enemy's reinforcements alike. Unravel stuns it half as long; Freedom's Embrace runs the clock on the
    /// player's orders; the blow that would kill it splits it into its four parts (Split The Minds).
    /// </summary>
    private static UnitDefinition CreateDeliverance(GameObject viewPrefab, UnitDefinition syne, UnitDefinition alio, UnitDefinition revl, UnitDefinition wex)
    {
        const string c = "Deliverance";
        var disassociate = Action(c, "Disassociate", a =>
        {
            Attack(a, "disassociate", "Disassociate", "basic_attack", range: 3f, windup: 0.2f, recovery: 0.2f, damage: 14f, accuracy: 1f, stun: 0f, strain: 15f, melee: true);
            a.Cooldown = 1f;
            a.TargetStatusesOnHit = new List<StatusSpec> { new StatusSpec { Id = StatusIds.Perplexity, Duration = 2f } };   // opens Slip on them
        });
        var hatred = Action(c, "PraasHatred", a =>
        {
            // A chain attack with no end: past 2 follow-ups on the same target, heavy and double damage (PraasHatredBehavior).
            Attack(a, "praas_hatred", "Praa's Hatred", "praas_hatred", range: 2f, windup: 0.1f, recovery: 0.1f, damage: 10f, accuracy: 1f, stun: 0.4f, strain: 5f, melee: true);
            a.StrainOnSelf = 5f; a.Cooldown = 0f;
            a.ChainFollowups = 99; a.ChainWindow = 0.6f; a.ChainCostMultiplier = 1f;
            a.Params = new List<BehaviorParam> { new BehaviorParam("heavy_after", 2f), new BehaviorParam("heavy_damage", 2f) };
        });
        var slip = Action(c, "Slip", a =>
        {
            // Through PERPLEXITY: behind a perplexed target at once, from anywhere (PerplexityStepBehavior).
            a.Id = "slip"; a.DisplayName = "Slip"; a.BehaviorId = "perplexity_step";
            a.Targeting = TargetType.Enemy; a.Range = 60f; a.IsRanged = false; a.PiercesTerrain = true;
            a.Windup = 0f; a.Recovery = 0f; a.Cooldown = 0f;
            a.Damage = 0f; a.StunDuration = 0f;
        });
        var dance = Action(c, "JoyousDance", a =>
        {
            // Dash away from enemies (keeping the target in range) during the windup, then a zap with one of three statuses.
            Attack(a, "joyous_dance", "Joyous Dance", "joyous_dance", range: 11f, windup: 0.2f, recovery: 0.1f, damage: 10f, accuracy: 1f, stun: 0f, strain: 5f);
            a.RequiredStatus = StatusIds.Festival;   // only during Festival Of Colors (not spent)
            a.Params = new List<BehaviorParam> { new BehaviorParam("dash_range", 5f) };
            a.RandomStatusesOnHit = new List<StatusSpec>
            {
                new StatusSpec { Id = StatusIds.Marked, Duration = 1.5f }.Add(StatId.IncomingAccuracy, 0.25f).Add(StatId.Dodge, -0.25f),
                new StatusSpec { Id = StatusIds.TimeSlowed, Duration = 1.5f }.Mult(StatId.ActionSpeed, 0.5f),
                new StatusSpec { Id = StatusIds.Immobilized, Duration = 1.5f }.With(StatusFlags.Rooted),
            };
        });
        var festival = Action(c, "FestivalOfColors", a =>
        {
            // +50% action speed (a status's action speed scales walking too: +50% move speed with it), and Joyous Dance.
            SelfBuff(a, "festival_of_colors", "Festival Of Colors", "self_buff", windup: 0.3f, recovery: 0f, cooldown: 9f,
                new StatusSpec { Id = StatusIds.Festival, Duration = 4f }.Mult(StatId.ActionSpeed, 1.5f));
            a.LinkedAction = dance;
        });
        var loop = Action(c, "CloseTheLoop", a =>
        {
            // Through walls; blocked or dodged, it teleports to them and FATES them (CloseTheLoopBehavior).
            Attack(a, "close_the_loop", "Close The Loop", "close_the_loop", range: 25f, windup: 0.5f, recovery: 0.1f, damage: 35f, accuracy: 1f, stun: 0.7f, strain: 17f, heavy: true);
            a.PiercesTerrain = true; a.Cooldown = 6f;
            a.Params = new List<BehaviorParam> { new BehaviorParam("fated_seconds", 1.5f), new BehaviorParam("fated_speed", 0.35f) };
        });
        var stitch = Action(c, "Stitch", a =>
        {
            // The stun is the meld: 1s, the target lifted 5m over it while Deliverance heals inside it (StitchBehavior).
            Attack(a, "stitch", "Stitch", "stitch", range: 2f, windup: 0.3f, recovery: 0f, damage: 0f, accuracy: 1f, stun: 1f, strain: 0f, melee: true);
            a.Cooldown = 6f;
            a.Params = new List<BehaviorParam> { new BehaviorParam("lift", 5f), new BehaviorParam("heal_per_second", 70f), new BehaviorParam("exit_dash", 5f) };
        });
        var glitch = Action(c, "Reduction", a =>
        {
            // A corrupted line: REDUCTION on everyone in it, half move speed and +1 strain every 0.1s for 6s.
            a.Id = "reduction_line"; a.DisplayName = "Q-<%~=}\\..?9RG*"; a.BehaviorId = "area_attack";
            a.Targeting = TargetType.Position; a.Shape = AreaShape.Line; a.Range = 11f; a.LineWidth = 5f; a.IsRanged = true;
            a.Windup = 0.4f; a.Recovery = 0.2f; a.Cooldown = 7f;
            a.Damage = 0f; a.BaseAccuracy = 1f; a.StunDuration = 0f;
            a.TargetStatusesOnHit = new List<StatusSpec> { new StatusSpec { Id = StatusIds.Reduction, Duration = 6f, StrainPerSecond = 10f }.Mult(StatId.MoveSpeed, 0.5f) };
        });
        var split = Action(c, "SplitTheMinds", a =>
        {
            // Never chosen: it starts by itself when the last blow lands (the split_the_minds passive).
            a.Id = "split_the_minds"; a.DisplayName = "SPLIT THE MINDS"; a.BehaviorId = "split_the_minds";
            a.Targeting = TargetType.Self; a.IsRanged = false;
            a.Windup = 2f; a.Recovery = 0f; a.Cooldown = 0f;
            a.Damage = 0f; a.StunDuration = 0f;
            a.DisplaceDistance = 9f;   // the pulse's shove (Params: its damage)
            a.Params = new List<BehaviorParam> { new BehaviorParam("max_health_damage", 0.75f) };
            a.SummonUnits = new List<UnitDefinition> { syne, alio, revl, wex };
        });
        return Unit(c, u =>
        {
            u.DisplayName = "Deliverance"; u.MaxHealth = 210f; u.MaxStrain = 160f; u.BaseDodge = 0.2f; u.BaseBlock = 0.25f;
            u.MoveSpeed = 5.5f; u.DashDistance = 3.5f; u.BodyRadius = 0.45f; u.AIProfile = "deliverance"; u.Boss = true;
            u.Passives = new List<string> { DeliverancePassives.UnravelResistance, DeliverancePassives.FreedomsEmbrace, DeliverancePassives.SplitTheMinds };
            u.BasicAttack = disassociate;
            u.Actions = new List<ActionDefinition> { hatred, slip, festival, dance, loop, stitch, glitch, split };
            u.ViewPrefab = viewPrefab;
        });
    }

    /// <summary>
    /// Deliverance's four parts (phase two). Each starts with one ability, Imprint Upon Itself (copy a random ability from a
    /// squad unit's loadout: the one asset all four share, each slowing its own copy as it uses it), and a passive of its own
    /// (DeliveranceParts). Revl also holds EQUIPOSE, which it never chooses: its passive casts it at max strain.
    /// </summary>
    private static ActionDefinition ImprintUponItself()
    {
        return Action("Deliverance", "ImprintUponItself", a =>
        {
            a.Id = "imprint_upon_itself"; a.DisplayName = "Imprint Upon Itself"; a.BehaviorId = "imprint";
            a.Targeting = TargetType.Enemy; a.Range = 25f; a.IsRanged = true; a.PiercesTerrain = true;
            a.Windup = 0.1f; a.Recovery = 0.1f; a.Cooldown = 0.25f;
            a.Damage = 0f; a.StunDuration = 0f;
            a.Params = new List<BehaviorParam> { new BehaviorParam("windup_step", 0.1f), new BehaviorParam("recovery_step", 0.1f), new BehaviorParam("range_step", 2f), new BehaviorParam("cooldown_step", 0.25f) };
        });
    }

    private static UnitDefinition CreateDeliverancePart(GameObject viewPrefab, string name, string style, string passive, float health, float strain, float moveSpeed,
                                                        float dodge = 0.15f, float block = 0f, ActionDefinition extra = null)
    {
        var imprint = ImprintUponItself();
        return Unit(name, u =>
        {
            u.DisplayName = name; u.MaxHealth = health; u.MaxStrain = strain; u.MoveSpeed = moveSpeed; u.BaseDodge = dodge; u.BaseBlock = block;
            u.AIProfile = style; u.Boss = true;
            u.Passives = new List<string> { DeliverancePassives.UnboundTime, passive };
            u.BasicAttack = null;
            u.Actions = extra != null ? new List<ActionDefinition> { imprint, extra } : new List<ActionDefinition> { imprint };
            u.ViewPrefab = viewPrefab;
        });
    }

    /// <summary>Revl's EQUIPOSE: Unstoppable over a 2s windup that drains its strain, then a 12m wave: 25 damage, 75% slower walking.</summary>
    private static ActionDefinition Equipose()
    {
        return Action("Revl", "Equipose", a =>
        {
            a.Id = "equipose"; a.DisplayName = "EQUIPOSE"; a.BehaviorId = "equipose";
            a.Targeting = TargetType.Self; a.Shape = AreaShape.Circle; a.AreaRadius = 12f; a.IsRanged = false;
            a.Windup = 2f; a.Recovery = 0.2f; a.Cooldown = 0f;
            a.Impact = ImpactType.Heavy; a.Damage = 25f; a.BaseAccuracy = 1f; a.StunDuration = 0f; a.StrainOnTarget = 12f;
            a.TargetStatusesOnHit = new List<StatusSpec> { new StatusSpec { Id = "strife", Duration = 3f }.Mult(StatId.MoveSpeed, 0.25f) };
        });
    }

    // ------------------------------------------------------------------ Wraith, Monarch, Zola, Primavaeran
    // Asked-for ranges of 8-11 on melee strikes are taken as melee reach (MeleeStrikeRange), as with the other enemy types.

    private static UnitDefinition CreateWraith(GameObject viewPrefab)
    {
        const string c = "Wraith";
        var rend = Action(c, "Rend", a =>
            Attack(a, "rend", "Rend", "basic_attack", range: MeleeStrikeRange, windup: 0.4f, recovery: 0.2f, damage: 6f, accuracy: 1f, stun: 0.2f, strain: 3f, melee: true));
        return Unit(c, u =>
        {
            u.DisplayName = "Wraith"; u.MaxHealth = 30f; u.BaseDodge = 0.1f; u.MoveSpeed = 6f; u.AIProfile = "wraith";
            u.BasicAttack = rend;
            u.Actions = new List<ActionDefinition>();
            u.ViewPrefab = viewPrefab;
        });
    }

    private static UnitDefinition CreateMonarch(GameObject viewPrefab, UnitDefinition wraith)
    {
        const string c = "Monarch";
        var javelin = Action(c, "DivineJavelin", a =>
        {
            Attack(a, "divine_javelin", "Divine Javelin", "basic_attack", range: 17f, windup: 0.5f, recovery: 0.3f, damage: 18f, accuracy: 1f, stun: 0.5f, strain: 9f);
            a.Cooldown = 1f;
        });
        var horde = Action(c, "HordeOfMemories", a =>
        {
            // Up 5m over the windup, four Wraiths below over a 1s channel, down over the recovery (SummonBehavior).
            a.Id = "horde_of_memories"; a.DisplayName = "Horde of Memories"; a.BehaviorId = "summon";
            a.Targeting = TargetType.Self; a.IsRanged = false;
            a.Windup = 1f; a.Recovery = 0.25f; a.Cooldown = 15f;
            a.Damage = 0f; a.StunDuration = 0f;
            a.DisplaceDistance = 5f;   // fly height
            a.ChannelSeconds = 1f; a.ChannelIgnoresDamage = true;
            a.SummonUnit = wraith; a.SummonCount = 4;
        });
        var incineration = Action(c, "Incineration", a =>
        {
            // Up 7m and forward over the windup until the target is within 12m, then a 3s beam: 15 damage a second (BeamBehavior).
            Attack(a, "incineration", "Incineration", "beam", range: 24f, windup: 0.75f, recovery: 0.5f, damage: 15f, accuracy: 1f, stun: 0.2f, strain: 2f);
            a.Cooldown = 12f;
            a.DisplaceDistance = 7f;   // fly height
            a.ChannelSeconds = 3f;
            a.Params = new List<BehaviorParam> { new BehaviorParam("beam_range", 12f), new BehaviorParam("hits_per_second", 4f) };
        });
        return Unit(c, u =>
        {
            u.DisplayName = "Monarch"; u.MaxHealth = 150f; u.BaseDodge = 0.1f; u.MoveSpeed = 5f; u.AIProfile = "monarch";
            u.Passives = new List<string> { Passives.Ascension };
            u.BasicAttack = javelin;
            u.Actions = new List<ActionDefinition> { incineration, horde };
            u.ViewPrefab = viewPrefab;
        });
    }

    private static UnitDefinition CreateZola(GameObject viewPrefab)
    {
        const string c = "Zola";
        var wasteful = Action(c, "WastefulThought", a =>
        {
            // Always on: a 7m dome around Zola adding 5 strain a second to the enemies inside (InnatePassive aura).
            a.Id = "wasteful_thought"; a.DisplayName = "Wasteful Thought"; a.BehaviorId = "aura";
            a.Targeting = TargetType.Self; a.IsRanged = false; a.InnatePassive = true;
            a.Shape = AreaShape.Circle; a.AreaRadius = 7f;
            a.Damage = 0f; a.StunDuration = 0f;
            a.AuraStrainPerSecond = 5f;
        });
        var stomp = Action(c, "Stomp", a =>
        {
            // Everyone within 7m: a heavy hit, stunned and flung 2m up (AreaLiftBehavior).
            a.Id = "stomp"; a.DisplayName = "Stomp"; a.BehaviorId = "area_lift";
            a.Targeting = TargetType.Self; a.Shape = AreaShape.Circle; a.AreaRadius = 7f; a.IsRanged = false;
            a.Windup = 0.7f; a.Recovery = 0.5f; a.Cooldown = 5f;
            a.Impact = ImpactType.Heavy; a.Damage = 21f; a.BaseAccuracy = 1f; a.StunDuration = 1.25f; a.StrainOnTarget = 10f;
            a.DisplaceDistance = 2f;   // knock-up height
        });
        var overload = Action(c, "MentalOverload", a =>
        {
            // Unstoppable through the windup, then a 15m pulse (10m tall) that damages, stuns and strains.
            a.Id = "mental_overload"; a.DisplayName = "Mental Overload"; a.BehaviorId = "area_attack";
            a.Targeting = TargetType.Self; a.Shape = AreaShape.Circle; a.AreaRadius = 15f; a.AreaHeight = 10f; a.IsRanged = false;
            a.Windup = 1.5f; a.Recovery = 1.5f; a.Cooldown = 18f;
            a.Impact = ImpactType.Heavy; a.Damage = 30f; a.BaseAccuracy = 1f; a.StunDuration = 2f; a.StrainOnTarget = 30f;
            a.SelfStatusesDuringWindup = new List<StatusSpec> { UnstoppableWindup() };
        });
        return Unit(c, u =>
        {
            u.DisplayName = "Zola"; u.MaxHealth = 220f; u.BaseDodge = 0f; u.BaseBlock = 0.15f; u.MoveSpeed = 3.5f; u.AIProfile = "zola";
            u.BodyRadius = 0.6f;
            u.Actions = new List<ActionDefinition> { overload, stomp, wasteful };
            u.ViewPrefab = viewPrefab;
        });
    }

    /// <summary>Unmake The Harm's delayed heal on its target.</summary>
    public const string UnmakingHarm = "unmaking_harm";
    /// <summary>Destiny Closes In landed: Fate Encumbers Its Prey is open at that target. Fate landed: Witness To The End is.</summary>
    public const string FateReady = "fate_ready", WitnessReady = "witness_ready";
    /// <summary>Seconds after a chain shot's recovery its follow-up stays open (SkyShotBehavior's "window" keeps the flight up as long).</summary>
    private const float ChainWindow = 1f;

    /// <summary>A follow-up's opening (SelfStatusesOnHit): through 'recovery' and ChainWindow after, never shown.</summary>
    private static StatusSpec ChainOpen(string id, float recovery)
    {
        return new StatusSpec { Id = id, Duration = recovery + ChainWindow, Look = new StatusLook { Hidden = true } };
    }

    // ------------------------------------------------------------------ Exemplar

    /// <summary>
    /// The Exemplar: a gold-adorned knight with a longsword that cycles between melee and range (ExemplarAI, a StanceCycleAI).
    /// Intrepid Advance and Light the Pyre are dash strikes (phantom_strike); Spark the Flame opens Light the Pyre when it lands
    /// (SelfStatusesOnHit, blocked hits included); Piercing Retreat (retreat_strike) and Split Apart The Sky (sky_smite) are
    /// its own behaviors.
    /// </summary>
    private static UnitDefinition CreateExemplar(GameObject viewPrefab)
    {
        const string c = "Exemplar";
        var advance = Action(c, "IntrepidAdvance", a =>
        {
            a.Id = "intrepid_advance"; a.DisplayName = "Intrepid Advance"; a.BehaviorId = "phantom_strike";
            a.Targeting = TargetType.Enemy; a.Range = 9f; a.IsRanged = false;
            a.Windup = 0.5f; a.Recovery = 0.3f; a.Cooldown = 3f;
            a.Impact = ImpactType.Light; a.Damage = 15f; a.BaseAccuracy = 1f; a.StunDuration = 0.5f; a.StrainOnTarget = 7f;
        });
        var pyre = Action(c, "LightThePyre", a =>
        {
            a.Id = "light_the_pyre"; a.DisplayName = "Light the Pyre"; a.BehaviorId = "phantom_strike";
            a.Targeting = TargetType.Enemy; a.Range = 5f; a.IsRanged = false;
            a.Windup = 0.7f; a.Recovery = 0.3f; a.Cooldown = 0f;
            a.Impact = ImpactType.Heavy; a.Damage = 19f; a.BaseAccuracy = 1f; a.StunDuration = 1f; a.StrainOnTarget = 12f;
            a.RequiredStatus = PyreReady; a.ConsumesRequiredStatus = true;   // opened by Spark the Flame landing
        });
        var spark = Action(c, "SparkTheFlame", a =>
        {
            Attack(a, "spark_the_flame", "Spark the Flame", "basic_attack", range: 2f, windup: 0.4f, recovery: 0.2f, damage: 9f, accuracy: 1f, stun: 0.3f, strain: 4f, melee: true);
            a.Cooldown = 2f;
            // Landing it (blocked or not) opens Light the Pyre through the recovery and PyreWindow seconds after.
            a.SelfStatusesOnHit = new List<StatusSpec> { new StatusSpec { Id = PyreReady, Duration = 0.2f + PyreWindow } };
            a.LinkedAction = pyre;
        });
        var retreat = Action(c, "PiercingRetreat", a =>
        {
            a.Id = "piercing_retreat"; a.DisplayName = "Piercing Retreat"; a.BehaviorId = "retreat_strike";
            a.Targeting = TargetType.Enemy; a.Range = 7f; a.IsRanged = false;   // the strike's reach
            a.Shape = AreaShape.Line; a.LineWidth = 2f;
            a.Windup = 0.3f; a.Recovery = 0.3f; a.Cooldown = 3f;
            a.Impact = ImpactType.Light; a.Damage = 12f; a.BaseAccuracy = 1f; a.StunDuration = 0.4f; a.StrainOnTarget = 6f;
            a.Params = new List<BehaviorParam> { new BehaviorParam("retreat", 5f) };
        });
        var smite = Action(c, "SplitApartTheSky", a =>
        {
            a.Id = "split_apart_the_sky"; a.DisplayName = "Split Apart The Sky"; a.BehaviorId = "sky_smite";
            a.Targeting = TargetType.Enemy; a.Range = 14f; a.IsRanged = true;   // how far the target can be
            a.Shape = AreaShape.Circle; a.AreaRadius = 5f;
            a.Windup = 0.7f; a.Recovery = 0.5f; a.Cooldown = 3f;
            a.Impact = ImpactType.Light; a.Damage = 16f; a.BaseAccuracy = 1f; a.StunDuration = 0.5f; a.StrainOnTarget = 8f;
            a.Params = new List<BehaviorParam> { new BehaviorParam("blink_range", 8f), new BehaviorParam("height", 2f), new BehaviorParam("min_distance", 4f) };
        });
        return Unit(c, u =>
        {
            u.DisplayName = "Exemplar"; u.MaxHealth = 90f; u.MaxStrain = 60f; u.BaseDodge = 0.2f; u.MoveSpeed = 6f; u.AIProfile = "exemplar";
            u.BasicAttack = spark;
            u.Actions = new List<ActionDefinition> { advance, pyre, retreat, smite };
            u.ViewPrefab = viewPrefab;
        });
    }

    /// <summary>The Exemplar's Spark the Flame landed: Light the Pyre is open.</summary>
    public const string PyreReady = "pyre_ready";
    /// <summary>Seconds after Spark the Flame's recovery that Light the Pyre stays open.</summary>
    private const float PyreWindow = 1f;

    private static UnitDefinition CreatePrimavaeran(GameObject viewPrefab)
    {
        const string c = "Primavaeran";
        var decay = Action(c, "AbstractDecay", a =>
        {
            // A 7m zone left at the point for 4s: 9 damage a second and half move speed for enemies inside (aura, AuraAtTarget).
            a.Id = "abstract_decay"; a.DisplayName = "Abstract Decay"; a.BehaviorId = "aura";
            a.Targeting = TargetType.Position; a.Range = 14f; a.IsRanged = true;
            a.Windup = 0.3f; a.Recovery = 0.5f; a.Cooldown = 0f;
            a.Damage = 0f; a.StunDuration = 0f; a.StrainOnSelf = 8f;
            a.Shape = AreaShape.Circle; a.AreaRadius = 7f; a.AuraSeconds = 4f; a.AuraAtTarget = true;
            a.AuraDamagePerSecond = 9f;
            a.AuraStatuses = new List<StatusSpec> { new StatusSpec { Id = StatusIds.Decayed, Duration = 4f, AppliesTo = StatusTarget.Enemies }.Mult(StatId.MoveSpeed, 0.5f) };
        });
        var repel = Action(c, "RecurrentRepel", a =>
        {
            a.Id = "recurrent_repel"; a.DisplayName = "Recurrent Repel"; a.BehaviorId = "area_attack";
            a.Targeting = TargetType.Self; a.Shape = AreaShape.Circle; a.AreaRadius = 6f; a.IsRanged = false;
            a.Windup = 0.5f; a.Recovery = 0.5f; a.Cooldown = 8f;
            a.Impact = ImpactType.Light; a.Damage = 10f; a.BaseAccuracy = 1f; a.StunDuration = 0.75f; a.StrainOnTarget = 8f;
            a.Displace = DisplaceMode.AwayFromSource; a.DisplaceDistance = 8f;
        });
        var distortion = Action(c, "Distortion", a =>
        {
            // Teleport up to 20m; a 4m rift where it stood hits and pulls enemies 4m in (DistortionBehavior).
            a.Id = "distortion"; a.DisplayName = "Distortion"; a.BehaviorId = "distortion";
            a.Targeting = TargetType.Position; a.Range = 20f; a.IsRanged = false;
            a.Windup = 0.2f; a.Recovery = 0.5f; a.Cooldown = 10f;
            a.Impact = ImpactType.Light; a.Damage = 15f; a.BaseAccuracy = 1f; a.StunDuration = 0f; a.StrainOnTarget = 5f;
            a.Params = new List<BehaviorParam> { new BehaviorParam("rift_radius", 4f), new BehaviorParam("pull", 4f) };
        });
        var tear = Action(c, "TearApartReality", a =>
        {
            // A 2s windup, then a 5s channel placing 10 delayed explosions spread over a 20m circle (RealityTearBehavior).
            a.Id = "tear_apart_reality"; a.DisplayName = "Tear Apart Reality"; a.BehaviorId = "reality_tear";
            a.Targeting = TargetType.Position; a.Range = 30f; a.IsRanged = true;
            a.Windup = 2f; a.Recovery = 1f; a.Cooldown = 25f; a.StartsOnCooldown = true;
            a.Impact = ImpactType.Light; a.Damage = 18f; a.BaseAccuracy = 1f; a.StunDuration = 0.5f; a.StrainOnTarget = 8f;
            a.Shape = AreaShape.Circle; a.AreaRadius = 20f;
            a.ChannelSeconds = 5f;
            a.ExtraCinematic = true;
            a.Params = new List<BehaviorParam> { new BehaviorParam("explosions", 10f), new BehaviorParam("explosion_delay", 0.5f), new BehaviorParam("explosion_radius", 3f) };
        });
        return Unit(c, u =>
        {
            u.DisplayName = "Primavaeran"; u.MaxHealth = 110f; u.BaseDodge = 0.15f; u.MoveSpeed = 4f; u.AIProfile = "primavaeran";
            u.Actions = new List<ActionDefinition> { tear, distortion, repel, decay };
            u.ViewPrefab = viewPrefab;
        });
    }

    private static UnitDefinition CreateSniper(GameObject viewPrefab)
    {
        const string c = "Sniper";
        var rifle = Action(c, "RifleShot", a =>
            Attack(a, "sniper_rifle_shot", "Rifle Shot", "basic_attack", range: 14f, windup: 0.6f, recovery: 0.5f, damage: 14f, accuracy: 1f, stun: 0.15f, strain: 5f));
        var snipe = Action(c, "Snipe", a =>
        {
            Attack(a, "sniper_snipe", "Snipe", "snipe", range: 25f, windup: 1.6f, recovery: 0.6f, damage: 45f, accuracy: 1f, stun: 1f, strain: 25f, heavy: true);
            a.Cooldown = 5f;
        });
        var marking = Action(c, "MarkingShot", a =>
        {
            Attack(a, "sniper_marking_shot", "Marking Shot", "basic_attack", range: 18f, windup: 0.6f, recovery: 0.4f, damage: 10f, accuracy: 1f, stun: 0.2f, strain: 4f);
            a.Cooldown = 10f;
            a.TargetStatusesOnHit = new List<StatusSpec> { new StatusSpec { Id = StatusIds.Marked, Duration = 8f }.Add(StatId.IncomingAccuracy, 0.25f).Add(StatId.Dodge, -0.25f) };
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
            Attack(a, "assassin_strike", "Strike", "basic_attack", range: MeleeStrikeRange, windup: 0.3f, recovery: 0.3f, damage: 15f, accuracy: 1f, stun: 0.3f, strain: 7f, melee: true));
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
            Attack(a, "hidden_strike", "Hidden Strike", "phantom_strike", range: 18f, windup: 0.9f, recovery: 0.5f, damage: 24f, accuracy: 1f, stun: 0.5f, strain: 12f, melee: true);
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
            Attack(a, "skirmisher_strike", "Strike", "basic_attack", range: MeleeStrikeRange, windup: 0.3f, recovery: 0.3f, damage: 15f, accuracy: 1f, stun: 0.3f, strain: 7f, melee: true));
        var flow = Action(c, "FlowingDeath", a =>
        {
            // Each one in a row winds up 0.1s faster, down to 0.1s (FlowingDeathBehavior).
            Attack(a, "flowing_death", "Flowing Death", "flowing_death", range: MeleeStrikeRange, windup: 0.5f, recovery: 0.1f, damage: 8f, accuracy: 1f, stun: 0.2f, strain: 4f, melee: true);
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
            a.Impact = ImpactType.Light; a.Damage = 11f; a.BaseAccuracy = 1f; a.StunDuration = 0.2f; a.StrainOnTarget = 5f;
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
            Attack(a, "kinetic_blast", "Kinetic Blast", "basic_attack", range: 14f, windup: 0.6f, recovery: 0.5f, damage: 14f, accuracy: 1f, stun: 0.15f, strain: 5f);
            a.Displace = DisplaceMode.AwayFromSource; a.DisplaceDistance = 3f;
        });
        var distort = Action(c, "DistortSpace", a =>
        {
            a.Id = "distort_space"; a.DisplayName = "Distort Space"; a.BehaviorId = "area_attack";
            a.Targeting = TargetType.Position; a.Shape = AreaShape.Circle; a.Range = 10f; a.AreaRadius = 1.5f; a.IsRanged = true;   // 3 wide
            a.Windup = 0.6f; a.Recovery = 0.5f; a.Cooldown = 6f;
            a.Impact = ImpactType.Heavy; a.Damage = 17f; a.BaseAccuracy = 1f; a.StunDuration = 0.6f; a.StrainOnTarget = 12f;
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
            Attack(a, "ravage_followup", "Ravage Follow-up", "basic_attack", range: MeleeStrikeRange, windup: 1.35f, recovery: 1.05f, damage: 42f, accuracy: 1f, stun: 0.8f, strain: 25f, heavy: true, melee: true);
            a.RequiredStatus = StatusIds.RavageReady; a.ConsumesRequiredStatus = true;
        });
        var ravage = Action(c, "Ravage", a =>
        {
            Attack(a, "ravage", "Ravage", "basic_attack", range: MeleeStrikeRange, windup: 0.9f, recovery: 0.7f, damage: 28f, accuracy: 1f, stun: 0.8f, strain: 25f, heavy: true, melee: true);
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
    private static readonly HashSet<string> QualifiedClasses = new HashSet<string> { "Sniper", "Assassin", "Skirmisher", "Healer", "Caster", "Brutebeast", "Wraith", "Monarch", "Zola", "Primavaeran", "Exemplar",
                                                                                     "Deliverance", "Syne", "Alio", "Revl", "Wex" };

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
                new StatusSpec { Id = StatusIds.Blocking, Duration = BlockSeconds + BattleSimulation.TickLength }.Add(StatId.Block, 0.5f).Mult(StatId.StunTaken, 0.5f));   // stuns on it last half as long
            a.SelfStatusesOnWindup = true;
            a.StrainOnSelf = 20f;
            a.IsFollowup = true;
            a.CanTranscend = true; a.TranscendStrain = 15f;   // 100% block, stun immune, and heavy hits don't put it on cooldown
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
        { "Flurry", "Enter a Flurry for 4 seconds: 150% faster actions and +50% dodge, but every ability except your basic attack is locked." },
        { "PierceTheWound", "Fire a fast shot that deals double damage if the target is stunned." },
        { "Extricate", "Fire a heavy shot whose recoil blasts you 5m backwards, away from the target." },
        { "MarkOfTheUnmaker", "Fire a shot that applies a stack of Unmake. If the target already has 3 stacks, they are spent instead: the target is Fated for 1 second, then Unravels, whatever its strain. Transcend (hold its key, +15 strain): you are hidden while you wind up." },
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
        { "AMillionCuts", "Dash to a target and strike it 15 times over 2 seconds, each strike a small hit and a brief stun. Once started it can't be cancelled (damage on you still breaks it)." },
        { "Polystep", "Blink to a point." },
        { "TheEndlessHunt", "Slow the target's actions while you wind up (Fated), then blink to it. Doesn't need line of sight." },
        { "You", "Attach a chain to an enemy for 3 seconds. If it ends up more than 5m from you by any means, it is yanked back after a short delay, taking damage and a stun." },
        { "CognitiveBurden", "Take up to 50 of an ally's strain onto yourself and become Burdened for 3 seconds. The first hit an enemy lands on you while Burdened hands them that strain, and Burdened ends." },
        { "Banishment", "Punch an enemy extremely high into the air: it falls 9m and takes the fall damage." },
        { "Savior", "Dash to an ally, granting them 50% Block, while you wind up a stomp that damages, stuns and pushes away every enemy within 7m." },
        { "DeathSentence", "Wind up (losing sight of the target breaks it), then telekinetically grab the enemy and channel for up to 3 seconds, pulling it to you. An airborne target is instead pulled straight to the ground at quadruple speed and takes double fall damage." },
        { "RegenerationSeal", "Etch a healing seal onto an ally: it heals 50 health over 4 seconds. If they take damage the seal breaks, ending the heal early but stunning the attacker for 1 second." },
        { "RecollectionsOfSelf", "Summon two Wraiths beside you. They fight on your side by themselves, going for the nearest enemy with Rend." },
        { "UnmakeTheHarm", "Target an ally or yourself: after 2 seconds they heal 15 plus 30% of their missing health." },
        { "Soulseeker", "Fire a shot and project your mind into it, beginning a CHANNEL. While channeling, send the bullet on up to 4 times to an enemy it can see (not the one it just hit), each a moment after the last, damaging and stunning each one." },
        { "RefractionCloak", "Become Hidden for 5 seconds: enemies can't target you. Taking damage ends it early." },
        { "DestinyClosesIn", "Rise into the air over the windup and fire a shot that MARKS the target. If it lands, follow up with Fate Encumbers Its Prey while the target is still in sight." },
        { "FateEncumbersItsPrey", "Follow-up to Destiny Closes In: stay in the air and fire another shot that makes the target FATED, its actions heavily slowed. If it lands, follow up with Witness To The End." },
        { "WitnessToTheEnd", "Follow-up to Fate Encumbers Its Prey: stay in the air and fire a final heavy shot that also deals 20% of the target's missing health, then come back down." },
        { "Trammels", "Ensnare enemies at the target area, dealing damage and rooting them for 2 seconds." },
        { "MinutesTurnToCenturies", "Target an ally or yourself, slowing their actions during the windup before relieving 50 of their strain." },
        { "TrueInversion", "Channel for up to 2 seconds to invert an ally's mind: while it lasts, any strain they would gain they lose instead. Any damage on you breaks the channel." },
        { "DreadfulFixation", "Set a piercing gaze on a target, ROOTING them for 2 seconds. No hit roll." },
        { "CripplingPursuit", "Dash to a target location and load your weapon with special rounds for 2 seconds. You deal 30% less damage during this time, but damage you deal CRIPPLES for 1 second: 50% slower movement, 50% shorter dodges, and no dashes." },
        { "MentalWeapon", "Fire a shot at an ally, storing a bullet in their mind: MENTAL WEAPON for 3 seconds. If they deal single-target damage in that time, the bullet fires out 0.5 seconds later, dealing heavy damage to their target and stunning." },
        // Paradigm User
        { "Spark", "Basic ranged spell. Always available." },
        { "WitheringEnd", "Channel cognitive decay on a target for up to 2.5 seconds while they stay in your sight, dealing damage over time and slowing their movement. If the channel runs its course, a final heavy burst damages and stuns them. Damage on you breaks the channel." },
        { "Madness", "Afflict an enemy or an ally with MADNESS for 3 seconds: 50% more damage, 75% faster actions and 50% faster movement, but they can only use their basic attack, again and again, on the nearest enemy. Without a basic attack, they gain Wild Swing, a weak, inaccurate melee attack, for as long as it lasts." },
        { "WildSwing", "A weak, inaccurate melee swing, lent by MADNESS to a unit with no basic attack." },
        { "Splinters", "Lodge a splinter in reality in an enemy: a stack of SPLINTER. When they become UNRAVELED, each SPLINTER deals 15 extra damage and stuns them 0.2 seconds longer." },
        { "BanefulDomain", "Create a zone that damages enemies inside, more the longer it stays up. Casting this again while the zone is up renews it, for 15 strain." },
        { "Mindshatter", "Deal heavy damage to a target. Transcend (hold its key, +40 strain): +1% damage for each point of strain on the target and on you, and you UNRAVEL right after." },
        { "Reconstruct", "Heal an ally and move them away from the nearest enemy." },
        { "RiftLine", "Deal damage in a line that pierces terrain and 25% block." },
        { "Stutter", "A near-instant jolt that interrupts the target's windup or charge, and breaks their Block: it comes down and can't be used for 2 seconds. Deals no damage and costs you 15 strain." },
        { "LanceOfDestiny", "Charge: start charging (strain builds while you do; any damage breaks it), then aim and release. The lance hits the first enemy in its path; damage and stun grow with charge time, up to 4x damage and 6x stun." },
        { "Cataclysm", "After a long channel, deal heavy damage in a huge area and stun. Any stun breaks the channel." },
        { "Chronosphere", "Channel: a sphere around you that halves enemies' action speed for up to 2.5 seconds. You can't act or cancel it while channeling, and any damage or stun breaks it." },
        { "SecondMind", "Speed up an ally's actions by 1.5x, or slow an enemy's by 1.5x, for 3 seconds. No hit roll." },
        { "WrinklesOfReality", "Pick an ally, an enemy or yourself, then a point within 10m of them: they teleport there. No hit roll." },
        { "Amblivate", "Stun an enemy for 1.5s and lift them 7m into the air, then drop them: they take fall damage." },
        // Universal
        { "Dash", "Key G. Dash toward a point as fast as a dodge, up to your dash distance (costs 20 strain). You can't act until you land. Transcend (hold G, +15 strain): dash 80% further, much faster." },
        { "Block", "Raise your guard for 1 second (costs 20 strain, even past your max): +50% block, and stuns that land last half as long. Getting hit while guarding lets you act again right away, but a heavy hit puts Block on cooldown for 1s. Follow-up: press Space to block again. Transcend (hold its key, +15 strain): Block goes to 100% (block-piercing hits still get through), stuns don't land, and heavy hits don't put it on cooldown." },
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
        { "Wraith/Rend", "A basic melee attack." },
        { "Monarch/DivineJavelin", "Throws a javelin that deals damage and stuns." },
        { "Monarch/HordeOfMemories", "Flies into the air over the windup and summons 4 Wraiths below over 1 second, then drops back down." },
        { "Monarch/Incineration", "Flies up and toward its target over the windup until it is within 12m, then fires a continuous beam for 3 seconds that deals massive damage. Losing sight of the target interrupts it. Then flies back down." },
        { "Zola/WastefulThought", "Always on: enemies within 7m gain 5 strain every second." },
        { "Zola/Stomp", "Stomps the ground around it, damaging and stunning everyone within 7m and flinging them into the air." },
        { "Zola/MentalOverload", "Unstoppable while it winds up, then emits a huge pulse that damages, stuns and strains every enemy within 15m." },
        { "Deliverance/Disassociate", "A melee attack that afflicts the target with PERPLEXITY. It does nothing on its own, but lets Deliverance Slip behind them at once." },
        { "Deliverance/PraasHatred", "A fast, powerful melee attack that can be followed up again and again on the same target. After the 2nd follow-up, each one is a Heavy impact dealing double damage." },
        { "Deliverance/Slip", "Teleport behind a PERPLEXED target instantly, from anywhere." },
        { "Deliverance/FestivalOfColors", "Gain 50% action speed and move speed for 4 seconds, and access to Joyous Dance." },
        { "Deliverance/JoyousDance", "During Festival Of Colors: always dash away from enemies for the whole windup, keeping the target in range, then zap it with one of Marked, Time Slowed or Immobilized at random." },
        { "Deliverance/CloseTheLoop", "A powerful, wall-piercing ranged attack. If it is blocked or dodged, Deliverance teleports to the target and makes it FATED." },
        { "Deliverance/Stitch", "A melee attack that melds Deliverance into its target: untargetable and taking no damage, it heals while the target is lifted into the air, then dashes out of them." },
        { "Deliverance/Reduction", "Afflict every enemy in a line with REDUCTION: half movement speed, and strain building up over time." },
        { "Deliverance/SplitTheMinds", "At death's door: unkillable and unstoppable, Deliverance splits into its four parts. A pulse takes 75% of every other enemy's maximum health and pushes every unit 9m away." },
        { "Deliverance/ImprintUponItself", "Copy a random ability from a player unit's loadout into this unit's own, where it can be used normally. Each use adds 0.1s to its windup and recovery, 0.25s to its cooldown, and takes 2m off its range." },
        { "Revl/Equipose", "Cast at maximum strain instead of being Unraveled: become UNSTOPPABLE and shed all strain over the windup, then release a giant wave of strife that damages enemies and slows them by 75%." },
        { "Syne/Amahaltel", "A heavy ranged attack. Syne gains 75% Dodge while winding it up." },
        { "Syne/Ripples", "An incredibly fast reaction shot." },
        { "Syne/ScryTheMeek", "Channel a 15m killzone for 5 seconds. Any enemy inside that uses an ability, Dash and Block included, is shot by Ripples." },
        { "Syne/WidenTheScope", "A piercing shot through walls that knocks back everyone in its line." },
        { "Exemplar/IntrepidAdvance", "Dash to a target, damaging and stunning it." },
        { "Exemplar/SparkTheFlame", "A melee attack. If it lands, even into a block, the Exemplar can follow up with Light the Pyre." },
        { "Exemplar/LightThePyre", "Follow-up to Spark the Flame: dash to the target and strike it with a strong slash, stunning it for a long time." },
        { "Exemplar/PiercingRetreat", "Dash away from the target while winding up a piercing strike that damages and stuns everything in a line." },
        { "Exemplar/SplitApartTheSky", "Blink up into the air to a random spot away from the target and call down a smite on the area around it. Hit the Exemplar while it hangs there to knock it down." },
        { "Primavaeran/AbstractDecay", "Creates a zone of decay for 4 seconds that damages enemies inside every second and halves their movement speed." },
        { "Primavaeran/RecurrentRepel", "Pushes every enemy around it far away, damaging and stunning them." },
        { "Primavaeran/Distortion", "Teleports to a point, leaving behind a rift that damages enemies near where it stood and pulls them in." },
        { "Primavaeran/TearApartReality", "A long windup, then a channel over a huge area: delayed explosions go off across it, spread out. Starts the battle on cooldown." },
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
        { "withering_end", new ActionVisuals(VfxKind.Beam, new Color(0.55f, 0.75f, 0.3f), 0.12f) },
        { "madness", new ActionVisuals(VfxKind.Bolt, new Color(1f, 0.25f, 0.35f), 0.06f) },
        { "wild_swing", new ActionVisuals(VfxKind.Slash, new Color(1f, 0.8f, 0.6f), 0.09f) },
        { "splinters", new ActionVisuals(VfxKind.Bolt, new Color(0.85f, 0.75f, 1f), 0.035f) },
        { "baneful_domain", new ActionVisuals(VfxKind.Shockwave, new Color(0.6f, 0.15f, 0.3f), 0.2f) },
        { "mindshatter", new ActionVisuals(VfxKind.Bolt, new Color(1f, 0.35f, 0.85f), 0.12f) },
        // Sharpshooter, round AF
        { "dreadful_fixation", new ActionVisuals(VfxKind.Beam, new Color(0.45f, 0.1f, 0.2f), 0.05f) },
        { "crippling_pursuit", new ActionVisuals(VfxKind.Buff, new Color(1f, 0.55f, 0.2f)) },
        { "mental_weapon", new ActionVisuals(VfxKind.Tracer, new Color(0.55f, 0.95f, 1f), 0.06f) },
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
        // Round O
        { "cognitive_burden", new ActionVisuals(VfxKind.Bolt, new Color(0.85f, 0.55f, 1f), 0.06f) },
        { "banishment", new ActionVisuals(VfxKind.Shockwave, new Color(1f, 0.85f, 0.5f), 0.2f) },
        { "death_sentence", new ActionVisuals(VfxKind.Chain, new Color(0.6f, 0.2f, 0.85f), 0.08f) },
        { "a_million_cuts", new ActionVisuals(VfxKind.Slash, new Color(0.9f, 0.95f, 1f), 0.05f) },
        { "fighter_polystep", new ActionVisuals(VfxKind.Buff, new Color(0.6f, 0.5f, 1f)) },
        { "the_endless_hunt", new ActionVisuals(VfxKind.Bolt, new Color(0.55f, 0.15f, 0.35f), 0.05f) },
        { "you", new ActionVisuals(VfxKind.Chain, new Color(0.78f, 0.8f, 0.86f), 0.07f) },
        { "savior", new ActionVisuals(VfxKind.Shockwave, new Color(0.95f, 0.85f, 0.55f), 0.2f) },
        { "regeneration_seal", new ActionVisuals(VfxKind.Heal, new Color(0.45f, 1f, 0.5f)) },
        { "true_inversion", new ActionVisuals(VfxKind.Bolt, new Color(0.4f, 1f, 0.85f), 0.05f) },
        { "unmake_the_harm", new ActionVisuals(VfxKind.Heal, new Color(0.5f, 1f, 0.65f), 0.1f) },
        { "recollections_of_self", new ActionVisuals(VfxKind.Buff, new Color(0.65f, 0.55f, 0.95f), 0.1f) },
        { "soulseeker", new ActionVisuals(VfxKind.Tracer, new Color(0.55f, 0.95f, 1f), 0.08f) },
        { "refraction_cloak", new ActionVisuals(VfxKind.Buff, new Color(0.7f, 0.9f, 1f), 0.1f) },
        { "destiny_closes_in", new ActionVisuals(VfxKind.Tracer, new Color(1f, 0.85f, 0.45f), 0.07f) },
        { "fate_encumbers_its_prey", new ActionVisuals(VfxKind.Tracer, new Color(0.85f, 0.55f, 1f), 0.08f) },
        { "witness_to_the_end", new ActionVisuals(VfxKind.Tracer, new Color(1f, 0.35f, 0.3f), 0.13f) },
        { "trammels", new ActionVisuals(VfxKind.Shockwave, new Color(0.55f, 0.85f, 0.4f), 0.1f) },
        { "minutes_turn_to_centuries", new ActionVisuals(VfxKind.Heal, new Color(0.55f, 0.6f, 1f)) },
        { "flurry", new ActionVisuals(VfxKind.Buff, new Color(1f, 0.85f, 0.3f)) },
        { "disassociate", new ActionVisuals(VfxKind.Slash, new Color(1f, 0.3f, 0.85f), 0.1f) },
        { "praas_hatred", new ActionVisuals(VfxKind.Slash, new Color(0.85f, 0.1f, 0.25f), 0.12f) },
        { "slip", new ActionVisuals(VfxKind.Buff, new Color(0.3f, 1f, 0.9f)) },
        { "festival_of_colors", new ActionVisuals(VfxKind.Buff, new Color(1f, 0.6f, 0.9f), 0.12f) },
        { "joyous_dance", new ActionVisuals(VfxKind.Bolt, new Color(1f, 0.75f, 0.35f), 0.06f) },
        { "close_the_loop", new ActionVisuals(VfxKind.Beam, new Color(1f, 0.85f, 0.4f), 0.14f) },
        { "stitch", new ActionVisuals(VfxKind.Slash, new Color(0.3f, 1f, 0.9f), 0.1f) },
        { "reduction_line", new ActionVisuals(VfxKind.Shockwave, new Color(0.55f, 0.75f, 0.3f), 0.2f) },
        { "split_the_minds", new ActionVisuals(VfxKind.Buff, new Color(1f, 0.3f, 0.85f), 0.25f) },
        { "imprint_upon_itself", new ActionVisuals(VfxKind.Bolt, new Color(1f, 0.3f, 0.85f), 0.05f) },
        { "equipose", new ActionVisuals(VfxKind.Shockwave, new Color(0.55f, 1f, 0.45f), 0.3f) },
        { "amahaltel", new ActionVisuals(VfxKind.Tracer, new Color(0.7f, 0.85f, 1f), 0.12f) },
        { "ripples", new ActionVisuals(VfxKind.Tracer, new Color(0.55f, 0.9f, 1f), 0.05f) },
        { "scry_the_meek", new ActionVisuals(VfxKind.Buff, new Color(1f, 0.3f, 0.3f), 0.15f) },
        { "widen_the_scope", new ActionVisuals(VfxKind.Shockwave, new Color(0.7f, 0.85f, 1f), 0.22f) },
        { "intrepid_advance", new ActionVisuals(VfxKind.Slash, new Color(1f, 0.85f, 0.4f), 0.12f) },
        { "spark_the_flame", new ActionVisuals(VfxKind.Slash, new Color(1f, 0.7f, 0.3f), 0.1f) },
        { "light_the_pyre", new ActionVisuals(VfxKind.Slash, new Color(1f, 0.45f, 0.15f), 0.24f) },
        { "piercing_retreat", new ActionVisuals(VfxKind.Beam, new Color(1f, 0.9f, 0.55f), 0.2f, true) },
        { "split_apart_the_sky", new ActionVisuals(VfxKind.Shockwave, new Color(1f, 0.88f, 0.45f), 0.1f) },
        { "rend", new ActionVisuals(VfxKind.Slash, new Color(0.55f, 0.45f, 0.85f), 0.1f) },
        { "divine_javelin", new ActionVisuals(VfxKind.Tracer, new Color(1f, 0.92f, 0.55f), 0.09f) },
        { "horde_of_memories", new ActionVisuals(VfxKind.Buff, new Color(0.55f, 0.45f, 0.85f)) },
        { "incineration", new ActionVisuals(VfxKind.Beam, new Color(1f, 0.6f, 0.2f), 0.35f) },
        { "wasteful_thought", new ActionVisuals(VfxKind.Buff, new Color(0.45f, 0.75f, 1f)) },
        { "stomp", new ActionVisuals(VfxKind.Shockwave, new Color(0.45f, 0.75f, 1f), 0.2f) },
        { "mental_overload", new ActionVisuals(VfxKind.Shockwave, new Color(0.55f, 0.85f, 1f), 0.3f) },
        { "abstract_decay", new ActionVisuals(VfxKind.Lob, new Color(0.55f, 0.2f, 0.45f)) },
        { "recurrent_repel", new ActionVisuals(VfxKind.Shockwave, new Color(0.75f, 0.3f, 0.6f), 0.2f) },
        { "distortion", new ActionVisuals(VfxKind.Buff, new Color(0.65f, 0.25f, 0.75f)) },
        { "tear_apart_reality", new ActionVisuals(VfxKind.Buff, new Color(0.85f, 0.2f, 0.45f)) },
        { "pierce_the_wound", new ActionVisuals(VfxKind.Tracer, new Color(1f, 0.4f, 0.35f), 0.04f) },
        { "extricate", new ActionVisuals(VfxKind.Tracer, new Color(1f, 0.75f, 0.4f), 0.12f) },
        { "mark_of_the_unmaker", new ActionVisuals(VfxKind.Tracer, new Color(0.75f, 0.2f, 0.9f), 0.05f) },
    };

    // Earlier default texts, replaced by the current ones where an asset still has one of them unedited. When a
    // description above changes, add the text it had here.
    private static readonly Dictionary<string, string[]> OldDescriptions = new Dictionary<string, string[]>
    {
        { "Monarch/Incineration", new[] { "Flies up and toward its target over the windup until it is within 12m, then fires a continuous beam for 3 seconds that deals massive damage. Then flies back down." } },
        { "Stutter", new[] { "A near-instant jolt that interrupts the target's windup or charge. Deals no damage and costs you 15 strain." } },
        { "BattleTrance", new[] { "Briefly enter a trance: sheds strain and speeds up your actions. Enables Final Cut." } },
        { "FinalCut", new[] { "Trance only. A finishing blow that deals more damage the more health the target is missing. A kill extends your trance by 3 seconds." } },
        { "Brace", new[] { "Raise your guard for +50% block for 5 seconds. Hits don't wear it down." } },
        { "Subjugate", new[] { "Become immune to stun and grab a target, stunning it for 2.5s and carrying it with you. While you hold it you can only move, and you headbutt it every 0.5s for 10 damage; after each headbutt you can let go early. When you let go or the stun ends, wind up a short throw that hurls it away for 15 damage." } },
        { "PhantomStrike", new[] { "Dash to the target and slash it, marking it (attacks hit it more easily). A wall in the way stops the dash short." } },
        { "Block", new[]
            {
                "Raise your guard for a moment (costs 20 strain): +50% block and immune to stun. Getting hit while guarding lets you act again right away. Follow-up: press Space to block again.",
                "Raise your guard for 1 second (costs 20 strain, even past your max): +50% block and immune to stun, except Unravel. Getting hit while guarding lets you act again right away. Follow-up: press Space to block again.",
                "Raise your guard for 1 second (costs 20 strain, even past your max): +50% block and immune to stun, except Unravel. Getting hit while guarding lets you act again right away, but a heavy hit puts Block on cooldown for 1s. Follow-up: press Space to block again. Transcend (hold its key, +15 strain): blocks all damage and heavy hits don't put it on cooldown.",
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

    /// <summary>
    /// New abilities for a class, added after DataVersion froze: each is put on the unit once, the first time this runs
    /// with it (after 'after' if it has that one, else at the end), and remembered in UnitDefinition.GrantedActions, so
    /// one removed in the Inspector stays removed.
    /// </summary>
    private static void Grant(UnitDefinition u, ActionDefinition after, params ActionDefinition[] actions)
    {
        if (u.GrantedActions == null) u.GrantedActions = new List<string>();
        bool changed = false;
        int at = after != null ? u.Actions.IndexOf(after) : -1;
        foreach (var a in actions)
        {
            if (a == null || u.GrantedActions.Contains(a.Id)) continue;
            u.GrantedActions.Add(a.Id);
            if (!u.Actions.Contains(a)) { if (at >= 0) u.Actions.Insert(++at, a); else u.Actions.Add(a); }
            changed = true;
        }
        if (changed) CombatTestSceneBuilder.MarkDirty(u);
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
