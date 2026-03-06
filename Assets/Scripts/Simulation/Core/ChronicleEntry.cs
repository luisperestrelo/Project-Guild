namespace ProjectGuild.Simulation.Core
{
    /// <summary>
    /// Direction tag for chronicle entries. Describes the runner's role in the event.
    /// </summary>
    public enum ChronicleDirection
    {
        /// <summary>Non-combat or neutral events (gathering, traveling, leveling).</summary>
        Neutral,
        /// <summary>Runner performed an action (attacked, healed, killed).</summary>
        Outgoing,
        /// <summary>Something happened TO the runner (took damage, died, was healed by another).</summary>
        Incoming,
    }

    /// <summary>
    /// A single entry in the player-facing Chronicle.
    /// Contains human-readable text (e.g., "Kira gathered Copper Ore").
    /// Separate from EventLogEntry which is dev/debug formatted.
    /// </summary>
    public class ChronicleEntry
    {
        public long TickNumber;
        public float GameTime;
        public string RunnerId;
        public string RunnerName;
        public string NodeId;
        public EventCategory Category;
        public string Text;
        public int RepeatCount;
        public string CollapseKey;
        /// <summary>Direction of the event relative to RunnerId.</summary>
        public ChronicleDirection Direction;
        /// <summary>Secondary runner affected by this entry (e.g. heal target). Null for most entries.</summary>
        public string AffectedRunnerId;
    }
}
