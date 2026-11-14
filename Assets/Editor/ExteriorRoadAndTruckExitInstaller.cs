using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Makes the rear access road reach the green outer boundary and aligns the
/// existing truck's straight exit with it. This deliberately leaves every
/// child of the truck untouched, including the hand-tuned visual scale,
/// materials, tarp and cargo anchors.
/// </summary>
public static class ExteriorRoadAndTruckExitInstaller
{
    private static readonly Vector3 RoadCentre = new Vector3(0f, 0.008f, 57.675f);
    private static readonly Vector3 RoadScale = new Vector3(8f, 0.006f, 87.15f);
    // The truck waits just beyond the gate until a round has been completed.
    // Its controller then drives it along the full access road to this exit.
    private static readonly Vector3 TruckWaitingPosition = new Vector3(0.2f, 0f, 25.5f);
    private static readonly Vector3 TruckExitPosition = new Vector3(0.2f, 0f, 99.5f);

    [MenuItem("Factory/Extend Entrance Road and Truck Exit")]
    private static void ExtendEntranceRoadAndTruckExit()
    {
        if (Application.isPlaying)
        {
            EditorUtility.DisplayDialog("Factory yard", "Stop Play mode before updating the road.", "OK");
            return;
        }

        var road = GameObject.Find("Entrance_Road");
        var truck = GameObject.Find("Delivery_Truck_Controller");
        var controller = truck != null ? truck.GetComponent<DeliveryTruckController>() : null;
        if (road == null || truck == null || controller == null)
        {
            EditorUtility.DisplayDialog(
                "Factory yard",
                "Open FactoryYard with its entrance road and delivery truck first.",
                "OK");
            return;
        }

        Undo.RecordObject(road.transform, "Extend entrance road");
        Undo.RecordObject(truck.transform, "Move truck exit");
        Undo.RecordObject(controller, "Set truck exit");

        road.transform.position = new Vector3(RoadCentre.x, road.transform.position.y, RoadCentre.z);
        road.transform.localScale = new Vector3(RoadScale.x, road.transform.localScale.y, RoadScale.z);

        // Change only the controller root. The imported model and all user
        // adjustments beneath it are intentionally preserved as-is.
        truck.transform.position = TruckWaitingPosition;
        controller.exitPosition = TruckExitPosition;

        EditorUtility.SetDirty(road.transform);
        EditorUtility.SetDirty(truck.transform);
        EditorUtility.SetDirty(controller);
        EditorSceneManager.MarkSceneDirty(truck.scene);
        EditorSceneManager.SaveScene(truck.scene);
        Selection.activeGameObject = road;
        Debug.Log("Extended the entrance road and the existing truck's straight exit without changing its visual model.");
    }
}
