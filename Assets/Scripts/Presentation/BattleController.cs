using System.Collections;
using UnityEngine;
using TacticsGame.Core;

namespace TacticsGame.Presentation
{
    /// <summary>
    /// Runs the battle loop:
    ///   simulate to the next decision point (instant) -> play that stretch back (real time by default, see PlaybackDirector.playbackSpeed) ->
    ///   hold the world and ask the player for orders -> repeat until the battle ends.
    /// After the battle, PlayReplay() re-plays the whole timeline at real-time speed.
    /// </summary>
    public class BattleController : MonoBehaviour
    {
        public BattleContext Context;
        public PlaybackDirector Director;
        public PlayerInputProvider Input;
        public int Seed = 12345;

        [Header("Unity-backed services")]
        public bool UseUnityServices = true;
        [Tooltip("Layers of STATIC level geometry that blocks sight. Do not include unit layers.")]
        public LayerMask EnvironmentMask = ~0;

        public BattleSimulation Sim { get; private set; }

        /// <summary>Freedom's Embrace (a living unit with the freedoms_embrace passive): how far the order clock has run, 0..1,
        /// while the world is held for orders (0 otherwise). The screen distortion follows it.</summary>
        public float EmbraceProgress { get; private set; }
        /// <summary>Phase two (unbound_time): time creeps on while orders are given. CreepProgress: how far the creep has
        /// ramped up this time, 0..1.</summary>
        public bool TimeUnbound { get; private set; }
        public float CreepProgress { get; private set; }
        public event System.Action<BattleOutcome> OnBattleFinished;
        /// <summary>A battle's simulation was just made, before it runs a single tick (a tutorial sets its rules here).</summary>
        public event System.Action<BattleSimulation> OnSimCreated;

        [Tooltip("Start as soon as the scene plays. Off when a PreBattleSetup screen starts it instead.")]
        public bool StartOnPlay = true;

        private void Start() { if (StartOnPlay) StartBattle(); }

        public void StartBattle()
        {
            var state = Context.BuildBattleState();

            IStaticOcclusion staticOcclusion = null;
            IPathService paths = null;
            if (UseUnityServices)
            {
                staticOcclusion = new PhysicsStaticOcclusion(EnvironmentMask);
                paths = new NavMeshPathService();
            }

            Sim = new BattleSimulation(state, Seed, staticOcclusion, paths);
            var tuning = Resources.Load<BattleTuning>(BattleTuning.ResourceName);   // battle rule numbers (Tools > Combat Test > Tuning & Text)
            if (tuning != null) tuning.ApplyTo(Sim);
            if (OnSimCreated != null) OnSimCreated(Sim);
            var distortion = GetComponent<DeliveranceDistortion>();
            if (distortion == null) distortion = gameObject.AddComponent<DeliveranceDistortion>();
            distortion.Controller = this;
            var summons = GetComponent<SummonViews>();
            if (summons == null) summons = gameObject.AddComponent<SummonViews>();
            summons.Bind(Director, Context);   // views for units that join mid-battle; before Load, so a reset clears them
            Director.Load(Sim.Timeline, Context.Registry);
            foreach (var view in Context.Registry.Values) view.Director = Director;
            StartCoroutine(BattleLoop());
        }

        // Freedom's Embrace: real seconds its order clock has run. Plain moves that run for less than
        // Sim.FreedomsEmbraceResetAfter before the next call for orders leave it running. Any other order starts it again the
        // moment it is given, however short (an ability, Block or a Dash whose targeting was just finished); so does a longer
        // move, or the clock running out. onlyMoves: every squad order given in the last hold was a Move.
        private float embraceClock;
        private bool onlyMoves;

        private void NoteOrder(UnitCommand cmd)
        {
            if (cmd.Type == CommandType.Move) return;
            onlyMoves = false;
            embraceClock = 0f;
        }

        private IEnumerator BattleLoop()
        {
            embraceClock = 0f;
            onlyMoves = true;
            Sim.CommandSubmitted += NoteOrder;
            while (true)
            {
                float ordersAt = Sim.Now;
                SimSegmentResult seg = Sim.RunUntilDecision();   // instant: no waiting on animation
                if (!onlyMoves || seg.Time - ordersAt >= Sim.FreedomsEmbraceResetAfter) embraceClock = 0f;
                onlyMoves = true;

                Director.PlayUntil(seg.Time);                     // present that stretch at playbackSpeed
                yield return new WaitUntil(() => Director.ReachedTarget);

                if (seg.End == SegmentEnd.BattleOver) break;
                if (seg.End != SegmentEnd.DecisionNeeded) continue;

                bool done = false;
                Input.RequestDecisions(Sim, seg.Decisions, () => done = true);
                yield return HoldForOrders(seg, () => done);
                EmbraceProgress = 0f; CreepProgress = 0f;
                if (!done) Input.CancelDecisions();   // ended from outside (the dev menu), or taken by Freedom's Embrace: the next segment plays on
            }

            Sim.CommandSubmitted -= NoteOrder;
            Director.ReleaseTimescale();   // results screen runs at normal engine time
            if (OnBattleFinished != null) OnBattleFinished(Sim.Outcome);
        }

        /// <summary>
        /// The world held for orders until they are in. With Freedom's Embrace on the field the hold has a real-time clock
        /// (Sim.FreedomsEmbraceSeconds), carried over from the last hold when the orders since were
        /// only moves that ran under Sim.FreedomsEmbraceResetAfter: when it runs out, the orders are dropped and the boss takes the moment
        /// (UnleashFreedomsEmbrace). With time unbound (phase two) it isn't held at all: it creeps on at Sim.DecisionCreepRate,
        /// played as it goes, while every decision is an order (a dodge's direction still holds it).
        /// </summary>
        private IEnumerator HoldForOrders(SimSegmentResult seg, System.Func<bool> done)
        {
            float held = 0f;   // this hold's real seconds (the creep ramps over them)
            bool canCreep = seg.Decisions != null && seg.Decisions.TrueForAll(d => d.Kind == DecisionKind.Action);
            while (!done() && Sim.Outcome == BattleOutcome.InProgress)
            {
                yield return null;
                if (Director.Paused) continue;   // a tutorial popup being read stops every clock
                held += Time.unscaledDeltaTime;
                var embrace = Sim.FreedomsEmbraceHolder();
                if (embrace != null) embraceClock += Time.unscaledDeltaTime;
                EmbraceProgress = embrace != null ? Mathf.Clamp01(embraceClock / Mathf.Max(0.1f, Sim.FreedomsEmbraceSeconds)) : 0f;
                if (embrace != null && embraceClock >= Sim.FreedomsEmbraceSeconds && !done())
                {
                    embraceClock = 0f;
                    Sim.UnleashFreedomsEmbrace(embrace);
                    yield break;   // the loop drops the orders and plays on
                }
                TimeUnbound = Sim.DecisionCreepActive;
                if (TimeUnbound && canCreep)
                {
                    float rate = Sim.DecisionCreepRate(held);
                    CreepProgress = Mathf.Clamp01(held / Mathf.Max(0.01f, Sim.DecisionCreepRamp));
                    Sim.CreepWhileDeciding(Time.unscaledDeltaTime * rate);
                    Director.CreepTo(Sim.Now, rate);
                }
            }
        }

        /// <summary>Call after the battle ends (e.g. from a results-screen button).</summary>
        public void PlayReplay() { Director.PlayReplay(1f); }
    }
}
