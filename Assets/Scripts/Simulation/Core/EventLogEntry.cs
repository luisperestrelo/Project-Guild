namespace ProjectGuild.Simulation.Core
{
    public enum EventCategory
    {
        Warning,
        Automation,
        StateChange,
        Production,
        Lifecycle,
    }

    public class EventLogEntry
    {
        public long TickNumber;
        public EventCategory Category;
        public string RunnerId;
        public string Summary;
        public int RepeatCount;
        public string CollapseKey;
    }
}
