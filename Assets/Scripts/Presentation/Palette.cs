using UnityEngine;

namespace TacticsGame.Presentation
{
    /// <summary>
    /// The art bible, in code. New presentation code takes its colours from here.
    ///   Style:   clean low-poly tactics arena: flat-coloured stone and earth under saturated team colours, every ability
    ///            readable by its own colour (ActionDefinition.Visuals).
    ///   Palette: a greybox arena, no browns: ground dark grey, raised ground mid grey, cover light grey (three clear steps
    ///            of brightness, so the map reads at a glance), the squad blue, the enemy red, shadow cool
    ///            slate, accent amber (critical strikes, the one thing allowed to glow gold).
    ///   Mood:    a clear, nearly white sun low across the field, cool bounce in the shadows, neutral haze.
    /// The scene's light, ambient, fog and post-processing (CombatTest.unity, Settings/SampleSceneProfile.asset) and the
    /// map materials (Assets/Materials) carry the same values: change them together.
    /// </summary>
    public static class Palette
    {
        // ---- surfaces
        public static readonly Color Ground = Hex(0x484B50);      // dark neutral grey (Materials/Ground.mat)
        public static readonly Color Raised = Hex(0x8A8F96);      // mid grey (Materials/Elevation.mat)
        public static readonly Color Cover = Hex(0xC3C7CC);       // light grey (Materials/Cover.mat)

        // ---- teams
        public static readonly Color Squad = Hex(0x4080FF);       // Materials/Team_Player.mat
        public static readonly Color Enemy = Hex(0xFF4D40);       // Materials/Team_Enemy.mat

        // ---- light
        public static readonly Color Sun = Hex(0xFFF4E6);         // the key light
        public static readonly Color SkyBounce = Hex(0x97A3B8);   // ambient from above
        public static readonly Color Horizon = Hex(0x767A80);     // ambient at the horizon
        public static readonly Color GroundBounce = Hex(0x34373C);// ambient from below
        public static readonly Color Haze = Hex(0xAEB3BA);        // fog and the camera's background
        public static readonly Color Shadow = Hex(0x3A4660);      // cool slate

        // ---- effects
        public static readonly Color Accent = Hex(0xFFCC33);      // amber: critical strikes
        public static readonly Color Dust = new Color(0.62f, 0.56f, 0.46f, 0.6f);   // kicked-up earth
        public static readonly Color Mote = new Color(1f, 0.92f, 0.78f, 0.35f);     // dust in the sunlight

        public static Color Hex(int rgb, float alpha = 1f)
        {
            return new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, alpha);
        }
    }
}
