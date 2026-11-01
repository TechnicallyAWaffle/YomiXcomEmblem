using UnityEngine;

namespace TacticsGame.Presentation
{
    /// <summary>
    /// Hold Alt to see the battle itself: every panel, label and popup hides except the windup bars and the health and
    /// strain bars. Each UI script checks On before drawing what should go.
    /// </summary>
    public static class UiDeclutter
    {
        public static bool On { get { return Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt); } }
    }
}
