using UnityEngine;

namespace TacticsGame.Presentation
{
    /// <summary>
    /// Deliverance's hold on time, drawn over the screen: a violet vignette that breathes, its edges split into a red and a
    /// cyan ghost, and faint bands that drift and shear across the view. Freedom's Embrace: it grows from nothing as the
    /// order clock runs down (BattleController.EmbraceProgress), strongest at the end. Phase two (BattleController.TimeUnbound):
    /// it stays, kept light (UnboundStrength), and grows a little as the creep ramps up. An overlay, not a screen warp: it
    /// never hides what the player needs to see.
    /// </summary>
    public class DeliveranceDistortion : MonoBehaviour
    {
        public BattleController Controller;
        [Tooltip("Strength when Freedom's Embrace's clock runs out (0..1).")]
        [Range(0f, 1f)] public float EmbraceStrength = 0.8f;
        [Tooltip("Phase two's lasting strength, and what the creep ramping up adds to it.")]
        [Range(0f, 1f)] public float UnboundStrength = 0.14f, UnboundRamp = 0.12f;
        public Color Tint = new Color(0.45f, 0.2f, 0.75f, 1f);

        private Texture2D vignette;
        private float shown;

        /// <summary>How strong it is right now (0..1).</summary>
        public float Strength
        {
            get
            {
                if (Controller == null || Controller.Sim == null) return 0f;
                float s = Controller.EmbraceProgress > 0f ? EmbraceStrength * Controller.EmbraceProgress * Controller.EmbraceProgress : 0f;
                if (Controller.TimeUnbound) s = Mathf.Max(s, UnboundStrength + UnboundRamp * Controller.CreepProgress);
                return s;
            }
        }

        private void Update()
        {
            shown = Mathf.MoveTowards(shown, Strength, Time.unscaledDeltaTime * 1.5f);   // eases in and out
        }

        private void OnGUI()
        {
            if (shown <= 0.002f || Event.current.type != EventType.Repaint) return;
            if (vignette == null) vignette = MakeVignette(128);
            float t = Time.unscaledTime, w = Screen.width, h = Screen.height;
            float breathe = 1f + 0.06f * Mathf.Sin(t * 1.7f) * shown;
            var full = new Rect(-w * (breathe - 1f) / 2f, -h * (breathe - 1f) / 2f, w * breathe, h * breathe);
            var old = GUI.color;

            // Split edges: a red ghost one way, a cyan one the other, then the violet vignette over both.
            float split = 6f + 18f * shown;
            GUI.color = new Color(1f, 0.15f, 0.25f, 0.35f * shown);
            GUI.DrawTexture(new Rect(full.x - split, full.y, full.width, full.height), vignette);
            GUI.color = new Color(0.1f, 0.9f, 1f, 0.35f * shown);
            GUI.DrawTexture(new Rect(full.x + split, full.y, full.width, full.height), vignette);
            GUI.color = new Color(Tint.r, Tint.g, Tint.b, 0.8f * shown);
            GUI.DrawTexture(full, vignette);

            // Drifting, shearing bands: thin translucent strips sliding sideways at different speeds.
            int bands = 4 + Mathf.RoundToInt(10f * shown);
            for (int i = 0; i < bands; i++)
            {
                float y = Mathf.Repeat(i * 0.137f + t * (0.02f + 0.013f * i), 1f) * h;
                float thick = 1.5f + 5f * shown * (0.5f + 0.5f * Mathf.Sin(t * 3f + i));
                float shear = Mathf.Sin(t * (1.3f + 0.2f * i) + i) * 40f * shown;
                GUI.color = new Color(i % 2 == 0 ? 0.8f : 0.3f, 0.5f, 1f, 0.06f + 0.1f * shown);
                GUI.DrawTexture(new Rect(shear, y, w, thick), Texture2D.whiteTexture);
            }
            GUI.color = old;
        }

        /// <summary>Clear in the middle, opaque at the edges: alpha by distance from the centre, eased.</summary>
        private static Texture2D MakeVignette(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
                    float d = Mathf.Clamp01((Mathf.Sqrt(dx * dx + dy * dy) - 0.45f) / 0.75f);
                    px[y * size + x] = new Color(1f, 1f, 1f, d * d * (3f - 2f * d));
                }
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        private void OnDestroy() { if (vignette != null) Destroy(vignette); }
    }
}
