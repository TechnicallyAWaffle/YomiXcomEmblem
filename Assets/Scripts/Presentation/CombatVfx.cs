using System.Collections.Generic;
using UnityEngine;
using TacticsGame.Core;

namespace TacticsGame.Presentation
{
    /// <summary>
    /// Placeholder combat VFX and feedback, built entirely from code (pooled lines, two particle systems, IMGUI text)
    /// so it needs no art assets. Driven only by dispatched timeline events, so it stays in step with playback,
    /// slow motion, the order freeze and replays. Swap pieces for real effects as art arrives (see StyleFor).
    ///   Windups:   a charge ring at the feet, an aim line to the target, a ground telegraph for area attacks.
    ///   Resolves:  per-ability tracers, bolts, slashes, beams, shockwaves, chains, heals, buffs and lobbed traps.
    ///   Reactions: hit sparks + flash, damage numbers, miss/cover/dodge/block text, dodge afterimages, stun stars,
    ///              unravel and death bursts, interrupt shards, status pops and lasting status rings.
    /// Effects run on scaled time, so they freeze with the world while the player gives orders.
    /// </summary>
    public class CombatVfx : MonoBehaviour
    {
        public PlaybackDirector Director;
        public Camera Camera;
        [Range(0.5f, 2f)] public float TextScale = 1f;

        public enum FxKind { Tracer, Bolt, Slash, Beam, Shockwave, Chain, Heal, Buff, Lob }

        public struct Style
        {
            public FxKind Kind; public Color Color; public float Width; public bool Pierce;
            public Style(FxKind kind, Color color, float width = 0.06f, bool pierce = false) { Kind = kind; Color = color; Width = width; Pierce = pierce; }
        }

        // Per-ability looks. Anything not listed gets a style derived from its ActionDefinition.
        private static readonly Dictionary<string, Style> Styles = new Dictionary<string, Style>
        {
            // Sharpshooter
            { "snipe", new Style(FxKind.Tracer, new Color(1f, 0.25f, 0.2f), 0.1f) },
            { "tracking_shot", new Style(FxKind.Tracer, new Color(0.5f, 0.95f, 1f), 0.13f, true) },
            { "snapshot", new Style(FxKind.Tracer, new Color(1f, 0.9f, 0.4f), 0.04f) },
            { "marking_shot", new Style(FxKind.Tracer, new Color(1f, 0.35f, 0.9f), 0.05f) },
            { "take_aim", new Style(FxKind.Buff, new Color(1f, 0.3f, 0.25f)) },
            { "retaliate", new Style(FxKind.Buff, new Color(1f, 0.6f, 0.2f)) },
            { "trap", new Style(FxKind.Lob, new Color(0.4f, 0.85f, 1f)) },
            { "rifle_shot", new Style(FxKind.Tracer, new Color(1f, 0.95f, 0.7f), 0.035f) },
            // Melee Fighter
            { "slash", new Style(FxKind.Slash, new Color(0.95f, 0.95f, 1f), 0.1f) },
            { "rapid_slices", new Style(FxKind.Slash, new Color(1f, 0.9f, 0.35f), 0.06f) },
            { "steady_strikes", new Style(FxKind.Slash, new Color(1f, 0.7f, 0.35f), 0.14f) },
            { "evasive_strike", new Style(FxKind.Slash, new Color(0.4f, 1f, 0.95f), 0.1f) },
            { "battle_trance", new Style(FxKind.Buff, new Color(0.75f, 0.35f, 1f)) },
            { "final_cut", new Style(FxKind.Slash, new Color(1f, 0.1f, 0.2f), 0.28f) },
            // Tank
            { "mace_strike", new Style(FxKind.Slash, new Color(0.9f, 0.8f, 0.5f), 0.16f) },
            { "shield_slam", new Style(FxKind.Shockwave, new Color(0.55f, 0.75f, 1f), 0.12f) },
            { "chain_hook", new Style(FxKind.Chain, new Color(0.75f, 0.75f, 0.8f), 0.06f) },
            { "shockwave", new Style(FxKind.Shockwave, new Color(0.95f, 0.8f, 0.55f), 0.16f) },
            { "brace", new Style(FxKind.Buff, new Color(0.4f, 0.65f, 1f)) },
            // Universal
            { "block", new Style(FxKind.Buff, new Color(0.55f, 0.85f, 1f)) },
            // Paradigm User
            { "spark", new Style(FxKind.Bolt, new Color(0.45f, 0.9f, 1f), 0.05f) },
            { "reconstruct", new Style(FxKind.Heal, new Color(0.35f, 1f, 0.5f)) },
            { "rift_line", new Style(FxKind.Beam, new Color(0.7f, 0.35f, 1f), 0.2f) },
            { "cataclysm", new Style(FxKind.Shockwave, new Color(0.65f, 0.3f, 1f), 0.25f) },
            // Enemies
            { "heavy_strike", new Style(FxKind.Slash, new Color(1f, 0.55f, 0.2f), 0.18f) },
        };

        // Lasting statuses: ring color at the feet, and the word that pops when it is applied.
        private static readonly Dictionary<string, KeyValuePair<Color, string>> StatusLooks = new Dictionary<string, KeyValuePair<Color, string>>
        {
            { StatusIds.Marked, new KeyValuePair<Color, string>(new Color(1f, 0.3f, 0.85f), "MARKED") },
            { StatusIds.Hidden, new KeyValuePair<Color, string>(new Color(0.6f, 0.65f, 0.75f), "HIDDEN") },
            { StatusIds.Rooted, new KeyValuePair<Color, string>(new Color(0.65f, 0.45f, 0.25f), "ROOTED") },
            { StatusIds.TakeAim, new KeyValuePair<Color, string>(new Color(1f, 0.3f, 0.25f), "TAKING AIM") },
            { StatusIds.Retaliate, new KeyValuePair<Color, string>(new Color(1f, 0.6f, 0.2f), "RETALIATE") },
            { StatusIds.Trance, new KeyValuePair<Color, string>(new Color(0.75f, 0.35f, 1f), "TRANCE") },
            { StatusIds.Evasive, new KeyValuePair<Color, string>(new Color(0.4f, 1f, 0.95f), "EVASIVE") },
            { StatusIds.Braced, new KeyValuePair<Color, string>(new Color(0.4f, 0.65f, 1f), "BRACED") },
            { StatusIds.Blocking, new KeyValuePair<Color, string>(new Color(0.55f, 0.85f, 1f), "BLOCK") },
        };

        private static readonly Color LightColor = new Color(0.55f, 0.9f, 1f);
        private static readonly Color HeavyColor = new Color(1f, 0.55f, 0.15f);

        private class Windup { public ActionDefinition Def; public string ActorId, TargetId; public Vector3 TargetPos; public float Start, Duration; }

        /// <summary>A windup in progress, as the battle timeline shows it.</summary>
        public struct WindupView { public string ActorId, TargetId; public ActionDefinition Def; public float Start, ResolveAt; }

        public IEnumerable<WindupView> Windups
        {
            get { foreach (var w in windups) yield return new WindupView { ActorId = w.ActorId, TargetId = w.TargetId, Def = w.Def, Start = w.Start, ResolveAt = w.Start + w.Duration }; }
        }

        /// <summary>Treat this unit as hovered (target line, ring and label) even when the mouse isn't over it: the timeline.</summary>
        [System.NonSerialized] public string HoverOverride;
        private class LineFx { public Vector3[] Points; public Color Color; public float Width, EndWidth, Age, Life; public bool Sweep; }
        private class RingFx { public Vector3 Center; public float From, To, Width, Age, Life; public Color Color; public int Segments = 40; }
        private class TextFx { public Vector3 Pos; public string Text; public Color Color; public float Size, Age, Life; }

        private readonly Dictionary<string, ActionDefinition> defs = new Dictionary<string, ActionDefinition>();
        private readonly Dictionary<string, float> heights = new Dictionary<string, float>();
        private readonly List<Windup> windups = new List<Windup>();
        private readonly Dictionary<string, KeyValuePair<string, QueuedActionInfo>> orders = new Dictionary<string, KeyValuePair<string, QueuedActionInfo>>();   // unit -> (target, order)
        private string hoverText;
        private Color hoverColor;
        private readonly List<LineFx> lines = new List<LineFx>();
        private readonly List<RingFx> rings = new List<RingFx>();
        private readonly List<TextFx> texts = new List<TextFx>();
        private readonly Dictionary<string, HashSet<string>> statuses = new Dictionary<string, HashSet<string>>();
        private readonly Dictionary<string, float> stunnedUntil = new Dictionary<string, float>();
        private readonly Dictionary<UnitView, float> flashes = new Dictionary<UnitView, float>();
        private readonly Dictionary<UnitView, Renderer[]> renderers = new Dictionary<UnitView, Renderer[]>();
        private readonly HashSet<UnitView> tinted = new HashSet<UnitView>();

        private WorldMarkers markers;
        private ParticleSystem sparks, puffs;
        private MaterialPropertyBlock block;
        private GUIStyle textStyle;

        private Camera Cam { get { return Camera != null ? Camera : Camera.main; } }
        private float Clock { get { return Director != null ? Director.PlaybackClock : 0f; } }

        // ------------------------------------------------------------------ setup

        private void Awake()
        {
            markers = new WorldMarkers(transform);
            block = new MaterialPropertyBlock();
            var mat = MakeParticleMaterial();
            sparks = MakeSystem("Sparks", mat, 1.2f, true);
            puffs = MakeSystem("Puffs", mat, -0.15f, false);
        }

        private void OnEnable()
        {
            if (Director == null) return;
            Director.OnPlaybackReset += ResetAll;
            Director.OnEventDispatched += OnEvent;
        }

        private void OnDisable()
        {
            if (Director == null) return;
            Director.OnPlaybackReset -= ResetAll;
            Director.OnEventDispatched -= OnEvent;
        }

        private void ResetAll()
        {
            windups.Clear(); lines.Clear(); rings.Clear(); texts.Clear();
            statuses.Clear(); stunnedUntil.Clear(); flashes.Clear(); orders.Clear();
            sparks.Clear(); puffs.Clear();
            defs.Clear(); heights.Clear();
            if (Director == null || Director.Timeline == null || Director.Timeline.InitialState == null) return;
            foreach (var u in Director.Timeline.InitialState.Units)
            {
                heights[u.Id] = u.BodyHeight;
                foreach (var a in u.Actions) AddDef(a);
            }
        }

        private void AddDef(ActionDefinition a)
        {
            if (a == null || defs.ContainsKey(a.Id)) return;
            defs[a.Id] = a;
            AddDef(a.LinkedAction);
        }

        private static Material MakeParticleMaterial()
        {
            var shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            var mat = new Material(shader);
            // Soft round dot, so particles read as sparks and puffs instead of squares.
            const int size = 32;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(size / 2f, size / 2f)) / (size / 2f);
                    float a = Mathf.Clamp01(1f - d);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
                }
            tex.Apply();
            mat.mainTexture = tex;
            return mat;
        }

        private ParticleSystem MakeSystem(string name, Material mat, float gravity, bool stretch)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 3000;
            main.gravityModifier = gravity;
            main.startSpeed = 0f;
            var emission = ps.emission; emission.enabled = false;
            var shape = ps.shape; shape.enabled = false;
            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                         new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.5f), new GradientAlphaKey(0f, 1f) });
            col.color = fade;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            if (stretch) { r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.05f; r.lengthScale = 1f; }
            ps.Play();
            return ps;
        }

        // ------------------------------------------------------------------ events

        private void OnEvent(TimelineEvent evt)
        {
            var actor = Director.FindView(evt.ActorId);
            var target = Director.FindView(evt.TargetId);

            switch (evt.Type)
            {
                case TacticsGame.Core.EventType.ActionStarted:
                {
                    var info = (ActionStartedInfo)evt.Payload;
                    ActionDefinition def;
                    if (actor == null || info.Windup <= 0f || !defs.TryGetValue(info.ActionId, out def)) break;
                    windups.Add(new Windup { Def = def, ActorId = evt.ActorId, TargetId = evt.TargetId, TargetPos = info.TargetPos, Start = evt.SimTime, Duration = info.Windup });
                    break;
                }
                case TacticsGame.Core.EventType.ActionResolved:
                {
                    var id = ((ActionResolvedInfo)evt.Payload).ActionId;
                    var w = windups.Find(x => x.ActorId == evt.ActorId && x.Def.Id == id);
                    if (w != null) windups.Remove(w);
                    ActionDefinition def;
                    if (actor != null && defs.TryGetValue(id, out def)) PlayResolve(def, actor, target, w != null ? w.TargetPos : actor.transform.position);
                    break;
                }
                case TacticsGame.Core.EventType.ActionInterrupted:
                {
                    windups.RemoveAll(x => x.ActorId == evt.ActorId);
                    if (actor == null) break;
                    var reason = ((InterruptInfo)evt.Payload).Reason ?? "";
                    if (reason == "dodge") break;   // the windup resumes after the dash; DODGE already says enough
                    Burst(sparks, Chest(actor), 14, new Color(0.75f, 0.75f, 0.8f), 3f, 0.12f, 0.4f);
                    string word = reason == "condition lost" ? "CANCELLED" : reason.StartsWith("resume failed") ? "CAN'T FOLLOW UP" : reason == "never came in range" ? "OUT OF REACH" : reason == "target lost" ? "TARGET LOST" : "INTERRUPTED";
                    AddText(Head(actor), word, new Color(0.8f, 0.8f, 0.85f), 15f);
                    break;
                }
                case TacticsGame.Core.EventType.Damage:
                {
                    if (target == null) break;
                    var d = (DamageInfo)evt.Payload;
                    bool heavy = d.Impact == ImpactType.Heavy;
                    Vector3 away = actor != null ? Flat(target.transform.position - actor.transform.position).normalized : Vector3.zero;
                    Color c = Styles.ContainsKey(d.ActionId ?? "") ? Color.Lerp(Styles[d.ActionId].Color, Color.white, 0.4f) : (heavy ? HeavyColor : LightColor);
                    Burst(sparks, Chest(target), heavy ? 28 : 12, c, heavy ? 7f : 4.5f, heavy ? 0.16f : 0.1f, 0.35f, away * 0.8f);
                    if (heavy) rings.Add(new RingFx { Center = target.transform.position, From = 0.3f, To = 1.8f, Width = 0.1f, Life = 0.3f, Color = HeavyColor });
                    flashes[target] = 0.12f;
                    AddText(Head(target), d.Amount.ToString("0"), heavy ? HeavyColor : Color.white, heavy ? 26f : 19f);
                    HashSet<string> guard;
                    if (statuses.TryGetValue(evt.TargetId, out guard) && guard.Contains(StatusIds.Blocking))
                    {
                        // Hit while guarding: the blocker gets to act again.
                        rings.Add(new RingFx { Center = target.transform.position, From = 1.2f, To = 0.4f, Width = 0.12f, Life = 0.3f, Color = new Color(0.55f, 0.85f, 1f), Segments = 6 });
                        AddText(Head(target) + Vector3.up * 0.7f, "GUARDED!", new Color(0.55f, 0.85f, 1f), 18f);
                    }
                    if (d.BlockedAmount > 0.5f)
                    {
                        Burst(sparks, Chest(target) - away * 0.3f, 8, new Color(0.5f, 0.7f, 1f), 3f, 0.08f, 0.25f, -away);
                        AddText(Head(target) + Vector3.up * 0.35f, "BLOCK " + d.BlockedAmount.ToString("0"), new Color(0.55f, 0.75f, 1f), 14f);
                    }
                    break;
                }
                case TacticsGame.Core.EventType.Miss:
                {
                    if (target == null) break;
                    var reason = ((MissInfo)evt.Payload).Reason;
                    string word = reason == "cover" ? "COVER" : reason == "out_of_range" ? "OUT OF RANGE" : reason == "hidden" ? "HIDDEN" : "MISS";
                    Burst(puffs, target.transform.position + Vector3.up * 0.2f + Random.insideUnitSphere * 0.5f, 6, new Color(0.7f, 0.65f, 0.55f, 0.7f), 0.8f, 0.35f, 0.6f);
                    AddText(Head(target), word, new Color(0.75f, 0.75f, 0.75f), 17f);
                    break;
                }
                case TacticsGame.Core.EventType.Dodge:
                    if (target != null) AddText(Head(target), "DODGE", new Color(0.4f, 1f, 0.95f), 19f);
                    break;
                case TacticsGame.Core.EventType.Dash:
                {
                    if (actor == null) break;
                    var dash = (DashInfo)evt.Payload;
                    if (dash.Settle) break;   // a small shuffle out of someone's body: no effect
                    if (dash.Forced)
                    {
                        AddLine(new[] { dash.From + Vector3.up * 0.8f, dash.To + Vector3.up * 0.8f }, new Color(1f, 0.6f, 0.3f), 0.18f, 0.4f);
                        Burst(puffs, dash.To + Vector3.up * 0.1f, 10, new Color(0.7f, 0.65f, 0.55f, 0.7f), 1.2f, 0.4f, 0.6f);
                    }
                    else
                    {
                        // Afterimage: a few streaks at body heights plus a ghost ring where the unit stood.
                        foreach (float h in new[] { 0.3f, 1f, 1.6f })
                            AddLine(new[] { dash.From + Vector3.up * h, dash.To + Vector3.up * h }, new Color(0.4f, 1f, 0.95f, 0.7f), 0.12f, 0.35f, 0.02f);
                        rings.Add(new RingFx { Center = dash.From, From = 0.5f, To = 0.2f, Width = 0.06f, Life = 0.4f, Color = new Color(0.4f, 1f, 0.95f) });
                    }
                    break;
                }
                case TacticsGame.Core.EventType.Stun:
                {
                    if (target == null) break;
                    stunnedUntil[evt.TargetId] = evt.SimTime + ((StunInfo)evt.Payload).Duration;
                    AddText(Head(target) + Vector3.up * 0.3f, "STUNNED", new Color(1f, 0.9f, 0.3f), 15f);
                    break;
                }
                case TacticsGame.Core.EventType.Unravel:
                    if (target == null) break;
                    Burst(sparks, Chest(target), 40, new Color(0.8f, 0.4f, 1f), 8f, 0.18f, 0.6f);
                    Burst(puffs, Chest(target), 16, new Color(0.6f, 0.3f, 0.9f, 0.6f), 1.5f, 0.7f, 0.9f);
                    rings.Add(new RingFx { Center = target.transform.position, From = 0.3f, To = 3f, Width = 0.18f, Life = 0.5f, Color = new Color(0.8f, 0.4f, 1f) });
                    AddText(Head(target) + Vector3.up * 0.4f, "UNRAVELED!", new Color(0.85f, 0.5f, 1f), 26f);
                    break;
                case TacticsGame.Core.EventType.Heal:
                    if (target == null) break;
                    Burst(puffs, target.transform.position + Vector3.up * 0.4f, 16, new Color(0.4f, 1f, 0.5f, 0.8f), 1f, 0.25f, 0.9f, Vector3.up * 1.5f);
                    AddText(Head(target), "+" + ((float)evt.Payload).ToString("0"), new Color(0.4f, 1f, 0.5f), 20f);
                    break;
                case TacticsGame.Core.EventType.EnergyChanged:
                {
                    var e = (EnergyInfo)evt.Payload;
                    if (actor != null && e.Amount > 0.5f) AddText(Head(actor) + Vector3.right * 0.4f, "+" + e.Amount.ToString("0") + " EN", new Color(0.45f, 0.75f, 1f), 13f);
                    break;
                }
                case TacticsGame.Core.EventType.StatusApplied:
                {
                    var id = (string)evt.Payload;
                    if (target == null || id == null) break;
                    HashSet<string> set;
                    if (!statuses.TryGetValue(evt.TargetId, out set)) statuses[evt.TargetId] = set = new HashSet<string>();
                    set.Add(id);
                    KeyValuePair<Color, string> look;
                    if (StatusLooks.TryGetValue(id, out look))
                    {
                        AddText(Head(target) + Vector3.up * 0.6f, look.Value, look.Key, 14f);
                        Burst(puffs, target.transform.position + Vector3.up * 0.3f, 10, WithAlpha(look.Key, 0.7f), 0.6f, 0.25f, 0.7f, Vector3.up * 1.2f);
                    }
                    break;
                }
                case TacticsGame.Core.EventType.StatusExpired:
                {
                    HashSet<string> set;
                    if (evt.ActorId != null && statuses.TryGetValue(evt.ActorId, out set)) set.Remove((string)evt.Payload);
                    break;
                }
                case TacticsGame.Core.EventType.QueuedActionChanged:
                    if (evt.TargetId == null) orders.Remove(evt.ActorId);
                    else orders[evt.ActorId] = new KeyValuePair<string, QueuedActionInfo>(evt.TargetId, (QueuedActionInfo)evt.Payload);
                    break;
                case TacticsGame.Core.EventType.TrapPlaced:
                {
                    var t = (TrapInfo)evt.Payload;
                    Burst(puffs, t.Position + Vector3.up * 0.1f, 8, new Color(0.7f, 0.7f, 0.7f, 0.6f), 0.8f, 0.3f, 0.5f);
                    break;
                }
                case TacticsGame.Core.EventType.TrapTriggered:
                    if (target == null) break;
                    rings.Add(new RingFx { Center = target.transform.position, From = 1.4f, To = 0.3f, Width = 0.12f, Life = 0.35f, Color = new Color(0.4f, 0.85f, 1f) });
                    Burst(sparks, target.transform.position + Vector3.up * 0.3f, 18, new Color(0.5f, 0.9f, 1f), 4f, 0.1f, 0.35f);
                    AddText(Head(target) + Vector3.up * 0.3f, "SNARED", new Color(0.5f, 0.9f, 1f), 18f);
                    break;
                case TacticsGame.Core.EventType.Death:
                    if (target == null) break;
                    windups.RemoveAll(x => x.ActorId == evt.TargetId);
                    statuses.Remove(evt.TargetId);
                    orders.Remove(evt.TargetId);
                    stunnedUntil.Remove(evt.TargetId);
                    Burst(sparks, Chest(target), 34, new Color(1f, 0.3f, 0.2f), 6f, 0.15f, 0.6f);
                    Burst(puffs, target.transform.position + Vector3.up * 0.5f, 22, new Color(0.25f, 0.22f, 0.22f, 0.8f), 1.6f, 0.8f, 1.1f);
                    AddText(Head(target) + Vector3.up * 0.4f, "DOWN", new Color(1f, 0.35f, 0.3f), 24f);
                    break;
            }
        }

        private void PlayResolve(ActionDefinition def, UnitView actor, UnitView target, Vector3 targetPos)
        {
            var s = StyleFor(def);
            Vector3 origin = Chest(actor);
            Vector3 aimAt = target != null ? Chest(target) : targetPos + Vector3.up * 1f;
            Vector3 dir = Flat(aimAt - origin);
            if (dir.sqrMagnitude < 1e-4f) dir = actor.transform.forward;
            dir.Normalize();

            switch (s.Kind)
            {
                case FxKind.Tracer:
                {
                    Vector3 muzzle = origin + dir * 0.5f + Vector3.up * 0.2f;
                    Vector3 end = s.Pierce ? aimAt + (aimAt - muzzle).normalized * 6f : aimAt;
                    bool big = s.Width >= 0.09f;
                    AddLine(new[] { muzzle, end }, s.Color, s.Width, big ? 0.35f : 0.15f, s.Width * 0.3f);
                    if (big) AddLine(new[] { muzzle, end }, new Color(0.8f, 0.8f, 0.8f, 0.35f), s.Width * 0.6f, 1.2f);   // lingering smoke trail
                    Burst(sparks, muzzle, big ? 14 : 6, Color.Lerp(s.Color, Color.white, 0.5f), 3f, 0.08f, 0.15f, dir * 1.5f);
                    break;
                }
                case FxKind.Bolt:
                {
                    for (int k = 0; k < 2; k++) AddLine(Zigzag(origin + dir * 0.4f, aimAt, 8, 0.3f), s.Color, s.Width, 0.2f, s.Width * 0.5f);
                    Burst(sparks, aimAt, 10, s.Color, 4f, 0.08f, 0.25f);
                    break;
                }
                case FxKind.Slash:
                {
                    float dist = target != null ? Vector3.Distance(Flat(actor.transform.position), Flat(target.transform.position)) : 1.5f;
                    float radius = Mathf.Clamp(dist * 0.9f, 1.1f, 2.4f);
                    var pts = new Vector3[14];
                    for (int i = 0; i < pts.Length; i++)
                    {
                        float t = i / (pts.Length - 1f);
                        Vector3 d = Quaternion.Euler(0f, Mathf.Lerp(-70f, 70f, t), 0f) * dir;
                        pts[i] = actor.transform.position + d * radius + Vector3.up * Mathf.Lerp(1.6f, 0.7f, t);
                    }
                    AddLine(pts, s.Color, s.Width, 0.28f, 0.01f, true);
                    break;
                }
                case FxKind.Beam:
                {
                    Vector3 end = origin + dir * def.Range;
                    float w = Mathf.Max(0.2f, def.LineWidth * 0.6f);
                    AddLine(new[] { origin, end }, WithAlpha(s.Color, 0.7f), w, 0.5f, w * 0.6f);
                    AddLine(new[] { origin, end }, Color.white, w * 0.25f, 0.3f, w * 0.15f);
                    for (float m = 1f; m < def.Range; m += 1.2f)
                        Burst(puffs, origin + dir * m, 2, WithAlpha(s.Color, 0.6f), 0.6f, 0.4f, 0.6f);
                    break;
                }
                case FxKind.Shockwave:
                {
                    Vector3 center = def.Shape == AreaShape.Circle && def.Targeting != TargetType.Self ? targetPos : (target != null ? target.transform.position : actor.transform.position);
                    float r = def.AreaRadius > 0f ? def.AreaRadius : 2f;
                    rings.Add(new RingFx { Center = center, From = 0.2f, To = r, Width = s.Width, Life = 0.45f, Color = s.Color, Segments = 56 });
                    rings.Add(new RingFx { Center = center, From = 0.1f, To = r * 0.6f, Width = s.Width * 0.6f, Life = 0.35f, Color = Color.white, Segments = 40 });
                    for (int i = 0; i < 20; i++)
                    {
                        float a = i * Mathf.PI * 2f / 20f;
                        Vector3 rim = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                        Burst(puffs, center + rim * r * 0.8f + Vector3.up * 0.2f, 1, new Color(0.7f, 0.65f, 0.55f, 0.6f), 1f, 0.5f, 0.6f, rim);
                    }
                    break;
                }
                case FxKind.Chain:
                {
                    AddLine(Zigzag(origin, aimAt, 16, 0.08f), s.Color, s.Width, 0.5f, s.Width);
                    Burst(sparks, aimAt, 8, Color.white, 3f, 0.08f, 0.2f);
                    break;
                }
                case FxKind.Heal:
                {
                    var on = target != null ? target : actor;
                    rings.Add(new RingFx { Center = on.transform.position, From = 0.2f, To = 1.4f, Width = 0.08f, Life = 0.5f, Color = s.Color });
                    break;
                }
                case FxKind.Buff:
                    rings.Add(new RingFx { Center = actor.transform.position, From = 0.3f, To = 1.4f, Width = 0.1f, Life = 0.45f, Color = s.Color });
                    Burst(puffs, actor.transform.position + Vector3.up * 0.2f, 14, WithAlpha(s.Color, 0.7f), 0.6f, 0.3f, 0.8f, Vector3.up * 2f);
                    break;
                case FxKind.Lob:
                {
                    var pts = new Vector3[16];
                    Vector3 a = origin, b = targetPos + Vector3.up * 0.1f;
                    for (int i = 0; i < pts.Length; i++)
                    {
                        float t = i / (pts.Length - 1f);
                        pts[i] = Vector3.Lerp(a, b, t) + Vector3.up * (4f * t * (1f - t) * 2f);
                    }
                    AddLine(pts, s.Color, 0.05f, 0.45f, 0.05f, true);
                    break;
                }
            }
        }

        private static Style StyleFor(ActionDefinition def)
        {
            Style s;
            if (Styles.TryGetValue(def.Id, out s)) return s;
            Color c = def.Impact == ImpactType.Heavy ? HeavyColor : LightColor;
            float w = def.Impact == ImpactType.Heavy ? 0.14f : 0.07f;
            if (def.BehaviorId == "trap") return new Style(FxKind.Lob, c);
            if (def.Shape == AreaShape.Line) return new Style(FxKind.Beam, c, w);
            if (def.Shape == AreaShape.Circle) return new Style(FxKind.Shockwave, c, w);
            if (def.Heal > 0f) return new Style(FxKind.Heal, new Color(0.35f, 1f, 0.5f));
            if (def.Targeting == TargetType.Self) return new Style(FxKind.Buff, c);
            if (def.Displace == DisplaceMode.TowardSource) return new Style(FxKind.Chain, c);
            return new Style(def.IsRanged ? FxKind.Tracer : FxKind.Slash, c, w);
        }

        // ------------------------------------------------------------------ per frame

        private void Update()
        {
            float dt = Time.deltaTime;   // scaled: effects freeze with the world while orders are given
            markers.Begin();
            DrawWindups();
            DrawStatuses();
            DrawHover();

            for (int i = lines.Count - 1; i >= 0; i--)
            {
                var l = lines[i];
                l.Age += dt;
                if (l.Age >= l.Life) { lines.RemoveAt(i); continue; }
                float t = l.Age / l.Life;
                Vector3[] pts = l.Points;
                if (l.Sweep)
                {
                    // Reveal along the path over the first 40% of the life, like a blade arc.
                    int n = Mathf.Clamp(Mathf.CeilToInt(Mathf.Clamp01(t / 0.4f) * pts.Length), 2, pts.Length);
                    if (n < pts.Length) { pts = new Vector3[n]; System.Array.Copy(l.Points, pts, n); }
                }
                Color c = WithAlpha(l.Color, l.Color.a * (1f - t * t));
                markers.Line(pts, c, WithAlpha(c, c.a * 0.6f), l.Width * (1f - t * 0.5f), l.EndWidth);
            }

            for (int i = rings.Count - 1; i >= 0; i--)
            {
                var r = rings[i];
                r.Age += dt;
                if (r.Age >= r.Life) { rings.RemoveAt(i); continue; }
                float t = r.Age / r.Life;
                float ease = 1f - (1f - t) * (1f - t);
                markers.Circle(r.Center, Mathf.Lerp(r.From, r.To, ease), WithAlpha(r.Color, 1f - t), r.Width * (1f - t * 0.5f), r.Segments);
            }
            markers.End();

            for (int i = texts.Count - 1; i >= 0; i--)
            {
                texts[i].Age += dt;
                if (texts[i].Age >= texts[i].Life) texts.RemoveAt(i);
            }

            UpdateTints(dt);
        }

        private void DrawWindups()
        {
            float now = Clock;
            foreach (var w in windups)
            {
                var actor = Director.FindView(w.ActorId);
                if (actor == null) continue;
                float p = Mathf.Clamp01((now - w.Start) / Mathf.Max(0.01f, w.Duration));
                var def = w.Def;
                var s = StyleFor(def);
                Color c = def.Impact == ImpactType.Heavy ? HeavyColor : LightColor;

                // Charge ring at the feet fills up to the moment it fires; heavy windups read orange.
                markers.Circle(actor.transform.position, Mathf.Lerp(0.15f, 0.95f, p), WithAlpha(c, 0.35f + 0.6f * p), 0.03f + 0.05f * p, 32);

                var target = Director.FindView(w.TargetId);
                if (target != null && target != actor && def.IsRanged)
                {
                    bool laser = s.Kind == FxKind.Tracer && s.Width >= 0.09f;   // snipe / tracking shot: a laser sight
                    Color aim = laser ? WithAlpha(s.Color, 0.25f + 0.6f * p) : WithAlpha(s.Color, 0.1f + 0.3f * p);
                    markers.Line(new[] { Chest(actor) + Vector3.up * 0.2f, Chest(target) }, aim, aim, laser ? 0.035f : 0.015f, laser ? 0.02f : 0.01f);
                    if (laser) markers.Circle(target.transform.position, Mathf.Lerp(1.2f, 0.5f, p), aim, 0.05f, 24);   // closing reticle
                }

                // Ground telegraphs for area attacks: the outline, and a fill that grows to the edge as it charges.
                if (def.Shape == AreaShape.Circle && def.AreaRadius > 0f)
                {
                    Vector3 center = def.Targeting == TargetType.Self ? actor.transform.position : w.TargetPos;
                    markers.Circle(center, def.AreaRadius, WithAlpha(s.Color, 0.7f), 0.06f, 48);
                    markers.Circle(center, def.AreaRadius * p, WithAlpha(s.Color, 0.4f), 0.04f, 48);
                }
                else if (def.Shape == AreaShape.Line)
                {
                    Vector3 from = actor.transform.position;
                    Vector3 dir = Flat(w.TargetPos - from);
                    if (dir.sqrMagnitude < 1e-4f) dir = actor.transform.forward;
                    dir.Normalize();
                    Vector3 side = Vector3.Cross(Vector3.up, dir) * (def.LineWidth / 2f);
                    Vector3 end = from + dir * def.Range;
                    markers.Polyline(new[] { from - side, end - side, end + side, from + side, from - side }, WithAlpha(s.Color, 0.7f), 0.05f);
                    markers.Segment(from, from + dir * def.Range * p, WithAlpha(s.Color, 0.45f), def.LineWidth * 0.8f);
                }
                else if (def.BehaviorId == "trap" && def.AreaRadius > 0f)
                {
                    markers.Circle(w.TargetPos, def.AreaRadius, WithAlpha(s.Color, 0.25f + 0.4f * p), 0.04f, 32);
                }
            }
        }

        /// <summary>
        /// Hovering a unit that is winding up shows what it is aiming at: a line and ring to its target (or the area it will
        /// hit), plus a label by the cursor. A unit chasing or holding a queued ranged order shows that target too.
        /// </summary>
        private void DrawHover()
        {
            hoverText = null;
            var view = HoverOverride != null ? Director.FindView(HoverOverride) : UnitUnderMouse();
            if (view == null) return;
            var w = windups.Find(x => x.ActorId == view.UnitId);
            KeyValuePair<string, QueuedActionInfo> order;
            bool hasOrder = orders.TryGetValue(view.UnitId, out order);
            if (w == null && !hasOrder) return;

            Vector3 from = Chest(view);
            if (w != null)
            {
                Color c = w.Def.Impact == ImpactType.Heavy ? HeavyColor : WithAlpha(StyleFor(w.Def).Color, 1f);
                var target = Director.FindView(w.TargetId);
                string aimed;
                if (target != null && target != view)
                {
                    markers.Line(new[] { from, Chest(target) }, c, c, 0.07f, 0.07f);
                    markers.Circle(target.transform.position, 0.95f, c, 0.09f, 40);
                    aimed = NameOf(w.TargetId);
                }
                else if (w.Def.Targeting == TargetType.Position)
                {
                    markers.Line(new[] { from, w.TargetPos + Vector3.up * 0.2f }, c, c, 0.05f, 0.05f);
                    markers.Circle(w.TargetPos, w.Def.AreaRadius > 0f ? w.Def.AreaRadius : 0.6f, c, 0.08f, 48);
                    aimed = "the marked area";
                }
                else aimed = "self";
                float left = Mathf.Max(0f, w.Start + w.Duration - Clock);
                hoverText = string.Format("{0} > {1}   {2:0.0}s", w.Def.DisplayName, aimed, left);
                hoverColor = c;
                return;
            }

            var ordered = Director.FindView(order.Key);
            if (ordered == null) return;
            Color oc = new Color(1f, 0.85f, 0.4f);
            markers.Line(new[] { from, Chest(ordered) }, oc, WithAlpha(oc, 0.4f), 0.05f, 0.03f);
            markers.Circle(ordered.transform.position, 0.95f, oc, 0.06f, 40);
            ActionDefinition od;
            string name = defs.TryGetValue(order.Value.ActionId ?? "", out od) ? od.DisplayName : order.Value.ActionId;
            hoverText = string.Format("{0} > {1}   ({2})", name, NameOf(order.Key), order.Value.Chase ? "chasing" : "queued, fires in range");
            hoverColor = oc;
        }

        private void DrawHoverLabel()
        {
            if (hoverText == null) return;
            var style = new GUIStyle(GUI.skin.box) { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            style.normal.textColor = hoverColor;
            Vector2 m = Event.current.mousePosition;
            var size = style.CalcSize(new GUIContent(hoverText));
            GUI.Box(new Rect(Mathf.Min(m.x + 18f, UiScale.Width - size.x - 6f), m.y - size.y - 6f, size.x + 4f, size.y + 2f), hoverText, style);
        }

        private UnitView UnitUnderMouse()
        {
            var cam = Cam;
            if (cam == null || Director == null) return null;
            Physics.SyncTransforms();   // views move by script; colliders must follow (physics may be frozen)
            var hits = Physics.RaycastAll(cam.ScreenPointToRay(Input.mousePosition), 500f);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var h in hits)
            {
                var v = h.collider.GetComponentInParent<UnitView>();
                if (v != null) return v;
            }
            return null;
        }

        private string NameOf(string unitId)
        {
            if (unitId == null || Director == null || Director.Timeline == null) return "?";
            var u = Director.Timeline.InitialState.Units.Find(x => x.Id == unitId);
            return u != null ? u.DisplayName : "?";
        }

        private void DrawStatuses()
        {
            float now = Clock;
            foreach (var kv in statuses)
            {
                var view = Director.FindView(kv.Key);
                if (view == null) continue;
                Vector3 feet = view.transform.position;
                float r = 0.75f;
                foreach (var id in kv.Value)
                {
                    KeyValuePair<Color, string> look;
                    if (!StatusLooks.TryGetValue(id, out look)) continue;
                    if (id == StatusIds.Marked)
                    {
                        // Spinning diamond over the head.
                        Vector3 top = Head(view) + Vector3.up * 0.45f;
                        float a = now * 3f;
                        var pts = new Vector3[5];
                        for (int i = 0; i < 5; i++)
                        {
                            float ang = a + i * Mathf.PI / 2f;
                            pts[i] = top + new Vector3(Mathf.Cos(ang) * 0.22f, (i % 2 == 0 ? 0.18f : -0.18f), Mathf.Sin(ang) * 0.22f);
                        }
                        markers.Line(pts, look.Key, look.Key, 0.04f, 0.04f);
                        continue;
                    }
                    float pulse = 0.75f + 0.25f * Mathf.Sin(now * 5f + r * 7f);
                    int segments = id == StatusIds.Braced || id == StatusIds.Blocking ? 6 : id == StatusIds.Rooted ? 8 : 36;   // shield hexagon, chain octagon
                    markers.Circle(feet, r, WithAlpha(look.Key, 0.8f * pulse), 0.05f, segments);
                    r += 0.15f;
                }
            }

            // Stun stars orbiting the head.
            foreach (var kv in stunnedUntil)
            {
                if (now >= kv.Value) continue;
                var view = Director.FindView(kv.Key);
                if (view == null) continue;
                Vector3 c = Head(view) + Vector3.up * 0.3f;
                for (int i = 0; i < 3; i++)
                {
                    float a = now * 4f + i * Mathf.PI * 2f / 3f;
                    markers.Circle(c + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.35f, 0.07f, new Color(1f, 0.9f, 0.3f), 0.07f, 6);
                }
            }
        }

        /// <summary>Hit flash (white) and hidden (darkened) as color overrides on the unit's renderers.</summary>
        private void UpdateTints(float dt)
        {
            if (Director == null || Director.Timeline == null) return;
            var keys = new List<UnitView>(flashes.Keys);
            foreach (var v in keys) flashes[v] -= dt;

            foreach (var u in Director.Timeline.InitialState.Units)
            {
                var view = Director.FindView(u.Id);
                if (view == null) continue;
                float flash;
                bool flashing = flashes.TryGetValue(view, out flash) && flash > 0f;
                HashSet<string> set;
                bool hidden = statuses.TryGetValue(u.Id, out set) && set.Contains(StatusIds.Hidden);
                if (!flashing && !hidden)
                {
                    if (tinted.Remove(view)) foreach (var r in Renderers(view)) if (r != null) r.SetPropertyBlock(null);
                    continue;
                }
                foreach (var r in Renderers(view))
                {
                    if (r == null || r.sharedMaterial == null) continue;
                    var m = r.sharedMaterial;
                    Color baseColor = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : m.HasProperty("_Color") ? m.GetColor("_Color") : Color.white;
                    Color c = hidden ? Color.Lerp(baseColor, new Color(0.15f, 0.18f, 0.25f), 0.65f) : baseColor;
                    if (flashing) c = Color.Lerp(c, Color.white, 0.85f);
                    r.GetPropertyBlock(block);
                    block.SetColor("_BaseColor", c);
                    block.SetColor("_Color", c);
                    r.SetPropertyBlock(block);
                }
                tinted.Add(view);
            }
        }

        private Renderer[] Renderers(UnitView view)
        {
            Renderer[] rs;
            if (!renderers.TryGetValue(view, out rs)) renderers[view] = rs = view.GetComponentsInChildren<Renderer>();
            return rs;
        }

        private void OnGUI()
        {
            var cam = Cam;
            if (cam == null) return;
            UiScale.Begin();
            DrawHoverLabel();
            if (texts.Count == 0) return;
            if (textStyle == null) textStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };

            foreach (var t in texts)
            {
                float k = t.Age / t.Life;
                Vector3 world = t.Pos + Vector3.up * (0.9f * k);
                Vector2 sp;
                if (!UiScale.WorldToGui(cam, world, out sp)) continue;
                float pop = k < 0.12f ? Mathf.Lerp(1.5f, 1f, k / 0.12f) : 1f;
                textStyle.fontSize = Mathf.RoundToInt(t.Size * TextScale * pop);
                float alpha = k < 0.7f ? 1f : 1f - (k - 0.7f) / 0.3f;
                var rect = new Rect(sp.x - 100f, sp.y - 20f, 200f, 40f);
                textStyle.normal.textColor = new Color(0f, 0f, 0f, 0.8f * alpha);
                GUI.Label(new Rect(rect.x + 2f, rect.y + 2f, rect.width, rect.height), t.Text, textStyle);
                textStyle.normal.textColor = WithAlpha(t.Color, alpha);
                GUI.Label(rect, t.Text, textStyle);
            }
        }

        // ------------------------------------------------------------------ helpers

        private float HeightOf(UnitView v)
        {
            float h;
            return heights.TryGetValue(v.UnitId ?? "", out h) ? h : 1.8f;
        }

        private Vector3 Chest(UnitView v) { return v.transform.position + Vector3.up * (HeightOf(v) * 0.6f); }
        private Vector3 Head(UnitView v) { return v.transform.position + Vector3.up * (HeightOf(v) + 0.2f); }
        private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
        private static Color WithAlpha(Color c, float a) { c.a = a; return c; }

        private void AddLine(Vector3[] points, Color color, float width, float life, float endWidth = -1f, bool sweep = false)
        {
            lines.Add(new LineFx { Points = points, Color = color, Width = width, EndWidth = endWidth < 0f ? width : endWidth, Life = life, Sweep = sweep });
        }

        private void AddText(Vector3 pos, string text, Color color, float size)
        {
            // Small jitter so simultaneous numbers on one unit don't stack exactly.
            pos += new Vector3(Random.Range(-0.25f, 0.25f), Random.Range(0f, 0.2f), Random.Range(-0.25f, 0.25f));
            texts.Add(new TextFx { Pos = pos, Text = text, Color = color, Size = size, Life = 1.1f });
        }

        private static Vector3[] Zigzag(Vector3 a, Vector3 b, int segments, float amplitude)
        {
            var pts = new Vector3[segments + 1];
            Vector3 side = Vector3.Cross(Vector3.up, (b - a).normalized);
            if (side.sqrMagnitude < 1e-4f) side = Vector3.right;
            for (int i = 0; i <= segments; i++)
            {
                float t = i / (float)segments;
                float off = (i == 0 || i == segments) ? 0f : Random.Range(-amplitude, amplitude);
                pts[i] = Vector3.Lerp(a, b, t) + side * off + Vector3.up * (off * 0.5f);
            }
            return pts;
        }

        private static void Burst(ParticleSystem ps, Vector3 pos, int count, Color color, float speed, float size, float life, Vector3 bias = default(Vector3))
        {
            var p = new ParticleSystem.EmitParams();
            for (int i = 0; i < count; i++)
            {
                p.position = pos;
                p.velocity = (Random.onUnitSphere + bias) * speed * Random.Range(0.4f, 1f);
                p.startColor = color;
                p.startSize = size * Random.Range(0.6f, 1.3f);
                p.startLifetime = life * Random.Range(0.6f, 1.3f);
                ps.Emit(p, 1);
            }
        }
    }
}
