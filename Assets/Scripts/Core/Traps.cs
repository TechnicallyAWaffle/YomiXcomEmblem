using System.Collections.Generic;
using UnityEngine;

namespace TacticsGame.Core
{
    /// <summary>
    /// A ground trap: arms after a delay, then springs on the first unit of another team that steps inside it,
    /// applying its statuses (Sharpshooter's Trap: rooted + marked). Pure data in BattleState, ticked by the sim.
    /// </summary>
    public class Trap
    {
        public string Id;
        public string OwnerId;
        public Team OwnerTeam;
        public Vector3 Position;
        public float Radius = 1.2f;
        public float ArmsAt;
        public float ExpiresAt = float.MaxValue;
        public List<StatusSpec> Statuses = new List<StatusSpec>();

        public Trap Clone()
        {
            return new Trap
            {
                Id = Id, OwnerId = OwnerId, OwnerTeam = OwnerTeam, Position = Position, Radius = Radius,
                ArmsAt = ArmsAt, ExpiresAt = ExpiresAt, Statuses = Statuses,
            };
        }
    }
}
