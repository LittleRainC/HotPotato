using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Chardin.Editor
{
    /// <summary>
    /// Materializes the tutorial dialogue in the scene so designers can edit its
    /// RectTransforms, images, and text directly in the Hierarchy/Inspector.
    /// </summary>
    [InitializeOnLoad]
    public static class TutorialDialogueHierarchyBuilder
    {
        const string TutorialSceneName = "Tutorial";

        static TutorialDialogueHierarchyBuilder()
        {
            // Keep waiting while Play Mode is active, then materialize the UI as
            // soon as Unity returns to Edit Mode.
            EditorApplication.update += BuildWhenReady;
        }

        static void BuildWhenReady()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode
                || EditorApplication.isCompiling
                || EditorApplication.isUpdating)
                return;

            EditorApplication.update -= BuildWhenReady;
            BuildForOpenTutorialScene();
        }

        [MenuItem("Tools/Hot Potato/Build Tutorial Dialogue Hierarchy")]
        public static void BuildForOpenTutorialScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || scene.name != TutorialSceneName)
                return;

            TutorialDialogueUI existing = Object.FindObjectOfType<TutorialDialogueUI>(true);
            if (existing != null)
                return;

            Canvas canvas = Object.FindObjectOfType<Canvas>();
            if (canvas == null)
            {
                Debug.LogError("[Tutorial] Cannot build dialogue hierarchy: Canvas missing.");
                return;
            }

            GameObject host = new GameObject("TutorialDialogue", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(host, "Build Tutorial Dialogue Hierarchy");
            host.transform.SetParent(canvas.transform, false);

            TutorialDialogueUI dialogue = host.AddComponent<TutorialDialogueUI>();
            dialogue.InitializeHierarchy();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Selection.activeGameObject = host;
            Debug.Log("[Tutorial] Editable dialogue hierarchy created and saved.");
        }
    }
}
