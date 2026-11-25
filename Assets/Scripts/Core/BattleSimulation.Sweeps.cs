using System.Collections.Generic;
using UnityEngine;

namespace TacticsGame.Core
{
    /// <summary>
    /// Something that travels out along a line and hits the enemies it reaches on the way (BattleSimulation.StartSweep):
    /// Fissure's crack in the ground, Impel's thrown ally. Its front moves Speed metres a second from Origin along Dir, up
    /// to Range (cut short by a wall), HalfWidth either side of the line. With a Rider (a unit sent flying), the front is
    /// that unit, wherever its glide has it; the sweep ends when the glide does. Each enemy is hit once (OnHit); with
    /// StopAtFirst only the first one reached is, and a Rider stops there.
    /// </summary>
    public class Sweep
    {
        public Unit Source;
        public ActionDefinition Definition;
        public Vector3 Origin, Dir;
        public float Speed = 4f, Range = 10f, HalfWidth = 1f;
        public Unit Rider;
        public bool StopAtFirst;
        public System.Action<BattleSimulation, Unit> OnHit;
        public string FxKind;            // a look the views draw for each stretch the front covers (AbilityFx; null: none)
        public float FxEvery = 0.75f;    // metres per stretch
        internal float Traveled, LastFx;
        internal readonly HashSet<string> Hit = new HashSet<string>();
    }

    public partial class BattleSimulation
    {
        private readonly List<Sweep> sweeps = new List<Sweep>();

        /// <summary>Send 's' on its way from now (its Range cut short at the first wall along its line).</summary>
        public void StartSweep(Sweep s)
        {
            if (s == null || s.Source == null) return;
            s.Dir.y = 0f;
            s.Dir = s.Dir.sqrMagnitude > 1e-6f ? s.Dir.normalized : Vector3.forward;
            Vector3 end = Paths.ClampDash(s.Origin, s.Origin + s.Dir * s.Range);
            Vector3 d = end - s.Origin; d.y = 0f;
            s.Range = Mathf.Min(s.Range, d.magnitude);
            sweeps.Add(s);
        }

        /// <summary>Each tick, after everyone has moved: every sweep's front moves on and hits what it has reached.</summary>
        private void TickSweeps()
        {
            for (int i = sweeps.Count - 1; i >= 0; i--)
            {
                var s = sweeps[i];
                float prev = s.Traveled;
                bool done;
                if (s.Rider != null)
                {
                    Vector3 r = s.Rider.Position - s.Origin; r.y = 0f;
                    s.Traveled = Mathf.Max(prev, Vector3.Dot(r, s.Dir));
                    done = !s.Rider.IsAlive || !IsGliding(s.Rider);
                }
                else
                {
                    s.Traveled = Mathf.Min(s.Range, s.Traveled + s.Speed * TickLength);
                    done = s.Traveled >= s.Range - 1e-4f;
                }
                if (!s.Source.IsAlive && s.Rider == null) done = true;

                float lead = s.Rider != null ? s.Rider.BodyRadius : 0f;
                Unit first = null; float firstAlong = float.MaxValue;
                foreach (var e in new List<Unit>(EnemiesOf(s.Source)))
                {
                    if (!e.IsAlive || s.Hit.Contains(e.Id) || e == s.Rider || IsVoided(e) || !IsTargetableBy(e, s.Source) || e.IsLifted) continue;
                    Vector3 v = e.Position - s.Origin; v.y = 0f;
                    float along = Vector3.Dot(v, s.Dir);
                    if ((v - s.Dir * along).magnitude > s.HalfWidth + e.BodyRadius) continue;
                    if (along > s.Traveled + e.BodyRadius + lead || along < prev - e.BodyRadius - lead) continue;
                    if (!s.StopAtFirst) { s.Hit.Add(e.Id); if (s.OnHit != null) s.OnHit(this, e); continue; }
                    if (along < firstAlong) { firstAlong = along; first = e; }
                }
                if (first != null)
                {
                    s.Hit.Add(first.Id);
                    if (s.Rider != null) StopGlide(s.Rider);
                    if (s.OnHit != null) s.OnHit(this, first);
                    done = true;
                }

                if (s.FxKind != null && (s.Traveled - s.LastFx >= s.FxEvery || (done && s.Traveled > s.LastFx + 0.05f)))
                {
                    EmitFx(s.Source, null, s.FxKind, s.Definition, s.Origin + s.Dir * s.Traveled, s.HalfWidth * 2f, s.Origin + s.Dir * s.LastFx);
                    s.LastFx = s.Traveled;
                }
                if (done) sweeps.RemoveAt(i);
            }
        }
    }
}
