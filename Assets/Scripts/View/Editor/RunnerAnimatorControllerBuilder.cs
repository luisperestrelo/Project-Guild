using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace ProjectGuild.View
{
    /// <summary>
    /// Editor utility that creates simple Animator Controllers for runners.
    /// Uses Synty Base Locomotion animation clips with a minimal Idle/Run state machine.
    ///
    /// Menu: Tools > Project Guild > Build Runner Animator Controllers
    /// Creates two controllers (masculine + feminine) at Assets/Art/AnimatorControllers/.
    /// </summary>
    public static class RunnerAnimatorControllerBuilder
    {
        private const string OutputFolder = "Assets/Art/AnimatorControllers";

        // Synty Base Locomotion clip paths (non-root-motion variants)
        private const string MascIdlePath =
            "Assets/Art/Synty/AnimationBaseLocomotion/Animations/Polygon/Masculine/Idle/A_Idle_Standing_Masc.fbx";
        private const string MascRunPath =
            "Assets/Art/Synty/AnimationBaseLocomotion/Animations/Polygon/Masculine/Locomotion/Run/A_Run_F_Masc.fbx";
        private const string FemnIdlePath =
            "Assets/Art/Synty/AnimationBaseLocomotion/Animations/Polygon/Feminine/Idle/A_Idle_Standing_Femn.fbx";
        private const string FemnRunPath =
            "Assets/Art/Synty/AnimationBaseLocomotion/Animations/Polygon/Feminine/Locomotion/Run/A_Run_F_Femn.fbx";

        [MenuItem("Tools/Project Guild/Build Runner Animator Controllers")]
        public static void BuildControllers()
        {
            if (!AssetDatabase.IsValidFolder(OutputFolder))
            {
                if (!AssetDatabase.IsValidFolder("Assets/Art"))
                    AssetDatabase.CreateFolder("Assets", "Art");
                AssetDatabase.CreateFolder("Assets/Art", "AnimatorControllers");
            }

            BuildController("AC_Runner_Masculine", MascIdlePath, MascRunPath);
            BuildController("AC_Runner_Feminine", FemnIdlePath, FemnRunPath);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[RunnerAnimatorControllerBuilder] Built masculine + feminine runner controllers.");
        }

        private static void BuildController(string name, string idleFbxPath, string runFbxPath)
        {
            var idleClip = LoadClipFromFbx(idleFbxPath);
            var runClip = LoadClipFromFbx(runFbxPath);

            if (idleClip == null)
            {
                Debug.LogError($"[RunnerAnimatorControllerBuilder] Could not load idle clip from {idleFbxPath}");
                return;
            }
            if (runClip == null)
            {
                Debug.LogError($"[RunnerAnimatorControllerBuilder] Could not load run clip from {runFbxPath}");
                return;
            }

            string path = $"{OutputFolder}/{name}.controller";
            var controller = AnimatorController.CreateAnimatorControllerAtPath(path);

            // Add Speed parameter (float, 0 = idle, > 0 = running)
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);

            // Get the default layer's state machine
            var rootStateMachine = controller.layers[0].stateMachine;

            // Create Idle state (default)
            var idleState = rootStateMachine.AddState("Idle", new Vector3(300, 0, 0));
            idleState.motion = idleClip;
            rootStateMachine.defaultState = idleState;

            // Create Run state
            var runState = rootStateMachine.AddState("Run", new Vector3(300, 100, 0));
            runState.motion = runClip;

            // Transition: Idle -> Run when Speed > 0.1
            var toRun = idleState.AddTransition(runState);
            toRun.AddCondition(AnimatorConditionMode.Greater, 0.1f, "Speed");
            toRun.duration = 0.15f;
            toRun.hasExitTime = false;

            // Transition: Run -> Idle when Speed < 0.1
            var toIdle = runState.AddTransition(idleState);
            toIdle.AddCondition(AnimatorConditionMode.Less, 0.1f, "Speed");
            toIdle.duration = 0.15f;
            toIdle.hasExitTime = false;

            EditorUtility.SetDirty(controller);
        }

        /// <summary>
        /// Load the first AnimationClip from an FBX file.
        /// Synty FBX files typically contain one clip each.
        /// </summary>
        private static AnimationClip LoadClipFromFbx(string fbxPath)
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath(fbxPath);
            foreach (var asset in assets)
            {
                if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__"))
                    return clip;
            }
            return null;
        }
    }
}
