using UnityEngine;
using TacticsGame.Core;
using TacticsGame.Presentation;

public class DevInput : MonoBehaviour
{
    public BattleController Controller;
    void Update()
    {
        // Replaying mid-battle would rewind playback under the running battle loop, so only allow it once it's over.
        bool battleOver = Controller.Sim != null && Controller.Sim.Outcome != BattleOutcome.InProgress;
        if (battleOver && Input.GetKeyDown(KeyCode.R)) Controller.PlayReplay();
    }
}
