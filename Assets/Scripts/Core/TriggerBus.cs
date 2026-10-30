using System;
using System.Collections.Generic;

namespace TacticsGame.Core
{
    public enum TriggerType { ActionStarted, ActionResolved, DamageTaken, Dodged, Killed, Stunned, UnitReady }

    public class TriggerContext
    {
        public TriggerType Type;
        public BattleSimulation Sim;
        public Unit Subject;            // who the trigger is about (the one dodging / damaged / acting)
        public Unit Other;              // the other party (attacker), if any
        public float Amount;
        public ImpactType Impact;
        public ActionDefinition Action;
        public string Tag;              // Killed: the tag of the hit that killed (see ActionInstance.Tag)
    }

    /// <summary>
    /// Passives, stances and reactions subscribe here. Examples: Energy on Dodge
    /// listens for Dodged; Retaliate listens for Dodged and calls
    /// sim.QueueFreeAction(...); Take Aim listens for ActionStarted of enemies.
    /// </summary>
    public class TriggerBus
    {
        private readonly Dictionary<TriggerType, List<Action<TriggerContext>>> listeners =
            new Dictionary<TriggerType, List<Action<TriggerContext>>>();

        public void Subscribe(TriggerType type, Action<TriggerContext> listener)
        {
            List<Action<TriggerContext>> list;
            if (!listeners.TryGetValue(type, out list)) listeners[type] = list = new List<Action<TriggerContext>>();
            list.Add(listener);
        }

        public void Unsubscribe(TriggerType type, Action<TriggerContext> listener)
        {
            List<Action<TriggerContext>> list;
            if (listeners.TryGetValue(type, out list)) list.Remove(listener);
        }

        public void Fire(TriggerContext ctx)
        {
            List<Action<TriggerContext>> list;
            if (!listeners.TryGetValue(ctx.Type, out list)) return;
            foreach (var l in list.ToArray()) l(ctx);   // copy: listeners may unsubscribe while firing
        }
    }
}
