using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Chardin.Editor
{
    [InitializeOnLoad]
    public static class ExplosionHierarchyBuilder
    {
        const string SessionKey = "HotPotato.ExplosionHierarchy.v1";
        static readonly string[] ScenePaths =
        {
            "Assets/Scenes/Tutorial.unity",
            "Assets/Scenes/Level1.unity",
            "Assets/Scenes/Level2.unity",
            "Assets/Scenes/Level3.unity",
            "Assets/Scenes/Level4.unity",
            "Assets/Scenes/Level5.unity"
        };

        static ExplosionHierarchyBuilder()
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
            BuildAll();
            SessionState.SetBool(SessionKey, true);
        }

        [MenuItem("Tools/Hot Potato/Build Explosion Hierarchies")]
        public static void BuildAll()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            Scene original = SceneManager.GetActiveScene();
            string originalPath = original.path;

            foreach (string path in ScenePaths)
            {
                Scene scene = FindLoaded(path);
                bool openedHere = !scene.IsValid();
                if (openedHere)
                    scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);

                BattleHud hud = FindHud(scene);
                if (hud == null)
                {
                    Debug.LogError("[Explosion] BattleHud missing in " + path);
                }
                else
                {
                    hud.EnsureEditableExplosionHierarchy(hud.transform);
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

            Debug.Log("[Explosion] Editable ExplosionAnimation saved to all battle scenes.");
        }

        static Scene FindLoaded(string path)
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (string.Equals(scene.path, path, StringComparison.OrdinalIgnoreCase))
                    return scene;
            }
            return default;
        }

        static BattleHud FindHud(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                BattleHud hud = root.GetComponentInChildren<BattleHud>(true);
                if (hud != null)
                    return hud;
            }
            return null;
        }
    }
}
