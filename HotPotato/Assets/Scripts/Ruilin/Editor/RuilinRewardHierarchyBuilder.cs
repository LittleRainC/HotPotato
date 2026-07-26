using System;
using Chardin;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Ruilin.Editor
{
    [InitializeOnLoad]
    public static class RuilinRewardHierarchyBuilder
    {
        const string SessionKey = "HotPotato.RebuildRewardHierarchy.v2";
        static readonly string[] ScenePaths =
        {
            "Assets/Scenes/Tutorial.unity",
            "Assets/Scenes/Level2.unity",
            "Assets/Scenes/Level3.unity",
            "Assets/Scenes/Level4.unity",
            "Assets/Scenes/Level5.unity"
        };

        static RuilinRewardHierarchyBuilder()
        {
            EditorApplication.update += BuildWhenReady;
        }

        static void BuildWhenReady()
        {
            if (SessionState.GetBool(SessionKey, false))
            {
                EditorApplication.update -= BuildWhenReady;
                return;
            }
            if (EditorApplication.isPlayingOrWillChangePlaymode
                || EditorApplication.isCompiling
                || EditorApplication.isUpdating)
                return;

            EditorApplication.update -= BuildWhenReady;
            RebuildAllScenes();
            SessionState.SetBool(SessionKey, true);
        }

        [MenuItem("Tools/Hot Potato/Rebuild Reward UI Hierarchies")]
        public static void RebuildAllScenes()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            Scene originalActive = SceneManager.GetActiveScene();
            string originalPath = originalActive.path;

            foreach (string path in ScenePaths)
            {
                Scene scene = FindLoadedScene(path);
                bool openedHere = !scene.IsValid();
                if (openedHere)
                    scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);

                BattleController battle = FindBattleController(scene);
                if (battle == null)
                {
                    Debug.LogError("[Ruilin] BattleController missing in " + path);
                }
                else
                {
                    var settlement = battle.GetComponent<RuilinLevel2Settlement>();
                    if (settlement == null)
                        settlement = battle.gameObject.AddComponent<RuilinLevel2Settlement>();
                    settlement.RebuildEditableRewardUi();
                    EditorSceneManager.SaveScene(scene);
                }

                if (openedHere)
                    EditorSceneManager.CloseScene(scene, true);
            }

            if (!string.IsNullOrEmpty(originalPath))
            {
                Scene restored = SceneManager.GetSceneByPath(originalPath);
                if (restored.IsValid())
                    SceneManager.SetActiveScene(restored);
            }

            Debug.Log("[Ruilin] Runtime-style reward UI saved to Tutorial and Level2-Level5.");
        }

        static Scene FindLoadedScene(string path)
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (string.Equals(scene.path, path, StringComparison.OrdinalIgnoreCase))
                    return scene;
            }
            return default;
        }

        static BattleController FindBattleController(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                BattleController found = root.GetComponentInChildren<BattleController>(true);
                if (found != null)
                    return found;
            }
            return null;
        }
    }
}
