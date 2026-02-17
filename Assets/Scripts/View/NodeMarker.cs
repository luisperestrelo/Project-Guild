using UnityEngine;

namespace ProjectGuild.View
{
    /// <summary>
    /// Identifies a node marker GameObject so raycasting can determine
    /// which world node was clicked. Attached by VisualSyncSystem.CreateNodeMarker().
    /// </summary>
    public class NodeMarker : MonoBehaviour
    {
        public string NodeId { get; private set; }

        public void Initialize(string nodeId)
        {
            NodeId = nodeId;
        }
    }
}
