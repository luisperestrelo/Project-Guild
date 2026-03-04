using UnityEngine;
using ProjectGuild.Data;

namespace ProjectGuild.View
{
    /// <summary>
    /// Editor-only gizmo visualization of overworld node layout.
    /// Reads the WorldMapAsset and draws gizmos for node positions, approach radii,
    /// and entrance offsets so you can see the overworld layout without entering Play mode.
    ///
    /// Disables itself in Play mode (VisualSyncSystem handles runtime visuals).
    /// </summary>
    [ExecuteAlways]
    public class OverworldPreview : MonoBehaviour
    {
        [Header("Data")]
        [Tooltip("The world map to preview. Drag the same WorldMapAsset used by SimulationRunner.")]
        [SerializeField] private WorldMapAsset _worldMapAsset;

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (_worldMapAsset == null || _worldMapAsset.Nodes == null) return;

            foreach (var node in _worldMapAsset.Nodes)
            {
                if (node == null) continue;

                Vector3 nodeCenter = TerrainHeightSampler.GetPositionOnTerrain(node.WorldX, node.WorldZ);

                if (node.IsEntranceNode)
                {
                    // Entrance node: green sphere at entrance + line from center
                    Vector3 entrancePos = new Vector3(
                        node.WorldX + node.EntranceOffset.x,
                        0f,
                        node.WorldZ + node.EntranceOffset.z);
                    entrancePos.y = TerrainHeightSampler.GetHeight(entrancePos.x, entrancePos.z)
                                    + node.EntranceOffset.y;

                    Gizmos.color = Color.green;
                    Gizmos.DrawWireSphere(entrancePos, 0.6f);
                    Gizmos.DrawLine(nodeCenter, entrancePos);
                }
                else
                {
                    // Area node: cyan wire disc at approach radius
                    UnityEditor.Handles.color = Color.cyan;
                    UnityEditor.Handles.DrawWireDisc(nodeCenter, Vector3.up, node.ApproachRadius);
                }

                // Node center marker
                Gizmos.color = Color.white;
                Gizmos.DrawWireSphere(nodeCenter, 0.3f);

                // Label
                UnityEditor.Handles.color = Color.yellow;
                UnityEditor.Handles.Label(nodeCenter + Vector3.up * 2f, node.Name);
            }
        }
#endif
    }
}
