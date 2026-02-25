namespace ProjectGuild.Simulation.Core
{
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
    }
}
