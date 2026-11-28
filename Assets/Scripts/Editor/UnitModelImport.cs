using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using TacticsGame.Core;

/// <summary>
/// Unit models made outside Unity (Blender): export an FBX to Assets/Models/&lt;Unit&gt;/&lt;Unit&gt;.fbx (the folder named
/// after the unit's Display Name, e.g. Assets/Models/Fighter/) and on every import this:
///   - sets it up as a Generic rig whose clips are named after the actions in the file ("FighterRig|Run" -> "Run"),
///     looping Idle, Run (or Walk) and Stun (the rest play once and hold their last frame);
///   - builds or rebuilds &lt;Unit&gt;.controller beside it: one state per clip it knows (Clips below), driven by the
///     parameters UnitView sets on battle events (its triggers, the Moving / Stunned / Dead bools, DashDir), plus one
///     state per ability clip ("&lt;ability id&gt;.Windup", ".Dash", ".Strike"), which UnitView plays by name;
///   - points every unit definition with that Display Name at the model, the controller and &lt;Unit&gt;Etchings.png
///     (UnitDefinition.Model, ModelAnimations, ModelEtchings), so the next battle shows it.
/// A clip missing from the file is just left out (an ability without its own clips uses Windup and Strike).
/// </summary>
public class UnitModelImport : AssetPostprocessor
{
    public const string ModelsFolder = "Assets/Models/";

    /// <summary>The shared clips a unit model can have, by name, and whether each loops.</summary>
    public static readonly Dictionary<string, bool> Clips = new Dictionary<string, bool>
    {
        { "Idle", true }, { "Run", true }, { "Walk", true }, { "Windup", false }, { "Strike", false }, { "Block", false },
        { "DashForward", false }, { "DashBack", false }, { "DashSide", false }, { "Dash", false },
        { "HitLight", false }, { "HitHeavy", false }, { "Interrupted", false }, { "Stun", true }, { "Death", false },
    };

    /// <summary>The phases an ability's own clips can have: "&lt;ability id&gt;.&lt;phase&gt;".</summary>
    public static readonly string[] AbilityPhases = { "Windup", "Dash", "Strike" };

    /// <summary>The Animator state for an ability's clip: its name with a space for the dot (UnitView.AbilityState).</summary>
    public static string AbilityState(string clipName) { return clipName.Replace('.', ' '); }

    private static bool IsAbilityClip(string name)
    {
        int dot = name.LastIndexOf('.');
        return dot > 0 && AbilityPhases.Contains(name.Substring(dot + 1));
    }

    private static bool IsUnitModel(string path)
    {
        return path.StartsWith(ModelsFolder) && path.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase);
    }

    private static string ClipName(string take)
    {
        int bar = take.LastIndexOf('|');
        return bar >= 0 ? take.Substring(bar + 1) : take;
    }

    private void OnPreprocessModel()
    {
        if (!IsUnitModel(assetPath)) return;
        var importer = (ModelImporter)assetImporter;
        importer.animationType = ModelImporterAnimationType.Generic;
        importer.importAnimation = true;
        // Kept so the slots arrive by name (Body, Metal, Etched); UnitLooks swaps them for the team's colours.
        importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        importer.importCameras = false;
        importer.importLights = false;
        importer.importBlendShapes = false;
        importer.isReadable = true;   // the tactical view (Alt, TacticalView) draws the model's edges from its mesh
    }

    private void OnPreprocessAnimation()
    {
        if (!IsUnitModel(assetPath)) return;
        var importer = (ModelImporter)assetImporter;
        var clips = importer.defaultClipAnimations;
        foreach (var c in clips)
        {
            c.name = ClipName(c.takeName);
            bool loops;
            c.loopTime = Clips.TryGetValue(c.name, out loops) && loops;
        }
        importer.clipAnimations = clips;
    }

    private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        foreach (var path in imported)
            if (IsUnitModel(path)) EditorApplication.delayCall += () => Hookup(path);   // after the import has settled
    }

    /// <summary>The controller for the model at 'path', and the unit definitions it belongs to.</summary>
    public static void Hookup(string path)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (model == null) return;
        var clips = AssetDatabase.LoadAllAssetRepresentationsAtPath(path).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__")).ToList();
        string folder = Path.GetDirectoryName(path).Replace('\\', '/');
        string unit = Path.GetFileName(folder);
        string controllerPath = folder + "/" + unit + ".controller";
        var controller = BuildController(controllerPath, clips);
        var etchings = AssetDatabase.LoadAssetAtPath<Texture2D>(folder + "/" + unit + "Etchings.png");

        int linked = 0;
        // A <Unit>Diverged model is that unit's Diverged form (the Forsaken): DivergedModel / DivergedAnimations.
        const string DivergedSuffix = "Diverged";
        bool divergedForm = unit.Length > DivergedSuffix.Length && unit.EndsWith(DivergedSuffix);
        string owner = divergedForm ? unit.Substring(0, unit.Length - DivergedSuffix.Length) : unit;
        foreach (var guid in AssetDatabase.FindAssets("t:UnitDefinition"))
        {
            var def = AssetDatabase.LoadAssetAtPath<UnitDefinition>(AssetDatabase.GUIDToAssetPath(guid));
            if (def == null || (def.DisplayName != owner && def.name != owner)) continue;
            linked++;
            if (divergedForm)
            {
                if (def.DivergedModel == model && def.DivergedAnimations == controller) continue;
                Undo.RecordObject(def, "Hook up unit model");
                def.DivergedModel = model;
                def.DivergedAnimations = controller;
                EditorUtility.SetDirty(def);
                continue;
            }
            if (def.Model == model && def.ModelAnimations == controller && def.ModelEtchings == etchings) continue;
            Undo.RecordObject(def, "Hook up unit model");
            def.Model = model;
            def.ModelAnimations = controller;
            def.ModelEtchings = etchings;
            EditorUtility.SetDirty(def);
        }
        AssetDatabase.SaveAssets();
        Debug.Log(string.Format("Unit model {0}: {1} clips ({2}) in {3}{4}, used by {5} unit definition(s).", path, clips.Count,
            string.Join(", ", clips.Select(c => c.name)), controllerPath, etchings != null ? ", etchings " + etchings.name : "", linked));
    }

    /// <summary>
    /// The model's Animator Controller, rebuilt from its clips (an existing one at 'path' is cleared and refilled, so
    /// references to it hold). Idle and Run (else Walk) follow Moving; a trigger from any state starts Windup, Strike
    /// (Resolve), Block, a dash (Dodge, by DashDir: 0 forward, 1 back, 2 / 3 to the right / left, DashSide mirrored by
    /// UnitView), a hit (HitLight, HitHeavy; Unravel plays HitHeavy), Interrupted, Stun or Death, none of them once Dead.
    /// The one-shot clips go back to Idle when they end (Block after a second); Stun when Stunned clears; Windup waits
    /// for Resolve or Interrupted, playing at WindupSpeed (UnitView fits it to the windup's length).
    /// An ability's own clips are states UnitView cross-fades to by name: its Windup (at WindupSpeed, held), then its
    /// Dash (if any, straight into its Strike) or its Strike, which goes back to Idle when it ends.
    /// </summary>
    private static AnimatorController BuildController(string path, List<AnimationClip> clips)
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
        if (controller == null) controller = AnimatorController.CreateAnimatorControllerAtPath(path);
        foreach (var p in controller.parameters.ToArray()) controller.RemoveParameter(p);
        while (controller.layers.Length > 1) controller.RemoveLayer(1);
        var sm = controller.layers[0].stateMachine;
        foreach (var s in sm.states.ToArray()) sm.RemoveState(s.state);
        foreach (var t in sm.anyStateTransitions.ToArray()) sm.RemoveAnyStateTransition(t);

        foreach (var trigger in new[] { "Windup", "Resolve", "Block", "Dodge", "HitLight", "HitHeavy", "Unravel", "Interrupted", "Stun", "Death" })
            controller.AddParameter(trigger, AnimatorControllerParameterType.Trigger);
        foreach (var flag in new[] { "Moving", "Stunned", "Dead" }) controller.AddParameter(flag, AnimatorControllerParameterType.Bool);
        controller.AddParameter("DashDir", AnimatorControllerParameterType.Int);
        controller.AddParameter(new AnimatorControllerParameter { name = "WindupSpeed", type = AnimatorControllerParameterType.Float, defaultFloat = 1f });
        controller.AddParameter(new AnimatorControllerParameter { name = "ActionSpeed", type = AnimatorControllerParameterType.Float, defaultFloat = 1f });

        var byName = new Dictionary<string, AnimationClip>();
        foreach (var c in clips) byName[c.name] = c;
        var states = new Dictionary<string, AnimatorState>();
        int row = 0;
        foreach (var name in Clips.Keys)
        {
            AnimationClip clip;
            if (!byName.TryGetValue(name, out clip)) continue;
            if (name == "Walk" && byName.ContainsKey("Run")) continue;   // Run is the walk
            var state = sm.AddState(name, new Vector3(name == "Idle" || name == "Run" || name == "Walk" ? 300f : 600f, 60f * row++, 0f));
            state.motion = clip;
            if (name == "Windup") { state.speedParameterActive = true; state.speedParameter = "WindupSpeed"; }
            states[name] = state;
        }
        if (states.ContainsKey("Idle")) sm.defaultState = states["Idle"];
        AnimatorState idle, walk;
        states.TryGetValue("Idle", out idle);
        if (!states.TryGetValue("Run", out walk)) states.TryGetValue("Walk", out walk);

        System.Action<AnimatorState, string, int> any = (to, param, dashDir) =>
        {
            if (to == null) return;
            var t = sm.AddAnyStateTransition(to);
            t.AddCondition(AnimatorConditionMode.If, 0f, param);
            if (dashDir >= 0) t.AddCondition(AnimatorConditionMode.Equals, dashDir, "DashDir");
            t.AddCondition(AnimatorConditionMode.IfNot, 0f, "Dead");
            t.hasExitTime = false; t.duration = 0.05f; t.canTransitionToSelf = param != "Stun";
        };
        System.Func<string, AnimatorState> S = n => { AnimatorState s; return states.TryGetValue(n, out s) ? s : null; };
        any(S("Windup"), "Windup", -1);
        any(S("Strike"), "Resolve", -1);
        any(S("Block"), "Block", -1);
        var fallbackDash = S("Dash") ?? S("DashForward");
        any(S("DashForward") ?? fallbackDash, "Dodge", 0);
        any(S("DashBack") ?? fallbackDash, "Dodge", 1);
        any(S("DashSide") ?? fallbackDash, "Dodge", 2);
        any(S("DashSide") ?? fallbackDash, "Dodge", 3);
        any(S("HitLight"), "HitLight", -1);
        any(S("HitHeavy"), "HitHeavy", -1);
        any(S("HitHeavy"), "Unravel", -1);
        any(S("Interrupted"), "Interrupted", -1);
        any(S("Stun"), "Stun", -1);
        if (states.ContainsKey("Death"))
        {
            var t = sm.AddAnyStateTransition(states["Death"]);
            t.AddCondition(AnimatorConditionMode.If, 0f, "Death");
            t.hasExitTime = false; t.duration = 0.05f; t.canTransitionToSelf = false;
        }

        if (idle != null && walk != null)
        {
            var go = idle.AddTransition(walk); go.AddCondition(AnimatorConditionMode.If, 0f, "Moving"); go.hasExitTime = false; go.duration = 0.1f;
            var stop = walk.AddTransition(idle); stop.AddCondition(AnimatorConditionMode.IfNot, 0f, "Moving"); stop.hasExitTime = false; stop.duration = 0.1f;
        }
        System.Action<AnimatorState, float> backToRest = (s, exit) =>
        {
            if (s == null || idle == null) return;
            if (walk != null)
            {
                var w = s.AddTransition(walk); w.hasExitTime = true; w.exitTime = exit; w.duration = 0.1f; w.AddCondition(AnimatorConditionMode.If, 0f, "Moving");
            }
            var back = s.AddTransition(idle); back.hasExitTime = true; back.exitTime = exit; back.duration = 0.12f;
        };
        // One-shots back to rest when they end: walking again if it is (Moving), else Idle.
        foreach (var name in new[] { "Strike", "DashForward", "DashBack", "DashSide", "Dash", "HitLight", "HitHeavy", "Interrupted" }) backToRest(S(name), 1f);
        backToRest(S("Block"), 6f);   // Block's raise held about a second (its guard)
        AnimatorState stun = S("Stun");
        if (stun != null && idle != null)
        {
            var up = stun.AddTransition(idle); up.AddCondition(AnimatorConditionMode.IfNot, 0f, "Stunned"); up.hasExitTime = false; up.duration = 0.15f;
        }

        // Each ability's own clips: a column of states per ability, played by name from UnitView.
        int col = 0;
        foreach (var ability in clips.Where(c => IsAbilityClip(c.name)).Select(c => c.name.Substring(0, c.name.LastIndexOf('.'))).Distinct().OrderBy(a => a))
        {
            var phase = new Dictionary<string, AnimatorState>();
            int r = 0;
            foreach (var p in AbilityPhases)
            {
                AnimationClip clip;
                if (!byName.TryGetValue(ability + "." + p, out clip)) continue;
                var s = sm.AddState(AbilityState(clip.name), new Vector3(900f + 260f * col, 60f * r++, 0f));
                s.motion = clip;
                if (p == "Windup") { s.speedParameterActive = true; s.speedParameter = "WindupSpeed"; }
                phase[p] = s;
            }
            col++;
            AnimatorState dash, strike;
            phase.TryGetValue("Strike", out strike);
            if (phase.TryGetValue("Dash", out dash) && strike != null)
            {
                var t = dash.AddTransition(strike); t.hasExitTime = true; t.exitTime = 1f; t.duration = 0.03f;
            }
            else backToRest(dash, 1f);
            backToRest(strike, 1f);
        }
        EditorUtility.SetDirty(controller);
        return controller;
    }
}
