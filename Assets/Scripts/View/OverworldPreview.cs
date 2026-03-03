using System.Collections.Generic;
using TMPro;
using UnityEngine;
using ProjectGuild.Data;

namespace ProjectGuild.View
{
    /// <summary>
    /// Editor-only preview of overworld entrance markers and node labels.
    /// Reads the WorldMapAsset and instantiates entrance marker prefabs at node positions
    /// so you can see the overworld layout without entering Play mode.
    ///
    /// All preview objects use HideFlags.DontSave — they exist in the Scene view
    /// but never serialize into the scene file. Use the Refresh button in the Inspector
    /// to rebuild after changing prefab assignments on WorldNodeAssets.
    ///
    /// Disables itself in Play mode (VisualSyncSystem handles runtime visuals).
    /// </summary>
    [ExecuteAlways]
    public class OverworldPreview : MonoBehaviour
    {
        [Header("Data")]
        [Tooltip("The world map to preview. Drag the same WorldMapAsset used by SimulationRunner.")]
        [SerializeField] private WorldMapAsset _worldMapAsset;

        [Header("Options")]
        [Tooltip("Show floating name labels above each marker.")]
        [SerializeField] private bool _showLabels = true;

        private readonly List<GameObject> _previewObjects = new();

        private void OnEnable()
        {
            if (Application.isPlaying) return;
            Rebuild();
        }

        private void OnDisable()
        {
            ClearPreview();
        }

        private void OnValidate()
        {
            if (Application.isPlaying) return;

            // Delay rebuild to next editor update to avoid instantiating during OnValidate
#if UNITY_EDITOR
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this != null && isActiveAndEnabled && !Application.isPlaying)
                    Rebuild();
            };
#endif
        }

        /// <summary>
        /// Tear down and rebuild all preview objects. Called automatically on enable
        /// and when Inspector values change. Can also be called from a custom Inspector button.
        /// </summary>
        public void Rebuild()
        {
            ClearPreview();

            if (_worldMapAsset == null || _worldMapAsset.Nodes == null) return;
            if (Application.isPlaying) return;

            foreach (var nodeAsset in _worldMapAsset.Nodes)
            {
                if (nodeAsset == null) continue;
                CreatePreviewMarker(nodeAsset);
            }
        }

        private void CreatePreviewMarker(WorldNodeAsset nodeAsset)
        {
            float terrainY = TerrainHeightSampler.GetHeight(nodeAsset.WorldX, nodeAsset.WorldZ);
            var position = new Vector3(nodeAsset.WorldX, terrainY, nodeAsset.WorldZ);

            GameObject marker;

            if (nodeAsset.EntranceMarkerPrefab != null)
            {
                marker = Instantiate(nodeAsset.EntranceMarkerPrefab, position, Quaternion.identity);
            }
            else
            {
                // Placeholder cylinder matching VisualSyncSystem's fallback
                marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                marker.transform.position = position;
                marker.transform.localScale = new Vector3(3f, 0.1f, 3f);
            }

            marker.name = $"[Preview] {nodeAsset.Name}";
            marker.hideFlags = HideFlags.DontSave;
            SetDontSaveRecursive(marker);

            if (_showLabels)
            {
                bool isPrefab = nodeAsset.EntranceMarkerPrefab != null;
                float labelHeight = isPrefab ? 4f : 2f;

                var labelObj = new GameObject($"[Preview] Label_{nodeAsset.Name}");
                labelObj.hideFlags = HideFlags.DontSave;
                labelObj.transform.SetParent(marker.transform, worldPositionStays: true);
                labelObj.transform.position = position + new Vector3(0f, labelHeight, 0f);

                // Counteract parent scale so label renders at world scale
                Vector3 parentScale = marker.transform.lossyScale;
                labelObj.transform.localScale = new Vector3(
                    1f / Mathf.Max(parentScale.x, 0.01f),
                    1f / Mathf.Max(parentScale.y, 0.01f),
                    1f / Mathf.Max(parentScale.z, 0.01f));

                var label = labelObj.AddComponent<TextMeshPro>();
                label.text = nodeAsset.Name;
                label.fontSize = 6f;
                label.alignment = TextAlignmentOptions.Center;
                label.color = Color.yellow;
                label.rectTransform.sizeDelta = new Vector2(8f, 2f);
            }

            _previewObjects.Add(marker);
        }

        private void ClearPreview()
        {
            foreach (var obj in _previewObjects)
            {
                if (obj != null)
                    DestroyImmediate(obj);
            }
            _previewObjects.Clear();
        }

        private static void SetDontSaveRecursive(GameObject obj)
        {
            obj.hideFlags = HideFlags.DontSave;
            foreach (Transform child in obj.transform)
                SetDontSaveRecursive(child.gameObject);
        }
    }
}
