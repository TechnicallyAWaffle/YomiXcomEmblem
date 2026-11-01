using System.Collections.Generic;
using UnityEngine;

namespace TacticsGame.Core
{
    /// <summary>
    /// How the AI plays a unit, as data (UnitDefinition.AI): a Style (one of UnitAIs.Styles, or "auto" to pick one from the
    /// unit's abilities by their roles) and numbers that replace the style's own (how far it keeps from brawlers, the strain
    /// it rests at, ...: any public number on the style's class, by field name). Without one, a unit is played by its
    /// AIProfile string as before. Edit them in Tools > Combat Test > Tuning &amp; Text.
    /// </summary>
    [CreateAssetMenu(menuName = "TacticsGame/AI Profile")]
    public class AIProfileDefinition : ScriptableObject
    {
        [Tooltip("sniper, assassin, skirmisher, healer, caster, brutebeast; auto = picked from the unit's abilities (AbilityRole); empty = the basic AI.")]
        public string Style = UnitAIs.Auto;
        [Tooltip("Numbers that replace the style's own, by field name (KeepDistance, HighStrain, RestedStrain, ...).")]
        public List<BattleTuning.Entry> Tuning = new List<BattleTuning.Entry>();

        public bool TryGet(string field, out float value)
        {
            var e = Tuning != null ? Tuning.Find(x => x.Field == field) : null;
            value = e != null ? e.Value : 0f;
            return e != null;
        }

        public void Set(string field, float value)
        {
            if (Tuning == null) Tuning = new List<BattleTuning.Entry>();
            var e = Tuning.Find(x => x.Field == field);
            if (e == null) Tuning.Add(e = new BattleTuning.Entry { Field = field });
            e.Value = value;
        }

        public void Clear(string field) { if (Tuning != null) Tuning.RemoveAll(x => x.Field == field); }

        /// <summary>Set the tuned numbers on an AI of this profile's style (names it doesn't have are skipped).</summary>
        public void ApplyTo(object ai)
        {
            if (ai == null || Tuning == null) return;
            foreach (var f in BattleTuning.TunableFields(ai.GetType()))
            {
                float v;
                if (TryGet(f.Name, out v)) BattleTuning.Write(ai, f, v);
            }
        }
    }
}
