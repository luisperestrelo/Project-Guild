using System;
using System.Collections.Generic;

namespace ProjectGuild.Simulation.Core
{
    [Serializable]
    public class LogbookState
    {
        public List<LogbookFolder> Folders = new();
    }

    [Serializable]
    public class LogbookFolder
    {
        public string Id;
        public string Name;
        /// <summary>
        /// Non-null for auto-created node folders. Null for player-created custom folders.
        /// Node folders cannot be deleted; custom folders can.
        /// </summary>
        public string NodeId;
        public List<LogbookPage> Pages = new();

        public LogbookFolder() { }

        public LogbookFolder(string name, string nodeId = null)
        {
            Id = Guid.NewGuid().ToString();
            Name = name;
            NodeId = nodeId;
            Pages.Add(new LogbookPage($"{name} Notes"));
        }
    }

    [Serializable]
    public class LogbookPage
    {
        public string Id;
        public string Name;
        public string Content = "";

        public LogbookPage() { }

        public LogbookPage(string name)
        {
            Id = Guid.NewGuid().ToString();
            Name = name;
        }
    }
}
