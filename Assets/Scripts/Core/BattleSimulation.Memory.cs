using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TacticsGame.Core
{
    /// <summary>A unit's latest signal (HowlBehavior): whom it called its pack onto, and when.</summary>
    public struct SignalInfo { public string TargetId; public float At; }

    /// <summary>
    /// BattleSimulation: what AI styles remember about one battle, and signals between units.
    /// Memory: AI styles are shared between battles (UnitAIs), so anything one must remember (a pack's leader and mood, see
    /// PackAI) lives on the battle instead: Memory&lt;T&gt;() is this battle's one T, made on first use.
    /// Signal: an ability calls its user's allies onto a target (Howling Lament); AIs read the latest with LastSignal.
    /// </summary>
    public partial class BattleSimulation
    {
        private readonly Dictionary<System.Type, object> memory = new Dictionary<System.Type, object>();
        private readonly Dictionary<string, SignalInfo> signals = new Dictionary<string, SignalInfo>();

        /// <summary>This battle's T (an AI style's notes), made the first time it is asked for.</summary>
        public T Memory<T>() where T : class, new()
        {
            object o;
            if (!memory.TryGetValue(typeof(T), out o)) memory[typeof(T)] = o = new T();
            return (T)o;
        }

        /// <summary>'from' calls its side onto 'target' now.</summary>
        public void Signal(Unit from, Unit target)
        {
            if (from == null || target == null) return;
            signals[from.Id] = new SignalInfo { TargetId = target.Id, At = Now };
        }

        /// <summary>The latest signal 'fromId' gave, if any.</summary>
        public bool LastSignal(string fromId, out SignalInfo info) { return signals.TryGetValue(fromId ?? "", out info); }

        /// <summary>An AI unit that is only waiting (an Idle or Wait) or walking is ready to decide again now: its side's plan
        /// changed (a pack set on its prey). A unit busy with anything else finishes it first.</summary>
        public void Wake(Unit u)
        {
            if (u == null || !u.IsAlive || u.PlayerOrdered) return;
            if (u.Phase == UnitPhase.Recovery && u.Waiting) { EndRecoveryNow(u); return; }
            if (u.Phase == UnitPhase.Moving && !u.IsJumping && u.Chase == null && !IsGliding(u)) EndMove(u);
        }

        /// <summary>How often (in ticks) AIs that keep watch (IBattleWatcher) get to look: 0.1s.</summary>
        public const int WatchTicks = 4;

        /// <summary>Every WatchTicks: each AI that keeps watch looks at the battle for each unit it plays.</summary>
        private void TickWatchers()
        {
            if (!IsStep(WatchTicks)) return;
            foreach (var u in State.Units.ToArray())
            {
                if (!u.IsAlive || u.PlayerOrdered || u.IsMaddened) continue;
                var ai = u.Team == Team.Player ? AllyAI : EnemyAI;
                var router = ai as AIRouter;
                var watcher = (router != null ? router.For(u) : ai) as IBattleWatcher;
                if (watcher != null) watcher.Watch(this, u);
            }
        }

        /// <summary>A stun put on 'u' (that would end by 'endsBy') ends now: whatever held it let go (Captivate's broken pull).
        /// A longer stun from something else stays.</summary>
        public void ReleaseStun(Unit u, float endsBy)
        {
            if (u == null || !u.IsAlive || u.Phase != UnitPhase.Stunned || u.PhaseEndsAt > endsBy + 1e-3f) return;
            u.PhaseEndsAt = Now;
        }
    }
}
