using System.Collections.Generic;
using UnityEngine;
using TacticsGame.Core;

/// <summary>
/// Creates the four squad classes from the design doc (Sharpshooter, Melee Fighter, Tank, Paradigm User) and their
/// abilities as assets under Assets/Actions/&lt;Class&gt; and Assets/Units/&lt;Class&gt;, plus the universal Block. Each asset is
/// written the first time and again only when DataVersion is bumped (once), so Inspector tweaks survive ordinary rebuilds;
/// delete an asset to regenerate it.
/// </summary>
public static class SquadAssets
{
    public struct Squad { public UnitDefinition Sharpshooter, Melee, Tank, Paradigm; }

    /// <summary>
    /// Bump when the squad data changes in a way existing assets must pick up. An asset written with an older revision is
    /// reset to defaults and re-initialised once (overwriting Inspector tweaks made before the bump, and its description);
    /// later builds leave it alone. The reset matters: init only sets the fields it names, so without it a value the new
    /// data no longer mentions (an old EnergyCost) would survive.
    /// 2: universal Block, Fighter on strain instead of energy, target strain on every attack, follow-up abilities.
    /// 3: redo 2 with the reset (2 left the Fighter's old energy costs in place).
    /// 4: Take Aim 3s and not cancelled by your own actions, 1s Block, Steady Strikes replaces the Focused Strikes,
    ///    Final Cut keeps the trance (a kill extends it), Brace's block isn't worn down.
    /// 5: Final Cut gets a 4s cooldown.
    /// 6: universal Dash; each unit gets Block OR Dash (ranged dash, melee block).
    /// </summary>
    public const int DataVersion = 6;

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
            // Focused Strike's stats; the follow-up on the same target gets Focused Strike+'s (SteadyStrikesBehavior).
            Attack(a, "steady_strikes", "Steady Strikes", "steady_strikes", range: 1.8f, windup: 0.6f, recovery: 0.5f, damage: 16f, accuracy: 0.8f, stun: 0.5f, strain: 8f, melee: true);
            a.IsFollowup = true;
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
                new StatusSpec { Id = StatusIds.Trance, Duration = 6f, ActionSpeedMult = 1.2f });
            a.StrainOnSelf = -35f;   // relieves strain
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
            u.Actions = new List<ActionDefinition> { finalCut, steady, rapid, evasive, trance };
            u.ViewPrefab = viewPrefab;
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
        var brace = Action(c, "Brace", a =>
            SelfBuff(a, "brace", "Brace", "self_buff", windup: 0.3f, recovery: 0.2f, cooldown: 10f,
                new StatusSpec { Id = StatusIds.Braced, Duration = 5f, BlockBonus = 0.5f, KeepsBlock = true }));

        return Unit(c, u =>
        {
            u.DisplayName = "Tank"; u.MaxHealth = 180f; u.BaseDodge = 0.05f; u.MaxStrain = 140f; u.MoveSpeed = 3.5f;
            u.BodyRadius = 0.5f;
            u.BasicAttack = mace;
            u.Actions = new List<ActionDefinition> { slam, hook, shockwave, brace };
            u.ViewPrefab = viewPrefab;
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
            u.Actions = new List<ActionDefinition> { reconstruct, riftLine, cataclysm };
            u.ViewPrefab = viewPrefab;
        });
    }

    // ------------------------------------------------------------------ helpers

    private static ActionDefinition Action(string cls, string file, System.Action<ActionDefinition> init)
    {
        var a = CombatTestSceneBuilder.GetOrCreate("Assets/Actions/" + cls + "/" + file + ".asset", init);
        if (a.DataVersion < DataVersion)
        {
            CombatTestSceneBuilder.ResetToDefaults(a);
            init(a);
            a.Description = "";   // refilled below with the current text
            a.DataVersion = DataVersion;
            CombatTestSceneBuilder.MarkDirty(a);
        }
        ApplyDescription(a, file);
        return a;
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
        });
    }

    /// <summary>Universal Dash (the other option): slide toward a point at dodge speed and reach, paid for in strain.</summary>
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
        });
    }

    /// <summary>Gives a unit definition its universal option if it has none: Dash for ranged units, Block for melee.</summary>
    public static void EnsureUniversal(UnitDefinition u)
    {
        if (u == null || u.Universal != null) return;
        u.Universal = DefaultUniversal(u);
        CombatTestSceneBuilder.MarkDirty(u);
    }

    public static ActionDefinition DefaultUniversal(UnitDefinition u) { return u.PrefersDash ? UniversalDash() : UniversalBlock(); }

    /// <summary>Fills in the hover text for an ability if it has none yet, or still has an older default from
    /// OldDescriptions (text edited in the Inspector is never overwritten).</summary>
    public static void ApplyDescription(ActionDefinition a, string key)
    {
        string text, old;
        if (a == null || !Descriptions.TryGetValue(key, out text) || a.Description == text) return;
        bool stale = OldDescriptions.TryGetValue(key, out old) && a.Description == old;
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
        { "SteadyStrikes", "A deliberate, steady melee strike. Follow-up: strike the same target again right after (Space) for an empowered blow that costs 20 strain, always hits and pierces 25% block." },
        { "EvasiveStrike", "Melee attack. Increases your dodge while you swing and for a short time afterwards." },
        { "BattleTrance", "Briefly enter a trance: sheds strain and speeds up your actions. Enables Final Cut." },
        { "FinalCut", "Trance only. A finishing blow that deals more damage the more health the target is missing. A kill extends your trance by 3 seconds." },
        // Tank
        { "MaceStrike", "Basic melee attack. Always available." },
        { "ShieldSlam", "Heavy shield blow: a long stun that interrupts windups and builds strain." },
        { "ChainHook", "Hook an enemy and drag them to you, stunning them briefly." },
        { "Shockwave", "Slam the ground, stunning every enemy around you and building their strain." },
        { "Brace", "Raise your guard for +50% block for 5 seconds. Hits don't wear it down." },
        // Paradigm User
        { "Spark", "Basic ranged spell. Always available." },
        { "Reconstruct", "Heal an ally and move them away from the nearest enemy." },
        { "RiftLine", "Deal damage in a line that pierces terrain and 25% block." },
        { "Cataclysm", "After a long channel, deal heavy damage in a huge area and stun. Any stun breaks the channel." },
        // Universal
        { "Dash", "Dash toward a point as fast as a dodge, up to your dash distance (costs 20 strain). You can't act until you land." },
        { "Block", "Raise your guard for 1 second (costs 20 strain, even past your max): +50% block and immune to stun, except Unravel. Getting hit while guarding lets you act again right away. Follow-up: press Space to block again." },
        // Enemies
        { "HeavyStrike", "Slow, heavy melee blow that stuns." },
    };

    // Earlier default texts, replaced by the current ones where an asset still has them unedited.
    private static readonly Dictionary<string, string> OldDescriptions = new Dictionary<string, string>
    {
        { "Block", "Raise your guard for a moment (costs 20 strain): +50% block and immune to stun. Getting hit while guarding lets you act again right away. Follow-up: press Space to block again." },
    };

    private static UnitDefinition Unit(string cls, System.Action<UnitDefinition> init)
    {
        var u = CombatTestSceneBuilder.GetOrCreate("Assets/Units/" + cls + "/" + cls + ".asset", init);
        if (u.DataVersion < DataVersion)
        {
            CombatTestSceneBuilder.ResetToDefaults(u);
            init(u);
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
