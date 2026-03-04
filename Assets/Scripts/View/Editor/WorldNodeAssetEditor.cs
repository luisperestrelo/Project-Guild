using UnityEditor;
using UnityEngine;
using ProjectGuild.Data;

namespace ProjectGuild.View
{
    /// <summary>
    /// Custom inspector for WorldNodeAsset with Scene View handles for visual editing.
    ///
    /// Inspector: Grays out ApproachRadius when IsEntranceNode is true, shows a mode summary
    /// line, and provides a "Focus in Scene View" button.
    ///
    /// Scene View: Draws approach visualization — wire disc + radius handle for area nodes,
    /// position handle + sphere for entrance nodes. All positions are terrain-aware.
    /// </summary>
    [CustomEditor(typeof(WorldNodeAsset))]
    public class WorldNodeAssetEditor : Editor
    {
        private static readonly Color AreaNodeColor = Color.cyan;
        private static readonly Color EntranceNodeColor = Color.green;
        private static readonly Color NodeCenterColor = Color.white;
        private const float CenterDotSize = 0.5f;
        private const float EntranceSphereRadius = 0.6f;
        private const float LabelOffsetY = 2f;

        private void OnEnable()
        {
            SceneView.duringSceneGui += OnSceneGUI;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
        }

        public override void OnInspectorGUI()
        {
            var node = (WorldNodeAsset)target;

            serializedObject.Update();

            // Draw properties up to IsEntranceNode (everything before approach section)
            DrawPropertiesExcluding(serializedObject,
                "IsEntranceNode", "EntranceOffset", "ApproachRadius", "Gatherables");

            // Approach mode section — draw manually for conditional graying.
            // Headers come from [Header] attributes on the SO fields via PropertyField.
            var isEntranceProp = serializedObject.FindProperty("IsEntranceNode");
            var entranceOffsetProp = serializedObject.FindProperty("EntranceOffset");
            var approachRadiusProp = serializedObject.FindProperty("ApproachRadius");

            EditorGUILayout.PropertyField(isEntranceProp);

            EditorGUI.BeginDisabledGroup(!node.IsEntranceNode);
            EditorGUILayout.PropertyField(entranceOffsetProp);
            EditorGUI.EndDisabledGroup();

            EditorGUI.BeginDisabledGroup(node.IsEntranceNode);
            EditorGUILayout.PropertyField(approachRadiusProp);
            EditorGUI.EndDisabledGroup();

            // Mode summary
            EditorGUILayout.Space(2);
            string modeSummary = node.IsEntranceNode
                ? $"Mode: Entrance Node (offset {node.EntranceOffset.x:F1}, {node.EntranceOffset.y:F1}, {node.EntranceOffset.z:F1})"
                : $"Mode: Area Node (radius {node.ApproachRadius:F1}m)";
            EditorGUILayout.HelpBox(modeSummary, MessageType.Info);

            // Gatherables
            var gatherablesProp = serializedObject.FindProperty("Gatherables");
            EditorGUILayout.PropertyField(gatherablesProp, true);

            serializedObject.ApplyModifiedProperties();

            // Focus button
            EditorGUILayout.Space(8);
            if (GUILayout.Button("Focus in Scene View", GUILayout.Height(28)))
            {
                FocusSceneViewOnNode(node);
            }
        }

        private void OnSceneGUI(SceneView sceneView)
        {
            var node = target as WorldNodeAsset;
            if (node == null) return;

            Vector3 nodeCenter = TerrainHeightSampler.GetPositionOnTerrain(node.WorldX, node.WorldZ);

            // Draw node center dot
            Handles.color = NodeCenterColor;
            Handles.DrawSolidDisc(nodeCenter, Vector3.up, CenterDotSize);

            // Draw node name label
            var labelStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                normal = { textColor = Color.white },
                alignment = TextAnchor.MiddleCenter,
                fontSize = 12
            };
            Handles.Label(nodeCenter + Vector3.up * LabelOffsetY, node.Name, labelStyle);

            if (node.IsEntranceNode)
            {
                DrawEntranceNodeHandles(node, nodeCenter);
            }
            else
            {
                DrawAreaNodeHandles(node, nodeCenter);
            }
        }

        private void DrawAreaNodeHandles(WorldNodeAsset node, Vector3 nodeCenter)
        {
            Handles.color = AreaNodeColor;

            // Wire disc at approach radius
            Handles.DrawWireDisc(nodeCenter, Vector3.up, node.ApproachRadius);

            // Radius handle — drag to resize
            float newRadius = Handles.RadiusHandle(Quaternion.identity, nodeCenter, node.ApproachRadius);
            if (!Mathf.Approximately(newRadius, node.ApproachRadius))
            {
                Undo.RecordObject(node, "Change Approach Radius");
                node.ApproachRadius = Mathf.Max(0f, newRadius);
                EditorUtility.SetDirty(node);
            }
        }

        private void DrawEntranceNodeHandles(WorldNodeAsset node, Vector3 nodeCenter)
        {
            // Compute entrance world position
            Vector3 entranceWorld = new Vector3(
                node.WorldX + node.EntranceOffset.x,
                0f,
                node.WorldZ + node.EntranceOffset.z);
            entranceWorld.y = TerrainHeightSampler.GetHeight(entranceWorld.x, entranceWorld.z)
                             + node.EntranceOffset.y;

            // Line from center to entrance
            Handles.color = EntranceNodeColor;
            Handles.DrawDottedLine(nodeCenter, entranceWorld, 4f);

            // Sphere at entrance
            Handles.SphereHandleCap(0, entranceWorld, Quaternion.identity,
                EntranceSphereRadius * 2f, EventType.Repaint);

            // Position handle — drag to move entrance
            EditorGUI.BeginChangeCheck();
            Vector3 newEntranceWorld = Handles.PositionHandle(entranceWorld, Quaternion.identity);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(node, "Move Entrance Offset");

                // Snap Y to terrain at new XZ
                float terrainY = TerrainHeightSampler.GetHeight(newEntranceWorld.x, newEntranceWorld.z);

                node.EntranceOffset = new Vector3(
                    newEntranceWorld.x - node.WorldX,
                    newEntranceWorld.y - terrainY,
                    newEntranceWorld.z - node.WorldZ);

                EditorUtility.SetDirty(node);
            }

            // Distance label at midpoint
            float dist = Vector3.Distance(nodeCenter, entranceWorld);
            Vector3 midpoint = (nodeCenter + entranceWorld) * 0.5f + Vector3.up * 1f;
            Handles.Label(midpoint, $"{dist:F1}m");
        }

        private static void FocusSceneViewOnNode(WorldNodeAsset node)
        {
            Vector3 position = TerrainHeightSampler.GetPositionOnTerrain(node.WorldX, node.WorldZ);
            float viewSize = node.IsEntranceNode
                ? Mathf.Max(node.EntranceOffset.magnitude * 2f, 20f)
                : Mathf.Max(node.ApproachRadius * 3f, 20f);

            SceneView sceneView = SceneView.lastActiveSceneView;
            if (sceneView != null)
            {
                sceneView.LookAt(position, sceneView.rotation, viewSize);
                sceneView.Repaint();
            }
        }
    }
}
