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

        public struct Style
        {
            public VfxKind Kind; public Color Color; public float Width; public bool Pierce;
            public Style(VfxKind kind, Color color, float width = 0.06f, bool pierce = false) { Kind = kind; Color = color; Width = width; Pierce = pierce; }
        }

        // Each ability's look is its ActionDefinition.Visuals; one left on Auto gets a style worked out from the ability (StyleFor).

        // Lasting statuses: ring colour at the feet and the word that pops when applied come from StatusDisplay.

        private static readonly Color TranscendColor = new Color(0.85f, 0.6f, 1f);

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
        private readonly Dictionary<string, Vector3> lastAim = new Dictionary<string, Vector3>();   // per actor: where its latest action was aimed
        private readonly Dictionary<string, Windup> charges = new Dictionary<string, Windup>();   // charging units (Duration = time to full)
        private class DomeFx { public string ActorId, ActionId; public float Radius, Height, Start, EndsAt; public bool SlowsTime; }   // SlowsTime: its statuses slow actions (a clock hand sweeps it)
        private readonly Dictionary<string, DomeFx> domes = new Dictionary<string, DomeFx>();   // aura id -> its dome (Sanctity, Chronosphere)
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
            windups.Clear(); lines.Clear(); rings.Clear(); texts.Clear(); lastAim.Clear(); slashRun.Clear(); slashAt.Clear();
            statuses.Clear(); stunnedUntil.Clear(); flashes.Clear(); orders.Clear(); charges.Clear(); domes.Clear();
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
                    if (!info.Free) charges.Remove(evt.ActorId);   // a released charge becomes an ordinary action
                    if (info.Transcended && actor != null)
                    {
                        // Transcend: a violet flare, a closing ring and the word over the unit.
                        Burst(sparks, Chest(actor), 22, TranscendColor, 3.5f, 0.12f, 0.45f, Vector3.up * 1.5f);
                        rings.Add(new RingFx { Center = actor.transform.position, From = 1.8f, To = 0.3f, Width = 0.1f, Life = 0.35f, Color = TranscendColor, Segments = 8 });
                        AddText(Head(actor) + Vector3.up * 0.9f, "TRANSCEND", TranscendColor, 20f);
                    }
                    lastAim[evt.ActorId] = info.TargetPos;   // an instant one (Lance of Destiny, released) has no windup entry to look at
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
                    Vector3 aimed;
                    if (actor != null && defs.TryGetValue(id, out def)) PlayResolve(def, actor, target, w != null ? w.TargetPos : def.Targeting == TargetType.Position && lastAim.TryGetValue(evt.ActorId, out aimed) ? aimed : actor.transform.position, CoverHit(evt));
                    break;
                }
                case TacticsGame.Core.EventType.ActionInterrupted:
                {
                    windups.RemoveAll(x => x.ActorId == evt.ActorId);
                    charges.Remove(evt.ActorId);
                    if (actor == null) break;
                    var reason = ((InterruptInfo)evt.Payload).Reason ?? "";
                    if (reason == "dodge") break;   // the windup resumes after the dash; DODGE already says enough
                    Burst(sparks, Chest(actor), 14, new Color(0.75f, 0.75f, 0.8f), 3f, 0.12f, 0.4f);
                    string word = reason == "charge broken" ? "CHARGE BROKEN" : reason == "stutter" ? "STUTTERED" : reason == "mesmerize" ? "MESMERIZE!" : reason == "condition lost" ? "CANCELLED" : reason.StartsWith("resume failed") ? "CAN'T FOLLOW UP" : reason == "never came in range" ? "OUT OF REACH" : reason == "target lost" ? "TARGET LOST" : "INTERRUPTED";
                    AddText(Head(actor), word, new Color(0.8f, 0.8f, 0.85f), 15f);
                    break;
                }
                case TacticsGame.Core.EventType.Damage:
                {
                    if (target == null) break;
                    var d = (DamageInfo)evt.Payload;
                    bool heavy = d.Impact == ImpactType.Heavy;
                    Vector3 away = actor != null ? Flat(target.transform.position - actor.transform.position).normalized : Vector3.zero;
                    ActionDefinition hitBy;
                    Color c = d.ActionId != null && defs.TryGetValue(d.ActionId, out hitBy) && hitBy.Visuals != null && hitBy.Visuals.IsSet ? Color.Lerp(hitBy.Visuals.Color, Color.white, 0.4f) : (heavy ? HeavyColor : LightColor);
                    Burst(sparks, Chest(target), heavy ? 28 : 12, c, heavy ? 7f : 4.5f, heavy ? 0.16f : 0.1f, 0.35f, away * 0.8f);
                    if (heavy) rings.Add(new RingFx { Center = target.transform.position, From = 0.3f, To = 1.8f, Width = 0.1f, Life = 0.3f, Color = HeavyColor });
                    flashes[target] = 0.12f;
                    ActionDefinition hitDef;
                    if (d.Amount >= 0.5f || d.BlockedAmount >= 0.5f) AddText(Head(target), d.Amount.ToString("0"), heavy ? HeavyColor : Color.white, heavy ? 26f : 19f);
                    else if (defs.TryGetValue(d.ActionId ?? "", out hitDef) && hitDef.StrainOnTarget > 0.5f)
                        AddText(Head(target), "+" + hitDef.StrainOnTarget.ToString("0") + " STRAIN", new Color(0.95f, 0.45f, 0.75f), 17f);   // a hit that only strains (Torrent of Strife)
                    if (d.ActionId == "fall")
                    {
                        // Hitting the ground: a dust ring where it landed.
                        Burst(puffs, target.transform.position + Vector3.up * 0.1f, 14, new Color(0.7f, 0.65f, 0.55f, 0.7f), 1.5f, 0.4f, 0.6f);
                        rings.Add(new RingFx { Center = target.transform.position, From = 0.3f, To = 1.4f, Width = 0.08f, Life = 0.35f, Color = new Color(1f, 0.6f, 0.3f) });
                    }
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
                    var miss = (MissInfo)evt.Payload;
                    var reason = miss.Reason;
                    if (miss.HitCover)
                    {
                        // The shot slammed into the cover that saved the target: chips, sparks and dust off the cover.
                        Vector3 back = actor != null ? (Chest(actor) - miss.CoverPoint).normalized : Vector3.up;
                        Burst(sparks, miss.CoverPoint, 14, new Color(1f, 0.85f, 0.55f), 4f, 0.07f, 0.25f, back * 2f);
                        Burst(puffs, miss.CoverPoint, 12, new Color(0.62f, 0.57f, 0.5f, 0.8f), 1.1f, 0.3f, 0.8f, back * 0.8f + Vector3.up * 0.4f);
                        Burst(puffs, miss.CoverPoint, 6, new Color(0.35f, 0.32f, 0.28f, 0.9f), 2.5f, 0.09f, 0.5f, back * 1.5f);   // debris
                        rings.Add(new RingFx { Center = miss.CoverPoint, From = 0.05f, To = 0.5f, Width = 0.05f, Life = 0.25f, Color = new Color(1f, 0.85f, 0.55f), Segments = 16 });
                        AddText(miss.CoverPoint + Vector3.up * 0.6f, "COVER", new Color(0.85f, 0.8f, 0.7f), 17f);
                        break;
                    }
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
                    if (dash.Teleport)
                    {
                        // Folded out of one place and into another: an imploding ring where it was, a bursting one where it is.
                        Color warp = new Color(0.75f, 0.45f, 1f);
                        rings.Add(new RingFx { Center = dash.From, From = 1.2f, To = 0.1f, Width = 0.08f, Life = 0.3f, Color = warp, Segments = 24 });
                        Burst(sparks, dash.From + Vector3.up * 1f, 16, warp, 2.5f, 0.08f, 0.3f);
                        rings.Add(new RingFx { Center = dash.To, From = 0.1f, To = 1.4f, Width = 0.1f, Life = 0.4f, Color = warp, Segments = 24 });
                        Burst(puffs, dash.To + Vector3.up * 0.8f, 14, WithAlpha(warp, 0.6f), 1.2f, 0.3f, 0.6f, Vector3.up);
                        if (!dash.Blink) AddText(dash.To + Vector3.up * (HeightOf(actor) + 0.6f), "WARPED", warp, 16f);   // sent, not its own blink
                        break;
                    }
                    if (dash.Fall) break;   // the Fall event and the landing's Damage show it
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
                case TacticsGame.Core.EventType.Jump:
                    if (actor != null) Burst(puffs, ((JumpInfo)evt.Payload).From + Vector3.up * 0.1f, 6, new Color(0.7f, 0.65f, 0.55f, 0.6f), 0.8f, 0.3f, 0.45f);
                    break;
                case TacticsGame.Core.EventType.Lift:
                {
                    if (target == null) break;
                    var l = (LiftInfo)evt.Payload;
                    Color pale = new Color(0.95f, 0.9f, 1f);
                    rings.Add(new RingFx { Center = l.From, From = 0.2f, To = 1.6f, Width = 0.1f, Life = 0.5f, Color = pale, Segments = 32 });
                    for (float h = 0.5f; h < l.To.y - l.From.y; h += 1f) Burst(puffs, l.From + Vector3.up * h, 2, WithAlpha(pale, 0.5f), 0.4f, 0.3f, 0.8f, Vector3.up * 2f);
                    AddText(l.To + Vector3.up * (HeightOf(target) + 0.5f), l.Hover ? "AIRBORNE" : "SUSPENDED", pale, 17f);   // its own leap, or lifted
                    break;
                }
                case TacticsGame.Core.EventType.Carry:
                {
                    if (target == null || !((CarryInfo)evt.Payload).Carrying) break;
                    Color grip = new Color(0.85f, 0.6f, 0.35f);
                    rings.Add(new RingFx { Center = target.transform.position, From = 1.2f, To = 0.3f, Width = 0.1f, Life = 0.3f, Color = grip, Segments = 8 });
                    AddText(Head(target) + Vector3.up * 0.6f, "GRABBED!", grip, 19f);
                    break;
                }
                case TacticsGame.Core.EventType.Fall:
                {
                    if (target == null) break;
                    var fall = (FallInfo)evt.Payload;
                    AddText(Head(target) + Vector3.up * 0.6f, fall.FromJump ? "KNOCKED OUT OF THE AIR!" : "KNOCKED DOWN", new Color(1f, 0.6f, 0.3f), 18f);
                    break;
                }
                case TacticsGame.Core.EventType.Stun:
                {
                    if (target == null) break;
                    stunnedUntil[evt.TargetId] = evt.SimTime + ((StunInfo)evt.Payload).Duration;
                    AddText(Head(target) + Vector3.up * 0.3f, "STUNNED", new Color(1f, 0.9f, 0.3f), 15f);
                    break;
                }
                case TacticsGame.Core.EventType.AuraStarted:
                {
                    var a = (AuraInfo)evt.Payload;
                    ActionDefinition auraDef;
                    bool slows = defs.TryGetValue(a.ActionId ?? "", out auraDef) && auraDef.AuraStatuses != null && auraDef.AuraStatuses.Exists(s => s != null && s.Total(StatId.ActionSpeed, true) < 1f);
                    domes[a.Id] = new DomeFx { ActorId = evt.ActorId, ActionId = a.ActionId, Radius = a.Radius, Height = a.Height, Start = evt.SimTime, EndsAt = a.EndsAt, SlowsTime = slows };
                    if (actor != null) rings.Add(new RingFx { Center = actor.transform.position, From = 0.3f, To = a.Radius, Width = 0.12f, Life = 0.3f, Color = DomeColor(a.ActionId), Segments = 48 });
                    break;
                }
                case TacticsGame.Core.EventType.AuraEnded:
                {
                    DomeFx d;
                    string id = (string)evt.Payload;
                    if (actor != null && domes.TryGetValue(id, out d))
                        rings.Add(new RingFx { Center = actor.transform.position, From = d.Radius, To = d.Radius * 1.2f, Width = 0.08f, Life = 0.35f, Color = DomeColor(d.ActionId), Segments = 48 });
                    domes.Remove(id);
                    break;
                }
                case TacticsGame.Core.EventType.HitRedirected:
                {
                    // The dome's owner pulls the hit off the one sheltering: a bright bent line from them to it, and a flare.
                    if (actor == null || target == null) break;
                    var c = StatusDisplay.Get(StatusIds.Sheltered).Color;   // the look of being sheltered by it
                    Vector3 from = Chest(target), to = Chest(actor), mid = (from + to) * 0.5f + Vector3.up * 0.8f;
                    AddLine(new[] { from, mid, to }, c, 0.09f, 0.35f, 0.04f);
                    Burst(sparks, from, 14, c, 3f, 0.1f, 0.35f);
                    rings.Add(new RingFx { Center = actor.transform.position, From = 1.4f, To = 0.5f, Width = 0.1f, Life = 0.3f, Color = c, Segments = 6 });
                    AddText(Head(actor) + Vector3.up * 0.5f, "INTERCEPT", c, 16f);
                    break;
                }
                case TacticsGame.Core.EventType.AerialSlamEnded:
                {
                    if ((bool)evt.Payload || target == null) break;   // knocked out of the air: the falls play it
                    var slam = new Color(1f, 0.55f, 0.2f);
                    Burst(puffs, target.transform.position + Vector3.up * 0.2f, 24, new Color(0.75f, 0.65f, 0.5f, 0.7f), 3.5f, 0.6f, 0.8f);
                    Burst(sparks, target.transform.position + Vector3.up * 0.3f, 26, slam, 6f, 0.14f, 0.45f);
                    rings.Add(new RingFx { Center = target.transform.position, From = 0.3f, To = 3.2f, Width = 0.2f, Life = 0.45f, Color = slam });
                    AddText(Head(target) + Vector3.up * 0.4f, "SLAM!", slam, 24f);
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
                    StatusDisplay.Look look;
                    if (StatusDisplay.TryGet(id, out look) && look.Word != null)
                    {
                        AddText(Head(target) + Vector3.up * 0.6f, look.Word, look.Color, 14f);
                        Burst(puffs, target.transform.position + Vector3.up * 0.3f, 10, WithAlpha(look.Color, 0.7f), 0.6f, 0.25f, 0.7f, Vector3.up * 1.2f);
                    }
                    break;
                }
                case TacticsGame.Core.EventType.StatusStacks:
                {
                    // A chain grew (Flowing Death): its count pops over the unit.
                    var st = (StatusStacksInfo)evt.Payload;
                    if (actor == null || st.Stacks < 2) break;
                    var look = StatusDisplay.Get(st.Id);
                    AddText(Head(actor) + Vector3.up * 0.9f, StatusDisplay.NameOf(st.Id).ToUpperInvariant() + " x" + st.Stacks, look.Color, 15f + Mathf.Min(8f, st.Stacks));
                    break;
                }
                case TacticsGame.Core.EventType.StatusExpired:
                {
                    HashSet<string> set;
                    if (evt.ActorId != null && statuses.TryGetValue(evt.ActorId, out set)) set.Remove((string)evt.Payload);
                    break;
                }
                case TacticsGame.Core.EventType.ChargeStarted:
                {
                    var c = (ChargeInfo)evt.Payload;
                    ActionDefinition cd;
                    if (actor != null && defs.TryGetValue(c.ActionId, out cd)) charges[evt.ActorId] = new Windup { Def = cd, ActorId = evt.ActorId, Start = evt.SimTime, Duration = Mathf.Max(0.01f, c.MaxSeconds) };
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
                    charges.Remove(evt.TargetId);
                    statuses.Remove(evt.TargetId);
                    orders.Remove(evt.TargetId);
                    stunnedUntil.Remove(evt.TargetId);
                    Burst(sparks, Chest(target), 34, new Color(1f, 0.3f, 0.2f), 6f, 0.15f, 0.6f);
                    Burst(puffs, target.transform.position + Vector3.up * 0.5f, 22, new Color(0.25f, 0.22f, 0.22f, 0.8f), 1.6f, 0.8f, 1.1f);
                    AddText(Head(target) + Vector3.up * 0.4f, "DOWN", new Color(1f, 0.35f, 0.3f), 24f);
                    break;
            }
        }

        /// <summary>
        /// The point a cover-stopped shot from this resolve strikes (MissInfo.HitCover), found by looking ahead in the
        /// already-simulated timeline: the miss comes a few events after the resolve, at the same sim time.
        /// </summary>
        private Vector3? CoverHit(TimelineEvent resolved)
        {
            var events = Director != null && Director.Timeline != null ? Director.Timeline.Events : null;
            int i = resolved.Sequence;
            if (events == null || i < 0 || i >= events.Count || events[i].Sequence != resolved.Sequence) return null;
            for (i++; i < events.Count && events[i].SimTime <= resolved.SimTime + 1e-4f; i++)
            {
                var e = events[i];
                if (e.Type != TacticsGame.Core.EventType.Miss || e.ActorId != resolved.ActorId) continue;
                var m = (MissInfo)e.Payload;
                if (m.HitCover) return m.CoverPoint;
            }
            return null;
        }

        /// <summary>blockedAt: the shot was stopped by cover there (MissInfo.HitCover), so it flies into the cover instead.</summary>
        private void PlayResolve(ActionDefinition def, UnitView actor, UnitView target, Vector3 targetPos, Vector3? blockedAt = null)
        {
            var s = StyleFor(def);
            Vector3 origin = Chest(actor);
            Vector3 aimAt = blockedAt ?? (target != null ? Chest(target) : targetPos + Vector3.up * 1f);
            Vector3 dir = Flat(aimAt - origin);
            if (dir.sqrMagnitude < 1e-4f) dir = actor.transform.forward;
            dir.Normalize();

            switch (s.Kind)
            {
                case VfxKind.Tracer:
                {
                    Vector3 muzzle = origin + dir * 0.5f + Vector3.up * 0.2f;
                    Vector3 end = s.Pierce && blockedAt == null ? aimAt + (aimAt - muzzle).normalized * 6f : aimAt;
                    bool big = s.Width >= 0.09f;
                    AddLine(new[] { muzzle, end }, s.Color, s.Width, big ? 0.35f : 0.15f, s.Width * 0.3f);
                    if (big) AddLine(new[] { muzzle, end }, new Color(0.8f, 0.8f, 0.8f, 0.35f), s.Width * 0.6f, 1.2f);   // lingering smoke trail
                    Burst(sparks, muzzle, big ? 14 : 6, Color.Lerp(s.Color, Color.white, 0.5f), 3f, 0.08f, 0.15f, dir * 1.5f);
                    break;
                }
                case VfxKind.Bolt:
                {
                    for (int k = 0; k < 2; k++) AddLine(Zigzag(origin + dir * 0.4f, aimAt, 8, 0.3f), s.Color, s.Width, 0.2f, s.Width * 0.5f);
                    Burst(sparks, aimAt, 10, s.Color, 4f, 0.08f, 0.25f);
                    break;
                }
                case VfxKind.Slash:
                {
                    // A run of quick strikes (A Thousand Cuts, Mesmerize's counter, Flowing Death): each cut in the run comes
                    // from its own angle, sweeping the other way, so they read as separate hits. The first is the plain one.
                    var cut = SlashCuts[SlashInRun(actor.UnitId) % SlashCuts.Length];
                    float dist = target != null ? Vector3.Distance(Flat(actor.transform.position), Flat(target.transform.position)) : 1.5f;
                    float radius = Mathf.Clamp(dist * 0.9f, 1.1f, 2.4f) * cut.Radius;
                    var pts = new Vector3[14];
                    for (int i = 0; i < pts.Length; i++)
                    {
                        float t = i / (pts.Length - 1f);
                        float k = cut.Reverse ? 1f - t : t;
                        Vector3 d = Quaternion.Euler(0f, cut.Yaw + Mathf.Lerp(-cut.Span, cut.Span, k), 0f) * dir;
                        pts[i] = actor.transform.position + d * radius + Vector3.up * Mathf.Lerp(cut.From, cut.To, k);
                    }
                    Color sc = Color.Lerp(s.Color, Color.white, cut.Brighten);
                    AddLine(pts, sc, s.Width, 0.28f, 0.01f, true);
                    if (cut.Brighten > 0f) Burst(sparks, pts[pts.Length / 2], 5, sc, 3f, 0.06f, 0.2f);
                    break;
                }
                case VfxKind.Beam:
                {
                    // Follows the sim's line: from the caster up or down to the aimed elevation, then level, at chest height.
                    Vector3 feet = actor.transform.position, lift = origin - feet;
                    float aimDist = Mathf.Min(Flat(targetPos - feet).magnitude, def.Range);
                    var beam = new[] { origin, LinePoint(def, feet, targetPos, dir, aimDist) + lift, LinePoint(def, feet, targetPos, dir, def.Range) + lift };
                    float w = Mathf.Max(0.2f, def.LineWidth * 0.6f);
                    AddLine(beam, WithAlpha(s.Color, 0.7f), w, 0.5f, w * 0.6f);
                    AddLine(beam, Color.white, w * 0.25f, 0.3f, w * 0.15f);
                    for (float m = 1f; m < def.Range; m += 1.2f)
                        Burst(puffs, LinePoint(def, feet, targetPos, dir, m) + lift, 2, WithAlpha(s.Color, 0.6f), 0.6f, 0.4f, 0.6f);
                    break;
                }
                case VfxKind.Shockwave:
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
                case VfxKind.Chain:
                {
                    AddLine(Zigzag(origin, aimAt, 16, 0.08f), s.Color, s.Width, 0.5f, s.Width);
                    Burst(sparks, aimAt, 8, Color.white, 3f, 0.08f, 0.2f);
                    break;
                }
                case VfxKind.Heal:
                {
                    var on = target != null ? target : actor;
                    rings.Add(new RingFx { Center = on.transform.position, From = 0.2f, To = 1.4f, Width = 0.08f, Life = 0.5f, Color = s.Color });
                    break;
                }
                case VfxKind.Buff:
                    rings.Add(new RingFx { Center = actor.transform.position, From = 0.3f, To = 1.4f, Width = 0.1f, Life = 0.45f, Color = s.Color });
                    Burst(puffs, actor.transform.position + Vector3.up * 0.2f, 14, WithAlpha(s.Color, 0.7f), 0.6f, 0.3f, 0.8f, Vector3.up * 2f);
                    break;
                case VfxKind.Lob:
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

        private struct Cut
        {
            public float From, To, Span, Yaw, Radius, Brighten; public bool Reverse;
            public Cut(float from, float to, float span, float yaw, float radius, float brighten, bool reverse)
            {
                From = from; To = to; Span = span; Yaw = yaw; Radius = radius; Brighten = brighten; Reverse = reverse;
            }
        }

        /// <summary>Slash arcs for strikes in a run, in order (heights at the ends of the arc, half its sweep in degrees, a
        /// turn off the aim, a radius scale, a brightening, and whether it sweeps right to left). The first is the plain one.</summary>
        private static readonly Cut[] SlashCuts =
        {
            new Cut(1.6f, 0.7f, 70f, 0f, 1f, 0f, false),      // high to low
            new Cut(0.6f, 1.5f, 60f, 12f, 0.9f, 0.15f, true),  // rising backhand
            new Cut(1.15f, 1.05f, 80f, -10f, 1.1f, 0.05f, false),   // flat sweep
            new Cut(1.8f, 0.4f, 45f, 20f, 0.95f, 0.25f, true), // steep chop
            new Cut(0.5f, 1.7f, 55f, -18f, 1f, 0.1f, false),   // uppercut
            new Cut(1.3f, 0.9f, 75f, 6f, 1.05f, 0.2f, true),   // wide return
        };

        private const float SlashRunSeconds = 1.2f;   // slashes this close together count as one run
        private readonly Dictionary<string, int> slashRun = new Dictionary<string, int>();
        private readonly Dictionary<string, float> slashAt = new Dictionary<string, float>();

        /// <summary>Which strike of its current run this slash of 'actorId' is (0 = the first).</summary>
        private int SlashInRun(string actorId)
        {
            float last; int n;
            n = slashAt.TryGetValue(actorId ?? "", out last) && Clock - last < SlashRunSeconds && Clock >= last && slashRun.TryGetValue(actorId ?? "", out n) ? n + 1 : 0;
            slashRun[actorId ?? ""] = n;
            slashAt[actorId ?? ""] = Clock;
            return n;
        }

        private static Style StyleFor(ActionDefinition def)
        {
            var v = def.Visuals;
            if (v != null && v.IsSet) return new Style(v.Kind, v.Color, v.Width, v.Pierce);
            Color c = def.Impact == ImpactType.Heavy ? HeavyColor : LightColor;
            float w = def.Impact == ImpactType.Heavy ? 0.14f : 0.07f;
            if (def.BehaviorId == "trap") return new Style(VfxKind.Lob, c);
            if (def.Shape == AreaShape.Line) return new Style(VfxKind.Beam, c, w);
            if (def.Shape == AreaShape.Circle) return new Style(VfxKind.Shockwave, c, w);
            if (def.Heal > 0f) return new Style(VfxKind.Heal, new Color(0.35f, 1f, 0.5f));
            if (def.Targeting == TargetType.Self) return new Style(VfxKind.Buff, c);
            if (def.Displace == DisplaceMode.TowardSource) return new Style(VfxKind.Chain, c);
            return new Style(def.IsRanged ? VfxKind.Tracer : VfxKind.Slash, c, w);
        }

        // ------------------------------------------------------------------ per frame

        private void Update()
        {
            float dt = Time.deltaTime;   // scaled: effects freeze with the world while orders are given
            markers.Begin();
            DrawWindups();
            DrawCharges();
            DrawStatuses();
            DrawHover();
            DrawDomes();

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

        /// <summary>A dome's colour: its ability's Visuals colour, or white.</summary>
        private Color DomeColor(string actionId)
        {
            ActionDefinition def;
            return actionId != null && defs.TryGetValue(actionId, out def) && def.Visuals != null && def.Visuals.IsSet ? def.Visuals.Color : new Color(0.9f, 0.9f, 0.9f);
        }

        /// <summary>
        /// Domes up around units (auras): a wireframe half-ellipsoid of the aura's radius and height over the unit, its
        /// latitude rings and slowly turning ribs fading in its last half second. A dome that slows actions (Chronosphere) also has a sweeping clock hand.
        /// </summary>
        private void DrawDomes()
        {
            float now = Clock;
            foreach (var d in domes.Values)
            {
                var view = Director.FindView(d.ActorId);
                if (view == null || now < d.Start) continue;
                float fade = Mathf.Clamp01((d.EndsAt - now) / 0.5f) * Mathf.Clamp01((now - d.Start) / 0.15f);
                if (fade <= 0f) continue;
                Color c = DomeColor(d.ActionId);
                Vector3 feet = view.transform.position;
                float h = d.Height > 0f ? d.Height : 2f;
                float pulse = 0.85f + 0.15f * Mathf.Sin(now * 6f);
                for (int i = 0; i < 4; i++)
                {
                    float k = i / 4f;   // 0 at the ground, up toward the top
                    markers.Circle(feet + Vector3.up * (h * k), d.Radius * Mathf.Sqrt(1f - k * k), WithAlpha(c, (i == 0 ? 0.75f : 0.4f) * fade * pulse), i == 0 ? 0.07f : 0.035f, 56);
                }
                const int ribs = 8;
                float spin = now * (d.SlowsTime ? -0.6f : 0.4f);
                for (int r = 0; r < ribs; r++)
                {
                    float a = spin + r * Mathf.PI * 2f / ribs;
                    Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    var pts = new Vector3[7];
                    for (int j = 0; j < pts.Length; j++)
                    {
                        float t = j / (float)(pts.Length - 1) * Mathf.PI * 0.5f;
                        pts[j] = feet + dir * (d.Radius * Mathf.Cos(t)) + Vector3.up * (h * Mathf.Sin(t));
                    }
                    markers.Polyline(pts, WithAlpha(c, 0.3f * fade), 0.03f);
                }
                if (d.SlowsTime)
                {
                    float a = -now * 2.5f;
                    markers.Line(new[] { feet + Vector3.up * 0.05f, feet + Vector3.up * 0.05f + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * d.Radius }, WithAlpha(c, 0.7f * fade), WithAlpha(c, 0.1f), 0.08f, 0.03f);
                }
            }
        }

        /// <summary>Charging: two purple rings at the feet that widen toward full power, pulsing faster once it is full.</summary>
        private void DrawCharges()
        {
            float now = Clock;
            foreach (var c in charges.Values)
            {
                var view = Director.FindView(c.ActorId);
                if (view == null) continue;
                float p = Mathf.Clamp01((now - c.Start) / c.Duration);
                float pulse = 0.6f + 0.4f * Mathf.Sin(now * (p >= 1f ? 18f : 7f));
                var color = new Color(0.8f, 0.45f, 1f, 0.5f + 0.4f * pulse);
                markers.Circle(view.transform.position, Mathf.Lerp(0.35f, 1.4f, p), color, 0.05f + 0.06f * p, 40);
                markers.Circle(view.transform.position, Mathf.Lerp(0.35f, 1.4f, p) * (0.55f + 0.1f * pulse), WithAlpha(color, 0.4f), 0.04f, 32);
            }
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
                    bool laser = s.Kind == VfxKind.Tracer && s.Width >= 0.09f;   // snipe / tracking shot: a laser sight
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
                    float aimDist = Mathf.Min(Flat(w.TargetPos - from).magnitude, def.Range);
                    Vector3 bend = LinePoint(def, from, w.TargetPos, dir, aimDist), end = LinePoint(def, from, w.TargetPos, dir, def.Range);
                    markers.Polyline(new[] { from - side, bend - side, end - side, end + side, bend + side, from + side, from - side }, WithAlpha(s.Color, 0.7f), 0.05f);
                    float fill = def.Range * p;
                    markers.Line(fill <= aimDist ? new[] { from, LinePoint(def, from, w.TargetPos, dir, fill) } : new[] { from, bend, LinePoint(def, from, w.TargetPos, dir, fill) },
                        WithAlpha(s.Color, 0.45f), WithAlpha(s.Color, 0.45f), def.LineWidth * 0.8f, def.LineWidth * 0.8f);
                }
                else if (def.BehaviorId == "charge")
                {
                    // Charge: the lane it will rush down (the full run, about a body wide), filling as it winds up.
                    Vector3 from = actor.transform.position;
                    Vector3 dir = Flat(w.TargetPos - from);
                    if (dir.sqrMagnitude < 1e-4f) dir = actor.transform.forward;
                    dir.Normalize();
                    Vector3 side = Vector3.Cross(Vector3.up, dir) * 0.6f, end = from + dir * def.Range;
                    markers.Polyline(new[] { from - side, end - side, end + side, from + side, from - side }, WithAlpha(s.Color, 0.7f), 0.05f);
                    markers.Line(new[] { from, from + dir * (def.Range * p) }, WithAlpha(s.Color, 0.45f), WithAlpha(s.Color, 0.45f), 1f, 1f);
                }
                else if (def.BehaviorId == "trap" && def.AreaRadius > 0f)
                {
                    markers.Circle(w.TargetPos, def.AreaRadius, WithAlpha(s.Color, 0.25f + 0.4f * p), 0.04f, 32);
                }
            }
        }

        /// <summary>
        /// Hovering a unit shows what it is aiming at: a line and ring to its target (or the area it will hit), plus a label
        /// by the cursor. A unit chasing or holding a queued ranged order shows that target too. It also shows everything
        /// aimed at it: each unit winding up on it, chasing it or holding a queued order on it (an area it stands in included)
        /// is highlighted, with a line to the hovered unit, both pulsing.
        /// </summary>
        private void DrawHover()
        {
            hoverText = null;
            var view = HoverOverride != null ? Director.FindView(HoverOverride) : UnitUnderMouse();
            if (view == null) return;
            string incoming = DrawIncoming(view);
            var w = windups.Find(x => x.ActorId == view.UnitId);
            KeyValuePair<string, QueuedActionInfo> order;
            bool hasOrder = orders.TryGetValue(view.UnitId, out order);
            if (w == null && !hasOrder)
            {
                if (incoming != null) { hoverText = incoming; hoverColor = HeavyColor; }
                return;
            }

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
                    UnitHighlighter.Request(target, c);   // outlined even behind cover
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
            }
            else
            {
                var ordered = Director.FindView(order.Key);
                if (ordered == null) return;
                Color oc = OrderColor;
                markers.Line(new[] { from, Chest(ordered) }, oc, WithAlpha(oc, 0.4f), 0.05f, 0.03f);
                markers.Circle(ordered.transform.position, 0.95f, oc, 0.06f, 40);
                UnitHighlighter.Request(ordered, oc);
                ActionDefinition od;
                string name = defs.TryGetValue(order.Value.ActionId ?? "", out od) ? od.DisplayName : order.Value.ActionId;
                hoverText = string.Format("{0} > {1}   ({2})", name, NameOf(order.Key), order.Value.Chase ? "chasing" : "queued, fires in range");
                hoverColor = oc;
            }
            if (incoming != null) hoverText += "\n" + incoming;
        }

        private static readonly Color OrderColor = new Color(1f, 0.85f, 0.4f);

        /// <summary>
        /// Everything aimed at 'view' (see DrawHover): pulsing lines from each attacker, rings and highlights on them.
        /// Returns a label line naming them, or null if nothing is.
        /// </summary>
        private string DrawIncoming(UnitView view)
        {
            float pulse = UnitHighlighter.Pulse;
            float width = Mathf.Lerp(0.04f, 0.12f, pulse);
            Vector3 to = Chest(view);
            var names = new List<string>();
            foreach (var w in windups)
            {
                if (w.ActorId == view.UnitId || !Threatens(w, view)) continue;
                var attacker = Director.FindView(w.ActorId);
                if (attacker == null) continue;
                Color c = w.Def.Impact == ImpactType.Heavy ? HeavyColor : WithAlpha(StyleFor(w.Def).Color, 1f);
                Color line = WithAlpha(c, Mathf.Lerp(0.45f, 1f, pulse));
                markers.Line(new[] { Chest(attacker), to }, line, line, width, width);
                markers.Circle(attacker.transform.position, Mathf.Lerp(0.8f, 1.05f, pulse), line, 0.07f, 40);
                UnitHighlighter.Request(attacker, c, true);
                names.Add(string.Format("{0} ({1}, {2:0.0}s)", NameOf(w.ActorId), w.Def.DisplayName, Mathf.Max(0f, w.Start + w.Duration - Clock)));
            }
            foreach (var pair in orders)
            {
                if (pair.Value.Key != view.UnitId || pair.Key == view.UnitId || windups.Exists(x => x.ActorId == pair.Key)) continue;
                var attacker = Director.FindView(pair.Key);
                if (attacker == null) continue;
                Color line = WithAlpha(OrderColor, Mathf.Lerp(0.35f, 0.9f, pulse));
                markers.Line(new[] { Chest(attacker), to }, line, line, width * 0.7f, width * 0.7f);
                UnitHighlighter.Request(attacker, OrderColor, true);
                names.Add(string.Format("{0} ({1})", NameOf(pair.Key), pair.Value.Value.Chase ? "chasing" : "queued"));
            }
            return names.Count > 0 ? "Targeted by: " + string.Join(", ", names.ToArray()) : null;
        }

        /// <summary>Whether this windup will hit 'view': aimed at it, or an enemy-hitting area that covers where it stands.</summary>
        private bool Threatens(Windup w, UnitView view)
        {
            if (w.TargetId == view.UnitId) return true;
            var def = w.Def;
            if (def.Shape == AreaShape.None || TeamOf(w.ActorId) == TeamOf(view.UnitId)) return false;
            var actor = Director.FindView(w.ActorId);
            if (actor == null) return false;
            Vector3 p = Flat(view.transform.position);
            const float body = 0.4f;
            if (def.Shape == AreaShape.Circle)
            {
                Vector3 center = Flat(def.Targeting == TargetType.Self ? actor.transform.position : w.TargetPos);
                return Vector3.Distance(p, center) <= def.AreaRadius + body;
            }
            Vector3 origin = Flat(actor.transform.position), dir = Flat(w.TargetPos) - origin;
            if (dir.sqrMagnitude < 1e-4f) return false;
            dir.Normalize();
            Vector3 v = p - origin;
            float along = Vector3.Dot(v, dir);
            return along >= -body && along <= def.Range + body && (v - dir * along).magnitude <= def.LineWidth / 2f + body;
        }

        private Team? TeamOf(string unitId)
        {
            if (unitId == null || Director == null || Director.Timeline == null || Director.Timeline.InitialState == null) return null;
            var u = Director.Timeline.InitialState.Units.Find(x => x.Id == unitId);
            return u != null ? u.Team : (Team?)null;
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
                    StatusDisplay.Look look;
                    if (!StatusDisplay.TryGet(id, out look) || look.Word == null) continue;
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
                        markers.Line(pts, look.Color, look.Color, 0.04f, 0.04f);
                        continue;
                    }
                    float pulse = 0.75f + 0.25f * Mathf.Sin(now * 5f + r * 7f);
                    int segments = id == StatusIds.Braced || id == StatusIds.Blocking ? 6 : id == StatusIds.Rooted ? 8 : 36;   // shield hexagon, chain octagon
                    markers.Circle(feet, r, WithAlpha(look.Color, 0.8f * pulse), 0.05f, segments);
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
            if (!renderers.TryGetValue(view, out rs))
                renderers[view] = rs = System.Array.FindAll(view.GetComponentsInChildren<Renderer>(), r => r.name != "HighlightShell");   // not UnitHighlighter's shells
            return rs;
        }

        private void OnGUI()
        {
            var cam = Cam;
            if (cam == null) return;
            GUI.depth = 3;   // over the HUD's bars, under the orders panel and its ability popup
            UiScale.Begin();
            DrawHoverLabel();
            if (texts.Count == 0) return;
            if (textStyle == null) textStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, wordWrap = false, clipping = TextClipping.Overflow };

            foreach (var t in texts)
            {
                float k = t.Age / t.Life;
                Vector3 world = t.Pos + Vector3.up * (0.9f * k);
                Vector2 sp;
                if (!UiScale.WorldToGui(cam, world, out sp)) continue;
                float pop = k < 0.12f ? Mathf.Lerp(1.5f, 1f, k / 0.12f) : 1f;
                textStyle.fontSize = Mathf.RoundToInt(t.Size * TextScale * pop);
                float alpha = k < 0.7f ? 1f : 1f - (k - 0.7f) / 0.3f;
                // As wide and tall as the text at its current size (it pops in bigger), so a long word never clips.
                Vector2 size = textStyle.CalcSize(new GUIContent(t.Text));
                float w = Mathf.Max(200f, size.x + 16f), h = Mathf.Max(40f, size.y + 8f);
                var rect = new Rect(sp.x - w / 2f, sp.y - h / 2f, w, h);
                OutlinedLabel(rect, t.Text, textStyle, WithAlpha(t.Color, alpha), Mathf.Max(1f, textStyle.fontSize / 12f));
            }
        }

        // ------------------------------------------------------------------ helpers

        private static readonly Vector2[] OutlineDirs =
        {
            new Vector2(1, 0), new Vector2(-1, 0), new Vector2(0, 1), new Vector2(0, -1),
            new Vector2(0.71f, 0.71f), new Vector2(-0.71f, 0.71f), new Vector2(0.71f, -0.71f), new Vector2(-0.71f, -0.71f),
        };

        /// <summary>Text with a dark outline all round (eight offset copies underneath), so it reads over anything.</summary>
        public static void OutlinedLabel(Rect rect, string text, GUIStyle style, Color color, float thickness = 1.5f)
        {
            var old = style.normal.textColor;
            style.normal.textColor = new Color(0f, 0f, 0f, 0.9f * color.a);
            foreach (var d in OutlineDirs) GUI.Label(new Rect(rect.x + d.x * thickness, rect.y + d.y * thickness, rect.width, rect.height), text, style);
            style.normal.textColor = color;
            GUI.Label(rect, text, style);
            style.normal.textColor = old;
        }

        private float HeightOf(UnitView v)
        {
            float h;
            return heights.TryGetValue(v.UnitId ?? "", out h) ? h : 1.8f;
        }

        private Vector3 Chest(UnitView v) { return v.transform.position + Vector3.up * (HeightOf(v) * 0.6f); }
        private Vector3 Head(UnitView v) { return v.transform.position + Vector3.up * (HeightOf(v) + 0.2f); }
        private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        /// <summary>A point 'along' metres down a Line attack aimed at targetPos, at the line's height there.</summary>
        private static Vector3 LinePoint(ActionDefinition def, Vector3 from, Vector3 targetPos, Vector3 dir, float along)
        {
            Vector3 p = from + dir * along;
            p.y = def.IsRanged ? BattleSimulation.LineHeightAt(from, targetPos, along) : from.y;   // melee sweeps stay level
            return p;
        }
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
