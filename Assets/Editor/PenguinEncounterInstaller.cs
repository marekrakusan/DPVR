using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Adds or refreshes only the self-contained penguin encounter. It never
/// rebuilds or repositions the player's hand-tuned halls, conveyors, truck or
/// forklift model.
/// </summary>
public static class PenguinEncounterInstaller
{
    private const string PenguinAssetPath = "Assets/FactoryYard/Models/PenguinParachute.fbx";

    [MenuItem("Factory/Add Penguin Encounter")]
    private static void AddPenguinEncounter()
    {
        if (Application.isPlaying)
        {
            EditorUtility.DisplayDialog("Penguin encounter", "Stop Play mode before adding the penguin encounter.", "OK");
            return;
        }

        var yard = GameObject.Find("Factory_Yard");
        var forklift = GameObject.Find("Forklift_Controller")?.GetComponent<ForkliftController>();
        var coordinator = UnityEngine.Object.FindAnyObjectByType<ConveyorDropCoordinator>();
        var penguinPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PenguinAssetPath);
        if (yard == null || forklift == null || coordinator == null || penguinPrefab == null)
        {
            EditorUtility.DisplayDialog(
                "Penguin encounter",
                "Open FactoryYard after importing PenguinParachute.fbx and creating the forklift and conveyor flow.",
                "OK");
            return;
        }

        var existing = yard.transform.Find("Penguin_Encounter");
        if (existing != null)
        {
            Undo.DestroyObjectImmediate(existing.gameObject);
        }

        var encounterRoot = new GameObject("Penguin_Encounter");
        Undo.RegisterCreatedObjectUndo(encounterRoot, "Add penguin encounter");
        encounterRoot.transform.SetParent(yard.transform, false);

        var landingPoints = CreateLandingPoints(encounterRoot.transform);
        var (aPillar, driverSeat) = EnsureForkliftAnchors(forklift.transform);
        var controller = encounterRoot.AddComponent<PenguinEncounterController>();
        controller.coordinator = coordinator;
        controller.forklift = forklift;
        controller.penguinPrefab = penguinPrefab;
        controller.landingPoints = landingPoints.ToArray();
        controller.aPillarAnchor = aPillar;
        controller.driverSeatAnchor = driverSeat;
        controller.triggerDeliveredPackageCount = 15;
        controller.targetLeadTime = 5f;
        controller.landingDelayAfterTriggerPackage = 2f;
        controller.descentDuration = 8.5f;
        controller.landedLifetime = 1f;
        controller.lowerConveyorLandingPause = 5f;
        controller.upperConveyorLandingPause = 5f;
        controller.parachuteStartHeight = 30f;
        controller.visualScaleMultiplier = 2.5f;
        controller.forkliftHitDistance = 1.7f;
        controller.escapeWallClearance = 0.16f;
        controller.escapeSpeed = 6.5f;
        controller.groundClearance = 0.01f;
        controller.parachuteSwayAmplitude = 0.36f;
        controller.parachuteSwayFrequency = 1.1f;
        controller.escapeSwayAngleDegrees = 8f;
        controller.escapeSwayFrequency = 8f;
        controller.escapeJumpDuration = 1.05f;
        controller.escapeGlideDuration = 4.25f;
        controller.escapeArrivalTolerance = 0.08f;
        controller.warningLabel = "Tučňák landing!";
        controller.warningBlinkFrequency = 1.25f;
        controller.forkliftSpeedBonusMultiplier = 1.10f;

        // In the current authored layout route B finishes at the lower zone
        // on both mirrored conveyor systems. Keeping this explicit means a
        // later path relabel does not silently change the landing pause.
        if (coordinator.leftSpawner != null) coordinator.leftSpawner.lowerBranch = ConveyorBranch.B;
        if (coordinator.rightSpawner != null) coordinator.rightSpawner.lowerBranch = ConveyorBranch.B;

        EditorUtility.SetDirty(controller);
        EditorSceneManager.MarkSceneDirty(yard.scene);
        EditorSceneManager.SaveScene(yard.scene);
        Selection.activeGameObject = encounterRoot;
        Debug.Log("Penguin encounter added: one random safe courtyard landing starts after parcel 15 from Level 2 onward.");
    }

    private static List<Transform> CreateLandingPoints(Transform parent)
    {
        var pointsRoot = new GameObject("Reachable_Courtyard_Landing_Points").transform;
        pointsRoot.SetParent(parent, false);

        // These points are all in the unobstructed central forklift courtyard:
        // clear of the side conveyors, hall walls, package landing zones and
        // the later truck manoeuvre near the northern gate.
        var positions = new[]
        {
            new Vector3(-5.5f, 0.026f, -8.0f), new Vector3(-1.8f, 0.026f, -7.2f),
            new Vector3( 2.0f, 0.026f, -7.3f), new Vector3( 5.5f, 0.026f, -7.9f),
            new Vector3(-5.2f, 0.026f, -3.1f), new Vector3(-1.6f, 0.026f, -2.0f),
            new Vector3( 2.1f, 0.026f, -2.4f), new Vector3( 5.1f, 0.026f, -3.3f),
            new Vector3(-5.0f, 0.026f,  2.2f), new Vector3(-1.5f, 0.026f,  3.4f),
            new Vector3( 2.0f, 0.026f,  2.8f), new Vector3( 5.1f, 0.026f,  3.6f),
            new Vector3(-4.7f, 0.026f,  7.9f), new Vector3(-1.4f, 0.026f,  8.8f),
            new Vector3( 2.0f, 0.026f,  8.4f), new Vector3( 4.8f, 0.026f,  8.0f),
        };

        var result = new List<Transform>(positions.Length);
        for (var index = 0; index < positions.Length; index++)
        {
            var point = new GameObject($"Landing_Point_{index + 1:00}").transform;
            point.SetParent(pointsRoot, false);
            point.position = positions[index];
            result.Add(point);
        }

        return result;
    }

    private static (Transform aPillar, Transform driverSeat) EnsureForkliftAnchors(Transform forklift)
    {
        var anchors = forklift.Find("Penguin_Driver_Anchors");
        if (anchors == null)
        {
            anchors = new GameObject("Penguin_Driver_Anchors").transform;
            anchors.SetParent(forklift, false);
        }

        var aPillar = anchors.Find("A_Pillar_Pivot");
        if (aPillar == null)
        {
            aPillar = new GameObject("A_Pillar_Pivot").transform;
            aPillar.SetParent(anchors, false);
            // Front-left cab corner in the controller's local frame. The
            // feature is a visual pivot only and does not alter the model.
        }
        aPillar.localPosition = new Vector3(-0.45f, 1.58f, -0.30f);

        var driverSeat = anchors.Find("Penguin_Driver_Seat");
        if (driverSeat == null)
        {
            driverSeat = new GameObject("Penguin_Driver_Seat").transform;
            driverSeat.SetParent(anchors, false);
        }
        // The model faces local -Z, matching the forklift's visible front.
        // Negative Z moves the driver closer to the steering wheel.
        driverSeat.localPosition = new Vector3(-0.08f, 1.17f, -0.25f);
        driverSeat.localRotation = Quaternion.identity;

        return (aPillar, driverSeat);
    }
}
