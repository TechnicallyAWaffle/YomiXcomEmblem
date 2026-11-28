using System.Collections.Generic;
using UnityEngine;
using TacticsGame.Core;

namespace TacticsGame.Presentation
{
    /// <summary>
    /// Temperance on screen, read from the timeline at the playback clock (so it plays back and scrubs with the replay):
    ///   a bar top left tracking its approach (an Omen's CONVERGE REALITIES jumps it on);
    ///   WarningSeconds before it arrives the screen starts to brighten and TEMPERANCE APPROACHES shows top middle, fading over
    ///   BannerSeconds of real time; the giant rings descend over the map's sky;
    ///   as it arrives, a flash; then its execution beams from the sky, and the threshold (ExecuteThreshold) that the squad's
    ///   health bars mark with a gold divider (BattleHUD, UnitPanel).
    /// Its explosions are ordinary blasts (CombatVfx). The BattleController adds it to itself.
    /// </summary>
    public class TemperanceDisplay : MonoBehaviour
    {
        public PlaybackDirector Director;

        [Header("Screen")]
        [Tooltip("Real seconds the TEMPERANCE APPROACHES banner takes to fade.")]
        [Min(0.1f)] public float BannerSeconds = 2f;
        [Tooltip("How bright the screen gets by the moment it arrives (white over it).")]
        [Range(0f, 1f)] public float WarningBrightness = 0.3f;
        [Tooltip("Real seconds the arrival flash takes to fade, and how bright it stays after.")]
        [Min(0.05f)] public float FlashSeconds = 0.8f;
        [Range(0f, 1f)] public float ArrivedBrightness = 0.08f;
        public Color BarColor = new Color(1f, 0.82f, 0.35f);
        public Color LightColor = new Color(1f, 0.96f, 0.85f);

        [Header("Rings")]
        [Tooltip("Height of the rings' centre over the map once it is here, and where they come down from.")]
        public float RingHeight = 260f, RingStartHeight = 700f;
        [Tooltip("Radii of the rings (metres).")]
        public float[] RingRadii = { 320f, 420f, 520f };
        public float RingThickness = 9f;
        public Color RingColor = new Color(0.95f, 0.85f, 0.55f);
        [Tooltip("Degrees a second the rings turn.")]
        public float RingSpin = 2f;

        [Header("Execution beam")]
        public Color BeamColor = new Color(1f, 0.85f, 0.3f);
        public float BeamHeight = 150f, BeamWidth = 1.2f;

        /// <summary>The squad's execution threshold now (share of max health; 0: none): the gold divider on their health bars.</summary>
        public static float ExecuteThreshold { get; private set; }

        // What the timeline says at the playback clock.
        private bool coming, arrived, warned;
        private TemperanceInfo info;
        private float lastClock = -1f;
        private int scanned;
        private float bannerAt = -99f, flashAt = -99f;
        private class Beam { public string TargetId; public float From, At; public LineRenderer Line; }
        private readonly List<Beam> beams = new List<Beam>();
        private GameObject rings;
        private readonly List<Transform> ringParts = new List<Transform>();
        private Material ringMaterial, beamMaterial;
        private GUIStyle bannerStyle, labelStyle;
        private static Texture2D white;

        private void OnDisable() { ExecuteThreshold = 0f; }

        private void Update()
        {
            var timeline = Director != null ? Director.Timeline : null;
            if (timeline == null) { ExecuteThreshold = 0f; return; }
            float clock = Director.PlaybackClock;
            if (clock < lastClock - 1e-4f || scanned > timeline.Events.Count) Reset();   // scrubbed back (or a new battle): read it again
            lastClock = clock;
            for (; scanned < timeline.Events.Count; scanned++)
            {
                var e = timeline.Events[scanned];
                if (e.SimTime > clock + 1e-4f) break;
                Read(e);
            }
            // The warning: once the clock is within it (and the flash, once arrived).
            if (coming && !arrived && !warned && clock >= info.ArrivesAt - info.WarningSeconds) { warned = true; bannerAt = Time.unscaledTime; }
            ExecuteThreshold = arrived ? Mathf.Clamp01(info.ThresholdStart + info.ThresholdPerSecond * (clock - info.ArrivesAt)) : 0f;
            UpdateRings(clock);
            UpdateBeams(clock);
        }

        private void Read(TimelineEvent e)
        {
            switch (e.Type)
            {
                case TacticsGame.Core.EventType.TemperanceTimer:
                    info = (TemperanceInfo)e.Payload; coming = true;
                    break;
                case TacticsGame.Core.EventType.TemperanceArrived:
                    info = (TemperanceInfo)e.Payload; coming = true; arrived = true;
                    if (!warned) { warned = true; bannerAt = Time.unscaledTime; }
                    if (Director.PlaybackClock - e.SimTime < 0.5f) flashAt = Time.unscaledTime;   // not when jumping far past it
                    break;
                case TacticsGame.Core.EventType.TemperanceExecution:
                    beams.Add(new Beam { TargetId = e.TargetId, From = e.SimTime, At = ((ExecutionInfo)e.Payload).At });
                    break;
            }
        }

        private void Reset()
        {
            coming = arrived = warned = false;
            scanned = 0;
            bannerAt = flashAt = -99f;
            foreach (var b in beams) if (b.Line != null) Destroy(b.Line.gameObject);
            beams.Clear();
        }

        // ------------------------------------------------------------------ rings

        /// <summary>Giant rings round the sky: they come down over the warning and hang there once it has arrived.</summary>
        private void UpdateRings(float clock)
        {
            float k = !coming ? 0f : arrived ? 1f : Mathf.Clamp01(1f - (info.ArrivesAt - clock) / Mathf.Max(0.01f, info.WarningSeconds));
            if (k <= 0f) { if (rings != null) rings.SetActive(false); return; }
            if (rings == null) BuildRings();
            rings.SetActive(true);
            float e = k * k * (3f - 2f * k);
            rings.transform.position = MapCentre() + Vector3.up * Mathf.Lerp(RingStartHeight, RingHeight, e);
            for (int i = 0; i < ringParts.Count; i++)
                ringParts[i].localRotation = Quaternion.Euler(8f + 7f * i, (i % 2 == 0 ? 1f : -1f) * RingSpin * clock + 40f * i, 4f * i);
            var c = RingColor; c.a = e;
            ringMaterial.color = c;
        }

        private Vector3 MapCentre()
        {
            var loader = FindFirstObjectByType<MapLoader>();
            return loader != null ? loader.transform.position : Vector3.zero;
        }

        private void BuildRings()
        {
            rings = new GameObject("TemperanceRings");
            ringMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default"));
            ringMaterial.color = RingColor;
            foreach (var r in RingRadii)
            {
                var part = new GameObject("Ring");
                part.transform.SetParent(rings.transform, false);
                part.AddComponent<MeshFilter>().sharedMesh = Torus(r, RingThickness, 160, 10);
                var mr = part.AddComponent<MeshRenderer>();
                mr.sharedMaterial = ringMaterial;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                ringParts.Add(part.transform);
            }
        }

        /// <summary>A torus around the Y axis: 'radius' to the tube's centre, the tube 'tube' thick.</summary>
        private static Mesh Torus(float radius, float tube, int around, int sides)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            for (int i = 0; i <= around; i++)
            {
                float a = i * Mathf.PI * 2f / around;
                Vector3 centre = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius;
                Vector3 outward = centre.normalized;
                for (int j = 0; j <= sides; j++)
                {
                    float b = j * Mathf.PI * 2f / sides;
                    verts.Add(centre + (outward * Mathf.Cos(b) + Vector3.up * Mathf.Sin(b)) * (tube / 2f));
                }
            }
            int row = sides + 1;
            for (int i = 0; i < around; i++)
                for (int j = 0; j < sides; j++)
                {
                    int a0 = i * row + j, a1 = a0 + 1, b0 = a0 + row, b1 = b0 + 1;
                    tris.Add(a0); tris.Add(b0); tris.Add(a1);
                    tris.Add(a1); tris.Add(b0); tris.Add(b1);
                }
            var m = new Mesh { name = "TemperanceRing" };
            m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(verts);
            m.SetTriangles(tris, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        // ------------------------------------------------------------------ execution beams

        /// <summary>A beam from the sky onto each unit marked for execution: thin as it winds up, a burst as it lands, then gone.</summary>
        private void UpdateBeams(float clock)
        {
            for (int i = beams.Count - 1; i >= 0; i--)
            {
                var b = beams[i];
                var view = Director.FindView(b.TargetId);
                if (view == null || clock > b.At + 0.35f) { if (b.Line != null) Destroy(b.Line.gameObject); beams.RemoveAt(i); continue; }
                if (b.Line == null) b.Line = MakeBeam();
                float k = Mathf.Clamp01((clock - b.From) / Mathf.Max(0.01f, b.At - b.From));
                bool landed = clock >= b.At;
                Vector3 foot = view.transform.position;
                b.Line.SetPosition(0, foot + Vector3.up * BeamHeight);
                b.Line.SetPosition(1, foot);
                float w = landed ? BeamWidth * 3f * (1f - (clock - b.At) / 0.35f) : BeamWidth * (0.15f + 0.5f * k);
                b.Line.startWidth = b.Line.endWidth = Mathf.Max(0.02f, w);
                var c = BeamColor; c.a = landed ? 1f : 0.35f + 0.5f * k + 0.15f * Mathf.Sin(clock * 40f);
                b.Line.startColor = b.Line.endColor = c;
            }
        }

        private LineRenderer MakeBeam()
        {
            if (beamMaterial == null) beamMaterial = new Material(Shader.Find("Sprites/Default"));
            var go = new GameObject("TemperanceBeam");
            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = beamMaterial;
            line.positionCount = 2;
            line.useWorldSpace = true;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return line;
        }

        // ------------------------------------------------------------------ screen

        private void OnGUI()
        {
            if (!coming || Director == null) return;
            UiScale.Begin();
            if (white == null) white = Texture2D.whiteTexture;
            if (bannerStyle == null)
            {
                bannerStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 40, fontStyle = FontStyle.Bold };
                labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold };
            }
            float clock = Director.PlaybackClock;
            float w = UiScale.Width, h = UiScale.Height;

            // The light: brightening over the warning, the flash as it arrives, a glow after.
            float light = 0f;
            if (arrived)
            {
                float f = (Time.unscaledTime - flashAt) / FlashSeconds;
                light = f < 1f ? Mathf.Lerp(1f, ArrivedBrightness, f * f) : ArrivedBrightness;
            }
            else if (warned) light = WarningBrightness * Mathf.Clamp01(1f - (info.ArrivesAt - clock) / Mathf.Max(0.01f, info.WarningSeconds));
            if (light > 0.001f)
            {
                GUI.depth = 10;   // under the battle's panels
                var c = LightColor; c.a = light;
                GUI.color = c;
                GUI.DrawTexture(new Rect(0f, 0f, w, h), white);
                GUI.color = Color.white;
            }

            // The approach bar, top left.
            float barX = 12f, barY = 12f, barW = 240f;
            float span = Mathf.Max(0.01f, info.ArrivesAt - info.StartedAt);
            float fill = arrived ? 1f : Mathf.Clamp01((clock - info.StartedAt) / span);
            string text = arrived ? "TEMPERANCE IS HERE   execute below " + Mathf.RoundToInt(ExecuteThreshold * 100f) + "%"
                                  : "TEMPERANCE   " + Mathf.CeilToInt(Mathf.Max(0f, info.ArrivesAt - clock)) + "s";
            CombatVfx.OutlinedLabel(new Rect(barX, barY, barW + 200f, 18f), text, labelStyle, BarColor, 1f);
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(new Rect(barX, barY + 20f, barW, 8f), white);
            GUI.color = BarColor;
            GUI.DrawTexture(new Rect(barX, barY + 20f, barW * fill, 8f), white);
            GUI.color = Color.white;

            // TEMPERANCE APPROACHES, fading over BannerSeconds of real time.
            float since = Time.unscaledTime - bannerAt;
            if (since >= 0f && since < BannerSeconds)
            {
                var c = BarColor; c.a = 1f - since / BannerSeconds;
                CombatVfx.OutlinedLabel(new Rect(0f, h * 0.12f, w, 60f), "TEMPERANCE APPROACHES", bannerStyle, c, 2f);
            }
        }
    }
}
