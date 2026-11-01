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
        public event System.Action<BattleOutcome> OnBattleFinished;

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
            Director.Load(Sim.Timeline, Context.Registry);
            foreach (var view in Context.Registry.Values) view.Director = Director;
            StartCoroutine(BattleLoop());
        }

        private IEnumerator BattleLoop()
        {
            while (true)
            {
                SimSegmentResult seg = Sim.RunUntilDecision();   // instant: no waiting on animation

                Director.PlayUntil(seg.Time);                     // present that stretch at playbackSpeed
                yield return new WaitUntil(() => Director.ReachedTarget);

                if (seg.End == SegmentEnd.BattleOver) break;
                if (seg.End != SegmentEnd.DecisionNeeded) continue;

                bool done = false;
                Input.RequestDecisions(Sim, seg.Decisions, () => done = true);
                yield return new WaitUntil(() => done);
            }

            Director.ReleaseTimescale();   // results screen runs at normal engine time
            if (OnBattleFinished != null) OnBattleFinished(Sim.Outcome);
        }

        /// <summary>Call after the battle ends (e.g. from a results-screen button).</summary>
        public void PlayReplay() { Director.PlayReplay(1f); }
    }
}
