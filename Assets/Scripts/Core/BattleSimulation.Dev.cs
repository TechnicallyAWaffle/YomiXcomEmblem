using System.Linq;
using UnityEngine;

namespace TacticsGame.Core
{
    /// <summary>
    /// Developer cheats (the ~ dev menu). Each one goes through the timeline like a real hit or heal, so the views and HUD
    /// follow; none of them is used by the game itself.
    /// </summary>
    public partial class BattleSimulation
    {
        /// <summary>Damage to this team's units is dropped (hits still land: stuns, statuses and knockback still apply).</summary>
        public bool DevInvulnerablePlayers;
        /// <summary>Any damage the player's units deal kills outright.</summary>
        public bool DevOneHitKills;
        /// <summary>Scripted battles (the tutorial): the player's units dodge every attack that rolls to hit.</summary>
        public bool PlayersAlwaysDodge;
        /// <summary>Scripted battles: damage can't take a player unit below this share of its max health (0 = off).</summary>
        public float PlayerHealthFloor;

        /// <summary>Kills every living unit on 'team' now, and calls off its reinforcements still to drop in. With no one left on a
        /// side, the battle ends (Outcome is set).</summary>
        public void DevKillTeam(Team team)
        {
            if (Outcome != BattleOutcome.InProgress) return;
            drops.RemoveAll(d => d.Team == team);   // reinforcements still on their way don't come (so Win Battle wins)
            foreach (var u in State.Units.Where(x => x.Team == team && x.IsAlive).ToList()) DevKill(u);
            ProcessDeaths();
        }

        /// <summary>Kills one unit now.</summary>
        public void DevKillUnit(Unit u)
        {
            if (Outcome != BattleOutcome.InProgress || u == null || !u.IsAlive) return;
            DevKill(u);
            ProcessDeaths();
        }

        private void DevKill(Unit u)
        {
            u.SpentOnce.Add(DeliverancePassives.SplitTheMinds);   // a dev kill is final: no next phase
            u.Statuses.RemoveAll(s => s.Has(StatusFlags.Unkillable));
            float amount = Mathf.Max(0f, u.Health);
            u.Health = 0f;
            u.LastAttackerId = null;
            u.LastHitAction = null;
            u.LastHitTag = "dev";
            Emit(EventType.Damage, null, u.Id, new DamageInfo { ActionId = "dev", Amount = amount, Impact = ImpactType.Heavy, RemainingHealth = 0f });
        }

        /// <summary>This team's living units back to full health, no strain, and every cooldown ready.</summary>
        public void DevRestoreTeam(Team team)
        {
            if (Outcome != BattleOutcome.InProgress) return;
            foreach (var u in State.Units)
            {
                if (u.Team != team || !u.IsAlive) continue;
                float heal = u.MaxHealth - u.Health;
                u.Health = u.MaxHealth;
                if (heal > 0f) Emit(EventType.Heal, null, u.Id, heal);
                float strain = u.Strain;
                u.Strain = 0f;
                if (strain > 0f) Emit(EventType.StrainChanged, u.Id, null, new StrainInfo { Amount = -strain, Total = 0f });
                u.CooldownReadyAt.Clear();
            }
        }

        /// <summary>This team's living units down to 1 health (for testing kills and low-health behavior).</summary>
        public void DevWeakenTeam(Team team)
        {
            if (Outcome != BattleOutcome.InProgress) return;
            foreach (var u in State.Units)
            {
                if (u.Team != team || !u.IsAlive || u.Health <= 1f) continue;
                float amount = u.Health - 1f;
                u.Health = 1f;
                Emit(EventType.Damage, null, u.Id, new DamageInfo { ActionId = "dev", Amount = amount, Impact = ImpactType.Light, RemainingHealth = 1f });
            }
        }

        /// <summary>DealDamage's dev overrides: god mode for the player's units, one-hit kills from them.</summary>
        private void DevAdjustDamage(Unit src, Unit target, ref float dealt)
        {
            if (DevInvulnerablePlayers && target.Team == Team.Player) dealt = 0f;
            else if (DevOneHitKills && src != null && src.Team == Team.Player && target.Team != Team.Player && dealt > 0f) dealt = Mathf.Max(dealt, target.Health);
            if (PlayerHealthFloor > 0f && target.Team == Team.Player && dealt > 0f)
                dealt = Mathf.Min(dealt, Mathf.Max(0f, target.Health - PlayerHealthFloor * target.MaxHealth));
        }
    }
}
