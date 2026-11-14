using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Fits only the lighter-grey playable courtyard slab to the inner edges of
/// the side halls and to their front façade line. It intentionally does not
/// rebuild ground, buildings, roads, fences, or any manually edited detail.
/// </summary>
public static class CourtyardEdgeAlignmentInstaller
{
    // The playable slab spans exactly between the two inward-facing side-hall
    // façades. This was already the approved horizontal extent; only its
    // front/south reach is derived from the current buildings below.
    private const float TargetCourtyardWidth = 43.0f;

    [MenuItem("Factory/Fix Courtyard/Extend Grey Edge to Buildings")]
    private static void ExtendGreyEdgeToBuildings()
    {
        if (Application.isPlaying)
        {
            EditorUtility.DisplayDialog(
                "Courtyard",
                "Stop Play mode before extending the courtyard edge.",
                "OK");
            return;
        }

        var ground = GameObject.Find("Ground_and_Paving")?.transform;
        var courtyard = ground != null ? ground.Find("Gameplay_Concrete_Area") : null;
        if (courtyard == null)
        {
            EditorUtility.DisplayDialog(
                "Courtyard",
                "Open FactoryYard with Gameplay_Concrete_Area first.",
                "OK");
            return;
        }

        var environment = GameObject.Find("Factory_Yard")?.transform.Find("Environment");
        var westWalls = environment != null ? environment.Find("West_Hall/Walls") : null;
        var eastWalls = environment != null ? environment.Find("East_Hall/Walls") : null;
        var westRenderer = westWalls != null ? westWalls.GetComponent<Renderer>() : null;
        var eastRenderer = eastWalls != null ? eastWalls.GetComponent<Renderer>() : null;
        if (westRenderer == null || eastRenderer == null)
        {
            EditorUtility.DisplayDialog(
                "Courtyard",
                "The current side-building walls could not be found.",
                "OK");
            return;
        }

        // X remains at the already-approved façade-to-façade width. Along Z
        // the existing north edge stays where it is; only the south/front
        // edge is extended to the same front line as the side buildings (the
        // red line in the approved top-down layout).
        var frontBuildingEdge = Mathf.Min(westRenderer.bounds.min.z, eastRenderer.bounds.min.z);
        var current = courtyard.localScale;
        var northEdge = courtyard.position.z + current.z * 0.5f;
        var targetDepth = Mathf.Max(0.01f, northEdge - frontBuildingEdge);

        Undo.RecordObject(courtyard, "Fit grey courtyard to buildings");
        courtyard.position = new Vector3(
            0f,
            courtyard.position.y,
            frontBuildingEdge + targetDepth * 0.5f);
        courtyard.localScale = new Vector3(
            TargetCourtyardWidth,
            current.y,
            targetDepth);
        EditorUtility.SetDirty(courtyard);

        EditorSceneManager.MarkSceneDirty(courtyard.gameObject.scene);
        EditorSceneManager.SaveScene(courtyard.gameObject.scene);
        Selection.activeGameObject = courtyard.gameObject;
        Debug.Log("Fitted Gameplay_Concrete_Area to the side walls and their front edge.");
    }
}
