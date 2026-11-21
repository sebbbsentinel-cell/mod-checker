using System.Collections.Generic;

namespace FanGame.ModChecker
{
    /// <summary>Intern tilstand per spiller.</summary>
    internal sealed class PlayerState
    {
        public string PlayerId;
        public double JoinTime;
        public Dictionary<string, string> Properties = new Dictionary<string, string>();
        public LinkedList<PlayerSnapshot> Snapshots = new LinkedList<PlayerSnapshot>();
        public double? AirborneSince;
        public float AirborneStartY;
        /// <summary>Antall målinger på rad over MaxSpeed.</summary>
        public int SpeedStreak;
        /// <summary>Fartssjekken er av til dette tidspunktet (etter teleport/respawn/lagg-hopp).</summary>
        public double SpeedSuppressUntil;
        /// <summary>Tidspunkter for store enkelthopp (respawn/portal), for å oppdage gjentatte hopp.</summary>
        public Queue<double> JumpTimes = new Queue<double>();

        /// <summary>Tidsstempler for brudd per kode (for "gjentatte samples").</summary>
        public Dictionary<string, Queue<double>> Violations = new Dictionary<string, Queue<double>>();
        /// <summary>Siste begrunnelse per kode.</summary>
        public Dictionary<string, string> LastViolationReason = new Dictionary<string, string>();
        /// <summary>Tidsstempler for nettverkshendelser per type.</summary>
        public Dictionary<NetworkEventType, Queue<double>> Events = new Dictionary<NetworkEventType, Queue<double>>();
        /// <summary>Ukjente RPC-navn denne spilleren har sendt.</summary>
        public HashSet<string> UnknownRpcs = new HashSet<string>();

        public PlayerInfo Info;
        public Verdict LastVerdict = Verdict.CLEAN;

        public PlayerSnapshot Latest => Snapshots.Last?.Value;
    }
}
