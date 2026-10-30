using System.Collections.Generic;
using UnityEngine;
using TacticsGame.Core;

namespace TacticsGame.Presentation
{
    /// <summary>
    /// Whatever collects the player's orders (UI, hotkeys, gamepad). The BattleController hands it the
    /// pending decisions while the world is held; call sim.SubmitCommand / sim.SubmitDodgeDash for each,
    /// then onComplete(). Build your real UI as a subclass of this (one class per file, file name = class name).
    /// Abstract, so it cannot be added to a GameObject itself; add a concrete subclass instead.
    /// </summary>
    public abstract class PlayerInputProvider : MonoBehaviour
    {
        public abstract void RequestDecisions(BattleSimulation sim, IReadOnlyList<DecisionRequest> requests, System.Action onComplete);
    }
}