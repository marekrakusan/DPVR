using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Adds the invisible skip-code listener without rebuilding the factory yard
/// or changing any hand-tuned scene object.
/// </summary>
public static class LevelSkipInputInstaller
{
    [MenuItem("Factory/Add Level Skip Codes")]
    private static void AddLevelSkipCodes()
    {
        if (Application.isPlaying)
        {
            EditorUtility.DisplayDialog("Factory yard", "Stop Play mode before adding the skip codes.", "OK");
            return;
        }

        // GameObject.Find only sees active objects. Resolve the scene root
        // directly as well, so this targeted installer also works when the
        // factory root has been temporarily disabled while being edited.
        // The playable content lives on Factory_Yard. The outer FactoryYard
        // object is only the scene container, so prefer the playable root.
        var yard = GameObject.Find("Factory_Yard") ?? GameObject.Find("FactoryYard");
        if (yard == null)
        {
            var roots = SceneManager.GetActiveScene().GetRootGameObjects();
            for (var index = 0; index < roots.Length; index++)
            {
                if (roots[index].name == "Factory_Yard" || roots[index].name == "FactoryYard")
                {
                    yard = roots[index];
                    break;
                }
            }
        }
        if (yard == null)
        {
            EditorUtility.DisplayDialog("Factory yard", "Open FactoryYard before adding the skip codes.", "OK");
            return;
        }

        var listener = yard.GetComponent<LevelSkipInput>();
        if (listener == null)
        {
            listener = Undo.AddComponent<LevelSkipInput>(yard);
        }

        EditorUtility.SetDirty(listener);
        EditorSceneManager.MarkSceneDirty(yard.scene);
        EditorSceneManager.SaveScene(yard.scene);
        Selection.activeGameObject = yard;
        Debug.Log("Added level skip codes: skip2, skip3, skip4 and skip5.");
    }
}
