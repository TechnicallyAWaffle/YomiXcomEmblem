using System.Collections.Generic;
using UnityEngine;

namespace TacticsGame.Core
{
    /// <summary>
    /// A special state a unit is in beyond its phase: holding someone, being held, lifted or hovering, in an aerial slam,
    /// channeling, in a Mesmerize counter. Kept on Unit.States. The simulation asks every state what it means for the unit
    /// (in the air? can it be teleported?) and tells every state what happens to it (a hit, a death, a shove), so a new
    /// kind of state declares its rules here instead of adding a check to each of those places.
    /// Each state's own running (its tick) stays with the mechanic in BattleSimulation.
    /// </summary>
    public abstract class ControlState
    {
        /// <summary>The unit it is on (set by Unit.AddState).</summary>
        public Unit Unit;

        /// <summary>Held up in the air: nothing shoves it, melee can't reach it, and it can't walk or use moving abilities (Unit.IsLifted).</summary>
        public virtual bool Airborne { get { return false; } }
        /// <summary>A dodge doesn't dash it away (it dodges where it stands).</summary>
        public virtual bool BlocksDodgeDash { get { return false; } }
        /// <summary>A Mesmerize counter can't take it over.</summary>
        public virtual bool BlocksMesmerCounter { get { return false; } }
        /// <summary>It can't be teleported (Wrinkles of Reality).</summary>
        public virtual bool BlocksTeleport { get { return false; } }

        /// <summary>A hit landed on the unit (it is alive), before the hit's statuses, pushes and interrupts.</summary>
        public virtual void OnStruck(BattleSimulation sim, HitContext hit) { }
        /// <summary>A hit landed, at the point where it breaks what the unit is doing (with charges and windups).</summary>
        public virtual void OnDamaged(BattleSimulation sim, HitContext hit) { }
        /// <summary>The unit died: let go of whatever this involves. States still needed afterwards stay (a slam's partner falls next tick).</summary>
        public virtual void OnDeath(BattleSimulation sim) { }
        /// <summary>The unit is being shoved, pulled or knocked somewhere (BattleSimulation.Relocate).</summary>
        public virtual void OnShoved(BattleSimulation sim) { }
    }

    /// <summary>Subjugate in progress, on the grappler (see BattleSimulation.TickGrapples).</summary>
    public class GrappleState : ControlState
    {
        public string TargetId;
        public ActionDefinition Definition;   // Subjugate: its LinkedAction is the headbutt
        public float NextHeadbuttAt;
        public float EndsAt;                  // the grab's stun runs out: the throw starts
        public Vector3 Facing;                // the held unit is carried this way from the grappler
        public bool Throwing;                 // the throw is winding up
        public bool AwaitingThrow;            // a player's grab ran out: it is asked which way to throw (any order throws)

        public override void OnDeath(BattleSimulation sim) { sim.EndGrapple(Unit); }
    }

    /// <summary>Held by a grappler (Subjugate).</summary>
    public class HeldState : ControlState
    {
        public string HolderId;

        public override void OnDeath(BattleSimulation sim) { sim.ReleaseHeld(Unit); }
        public override void OnShoved(BattleSimulation sim) { sim.ReleaseHeld(Unit); }   // shoved out of the grip
    }

    /// <summary>Lifted (Amblivate): raised From -> To over [Start, RiseEnd), held there, dropped at DropAt (a fall). See BattleSimulation.TickLifts.</summary>
    public class LiftState : ControlState
    {
        public Vector3 From, To;
        public float Start, RiseEnd, DropAt;
        public string SourceId;

        public override bool Airborne { get { return true; } }
        public override void OnDeath(BattleSimulation sim) { Unit.RemoveState(this); }
    }

    /// <summary>
    /// Hovering (Skyward Demise): a lift the unit made itself; it can act, not move. The ability it starts up there ends the
    /// hover once it lands (EndsAfterAction); Statuses come off when the hover ends. Damage knocks it down.
    /// </summary>
    public class HoverState : LiftState
    {
        public bool EndsAfterAction;
        public List<string> Statuses;

        public override void OnStruck(BattleSimulation sim, HitContext hit) { if (hit.Dealt > 0f) sim.EndHover(Unit, true, hit.Source); }
    }

    /// <summary>A channeled ability running (UnitPhase.Channeling; see BattleSimulation.StartChannel).</summary>
    public class ChannelState : ControlState
    {
        public ActionInstance Action;   // what was channeled (its target, behavior)
        public float EndsAt;            // runs out here, then the recovery

        public override bool BlocksDodgeDash { get { return true; } }
        public override bool BlocksMesmerCounter { get { return true; } }
        // Any damage breaks it, unless the unit is Unstoppable or the hit was negated.
        public override void OnDamaged(BattleSimulation sim, HitContext hit)
        {
            if (Unit.Phase == UnitPhase.Channeling && !hit.Negated && !Unit.IsUnstoppable) sim.EndChannel(Unit, "channel broken", !hit.WillStun);
        }
        public override void OnDeath(BattleSimulation sim) { sim.EndChannel(Unit, "death", false); }
    }

    /// <summary>Heretic's Ascent in progress, on the one carrying (see BattleSimulation.StartAerialSlam).</summary>
    public class AerialSlamState : ControlState
    {
        public string HeldId;
        public ActionDefinition Definition;
        public AerialSlamInfo Path;     // the scripted flight both follow (AerialSlamPath)

        public override bool Airborne { get { return true; } }
        public override bool BlocksDodgeDash { get { return true; } }
        public override bool BlocksMesmerCounter { get { return true; } }
        public override bool BlocksTeleport { get { return true; } }
        // Damage before the slam starts knocks both out of the air.
        public override void OnDamaged(BattleSimulation sim, HitContext hit)
        {
            if (!hit.Negated && AerialSlamPath.BeforeSlam(Path, sim.Now)) sim.BreakAerialSlam(Unit, hit.Source);
        }
    }

    /// <summary>Carried up in someone's aerial slam.</summary>
    public class SlamCarriedState : ControlState
    {
        public string CarrierId;

        public override bool Airborne { get { return true; } }
        public override bool BlocksDodgeDash { get { return true; } }
        public override bool BlocksMesmerCounter { get { return true; } }
        public override bool BlocksTeleport { get { return true; } }
    }

    /// <summary>Mesmerize's counter in progress: rapid strikes left at TargetId (each is a decision; anything else ends it).</summary>
    public class MesmerState : ControlState
    {
        public string TargetId;
        public int StrikesLeft;
        public ActionDefinition Strike;

        public override bool BlocksDodgeDash { get { return StrikesLeft > 0; } }   // Mesmerize blinked it to the attacker instead
    }
}
