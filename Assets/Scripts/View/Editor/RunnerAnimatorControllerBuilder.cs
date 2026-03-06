using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace ProjectGuild.View
{
    /// <summary>
    /// Editor utility that creates Animator Controllers for runners with locomotion
    /// and combat states. Uses Synty animation clips.
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

        // Combat animation clip paths (shared between masculine/feminine, generic humanoid)
        private const string CombatIdlePath =
            "Assets/Art/Synty/AnimationSwordCombat/Animations/Polygon/Idle/Menacing01/A_Idle_Menacing01_Sword.fbx";
        private const string AttackPath =
            "Assets/Art/Synty/AnimationSwordCombat/Animations/Polygon/Attack/LightCombo01/A_Attack_LightCombo01A_Sword.fbx";
        private const string HitReactPath =
            "Assets/Art/Synty/AnimationSwordCombat/Animations/Polygon/Hit/HitReact/A_Hit_F_React_Sword.fbx";
        private const string DeathPath =
            "Assets/Art/Synty/AnimationSwordCombat/Animations/Polygon/Death/A_Death_F_01_Sword.fbx";

        // Cast animation: use the roar animation as a stand-in for spellcasting
        private const string CastPath =
            "Assets/Art/Synty/AnimationEmotesAndTaunts/Animations/Polygon/Masculine/Aggressive/A_POLY_EMOT_Aggressive_Roar_High_Masc.fbx";

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
            Debug.Log("[RunnerAnimatorControllerBuilder] Built masculine + feminine runner controllers with combat states.");
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

            // Load combat clips (shared)
            var combatIdleClip = LoadClipFromFbx(CombatIdlePath);
            var attackClip = LoadClipFromFbx(AttackPath);
            var hitReactClip = LoadClipFromFbx(HitReactPath);
            var deathClip = LoadClipFromFbx(DeathPath);
            var castClip = LoadClipFromFbx(CastPath);

            string path = $"{OutputFolder}/{name}.controller";
            var controller = AnimatorController.CreateAnimatorControllerAtPath(path);

            // Parameters
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("InCombat", AnimatorControllerParameterType.Bool);
            controller.AddParameter("IsDead", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Cast", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Hit", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Die", AnimatorControllerParameterType.Trigger);

            var rootSM = controller.layers[0].stateMachine;

            // ─── Locomotion states ───
            var idleState = rootSM.AddState("Idle", new Vector3(300, 0, 0));
            idleState.motion = idleClip;
            rootSM.defaultState = idleState;

            var runState = rootSM.AddState("Run", new Vector3(300, 100, 0));
            runState.motion = runClip;

            // Idle <-> Run
            AddTransition(idleState, runState, "Speed", AnimatorConditionMode.Greater, 0.1f);
            AddTransition(runState, idleState, "Speed", AnimatorConditionMode.Less, 0.1f);

            // ─── Combat states ───
            AnimatorState combatIdleState;
            if (combatIdleClip != null)
            {
                combatIdleState = rootSM.AddState("CombatIdle", new Vector3(600, 0, 0));
                combatIdleState.motion = combatIdleClip;
            }
            else
            {
                combatIdleState = rootSM.AddState("CombatIdle", new Vector3(600, 0, 0));
                combatIdleState.motion = idleClip;
            }

            // Idle -> CombatIdle (enter combat)
            AddTransition(idleState, combatIdleState, "InCombat", AnimatorConditionMode.If, 0f);
            AddTransition(runState, combatIdleState, "InCombat", AnimatorConditionMode.If, 0f);
            // CombatIdle -> Idle (exit combat)
            AddTransition(combatIdleState, idleState, "InCombat", AnimatorConditionMode.IfNot, 0f);

            // Attack state
            if (attackClip != null)
            {
                var attackState = rootSM.AddState("Attack", new Vector3(600, 100, 0));
                attackState.motion = attackClip;

                var toAttack = combatIdleState.AddTransition(attackState);
                toAttack.AddCondition(AnimatorConditionMode.If, 0f, "Attack");
                toAttack.duration = 0.1f;
                toAttack.hasExitTime = false;

                // Return to combat idle after attack finishes
                var fromAttack = attackState.AddTransition(combatIdleState);
                fromAttack.hasExitTime = true;
                fromAttack.exitTime = 0.9f;
                fromAttack.duration = 0.1f;
            }

            // Cast state
            if (castClip != null)
            {
                var castState = rootSM.AddState("Cast", new Vector3(600, 200, 0));
                castState.motion = castClip;

                var toCast = combatIdleState.AddTransition(castState);
                toCast.AddCondition(AnimatorConditionMode.If, 0f, "Cast");
                toCast.duration = 0.1f;
                toCast.hasExitTime = false;

                var fromCast = castState.AddTransition(combatIdleState);
                fromCast.hasExitTime = true;
                fromCast.exitTime = 0.9f;
                fromCast.duration = 0.1f;
            }

            // Hit react state
            if (hitReactClip != null)
            {
                var hitState = rootSM.AddState("HitReact", new Vector3(600, 300, 0));
                hitState.motion = hitReactClip;

                var toHit = combatIdleState.AddTransition(hitState);
                toHit.AddCondition(AnimatorConditionMode.If, 0f, "Hit");
                toHit.duration = 0.05f;
                toHit.hasExitTime = false;

                var fromHit = hitState.AddTransition(combatIdleState);
                fromHit.hasExitTime = true;
                fromHit.exitTime = 0.8f;
                fromHit.duration = 0.1f;
            }

            // Death state
            if (deathClip != null)
            {
                var deathState = rootSM.AddState("Death", new Vector3(300, 300, 0));
                deathState.motion = deathClip;

                // Any state -> Death
                var toDeath = rootSM.AddAnyStateTransition(deathState);
                toDeath.AddCondition(AnimatorConditionMode.If, 0f, "Die");
                toDeath.duration = 0.1f;
                toDeath.hasExitTime = false;

                // Death -> Idle when no longer dead (respawn)
                var fromDeath = deathState.AddTransition(idleState);
                fromDeath.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsDead");
                fromDeath.duration = 0.2f;
                fromDeath.hasExitTime = false;
            }

            EditorUtility.SetDirty(controller);
        }

        private static void AddTransition(AnimatorState from, AnimatorState to,
            string param, AnimatorConditionMode mode, float threshold)
        {
            var t = from.AddTransition(to);
            t.AddCondition(mode, threshold, param);
            t.duration = 0.15f;
            t.hasExitTime = false;
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
