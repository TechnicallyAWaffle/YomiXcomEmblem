using UnityEngine;

namespace TacticsGame.Presentation
{
    /// <summary>
    /// Small white icons drawn in code (tint them with GUI.color) until there is art: a crosshair for ranged attacks and a
    /// sword for melee ones, used next to windup bars.
    /// </summary>
    public static class HudIcons
    {
        private const int Size = 32;
        private static Texture2D ranged, melee;

        public static Texture2D Ranged { get { return ranged != null ? ranged : (ranged = Draw(Crosshair)); } }
        public static Texture2D Melee { get { return melee != null ? melee : (melee = Draw(Sword)); } }

        /// <summary>Coverage (0..1) of the crosshair at a point in [0, 1] x [0, 1]: a ring, four ticks and a dot.</summary>
        private static float Crosshair(Vector2 p)
        {
            Vector2 c = new Vector2(0.5f, 0.5f);
            float r = (p - c).magnitude;
            float a = Band(Mathf.Abs(r - 0.32f), 0.06f);
            a = Mathf.Max(a, Band(Segment(p, new Vector2(0.5f, 0.02f), new Vector2(0.5f, 0.3f)), 0.05f));
            a = Mathf.Max(a, Band(Segment(p, new Vector2(0.5f, 0.7f), new Vector2(0.5f, 0.98f)), 0.05f));
            a = Mathf.Max(a, Band(Segment(p, new Vector2(0.02f, 0.5f), new Vector2(0.3f, 0.5f)), 0.05f));
            a = Mathf.Max(a, Band(Segment(p, new Vector2(0.7f, 0.5f), new Vector2(0.98f, 0.5f)), 0.05f));
            return Mathf.Max(a, Band(r, 0.08f));
        }

        /// <summary>A sword pointing up and right: blade, crossguard and grip.</summary>
        private static float Sword(Vector2 p)
        {
            float a = Band(Segment(p, new Vector2(0.3f, 0.3f), new Vector2(0.92f, 0.92f)), 0.075f);
            a = Mathf.Max(a, Band(Segment(p, new Vector2(0.12f, 0.42f), new Vector2(0.42f, 0.12f)), 0.07f));
            return Mathf.Max(a, Band(Segment(p, new Vector2(0.06f, 0.06f), new Vector2(0.3f, 0.3f)), 0.06f));
        }

        private static Texture2D Draw(System.Func<Vector2, float> coverage)
        {
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            var px = new Color32[Size * Size];
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    // Four samples per pixel for smooth edges. Texture rows go bottom-up, as does p.y.
                    float a = 0f;
                    for (int s = 0; s < 4; s++)
                        a += coverage(new Vector2((x + 0.25f + 0.5f * (s & 1)) / Size, (y + 0.25f + 0.5f * (s >> 1)) / Size));
                    px[y * Size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(255f * Mathf.Clamp01(a / 4f)));
                }
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }

        /// <summary>1 inside half-width 'w' of a shape (distance 'd' from it), 0 outside.</summary>
        private static float Band(float d, float w) { return d <= w ? 1f : 0f; }

        private static float Segment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
            return (p - (a + ab * t)).magnitude;
        }
    }
}
