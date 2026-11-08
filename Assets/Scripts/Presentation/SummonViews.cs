using System.Collections.Generic;
using UnityEngine;
using TacticsGame.Core;

namespace TacticsGame.Presentation
{
    /// <summary>
    /// Views for units that join a battle mid-way (summons: UnitSpawned). Each arrival gets a view made from its definition's
    /// ViewPrefab (or the launcher's fallback capsule), its look (UnitLooks) and its team's colour, and goes in the battle's
    /// registry so every event finds it. A replay rewinds to the start, so the summoned views are taken away again and come
    /// back as their UnitSpawned events replay. BattleController puts one on itself (Bind).
    /// </summary>
    public class SummonViews : MonoBehaviour
    {
        private PlaybackDirector director;
        private BattleContext context;
        private BattleLauncher launcher;
        private readonly List<string> spawned = new List<string>();

        public void Bind(PlaybackDirector director, BattleContext context)
        {
            Unbind();
            this.director = director;
            this.context = context;
            launcher = FindFirstObjectByType<BattleLauncher>();
            if (director == null) return;
            director.OnEventDispatched += OnEvent;
            director.OnPlaybackReset += Clear;
        }

        private void Unbind()
        {
            if (director == null) return;
            director.OnEventDispatched -= OnEvent;
            director.OnPlaybackReset -= Clear;
        }

        private void OnDestroy() { Unbind(); }

        private void OnEvent(TimelineEvent evt)
        {
            if (evt.Type != TacticsGame.Core.EventType.UnitSpawned || context == null || context.Registry == null) return;
            var info = (SpawnInfo)evt.Payload;
            var unit = info.Unit;
            if (unit == null || context.Registry.ContainsKey(unit.Id)) return;
            var prefab = info.Definition != null && info.Definition.ViewPrefab != null ? info.Definition.ViewPrefab : launcher != null ? launcher.FallbackUnitPrefab : null;
            if (prefab == null) return;
            var go = Instantiate(prefab, unit.Position, FacingFor(unit), context.transform);
            go.name = (unit.Team == Team.Enemy ? "Enemy_" : "Player_") + unit.DisplayName + "_" + unit.Id;
            var view = go.GetComponent<UnitView>();
            if (view == null) { Destroy(go); return; }
            var material = TeamMaterial(unit.Team);
            if (material != null) foreach (var r in go.GetComponentsInChildren<Renderer>()) r.sharedMaterial = material;
            view.UnitId = unit.Id;
            UnitLooks.Apply(view, info.Definition);
            view.Director = director;
            view.ResetTo(unit);
            context.Registry[unit.Id] = view;
            spawned.Add(unit.Id);
        }

        /// <summary>Back to the opening state (a replay): the summons aren't there yet.</summary>
        private void Clear()
        {
            if (context == null || context.Registry == null) { spawned.Clear(); return; }
            foreach (var id in spawned)
            {
                UnitView view;
                if (!context.Registry.TryGetValue(id, out view)) continue;
                context.Registry.Remove(id);
                if (view != null) Destroy(view.gameObject);
            }
            spawned.Clear();
        }

        /// <summary>The team's body material: from a unit of that team already in the battle, else the launcher's.</summary>
        private Material TeamMaterial(Team team)
        {
            foreach (var p in context.Placements)
            {
                if (p.Team != team || p.View == null) continue;
                var r = p.View.Model != null ? p.View.Model.GetComponent<Renderer>() : p.View.GetComponentInChildren<Renderer>();
                if (r != null) return r.sharedMaterial;
            }
            return launcher != null ? (team == Team.Player ? launcher.PlayerMaterial : launcher.EnemyMaterial) : null;
        }

        private Quaternion FacingFor(Unit unit)
        {
            if (launcher != null) return launcher.Facing(unit.Team, unit.Position);
            return Quaternion.LookRotation(unit.Team == Team.Player ? Vector3.forward : Vector3.back);
        }
    }
}
