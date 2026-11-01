using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace TacticsGame.Core
{
    /// <summary>
    /// Battle-wide rule numbers, as overrides of BattleSimulation's tunable fields (its public float, int and bool fields:
    /// fall damage, focus, grapple timings...). Only the fields listed here change; everything else keeps the value written
    /// in BattleSimulation. The battle loads it from Resources (ResourceName) and applies it to each new simulation; edit it
    /// in Tools > Combat Test > Tuning &amp; Text.
    /// </summary>
    [CreateAssetMenu(menuName = "TacticsGame/Battle Tuning")]
    public class BattleTuning : ScriptableObject
    {
        /// <summary>Where the battle looks for it: Assets/Resources/BattleTuning.asset.</summary>
        public const string ResourceName = "BattleTuning";

        [System.Serializable]
        public class Entry
        {
            public string Field;
            public float Value;   // bools as 0 / 1, ints rounded
        }

        public List<Entry> Overrides = new List<Entry>();

        /// <summary>The BattleSimulation fields that can be tuned, in declaration order.</summary>
        public static List<FieldInfo> TunableFields() { return TunableFields(typeof(BattleSimulation)); }

        /// <summary>Any type's tunable fields (public float, int and bool instance fields), in declaration order, base class first: an AI style's numbers.</summary>
        public static List<FieldInfo> TunableFields(System.Type type)
        {
            return type.GetFields(BindingFlags.Public | BindingFlags.Instance)
                .Where(f => !f.IsInitOnly && (f.FieldType == typeof(float) || f.FieldType == typeof(int) || f.FieldType == typeof(bool)))
                .OrderBy(f => Depth(f.DeclaringType)).ThenBy(f => f.MetadataToken).ToList();
        }

        private static int Depth(System.Type t) { int d = 0; for (; t != null; t = t.BaseType) d++; return d; }

        /// <summary>A field's value as a float (bools 0 / 1).</summary>
        public static float Read(object owner, FieldInfo f)
        {
            object v = f.GetValue(owner);
            if (v is bool) return (bool)v ? 1f : 0f;
            if (v is int) return (int)v;
            return (float)v;
        }

        public static void Write(object owner, FieldInfo f, float value)
        {
            if (f.FieldType == typeof(bool)) f.SetValue(owner, value >= 0.5f);
            else if (f.FieldType == typeof(int)) f.SetValue(owner, Mathf.RoundToInt(value));
            else f.SetValue(owner, value);
        }

        public bool TryGet(string field, out float value)
        {
            if (Overrides == null) Overrides = new List<Entry>();
            var e = Overrides.Find(x => x.Field == field);
            value = e != null ? e.Value : 0f;
            return e != null;
        }

        public void Set(string field, float value)
        {
            if (Overrides == null) Overrides = new List<Entry>();
            var e = Overrides.Find(x => x.Field == field);
            if (e == null) Overrides.Add(e = new Entry { Field = field });
            e.Value = value;
        }

        public void Clear(string field) { if (Overrides != null) Overrides.RemoveAll(x => x.Field == field); }

        /// <summary>Set the overridden fields on 'sim' (unknown names, from a field since renamed, are skipped).</summary>
        public void ApplyTo(BattleSimulation sim)
        {
            if (sim == null || Overrides == null) return;
            var fields = TunableFields().ToDictionary(f => f.Name);
            foreach (var e in Overrides)
            {
                FieldInfo f;
                if (e != null && e.Field != null && fields.TryGetValue(e.Field, out f)) Write(sim, f, e.Value);
            }
        }
    }
}
