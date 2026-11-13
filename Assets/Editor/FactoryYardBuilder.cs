using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Builds a self-contained factory-yard scene while keeping SampleScene intact.
/// The scene uses the existing conveyor and forklift FBX files from this project.
/// </summary>
public static class FactoryYardBuilder
{
    private const string ConveyorAssetPath = "Assets/Objects/conveyor_unity.fbx";
    private const string ForkliftAssetPath = "Assets/Objects/forklift_unity.fbx";
    private const string DeliveryTruckAssetPath = "Assets/Objects/DeliveryTruck.fbx";
    private const string ScenePath = "Assets/Scenes/FactoryYard.unity";
    private const string MaterialFolder = "Assets/FactoryYard/Materials";
    // The existing FBX uses centimetre file units, whereas this yard is laid out in metres.
    private const float ConveyorScale = 100f;
    // Shared façade values keep regenerated details consistent with the yard.
    private const float GabledRoofRise = 1.75f;
    // Keep the hip connector and its flashing visibly above the main roof at
    // the point where the side halls meet it. Without this, the front half
    // of the diagonal trim can sit inside the main roof after its depth was
    // increased.
    private const float SideRoofJunctionSurfaceLift = 0.04f;
    private const float SideRoofJunctionTrimLift = 0.13f;
    private static readonly Color FactoryWallColor = new Color(0.98f, 0.60f, 0.025f);
    private static readonly Color FactoryWallGrooveColor = new Color(0.72f, 0.36f, 0.012f);
    // Keep the complete roof treatment in one light-grey family.  These
    // materials are unlit so the two roof slopes do not appear to change
    // from light grey at the ridge to dark grey at their outer edges.
    private static readonly Color RoofPanelColor = new Color(0.43f, 0.47f, 0.54f);
    private static readonly Color RoofSeamColor = new Color(0.38f, 0.42f, 0.49f);
    private static readonly Color RoofRidgeColor = new Color(0.32f, 0.36f, 0.43f);
    private static readonly Color SafetyYellowColor = new Color(0.98f, 0.66f, 0.03f);
    private static readonly Color SafetyBlackColor = new Color(0.035f, 0.045f, 0.055f);

    private static readonly Vector3 LeftConveyorOrigin = new Vector3(-11.5f, 0f, 7f);
    private static readonly Vector3 RightConveyorOrigin = new Vector3(11.5f, 0f, 7f);
    private static readonly Vector3 CameraFocusPoint = new Vector3(0f, 2.4f, 4f);
    // Turn the source model toward the loading windows: its two curved outlets then face the yard centre.
    private static readonly Quaternion ConveyorRotation = Quaternion.Euler(0f, 180f, 0f) * Quaternion.Euler(270f, 90f, 0f);

    [MenuItem("Factory/Build Factory Yard Scene")]
    public static void BuildFactoryYard()
    {
        var conveyorAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ConveyorAssetPath);
        var forkliftAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ForkliftAssetPath);

        if (conveyorAsset == null || forkliftAsset == null)
        {
            EditorUtility.DisplayDialog(
                "Factory Yard",
                "The conveyor or forklift FBX could not be found in Assets/Objects.",
                "OK");
            return;
        }

        EnsureFolder("Assets/FactoryYard");
        EnsureFolder(MaterialFolder);

        var concrete = GetMaterial("Concrete", new Color(0.39f, 0.42f, 0.44f), 0.72f, 0f);
        var hallYellow = GetMaterial("Factory_Yellow", FactoryWallColor, 0.42f, 0.05f);
        var roofGray = GetUniformRoofMaterial("Roof_Gray", RoofPanelColor);
        var dark = GetMaterial("Door_And_Window", new Color(0.035f, 0.045f, 0.055f), 0.30f, 0.1f);
        var frame = GetMaterial("Window_Frame", new Color(0.47f, 0.51f, 0.55f), 0.35f, 0.45f);
        var skylight = GetMaterial("Skylight_Glass", new Color(0.52f, 0.68f, 0.78f), 0.88f, 0.12f);
        var hazard = GetMaterial("Safety_Yellow", SafetyYellowColor, 0.35f, 0.1f);
        var cardboard = GetMaterial("Generated_Cardboard", new Color(0.56f, 0.31f, 0.10f), 0.72f, 0f);
        var tape = GetMaterial("Generated_Packing_Tape", new Color(0.96f, 0.80f, 0.45f), 0.46f, 0f);
        EnsureOpaqueBuildingMaterial(hallYellow);
        EnsureOpaqueBuildingMaterial(roofGray);

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var root = new GameObject("Factory_Yard");

        BuildEnvironment(root.transform, concrete, hallYellow, roofGray, dark, frame, skylight, hazard);
        var boxes = new GameObject("Generated_Boxes").transform;
        boxes.SetParent(root.transform);

        var conveyorRoot = new GameObject("Conveyors").transform;
        conveyorRoot.SetParent(root.transform);
        var leftConveyor = AddConveyor(conveyorAsset, "Conveyor_Left", conveyorRoot, LeftConveyorOrigin, false);
        var rightConveyor = AddConveyor(conveyorAsset, "Conveyor_Right_Mirrored", conveyorRoot, RightConveyorOrigin, true);
        HideDemoPackages(leftConveyor);
        HideDemoPackages(rightConveyor);
        EnsureConveyorCollision(leftConveyor);
        EnsureConveyorCollision(rightConveyor);

        AddForklift(forkliftAsset, root.transform);
        EnsureForkliftLoadCounter(root.transform, dark, frame);
        AddRuntimeBoxFlow(root.transform, boxes, cardboard, tape);
        AddLightingAndCamera(root.transform);
        EnsureLevelIntroOverlay(root.transform);
        EnsureLevelOutcomeOverlay(root.transform);
        EnsureGamePauseOverlay(root.transform);
        AddDeliveryTruckWorkflow();
        EnsureFactoryYardInBuildSettings();

        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveScene(scene, ScenePath);
        Selection.activeGameObject = root;
        Debug.Log($"Factory yard scene created at {ScenePath}");
    }

    [MenuItem("Factory/Focus Factory Yard")]
    private static void FocusFactoryYard()
    {
        var root = GameObject.Find("Factory_Yard");
        if (root == null)
        {
            return;
        }

        Selection.activeGameObject = root;
        SceneView.lastActiveSceneView?.FrameSelected();
    }

    [MenuItem("Factory/Focus Left Conveyor")]
    private static void FocusLeftConveyor()
    {
        var conveyor = GameObject.Find("Conveyor_Left");
        if (conveyor == null)
        {
            return;
        }

        Selection.activeGameObject = conveyor;
        SceneView.lastActiveSceneView?.FrameSelected();
    }

    [MenuItem("Factory/Top-Down Layout Preview")]
    private static void TopDownLayoutPreview()
    {
        var sceneView = SceneView.lastActiveSceneView;
        if (sceneView == null)
        {
            return;
        }

        sceneView.LookAt(new Vector3(0f, 0f, 3f), Quaternion.Euler(90f, 0f, 0f), 31f, true, true);
        sceneView.Repaint();
    }

    /// <summary>
    /// Replaces only the visible building treatment in the current yard:
    /// gabled roofs, wall windows and roof skylights. Conveyors, forklift and
    /// their placement are deliberately left untouched.
    /// </summary>
    [MenuItem("Factory/Upgrade Building Roofs and Windows")]
    private static void UpgradeBuildingRoofsAndWindows()
    {
        if (Application.isPlaying)
        {
            EditorUtility.DisplayDialog("Factory Yard", "Stop Play mode before updating the buildings.", "OK");
            return;
        }

        var root = GameObject.Find("Factory_Yard");
        var environment = root != null ? root.transform.Find("Environment") : null;
        if (environment == null)
        {
            EditorUtility.DisplayDialog("Factory Yard", "Open the FactoryYard scene first.", "OK");
            return;
        }

        EnsureFolder("Assets/FactoryYard");
        EnsureFolder(MaterialFolder);
        var hallYellow = GetMaterial("Factory_Yellow", FactoryWallColor, 0.42f, 0.05f);
        var roofGray = GetUniformRoofMaterial("Roof_Gray", RoofPanelColor);
        var dark = GetMaterial("Door_And_Window", new Color(0.035f, 0.045f, 0.055f), 0.30f, 0.1f);
        var frame = GetMaterial("Window_Frame", new Color(0.47f, 0.51f, 0.55f), 0.35f, 0.45f);
        var skylight = GetMaterial("Skylight_Glass", new Color(0.52f, 0.68f, 0.78f), 0.88f, 0.12f);

        EnsureOpaqueBuildingMaterial(hallYellow);
        EnsureOpaqueBuildingMaterial(roofGray);
        ApplyBuildingFacadeMaterials(environment, hallYellow, roofGray);
        AddBuildingDetails(environment, roofGray, dark, frame, skylight);
        // Deliberately leave the main-gate safety frames untouched: their
        // visual treatment is manually maintained in the current scene.
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(root.scene);
        EditorSceneManager.SaveScene(root.scene);
        Selection.activeGameObject = environment.gameObject;
        Debug.Log("Upgraded the factory façades, roofs, skylights and side-hall doors.");
    }

    /// <summary>
    /// Restores only the agreed main-hall window layout. Unlike the complete
    /// façade update, this command preserves every other manually adjusted
    /// building element in the open scene.
    /// </summary>
    [MenuItem("Factory/Restore Main Hall Window Alignment")]
    private static void RestoreMainHallWindowAlignment()
    {
        if (Application.isPlaying)
        {
            EditorUtility.DisplayDialog("Factory Yard", "Stop Play mode before aligning the windows.", "OK");
            return;
        }

        var root = GameObject.Find("Factory_Yard");
        var environment = root != null ? root.transform.Find("Environment") : null;
        var mainHall = environment != null ? environment.Find("North_Main_Hall") : null;
        var walls = mainHall != null ? mainHall.Find("Walls") : null;
        var wallRenderer = walls != null ? walls.GetComponent<Renderer>() : null;
        if (root == null || mainHall == null || wallRenderer == null)
        {
            EditorUtility.DisplayDialog("Factory Yard", "Open the FactoryYard scene first.", "OK");
            return;
        }

        var panelWidth = Mathf.Min(3.15f, wallRenderer.bounds.size.x * 0.085f);
        var targets = GetMainFrontWindowOffsets(wallRenderer.bounds.size, panelWidth);
        var windows = new List<Transform>();
        foreach (var candidate in mainHall.GetComponentsInChildren<Transform>(true))
        {
            if (candidate.name == "Front_Window" && candidate.gameObject.activeInHierarchy && candidate.Find("Glass") != null)
            {
                windows.Add(candidate);
            }
        }

        windows.Sort((left, right) =>
        {
            var leftGlass = left.Find("Glass")?.GetComponent<Renderer>();
            var rightGlass = right.Find("Glass")?.GetComponent<Renderer>();
            return (leftGlass != null ? leftGlass.bounds.center.x : 0f)
                .CompareTo(rightGlass != null ? rightGlass.bounds.center.x : 0f);
        });

        if (windows.Count != targets.Length)
        {
            EditorUtility.DisplayDialog("Factory Yard", "The four current main-hall windows could not be found.", "OK");
            return;
        }

        for (var index = 0; index < windows.Count; index++)
        {
            var glassRenderer = windows[index].Find("Glass")?.GetComponent<Renderer>();
            if (glassRenderer == null)
            {
                continue;
            }

            var targetX = wallRenderer.bounds.center.x + targets[index];
            windows[index].position += new Vector3(targetX - glassRenderer.bounds.center.x, 0f, 0f);
        }

        EditorSceneManager.MarkSceneDirty(root.scene);
        EditorSceneManager.SaveScene(root.scene);
        Selection.activeGameObject = mainHall.gameObject;
        Debug.Log("Restored the main-hall windows to the package-exit alignment.");
    }

    /// <summary>
    /// Re-fits only the two short hip sections where the side roofs meet the
    /// main hall. It deliberately leaves all other building details and
    /// user-positioned scene objects untouched.
    /// </summary>
    [MenuItem("Factory/Refit Side Roof Junctions")]
    private static void RefitSideRoofJunctions()
    {
        if (Application.isPlaying)
        {
            EditorUtility.DisplayDialog("Factory Yard", "Stop Play mode before refitting the roof junctions.", "OK");
            return;
        }

        var root = GameObject.Find("Factory_Yard");
        var environment = root != null ? root.transform.Find("Environment") : null;
        if (root == null || environment == null || !TryGetMainHallWallBounds(environment, out var mainWallBounds))
        {
            EditorUtility.DisplayDialog("Factory Yard", "Open the FactoryYard scene first.", "OK");
            return;
        }

        var updated = 0;
        foreach (var hallName in new[] { "West_Hall", "East_Hall" })
        {
            var hall = environment.Find(hallName);
            var walls = hall != null ? hall.Find("Walls") : null;
            var wallRenderer = walls != null ? walls.GetComponent<Renderer>() : null;
            var connector = FindActiveSideRoofHipConnection(hall);
            if (wallRenderer != null && connector != null && RefitSideRoofHipConnection(connector, wallRenderer.bounds, mainWallBounds))
            {
                updated++;
            }
        }

        if (updated == 0)
        {
            EditorUtility.DisplayDialog("Factory Yard", "The current side-roof junctions could not be found.", "OK");
            return;
        }

        EditorSceneManager.MarkSceneDirty(root.scene);
        EditorSceneManager.SaveScene(root.scene);
        Selection.activeGameObject = environment.gameObject;
        Debug.Log("Refitted the side-roof junctions so their flashing stays visible above the main roof.");
    }

    /// <summary>
    /// Makes the existing halls physically solid without moving their current
    /// layout. The forklift uses a CharacterController, so it will now stop
    /// when it reaches the solid wall or roof colliders.
    /// </summary>
    [MenuItem("Factory/Make Buildings Solid")]
    private static void MakeBuildingsSolid()
    {
        if (Application.isPlaying)
        {
            EditorUtility.DisplayDialog("Factory Yard", "Stop Play mode before making the buildings solid.", "OK");
            return;
        }

        var root = GameObject.Find("Factory_Yard");
        var environment = root != null ? root.transform.Find("Environment") : null;
        if (environment == null)
        {
            EditorUtility.DisplayDialog("Factory Yard", "Open the FactoryYard scene first.", "OK");
            return;
        }

        EnsureFolder("Assets/FactoryYard");
        EnsureFolder(MaterialFolder);
        var hallYellow = GetMaterial("Factory_Yellow", FactoryWallColor, 0.42f, 0.05f);
        var roofGray = GetUniformRoofMaterial("Roof_Gray", RoofPanelColor);
        var dark = GetMaterial("Door_And_Window", new Color(0.035f, 0.045f, 0.055f), 0.30f, 0.1f);
        var frame = GetMaterial("Window_Frame", new Color(0.47f, 0.51f, 0.55f), 0.35f, 0.45f);
        var skylight = GetMaterial("Skylight_Glass", new Color(0.52f, 0.68f, 0.78f), 0.88f, 0.12f);

        EnsureOpaqueBuildingMaterial(hallYellow);
        EnsureOpaqueBuildingMaterial(roofGray);
        ApplyBuildingFacadeMaterials(environment, hallYellow, roofGray);
        AddBuildingDetails(environment, roofGray, dark, frame, skylight);
        EnsureBuildingWallColliders(environment);

        var controllerRoot = GameObject.Find("Forklift_Controller");
        if (controllerRoot != null)
        {
            EnsureForkliftCollision(controllerRoot);
        }

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(root.scene);
        EditorSceneManager.SaveScene(root.scene);
        Selection.activeGameObject = environment.gameObject;
        Debug.Log("Buildings are opaque and solid; the forklift now collides with their walls and roofs.");
    }

    /// <summary>
    /// Restores the north/main hall's original X width after the previous
    /// expansion and doubles its depth along Z. The front façade stays in the
    /// same place, so the truck gate and conveyor loading windows remain aligned.
    /// </summary>
    [MenuItem("Factory/Correct Main Hall: Restore X and Double Z")]
    private static void CorrectMainHallDimensions()
    {
        if (Application.isPlaying)
        {
            EditorUtility.DisplayDialog("Factory Yard", "Stop Play mode before resizing the main hall.", "OK");
            return;
        }

        var root = GameObject.Find("Factory_Yard");
        var environment = root != null ? root.transform.Find("Environment") : null;
        var mainHall = environment != null ? environment.Find("North_Main_Hall") : null;
        var walls = mainHall != null ? mainHall.Find("Walls") : null;
        if (root == null || environment == null || mainHall == null || walls == null)
        {
            EditorUtility.DisplayDialog("Factory Yard", "Open the FactoryYard scene first.", "OK");
            return;
        }

        Undo.RecordObject(walls, "Correct Main Hall Dimensions");
        var wallScale = walls.localScale;
        walls.localScale = new Vector3(wallScale.x * 0.5f, wallScale.y, wallScale.z * 2f);
        // Move the enlarged hall toward its rear so its existing front face,
        // gate and two conveyor entry openings do not move into the courtyard.
        walls.localPosition += new Vector3(0f, 0f, wallScale.z * 0.5f);

        var concreteBase = mainHall.Find("Concrete_Base");
        if (concreteBase != null)
        {
            Undo.RecordObject(concreteBase, "Correct Main Hall Base Dimensions");
            var baseScale = concreteBase.localScale;
            concreteBase.localScale = new Vector3(baseScale.x * 0.5f, baseScale.y, baseScale.z * 2f);
            concreteBase.localPosition += new Vector3(0f, 0f, baseScale.z * 0.5f);
        }

        EnsureFolder("Assets/FactoryYard");
        EnsureFolder(MaterialFolder);
        var roofGray = GetUniformRoofMaterial("Roof_Gray", RoofPanelColor);
        var dark = GetMaterial("Door_And_Window", new Color(0.035f, 0.045f, 0.055f), 0.30f, 0.1f);
        var frame = GetMaterial("Window_Frame", new Color(0.47f, 0.51f, 0.55f), 0.35f, 0.45f);
        var skylight = GetMaterial("Skylight_Glass", new Color(0.52f, 0.68f, 0.78f), 0.88f, 0.12f);

        // Regenerating all façade details also corrects the side skylight angle.
        AddBuildingDetails(environment, roofGray, dark, frame, skylight);
        EditorSceneManager.MarkSceneDirty(root.scene);
        EditorSceneManager.SaveScene(root.scene);
        Selection.activeGameObject = mainHall.gameObject;
        Debug.Log("The main hall X width was restored, its Z depth was doubled, and the fitted roof, windows and skylights were refreshed.");
    }

    /// <summary>
    /// Adds collision volumes to the existing conveyors and a live display
    /// above the truck gate showing the packages currently on the forklift.
    /// </summary>
    [MenuItem("Factory/Make Conveyors Solid and Add Load Counter")]
    private static void MakeConveyorsSolidAndAddLoadCounter()
    {
        if (Application.isPlaying)
        {
            EditorUtility.DisplayDialog("Factory Yard", "Stop Play mode before updating the conveyors.", "OK");
            return;
        }

        var root = GameObject.Find("Factory_Yard");
        var conveyors = root != null ? root.transform.Find("Conveyors") : null;
        if (root == null || conveyors == null)
        {
            EditorUtility.DisplayDialog("Factory Yard", "Open the FactoryYard scene first.", "OK");
            return;
        }

        EnsureConveyorCollision(conveyors.Find("Conveyor_Left")?.gameObject);
        EnsureConveyorCollision(conveyors.Find("Conveyor_Right_Mirrored")?.gameObject);

        EnsureFolder("Assets/FactoryYard");
        EnsureFolder(MaterialFolder);
        var dark = GetMaterial("Door_And_Window", new Color(0.035f, 0.045f, 0.055f), 0.30f, 0.1f);
        var frame = GetMaterial("Window_Frame", new Color(0.47f, 0.51f, 0.55f), 0.35f, 0.45f);
        EnsureForkliftLoadCounter(root.transform, dark, frame);

        EditorSceneManager.MarkSceneDirty(root.scene);
        EditorSceneManager.SaveScene(root.scene);
        Selection.activeGameObject = root;
        Debug.Log("The conveyors are solid and the main-gate load counter is ready (0 / 30).");
    }

    [MenuItem("Factory/Add Delivery Truck Workflow")]
    private static void AddDeliveryTruckWorkflow()
    {
        if (Application.isPlaying)
        {
            EditorUtility.DisplayDialog("Factory Yard", "Stop Play mode before adding the delivery truck.", "OK");
            return;
        }

        var root = GameObject.Find("Factory_Yard");
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(DeliveryTruckAssetPath);
        var coordinator = root != null ? root.transform.Find("Runtime_Box_Flow")?.GetComponent<ConveyorDropCoordinator>() : null;
        var forkliftLoad = GameObject.Find("Forklift_Controller")?.GetComponent<ForkliftLoadArea>();
        var gate = root != null ? root.transform.Find("Environment/Main_Truck_Gate") : null;
        if (root == null || prefab == null || coordinator == null || forkliftLoad == null || gate == null)
        {
            EditorUtility.DisplayDialog("Factory Yard", "Open FactoryYard after importing DeliveryTruck.fbx and creating the parcel flow.", "OK");
            return;
        }

        var hallYellow = GetMaterial("Factory_Yellow", FactoryWallColor, 0.42f, 0.05f);
        EnsureMainGateOpening(root.transform, hallYellow);
        EnsureFolder("Assets/FactoryYard");
        EnsureFolder(MaterialFolder);

        var truck = GameObject.Find("Delivery_Truck_Controller");
        if (truck == null)
        {
            truck = new GameObject("Delivery_Truck_Controller");
            truck.transform.SetParent(root.transform, false);
        }

        // Replace the visual whenever this command runs. It keeps the current
        // scene aligned with the most recently exported Blender truck (for
        // example the DPVR lettering) without touching its route/controller.
        var oldVisual = truck.transform.Find("Delivery_Truck_Visual");
        if (oldVisual != null) Undo.DestroyObjectImmediate(oldVisual.gameObject);
        var visual = (GameObject)PrefabUtility.InstantiatePrefab(prefab, truck.transform);
        visual.name = "Delivery_Truck_Visual";
        visual.transform.localPosition = Vector3.zero;
        visual.transform.localRotation = Quaternion.identity;
        visual.transform.localScale = Vector3.one;

        // The truck starts completely outside the rear of the north hall,
        // points into the courtyard and stops under the opening at the end of
        // every parcel round.  Z=23 put the rear of the earlier 6.65 m model
        // inside the hall, where it could be hidden by the building.
        const float truckStartZ = 25.5f;
        truck.transform.position = new Vector3(0f, 0f, truckStartZ);
        truck.transform.rotation = Quaternion.identity;

        var cargo = truck.transform.Find("Truck_Cargo_Anchor");
        if (cargo == null)
        {
            cargo = new GameObject("Truck_Cargo_Anchor").transform;
            cargo.SetParent(truck.transform, false);
        }
        cargo.localPosition = new Vector3(0f, 1.18f, 1.15f);
        cargo.localRotation = Quaternion.identity;

        var loadingPoint = truck.transform.Find("Truck_Loading_Point");
        if (loadingPoint == null)
        {
            loadingPoint = new GameObject("Truck_Loading_Point").transform;
            loadingPoint.SetParent(truck.transform, false);
        }
        // The cargo bed is loaded from its right-hand side, which faces the
        // open courtyard at the planned left-hand parking position.
        loadingPoint.localPosition = new Vector3(1.40f, 0.48f, 1.15f);
        loadingPoint.localRotation = Quaternion.identity;

        var controller = truck.GetComponent<DeliveryTruckController>();
        if (controller == null) controller = Undo.AddComponent<DeliveryTruckController>(truck);
        controller.coordinator = coordinator;
        controller.forkliftLoad = forkliftLoad;
        controller.truckVisual = truck.transform.Find("Delivery_Truck_Visual");
        if (controller.truckVisual != null)
        {
            // The FBX intentionally retains its Blender material names, but
            // Unity does not create usable material assets for this import.
            // Apply project-owned materials by the stable object names so the
            // orange cab, dark tarp, tyres and white DPVR logo stay coloured
            // after every re-import of the truck.
            ApplyTruckMaterials(controller.truckVisual);

            // The Blender FBX can carry centimetre units even when its scene
            // is authored in metres.  Normalize the imported visual to its
            // intended road-going footprint so it is plainly visible in this
            // yard and fits through the 6.35 m gate.
            NormalizeTruckVisualDimensions(controller.truckVisual);
            AlignTruckVisualOnWheels(controller.truckVisual);

            // The Blender truck may be refined without changing this builder.
            // Fit its collider after it has been placed on the concrete rather
            // than relying on obsolete hard-coded vehicle dimensions.
            FitTruckColliderToVisual(truck, controller.truckVisual);
        }
        controller.cargoAnchor = cargo;
        controller.loadingPoint = loadingPoint;
        controller.cargoCover = FindDescendant(controller.truckVisual, "Cargo_Tarp")?.gameObject;
        controller.cargoPresentation = new[]
        {
            controller.cargoCover,
            FindDescendant(controller.truckVisual, "Tarp_Rib")?.gameObject,
            FindDescendant(controller.truckVisual, "Tarp_Rib.001")?.gameObject,
            FindDescendant(controller.truckVisual, "Tarp_Rib.002")?.gameObject,
            FindDescendant(controller.truckVisual, "Tarp_Rib.003")?.gameObject,
            FindDescendant(controller.truckVisual, "DPVR_Side_1")?.gameObject,
            FindDescendant(controller.truckVisual, "DPVR_Side_2")?.gameObject,
        };
        controller.gate = gate;
        controller.gateEntryPosition = new Vector3(0f, 0f, 10.7f);
        controller.loadingStopPosition = new Vector3(-3f, 0f, 6.5f);
        controller.loadingStopYaw = 50f;
        controller.reverseTurnPosition = new Vector3(3f, 0f, 8.2f);
        controller.gateExitPosition = new Vector3(0.2f, 0f, 14.1f);
        controller.exitPosition = new Vector3(0.2f, 0f, truckStartZ);
        controller.loadingDistance = 1f;

        EnsureLevelIntroOverlay(root.transform);
        controller.outcomeOverlay = EnsureLevelOutcomeOverlay(root.transform);
        EnsureFactoryYardInBuildSettings();

        EditorUtility.SetDirty(controller);
        EditorSceneManager.MarkSceneDirty(root.scene);
        EditorSceneManager.SaveScene(root.scene);
        Selection.activeGameObject = truck;
        Debug.Log("Added delivery truck: it opens the gate only after parcel 30 reaches a landing zone, waits for the forklift, then leaves with its load.");
    }

    private static void NormalizeTruckVisualDimensions(Transform visual)
    {
        if (!TryGetVisualBounds(visual, out var sourceBounds))
        {
            return;
        }

        // Dimensions in the Unity yard: conventional 2.5 m wide, 3.8 m tall
        // and 6.25 m long. The extra cargo height covers the five loaded
        // parcel layers while retaining ample clearance in the 6.35 m-high
        // gate. The model points along Z.
        var targetSize = new Vector3(2.50f, 3.80f, 6.25f);
        var sourceSize = sourceBounds.size;
        visual.localScale = new Vector3(
            targetSize.x / Mathf.Max(0.001f, sourceSize.x),
            targetSize.y / Mathf.Max(0.001f, sourceSize.y),
            targetSize.z / Mathf.Max(0.001f, sourceSize.z));
    }

    private static bool TryGetVisualBounds(Transform visual, out Bounds bounds)
    {
        bounds = new Bounds();
        if (visual == null)
        {
            return false;
        }

        var hasBounds = false;
        foreach (var renderer in visual.GetComponentsInChildren<Renderer>(true))
        {
            if (!renderer.enabled)
            {
                continue;
            }

            if (hasBounds)
            {
                bounds.Encapsulate(renderer.bounds);
            }
            else
            {
                bounds = renderer.bounds;
                hasBounds = true;
            }
        }

        return hasBounds;
    }

    /// <summary>
    /// Places the delivery vehicle by the tyres rather than by whichever
    /// decorative renderer happens to extend lowest. This makes the wheels
    /// meet the concrete reliably after any future Blender re-export.
    /// </summary>
    private static void AlignTruckVisualOnWheels(Transform visual)
    {
        if (visual == null)
        {
            return;
        }

        var hasWheelBounds = false;
        var wheelBounds = new Bounds();
        foreach (var renderer in visual.GetComponentsInChildren<Renderer>(true))
        {
            if (!renderer.enabled || !renderer.gameObject.name.StartsWith("Wheel", System.StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (hasWheelBounds)
            {
                wheelBounds.Encapsulate(renderer.bounds);
            }
            else
            {
                wheelBounds = renderer.bounds;
                hasWheelBounds = true;
            }
        }

        if (hasWheelBounds)
        {
            visual.localPosition += Vector3.up * -wheelBounds.min.y;
        }
        else if (TryGetVisualBounds(visual, out var bounds))
        {
            visual.localPosition += Vector3.up * -bounds.min.y;
        }
    }

    /// <summary>
    /// Makes the controller's collision volume follow the imported truck.
    /// The source model is intentionally kept in Blender, therefore this
    /// avoids coupling the Unity route to a particular mesh size.
    /// </summary>
    private static void FitTruckColliderToVisual(GameObject truck, Transform visual)
    {
        if (truck == null || visual == null)
        {
            return;
        }

        if (!TryGetVisualBounds(visual, out var bounds))
        {
            return;
        }

        var collider = truck.GetComponent<BoxCollider>();
        if (collider == null)
        {
            collider = Undo.AddComponent<BoxCollider>(truck);
        }

        collider.center = truck.transform.InverseTransformPoint(bounds.center);
        collider.size = new Vector3(
            bounds.size.x + 0.08f,
            bounds.size.y + 0.06f,
            bounds.size.z + 0.08f);
        collider.isTrigger = false;
    }

    /// <summary>
    /// Gives the imported Blender truck its authored visual treatment using
    /// material assets owned by this Unity project. The FBX names are stable
    /// and this keeps colours reliable when Unity cannot extract embedded FBX
    /// materials automatically.
    /// </summary>
    private static void ApplyTruckMaterials(Transform visual)
    {
        if (visual == null)
        {
            return;
        }

        var cabOrange = GetMaterial("Truck_Cab_Orange", new Color(0.90f, 0.23f, 0.035f), 0.42f, 0.08f);
        var tarpGray = GetMaterial("Truck_Tarp_Gray", new Color(0.32f, 0.34f, 0.36f), 0.32f, 0.05f);
        var darkMetal = GetMaterial("Truck_Dark_Metal", new Color(0.09f, 0.12f, 0.15f), 0.48f, 0.58f);
        var tyre = GetMaterial("Truck_Tyre", new Color(0.018f, 0.022f, 0.028f), 0.18f, 0.02f);
        var glass = GetMaterial("Truck_Glass", new Color(0.045f, 0.14f, 0.22f), 0.82f, 0.10f);
        var headlamp = GetMaterial("Truck_Headlamp", new Color(1f, 0.72f, 0.24f), 0.72f, 0.05f);
        var logo = GetMaterial("Truck_DPVR_White", new Color(0.96f, 0.98f, 1f), 0.48f, 0f);

        foreach (var renderer in visual.GetComponentsInChildren<Renderer>(true))
        {
            var objectName = renderer.gameObject.name.ToLowerInvariant();
            Material assignedMaterial;

            if (objectName.Contains("dpvr"))
            {
                assignedMaterial = logo;
            }
            else if (objectName.Contains("cargo_tarp"))
            {
                assignedMaterial = tarpGray;
            }
            else if (objectName.StartsWith("wheel"))
            {
                assignedMaterial = tyre;
            }
            else if (objectName.Contains("headlight"))
            {
                assignedMaterial = headlamp;
            }
            else if (objectName.Contains("windshield"))
            {
                assignedMaterial = glass;
            }
            else if (objectName.Contains("chassis") ||
                     objectName.Contains("bumper") ||
                     objectName.Contains("grille") ||
                     objectName.Contains("hub") ||
                     objectName.Contains("mirror") ||
                     objectName.Contains("tarp_rib") ||
                     objectName.Contains("interior") ||
                     objectName.Contains("dashboard") ||
                     objectName.Contains("steering"))
            {
                assignedMaterial = darkMetal;
            }
            else
            {
                // The cab mesh and Cargo_Base are both deliberately painted
                // orange. It also makes unnamed cab submeshes safely colourful.
                assignedMaterial = cabOrange;
            }

            AssignAllMaterialSlots(renderer, assignedMaterial);
        }
    }

    private static void AssignAllMaterialSlots(Renderer renderer, Material material)
    {
        if (renderer == null || material == null)
        {
            return;
        }

        var slotCount = Mathf.Max(1, renderer.sharedMaterials.Length);
        var slots = new Material[slotCount];
        for (var index = 0; index < slots.Length; index++)
        {
            slots[index] = material;
        }

        renderer.sharedMaterials = slots;
    }

    private static void EnsureMainGateOpening(Transform root, Material wallMaterial)
    {
        var environment = root.Find("Environment");
        var walls = environment != null ? environment.Find("North_Main_Hall/Walls") : null;
        var wallRenderer = walls != null ? walls.GetComponent<Renderer>() : null;
        if (walls == null || wallRenderer == null)
        {
            return;
        }

        var bounds = wallRenderer.bounds;
        wallRenderer.enabled = false;
        var oldWallCollider = walls.GetComponent<Collider>();
        if (oldWallCollider != null) oldWallCollider.enabled = false;

        var previous = environment.Find("Main_Hall_Gate_Opening");
        if (previous != null) Object.DestroyImmediate(previous.gameObject);
        var opening = new GameObject("Main_Hall_Gate_Opening").transform;
        opening.SetParent(environment, false);

        const float openingWidth = 6.35f;
        const float openingHeight = 6.45f;
        const float frontDepth = 0.36f;
        var sideWidth = (bounds.size.x - openingWidth) * 0.5f;
        var frontZ = bounds.min.z + frontDepth * 0.5f - 0.05f;
        CreateCube("Gate_Wall_Left", opening,
            new Vector3(bounds.center.x - openingWidth * 0.5f - sideWidth * 0.5f, bounds.center.y, bounds.center.z),
            new Vector3(sideWidth, bounds.size.y, bounds.size.z), wallMaterial);
        CreateCube("Gate_Wall_Right", opening,
            new Vector3(bounds.center.x + openingWidth * 0.5f + sideWidth * 0.5f, bounds.center.y, bounds.center.z),
            new Vector3(sideWidth, bounds.size.y, bounds.size.z), wallMaterial);
        CreateCube("Gate_Wall_Above", opening,
            new Vector3(bounds.center.x, bounds.max.y - (bounds.size.y - openingHeight) * 0.5f, frontZ),
            new Vector3(openingWidth, bounds.size.y - openingHeight, frontDepth), wallMaterial);
    }

    /// <summary>
    /// Refreshes only the runtime parcel paths in the open factory scene.
    /// It deliberately leaves the halls, forklift and conveyor placement intact.
    /// </summary>
    [MenuItem("Factory/Apply Updated Conveyor Layout")]
    private static void ApplyUpdatedConveyorLayout()
    {
        if (Application.isPlaying)
        {
            EditorUtility.DisplayDialog("Factory Yard", "Stop Play mode before updating the conveyor layout.", "OK");
            return;
        }

        var root = GameObject.Find("Factory_Yard");
        if (root == null)
        {
            EditorUtility.DisplayDialog("Factory Yard", "Open the FactoryYard scene first.", "OK");
            return;
        }

        var previousFlow = root.transform.Find("Runtime_Box_Flow");
        if (previousFlow != null)
        {
            Undo.DestroyObjectImmediate(previousFlow.gameObject);
        }

        var boxes = root.transform.Find("Generated_Boxes");
        if (boxes == null)
        {
            boxes = new GameObject("Generated_Boxes").transform;
            Undo.RegisterCreatedObjectUndo(boxes.gameObject, "Create generated boxes root");
            boxes.SetParent(root.transform);
        }

        for (var index = boxes.childCount - 1; index >= 0; index--)
        {
            var box = boxes.GetChild(index).gameObject;
            if (box.name.StartsWith("Generated_Box_"))
            {
                Undo.DestroyObjectImmediate(box);
            }
        }

        EnsureFolder("Assets/FactoryYard");
        EnsureFolder(MaterialFolder);
        var cardboard = GetMaterial("Generated_Cardboard", new Color(0.56f, 0.31f, 0.10f), 0.72f, 0f);
        var tape = GetMaterial("Generated_Packing_Tape", new Color(0.96f, 0.80f, 0.45f), 0.46f, 0f);
        AddRuntimeBoxFlow(root.transform, boxes, cardboard, tape);
        EnsureCameraZoom(root.transform);
        RebindDeliveryTruckCoordinator(root.transform);

        EditorSceneManager.MarkSceneDirty(root.scene);
        EditorSceneManager.SaveScene(root.scene);
        Selection.activeGameObject = root;
        Debug.Log("Updated only the conveyor parcel paths for the extended conveyor layout.");
    }

    /// <summary>
    /// Applies the parcel timing rules and camera zoom to the open factory
    /// scene without recreating its existing conveyors or generated objects.
    /// </summary>
    [MenuItem("Factory/Apply Flow Rules and Camera Zoom")]
    private static void ApplyFlowRulesAndCameraZoom()
    {
        if (Application.isPlaying)
        {
            EditorUtility.DisplayDialog("Factory Yard", "Stop Play mode before updating the flow rules.", "OK");
            return;
        }

        var root = GameObject.Find("Factory_Yard");
        if (root == null)
        {
            EditorUtility.DisplayDialog("Factory Yard", "Open the FactoryYard scene first.", "OK");
            return;
        }

        var runtime = root.transform.Find("Runtime_Box_Flow");
        var coordinator = runtime != null ? runtime.GetComponent<ConveyorDropCoordinator>() : null;
        if (coordinator == null)
        {
            EditorUtility.DisplayDialog(
                "Factory Yard",
                "The runtime conveyor flow could not be found. Use Apply Updated Conveyor Layout to create it.",
                "OK");
            return;
        }

        coordinator.minimumDelayBetweenFalls = 4f;
        coordinator.maximumDelayBetweenFalls = 7f;
        coordinator.minimumDispatchGap = 0.55f;
        coordinator.maximumPackagesPerRound = 30;
        var zoom = EnsureCameraZoom(root.transform);

        EditorUtility.SetDirty(coordinator);
        if (zoom != null)
        {
            EditorUtility.SetDirty(zoom);
        }

        EditorSceneManager.MarkSceneDirty(root.scene);
        EditorSceneManager.SaveScene(root.scene);
        Selection.activeGameObject = runtime.gameObject;
        Debug.Log("Applied parcel limits (3 branches, 5 sides, 4–7 s falls) and camera zoom.");
    }

    /// <summary>
    /// Adds mouse-wheel zoom to the existing scene camera without rebuilding
    /// the yard. The camera's present distance becomes its zoom-out limit.
    /// </summary>
    [MenuItem("Factory/Enable Camera Zoom")]
    private static void EnableCameraZoom()
    {
        if (Application.isPlaying)
        {
            EditorUtility.DisplayDialog("Factory Yard", "Stop Play mode before enabling camera zoom.", "OK");
            return;
        }

        var root = GameObject.Find("Factory_Yard");
        if (root == null)
        {
            EditorUtility.DisplayDialog("Factory Yard", "Open the FactoryYard scene first.", "OK");
            return;
        }

        var zoom = EnsureCameraZoom(root.transform);
        if (zoom == null)
        {
            EditorUtility.DisplayDialog("Factory Yard", "The scene camera could not be found.", "OK");
            return;
        }

        EditorUtility.SetDirty(zoom);
        EditorSceneManager.MarkSceneDirty(root.scene);
        EditorSceneManager.SaveScene(root.scene);
        Selection.activeGameObject = zoom.gameObject;
        Debug.Log("Camera zoom enabled: mouse wheel zooms in and out up to the original camera distance.");
    }

    /// <summary>
    /// Adds the forklift keyboard controller to the current scene without
    /// rebuilding the yard or changing its manually adjusted placement.
    /// </summary>
    [MenuItem("Factory/Enable Forklift Controls")]
    private static void EnableForkliftControls()
    {
        if (Application.isPlaying)
        {
            EditorUtility.DisplayDialog("Factory Yard", "Stop Play mode before enabling forklift controls.", "OK");
            return;
        }

        var forklift = GameObject.Find("Forklift_Courtyard_Centre");
        if (forklift == null)
        {
            EditorUtility.DisplayDialog("Factory Yard", "The forklift could not be found in the open scene.", "OK");
            return;
        }

        var controllerRoot = EnsureForkliftControllerRoot(forklift);
        var directController = forklift.GetComponent<ForkliftController>();
        if (directController != null)
        {
            Undo.DestroyObjectImmediate(directController);
        }

        var controller = controllerRoot.GetComponent<ForkliftController>();
        if (controller == null)
        {
            controller = Undo.AddComponent<ForkliftController>(controllerRoot);
        }

        EnsureForkliftCollision(controllerRoot);
        EnsureForkliftLoadArea(controllerRoot);

        EditorUtility.SetDirty(controller);
        EditorSceneManager.MarkSceneDirty(controllerRoot.scene);
        EditorSceneManager.SaveScene(controllerRoot.scene);
        Selection.activeGameObject = controllerRoot;
        Debug.Log("Forklift controls enabled: W/S move along Z, A/D move along X.");
    }

    /// <summary>
    /// Adds the Escape pause menu to the current factory-yard scene without
    /// rebuilding or moving any existing objects.
    /// </summary>
    [MenuItem("Factory/Enable Pause Menu")]
    private static void EnablePauseMenu()
    {
        if (Application.isPlaying)
        {
            EditorUtility.DisplayDialog("Factory Yard", "Stop Play mode before enabling the pause menu.", "OK");
            return;
        }

        var root = GameObject.Find("Factory_Yard");
        if (root == null)
        {
            EditorUtility.DisplayDialog("Factory Yard", "The Factory_Yard root could not be found in the open scene.", "OK");
            return;
        }

        var pause = EnsureGamePauseOverlay(root.transform);
        EditorUtility.SetDirty(pause);
        EditorSceneManager.MarkSceneDirty(root.scene);
        EditorSceneManager.SaveScene(root.scene);
        Selection.activeGameObject = pause.gameObject;
        Debug.Log("Pause menu enabled: Escape opens Continue and Restart; Space restarts while paused.");
    }

    /// <summary>
    /// Adds the 30-package loading area to the current forklift without
    /// changing its placement, its model or the conveyor layout.
    /// </summary>
    [MenuItem("Factory/Enable Forklift Package Loading")]
    private static void EnableForkliftPackageLoading()
    {
        if (Application.isPlaying)
        {
            EditorUtility.DisplayDialog("Factory Yard", "Stop Play mode before enabling forklift package loading.", "OK");
            return;
        }

        var forklift = GameObject.Find("Forklift_Courtyard_Centre");
        if (forklift == null)
        {
            EditorUtility.DisplayDialog("Factory Yard", "The forklift could not be found in the open scene.", "OK");
            return;
        }

        var controllerRoot = EnsureForkliftControllerRoot(forklift);
        var loadArea = EnsureForkliftLoadArea(controllerRoot);

        EditorUtility.SetDirty(loadArea);
        EditorSceneManager.MarkSceneDirty(controllerRoot.scene);
        EditorSceneManager.SaveScene(controllerRoot.scene);
        Selection.activeGameObject = controllerRoot;
        Debug.Log("Forklift package loading enabled: 30 parcels, arranged 2 x 3 x 5.");
    }

    /// <summary>
    /// Adds the exterior factory yard as a self-contained extension. It never
    /// rebuilds the halls, conveyors, forklift, truck or their hand-tuned
    /// transforms; rerunning the command replaces only this named extension.
    /// </summary>
    [MenuItem("Factory/Add Exterior Yard, Fence and Parking")]
    private static void AddExteriorYardFenceAndParking()
    {
        if (Application.isPlaying)
        {
            EditorUtility.DisplayDialog("Factory Yard", "Stop Play mode before expanding the exterior yard.", "OK");
            return;
        }

        var factoryRoot = GameObject.Find("Factory_Yard")?.transform;
        if (factoryRoot == null)
        {
            EditorUtility.DisplayDialog("Factory Yard", "Open the FactoryYard scene first.", "OK");
            return;
        }

        var previous = factoryRoot.Find("Exterior_Yard_Expansion");
        if (previous != null)
        {
            Undo.DestroyObjectImmediate(previous.gameObject);
        }

        EnsureFolder("Assets/FactoryYard");
        EnsureFolder(MaterialFolder);
        var exteriorConcrete = GetMaterial("Exterior_Yard_Concrete", new Color(0.31f, 0.34f, 0.36f), 0.54f, 0.03f);
        var gameplayConcrete = GetMaterial("Gameplay_Yard_Concrete", new Color(0.42f, 0.45f, 0.47f), 0.62f, 0.02f);
        var pavingJoint = GetMaterial("Gameplay_Paving_Joint", new Color(0.29f, 0.32f, 0.34f), 0.34f, 0.03f);
        // Keep the grass unlit and opaque so it stays distinctly green even
        // where the exterior receives little directional light.
        var grass = GetUniformRoofMaterial("Yard_Grass", new Color(0.10f, 0.48f, 0.12f));
        var roadAsphalt = GetMaterial("Yard_Road_Asphalt", new Color(0.12f, 0.14f, 0.16f), 0.44f, 0.04f);
        var fenceMetal = GetMaterial("Yard_Fence_Metal", new Color(0.13f, 0.16f, 0.18f), 0.57f, 0.72f);
        var fenceWire = GetMaterial("Yard_Fence_Wire", new Color(0.31f, 0.35f, 0.38f), 0.50f, 0.78f);
        var parkingAsphalt = GetMaterial("Parking_Asphalt", new Color(0.15f, 0.17f, 0.19f), 0.44f, 0.04f);
        var parkingPaint = GetMaterial("Parking_Paint", new Color(0.92f, 0.94f, 0.95f), 0.35f, 0.02f);
        var cableMaterial = GetMaterial("Cable_Black", new Color(0.025f, 0.03f, 0.035f), 0.40f, 0.12f);
        var reelMaterial = GetMaterial("Cable_Reel_Orange", new Color(0.76f, 0.29f, 0.06f), 0.43f, 0.28f);
        var pipeMaterial = GetMaterial("Pipe_Blue", new Color(0.12f, 0.31f, 0.49f), 0.48f, 0.74f);
        var woodMaterial = GetMaterial("Yard_Pallet_Wood", new Color(0.37f, 0.20f, 0.075f), 0.50f, 0.02f);
        var cardboardMaterial = GetMaterial("Yard_Cardboard", new Color(0.61f, 0.38f, 0.15f), 0.58f, 0.01f);
        var barrelMaterial = GetMaterial("Yard_Barrel_Blue", new Color(0.035f, 0.21f, 0.43f), 0.55f, 0.70f);
        var storageMaterial = GetMaterial("Yard_Storage_Grey", new Color(0.24f, 0.29f, 0.32f), 0.52f, 0.58f);

        var exterior = new GameObject("Exterior_Yard_Expansion").transform;
        Undo.RegisterCreatedObjectUndo(exterior.gameObject, "Add exterior factory yard");
        exterior.SetParent(factoryRoot, false);

        BuildExteriorGround(exterior, exteriorConcrete, gameplayConcrete, pavingJoint, grass, roadAsphalt);
        BuildPerimeterFence(exterior, fenceMetal, fenceWire);
        BuildParkingAreas(exterior, parkingAsphalt, parkingPaint);
        BuildYardProps(
            exterior,
            reelMaterial,
            cableMaterial,
            pipeMaterial,
            woodMaterial,
            cardboardMaterial,
            barrelMaterial,
            storageMaterial);
        EnsureFactoryAudio(factoryRoot);

        EditorSceneManager.MarkSceneDirty(factoryRoot.gameObject.scene);
        EditorSceneManager.SaveScene(factoryRoot.gameObject.scene);
        Selection.activeGameObject = exterior.gameObject;
        Debug.Log("Added exterior yard: solid perimeter fence, parking, paving and industrial props. Existing factory objects were not changed.");
    }

    private static void BuildExteriorGround(
        Transform parent,
        Material exteriorConcrete,
        Material gameplayConcrete,
        Material pavingJoint,
        Material grass,
        Material roadAsphalt)
    {
        var ground = new GameObject("Ground_and_Paving").transform;
        ground.SetParent(parent, false);

        // The green base makes the outside of the fenced yard clearly read as
        // grounds rather than as an endlessly paved industrial slab.
        CreateVisualCube(
            "Exterior_Grass_Ground",
            ground,
            // Match the complete original grey yard slab below it. This makes
            // every exterior area beyond the fenced concrete visibly green.
            new Vector3(0f, 0.002f, 1.5f),
            new Vector3(200f, 0.004f, 200f),
            grass);

        // Concrete belongs inside the fenced factory compound. It stops just
        // before the rear fence so the parking and access road beyond it sit
        // visibly in the green exterior zone.
        CreateVisualCube(
            "Exterior_Concrete_Yard",
            ground,
            new Vector3(0f, 0.005f, -4.325f),
            new Vector3(80f, 0.004f, 50.35f),
            exteriorConcrete);

        // The gate opening continues as a road through the green space, then
        // meets a horizontal road directly alongside both parking areas.
        CreateVisualCube(
            "Entrance_Road",
            ground,
            new Vector3(0f, 0.008f, 27.85f),
            new Vector3(8f, 0.006f, 14.0f),
            roadAsphalt);
        CreateVisualCube(
            "North_Access_Road",
            ground,
            new Vector3(0f, 0.008f, 32.0f),
            new Vector3(200f, 0.006f, 5.50f),
            roadAsphalt);

        // A subtly different inner slab identifies the playable forklift
        // courtyard while leaving the outer factory yard slightly darker.
        var gameplayCentre = new Vector3(0f, 0.009f, 0.9f);
        const float gameplayWidth = 40.5f;
        const float gameplayDepth = 29.0f;
        CreateVisualCube(
            "Gameplay_Concrete_Area",
            ground,
            gameplayCentre,
            new Vector3(gameplayWidth, 0.006f, gameplayDepth),
            gameplayConcrete);

        // Fine 4-metre joints give the centre slab a readable paved texture
        // without making the floor bumpy or affecting vehicle movement.
        var left = gameplayCentre.x - gameplayWidth * 0.5f;
        var right = gameplayCentre.x + gameplayWidth * 0.5f;
        var front = gameplayCentre.z - gameplayDepth * 0.5f;
        var back = gameplayCentre.z + gameplayDepth * 0.5f;
        for (var x = left + 4f; x < right - 0.1f; x += 4f)
        {
            CreateVisualCube(
                $"Gameplay_Joint_X_{x:0}",
                ground,
                new Vector3(x, 0.014f, gameplayCentre.z),
                new Vector3(0.030f, 0.002f, gameplayDepth - 0.16f),
                pavingJoint);
        }

        for (var z = front + 4f; z < back - 0.1f; z += 4f)
        {
            CreateVisualCube(
                $"Gameplay_Joint_Z_{z:0}",
                ground,
                new Vector3(gameplayCentre.x, 0.014f, z),
                new Vector3(gameplayWidth - 0.16f, 0.002f, 0.030f),
                pavingJoint);
        }
    }

    private static void BuildPerimeterFence(Transform parent, Material metal, Material wire)
    {
        var fence = new GameObject("Perimeter_Fence").transform;
        fence.SetParent(parent, false);

        const float left = -40f;
        const float right = 40f;
        const float south = -29.5f;
        const float north = 20.85f;
        const float entranceHalfWidth = 4.5f;

        // The rear boundary follows the main hall's outer wall level, while a
        // 9-metre central opening keeps the delivery-truck entry completely
        // free. The two sections have separate colliders, so the visual gap
        // is also a real route through the fence.
        CreateFenceRun(fence, "Fence_North_West", new Vector3(left, 0f, north), new Vector3(-entranceHalfWidth, 0f, north), metal, wire);
        CreateFenceRun(fence, "Fence_North_East", new Vector3(entranceHalfWidth, 0f, north), new Vector3(right, 0f, north), metal, wire);
        CreateFenceRun(fence, "Fence_East", new Vector3(right, 0f, north), new Vector3(right, 0f, south), metal, wire);
        CreateFenceRun(fence, "Fence_South", new Vector3(right, 0f, south), new Vector3(left, 0f, south), metal, wire);
        CreateFenceRun(fence, "Fence_West", new Vector3(left, 0f, south), new Vector3(left, 0f, north), metal, wire);
    }

    private static void CreateFenceRun(
        Transform parent,
        string name,
        Vector3 start,
        Vector3 end,
        Material metal,
        Material wire)
    {
        var direction = end - start;
        direction.y = 0f;
        var length = direction.magnitude;
        if (length < 0.01f)
        {
            return;
        }

        var run = new GameObject(name).transform;
        run.SetParent(parent, false);
        var centre = (start + end) * 0.5f;
        var rotation = Quaternion.FromToRotation(Vector3.right, direction.normalized);

        CreateYardBeam(run, "Top_Rail", centre + Vector3.up * 2.35f, new Vector3(length + 0.18f, 0.10f, 0.10f), rotation, metal);
        CreateYardBeam(run, "Middle_Rail", centre + Vector3.up * 1.25f, new Vector3(length + 0.18f, 0.055f, 0.055f), rotation, wire);
        CreateYardBeam(run, "Bottom_Rail", centre + Vector3.up * 0.22f, new Vector3(length + 0.18f, 0.08f, 0.08f), rotation, metal);

        var postCount = Mathf.CeilToInt(length / 4f);
        for (var index = 0; index <= postCount; index++)
        {
            var t = index / (float)postCount;
            var position = Vector3.Lerp(start, end, t) + Vector3.up * 1.18f;
            CreateVisualCube($"Post_{index:00}", run, position, new Vector3(0.14f, 2.36f, 0.14f), metal);
        }

        var wireCount = Mathf.FloorToInt(length / 1.05f);
        for (var index = 1; index < wireCount; index++)
        {
            var t = index / (float)wireCount;
            var position = Vector3.Lerp(start, end, t) + Vector3.up * 1.25f;
            CreateVisualCube($"Wire_{index:000}", run, position, new Vector3(0.028f, 1.90f, 0.028f), wire);
        }

        // One continuous, invisible collider per fence side is robust for
        // the forklift's CharacterController and avoids gaps between posts.
        var blocker = new GameObject("Boundary_Collider");
        blocker.transform.SetParent(run, false);
        blocker.transform.position = centre + Vector3.up * 1.15f;
        blocker.transform.rotation = rotation;
        var collider = blocker.AddComponent<BoxCollider>();
        collider.center = Vector3.zero;
        collider.size = new Vector3(length, 2.30f, 0.18f);
    }

    private static void BuildParkingAreas(Transform parent, Material asphalt, Material paint)
    {
        var parking = new GameObject("Parking_Areas").transform;
        parking.SetParent(parent, false);

        // The lots sit beyond the fence, directly below the transverse road.
        // A generous central corridor remains free for the factory entrance.
        // Their outer edges meet the two side fence lines at X = -40 and
        // X = +40, making the lots feel built directly against the perimeter.
        CreateParkingArea(parking, "Parking_West", new Vector3(-29f, 0f, 25.75f), 22f, 7f, asphalt, paint);
        CreateParkingArea(parking, "Parking_East", new Vector3(29f, 0f, 25.75f), 22f, 7f, asphalt, paint);
    }

    private static void CreateParkingArea(
        Transform parent,
        string name,
        Vector3 centre,
        float width,
        float depth,
        Material asphalt,
        Material paint)
    {
        var lot = new GameObject(name).transform;
        lot.SetParent(parent, false);
        CreateVisualCube("Parking_Surface", lot, centre + Vector3.up * 0.008f, new Vector3(width, 0.006f, depth), asphalt);

        var y = 0.016f;
        var xMin = centre.x - width * 0.5f;
        var xMax = centre.x + width * 0.5f;
        var zMin = centre.z - depth * 0.5f;

        // Both lots use this same world-space diagonal direction: every white
        // marking slopes identically, instead of mirroring across the gate.
        // The markings remain short and start away from the fence side.
        for (var x = xMin + 1.10f; x < xMax - 1.10f; x += 4.85f)
        {
            var start = new Vector3(x, y + 0.002f, zMin + 0.55f);
            var end = new Vector3(Mathf.Min(x + 2.15f, xMax - 0.45f), y + 0.002f, zMin + 4.10f);
            CreateYardBeamBetween(lot, "Diagonal_Bay", start, end, 0.10f, paint);
        }
    }

    private static void BuildYardProps(
        Transform parent,
        Material reelMaterial,
        Material cableMaterial,
        Material pipeMaterial,
        Material woodMaterial,
        Material cardboardMaterial,
        Material barrelMaterial,
        Material storageMaterial)
    {
        var props = new GameObject("Exterior_Industrial_Props").transform;
        props.SetParent(parent, false);

        // Keep the centre, truck route and the entire lower foreground empty.
        // All utility objects live in compact side strips beside the halls.
        // Each original group is now represented three times as often.
        var reelZ = new[] { -10.5f, 1.0f, 13.0f };
        var pipeZ = new[] { -7.5f, 4.0f, 15.5f };
        var crateSlots = new[]
        {
            new Vector2(28.4f, -10.4f), new Vector2(28.4f, -3.3f), new Vector2(28.4f, 3.8f),
            new Vector2(28.4f, 10.9f), new Vector2(30.7f, -7.0f), new Vector2(30.7f, 0.1f),
            new Vector2(30.7f, 7.2f), new Vector2(30.7f, 14.3f), new Vector2(33.0f, 12.0f),
        };
        var palletSlots = new[]
        {
            new Vector2(33.2f, -10.7f), new Vector2(33.2f, -4.5f), new Vector2(33.2f, 1.7f),
            new Vector2(33.2f, 7.9f), new Vector2(35.5f, -8.0f), new Vector2(35.5f, -1.8f),
            new Vector2(35.5f, 4.4f), new Vector2(35.5f, 10.6f), new Vector2(37.8f, -5.2f),
            new Vector2(37.8f, 1.0f), new Vector2(37.8f, 7.2f), new Vector2(37.8f, 13.4f),
        };
        var barrelSlots = new[]
        {
            new Vector2(38.5f, -10.7f), new Vector2(38.5f, -5.2f), new Vector2(38.5f, 0.3f),
            new Vector2(38.5f, 5.8f), new Vector2(38.5f, 11.3f), new Vector2(36.3f, -8.0f),
            new Vector2(36.3f, -2.5f), new Vector2(36.3f, 3.0f), new Vector2(36.3f, 8.5f),
        };

        for (var sideIndex = 0; sideIndex < 2; sideIndex++)
        {
            var side = sideIndex == 0 ? -1f : 1f;
            var sideName = side < 0f ? "West" : "East";
            for (var index = 0; index < reelZ.Length; index++)
            {
                CreateCableReel(props, $"Cable_Reel_{sideName}_{index + 1:00}", new Vector3(side * 37.2f, 0f, reelZ[index]), side * 24f, reelMaterial, cableMaterial);
                CreatePipeBundle(props, $"Pipe_Bundle_{sideName}_{index + 1:00}", new Vector3(side * 34.5f, 0f, pipeZ[index]), 90f, pipeMaterial, storageMaterial);
            }

            for (var index = 0; index < crateSlots.Length; index++)
            {
                var slot = crateSlots[index];
                CreateCrateStack(props, $"Crate_Stack_{sideName}_{index + 1:00}", new Vector3(side * slot.x, 0f, slot.y), index % 2 == 0, woodMaterial, cardboardMaterial);
            }

            for (var index = 0; index < palletSlots.Length; index++)
            {
                var slot = palletSlots[index];
                CreatePalletLoad(props, $"Pallet_Load_{sideName}_{index + 1:00}", new Vector3(side * slot.x, 0f, slot.y), (index % 3) + 1, woodMaterial, cardboardMaterial);
            }

            for (var index = 0; index < barrelSlots.Length; index++)
            {
                var slot = barrelSlots[index];
                CreateBarrelCluster(props, $"Barrel_Cluster_{sideName}_{index + 1:00}", new Vector3(side * slot.x, 0f, slot.y), barrelMaterial, storageMaterial);
            }
        }
    }

    private static void EnsureFactoryAudio(Transform factoryRoot)
    {
        var audioRoot = factoryRoot.Find("Factory_Audio");
        if (audioRoot == null)
        {
            audioRoot = new GameObject("Factory_Audio").transform;
            audioRoot.SetParent(factoryRoot, false);
            Undo.RegisterCreatedObjectUndo(audioRoot.gameObject, "Add factory audio");
        }

        var controller = audioRoot.GetComponent<FactoryAudioController>();
        if (controller == null)
        {
            controller = Undo.AddComponent<FactoryAudioController>(audioRoot.gameObject);
        }

        controller.playAmbientMusic = true;
        controller.ambientVolume = 0.045f;
        controller.packageCatchVolume = 0.16f;
        controller.packageDropVolume = 0.22f;
        controller.truckLoadVolume = 0.24f;
        controller.truckEngineVolume = 0.42f;
        controller.truckReverseBeepVolume = 0.22f;
        EditorUtility.SetDirty(controller);
    }

    private static void CreateCableReel(
        Transform parent,
        string name,
        Vector3 position,
        float yaw,
        Material reelMaterial,
        Material cableMaterial)
    {
        var reel = new GameObject(name).transform;
        reel.SetParent(parent, false);
        var rotation = Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(0f, 0f, 90f);
        var centre = position + Vector3.up * 0.88f;

        CreateYardCylinder(reel, "Cable_Coil", centre, new Vector3(1.16f, 0.48f, 1.16f), rotation, cableMaterial);
        var axleDirection = rotation * Vector3.up;
        CreateYardCylinder(reel, "Flange_Left", centre - axleDirection * 0.54f, new Vector3(1.55f, 0.11f, 1.55f), rotation, reelMaterial);
        CreateYardCylinder(reel, "Flange_Right", centre + axleDirection * 0.54f, new Vector3(1.55f, 0.11f, 1.55f), rotation, reelMaterial);
        CreateYardCylinder(reel, "Hub", centre, new Vector3(0.44f, 0.68f, 0.44f), rotation, reelMaterial);
    }

    private static void CreatePipeBundle(
        Transform parent,
        string name,
        Vector3 position,
        float yaw,
        Material pipeMaterial,
        Material bracketMaterial)
    {
        var bundle = new GameObject(name).transform;
        bundle.SetParent(parent, false);
        var rotation = Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(90f, 0f, 0f);
        var across = Quaternion.Euler(0f, yaw, 0f) * Vector3.right;
        var up = Vector3.up;
        for (var row = 0; row < 2; row++)
        {
            for (var column = 0; column < 3; column++)
            {
                var pipePosition = position + across * ((column - 1) * 0.34f) + up * (0.22f + row * 0.31f);
                CreateYardCylinder(bundle, $"Pipe_{row}_{column}", pipePosition, new Vector3(0.24f, 2.20f, 0.24f), rotation, pipeMaterial);
            }
        }

        var lengthDirection = rotation * Vector3.up;
        foreach (var distance in new[] { -1.10f, 1.10f })
        {
            var supportPosition = position + lengthDirection * distance + Vector3.up * 0.36f;
            CreateYardBeam(bundle, "Pipe_Strap", supportPosition, new Vector3(1.35f, 0.10f, 0.14f), Quaternion.Euler(0f, yaw, 0f), bracketMaterial);
        }
    }

    private static void CreateCrateStack(
        Transform parent,
        string name,
        Vector3 position,
        bool tall,
        Material woodMaterial,
        Material cardboardMaterial)
    {
        var stack = new GameObject(name).transform;
        stack.SetParent(parent, false);
        CreateVisualCube("Pallet_Base", stack, position + Vector3.up * 0.10f, new Vector3(1.70f, 0.20f, 1.25f), woodMaterial);
        CreateVisualCube("Carton_A", stack, position + new Vector3(-0.35f, 0.46f, -0.12f), new Vector3(0.82f, 0.55f, 0.78f), cardboardMaterial);
        CreateVisualCube("Carton_B", stack, position + new Vector3(0.42f, 0.43f, 0.14f), new Vector3(0.64f, 0.50f, 0.70f), cardboardMaterial);
        if (tall)
        {
            CreateVisualCube("Carton_Top", stack, position + new Vector3(-0.02f, 0.99f, 0.03f), new Vector3(0.88f, 0.50f, 0.70f), cardboardMaterial);
        }
    }

    private static void CreatePalletLoad(
        Transform parent,
        string name,
        Vector3 position,
        int layers,
        Material woodMaterial,
        Material cardboardMaterial)
    {
        var pallet = new GameObject(name).transform;
        pallet.SetParent(parent, false);
        CreateVisualCube("Pallet", pallet, position + Vector3.up * 0.09f, new Vector3(1.40f, 0.18f, 1.05f), woodMaterial);
        for (var layer = 0; layer < layers; layer++)
        {
            CreateVisualCube(
                $"Pallet_Carton_{layer + 1}",
                pallet,
                position + new Vector3(0f, 0.37f + layer * 0.42f, 0f),
                new Vector3(1.12f, 0.38f, 0.82f),
                cardboardMaterial);
        }
    }

    private static void CreateBarrelCluster(
        Transform parent,
        string name,
        Vector3 position,
        Material barrelMaterial,
        Material storageMaterial)
    {
        var cluster = new GameObject(name).transform;
        cluster.SetParent(parent, false);
        var offsets = new[]
        {
            new Vector3(-0.28f, 0f, -0.24f), new Vector3(0.30f, 0f, -0.18f), new Vector3(0.02f, 0f, 0.32f),
        };
        for (var index = 0; index < offsets.Length; index++)
        {
            CreateYardCylinder(cluster, $"Drum_{index + 1}", position + offsets[index] + Vector3.up * 0.52f, new Vector3(0.48f, 0.52f, 0.48f), Quaternion.identity, barrelMaterial);
        }

        CreateVisualCube("Drum_Platform", cluster, position + Vector3.up * 0.06f, new Vector3(1.40f, 0.12f, 1.22f), storageMaterial);
    }

    private static void CreateYardBeam(Transform parent, string name, Vector3 position, Vector3 size, Quaternion rotation, Material material)
    {
        var beam = CreateVisualCube(name, parent, position, size, material);
        beam.transform.rotation = rotation;
    }

    private static void CreateYardBeamBetween(Transform parent, string name, Vector3 start, Vector3 end, float width, Material material)
    {
        var direction = end - start;
        direction.y = 0f;
        var length = direction.magnitude;
        if (length < 0.01f)
        {
            return;
        }

        CreateYardBeam(
            parent,
            name,
            (start + end) * 0.5f,
            new Vector3(length, 0.012f, width),
            Quaternion.FromToRotation(Vector3.right, direction.normalized),
            material);
    }

    private static void CreateYardCylinder(Transform parent, string name, Vector3 position, Vector3 scale, Quaternion rotation, Material material)
    {
        var cylinder = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        cylinder.name = name;
        cylinder.transform.SetParent(parent, false);
        cylinder.transform.position = position;
        cylinder.transform.rotation = rotation;
        cylinder.transform.localScale = scale;
        cylinder.GetComponent<MeshRenderer>().sharedMaterial = material;
        var collider = cylinder.GetComponent<Collider>();
        if (collider != null)
        {
            Object.DestroyImmediate(collider);
        }
    }

    private static void BuildEnvironment(
        Transform root,
        Material concrete,
        Material hallYellow,
        Material roofGray,
        Material dark,
        Material frame,
        Material skylight,
        Material hazard)
    {
        var environment = new GameObject("Environment").transform;
        environment.SetParent(root);

        CreateCube("Concrete_Yard", environment, new Vector3(0f, -0.12f, 1.5f), new Vector3(56f, 0.24f, 36f), concrete);

        CreateHall("North_Main_Hall", environment, new Vector3(0f, 4.5f, 17.5f), new Vector3(48f, 9f, 5f), hallYellow, roofGray);
        CreateHall("West_Hall", environment, new Vector3(-24f, 4.5f, 2.5f), new Vector3(5f, 9f, 30f), hallYellow, roofGray);
        CreateHall("East_Hall", environment, new Vector3(24f, 4.5f, 2.5f), new Vector3(5f, 9f, 30f), hallYellow, roofGray);
        AddBuildingDetails(environment, roofGray, dark, frame, skylight);

        // The large centre gate is intentionally kept clear for a future truck route.
        CreateCube("Main_Truck_Gate", environment, new Vector3(0f, 3.15f, 14.88f), new Vector3(6f, 6.3f, 0.22f), dark);
        CreateCube("Gate_Top_Frame", environment, new Vector3(0f, 6.42f, 14.73f), new Vector3(6.45f, 0.18f, 0.18f), frame);
        CreateCube("Gate_Left_Frame", environment, new Vector3(-3.1f, 3.15f, 14.73f), new Vector3(0.18f, 6.5f, 0.18f), frame);
        CreateCube("Gate_Right_Frame", environment, new Vector3(3.1f, 3.15f, 14.73f), new Vector3(0.18f, 6.5f, 0.18f), frame);
        var safetyBlack = GetMaterial("Safety_Black", SafetyBlackColor, 0.30f, 0.05f);
        EnsureMainGateSafetyFrames(environment, hazard, safetyBlack);

        AddLoadingWindow(environment, -11.5f, dark, frame, hazard);
        AddLoadingWindow(environment, 11.5f, dark, frame, hazard);

        // Concrete expansion joints make the large open yard easier to read in the scene view.
        for (var x = -18f; x <= 18f; x += 6f)
        {
            CreateCube($"Yard_Joint_X_{x:0}", environment, new Vector3(x, 0.012f, 1.5f), new Vector3(0.035f, 0.008f, 35.7f), roofGray);
        }

        for (var z = -12f; z <= 12f; z += 6f)
        {
            CreateCube($"Yard_Joint_Z_{z:0}", environment, new Vector3(0f, 0.012f, z), new Vector3(55.7f, 0.008f, 0.035f), roofGray);
        }
    }

    private static void CreateHall(string name, Transform parent, Vector3 position, Vector3 size, Material wallMaterial, Material roofMaterial)
    {
        var hall = new GameObject(name).transform;
        hall.SetParent(parent);
        CreateCube("Walls", hall, position, size, wallMaterial);
        CreateCube("Roof", hall, position + new Vector3(0f, size.y * 0.5f + 0.18f, 0f), new Vector3(size.x + 0.5f, 0.36f, size.z + 0.5f), roofMaterial);
        CreateCube("Concrete_Base", hall, position + new Vector3(0f, -size.y * 0.42f, 0f), new Vector3(size.x + 0.04f, 0.48f, size.z + 0.04f), roofMaterial);
    }

    private static void AddBuildingDetails(Transform environment, Material roof, Material window, Material frame, Material skylight)
    {
        // The halls can be repositioned or resized directly in the scene. Read
        // their current wall bounds instead of relying on the dimensions used
        // when the factory was first created.
        var wallGroove = GetMaterial("Factory_Wall_Groove", FactoryWallGrooveColor, 0.30f, 0.04f);
        var roofSeam = GetUniformRoofMaterial("Roof_Corrugation", RoofSeamColor);
        var roofRidge = GetUniformRoofMaterial("Roof_Ridge", RoofRidgeColor);
        var sideDoor = GetMaterial("Side_Hall_Metal_Door", new Color(0.12f, 0.16f, 0.20f), 0.50f, 0.75f);
        var sideDoorRib = GetMaterial("Side_Hall_Door_Rib", new Color(0.26f, 0.31f, 0.36f), 0.48f, 0.58f);
        UpgradeHallExterior(environment.Find("North_Main_Hall"), roof, roofSeam, roofRidge, wallGroove, window, frame, skylight, sideDoor, sideDoorRib);
        UpgradeHallExterior(environment.Find("West_Hall"), roof, roofSeam, roofRidge, wallGroove, window, frame, skylight, sideDoor, sideDoorRib);
        UpgradeHallExterior(environment.Find("East_Hall"), roof, roofSeam, roofRidge, wallGroove, window, frame, skylight, sideDoor, sideDoorRib);
    }

    private static void ApplyBuildingFacadeMaterials(Transform environment, Material wallMaterial, Material roofMaterial)
    {
        foreach (var hallName in new[] { "North_Main_Hall", "West_Hall", "East_Hall" })
        {
            var hall = environment.Find(hallName);
            if (hall == null)
            {
                continue;
            }

            var wallsRenderer = hall.Find("Walls")?.GetComponent<Renderer>();
            if (wallsRenderer != null) wallsRenderer.sharedMaterial = wallMaterial;

            var baseRenderer = hall.Find("Concrete_Base")?.GetComponent<Renderer>();
            if (baseRenderer != null) baseRenderer.sharedMaterial = roofMaterial;

            var originalRoofRenderer = hall.Find("Roof")?.GetComponent<Renderer>();
            if (originalRoofRenderer != null) originalRoofRenderer.sharedMaterial = roofMaterial;
        }
    }

    private static void EnsureMainGateSafetyFrames(Transform environment, Material yellowMaterial, Material blackMaterial)
    {
        var existing = environment.Find("Main_Gate_Safety_Frames");
        if (existing != null)
        {
            Object.DestroyImmediate(existing.gameObject);
        }

        var gate = environment.Find("Main_Truck_Gate");
        var gateRenderer = gate != null ? gate.GetComponent<Renderer>() : null;
        if (gateRenderer == null)
        {
            return;
        }

        // Derive the protectors from the real gate bounds so they stay beside
        // the passage if the user later moves or resizes the main hall.
        var gateBounds = gateRenderer.bounds;
        var frames = new GameObject("Main_Gate_Safety_Frames").transform;
        frames.SetParent(environment, false);
        const float postWidth = 0.42f;
        const float postHeight = 2.20f;
        const float postDepth = 0.32f;
        var postCentreY = gateBounds.min.y + postHeight * 0.5f;
        var postZ = gateBounds.min.z - 0.25f;
        var postOffset = gateBounds.extents.x + 0.52f;

        foreach (var side in new[] { -1f, 1f })
        {
            var postPosition = new Vector3(gateBounds.center.x + side * postOffset, postCentreY, postZ);
            var post = CreateCube(side < 0f ? "Safety_Post_Left" : "Safety_Post_Right", frames, postPosition,
                new Vector3(postWidth, postHeight, postDepth), yellowMaterial);

            // The dark bands are visual-only; the yellow post itself remains
            // the single physical guard while the truck passage stays clear.
            for (var band = 0; band < 5; band++)
            {
                var y = gateBounds.min.y + 0.34f + band * 0.40f;
                var stripe = CreateVisualCube("Safety_Stripe", post.transform,
                    new Vector3(postPosition.x, y, postZ - postDepth * 0.53f),
                    new Vector3(postWidth + 0.08f, 0.12f, 0.045f), blackMaterial);
                stripe.transform.rotation = Quaternion.Euler(0f, 0f, -45f);
            }
        }
    }

    private static void EnsureBuildingWallColliders(Transform environment)
    {
        foreach (var hallName in new[] { "North_Main_Hall", "West_Hall", "East_Hall" })
        {
            var walls = environment.Find($"{hallName}/Walls");
            if (walls == null)
            {
                continue;
            }

            // The main hall's original single cube would seal the doorway.
            // Once the gate opening exists, its left/right/top replacement
            // segments already carry their own solid colliders.
            if (hallName == "North_Main_Hall" && environment.Find("Main_Hall_Gate_Opening") != null)
            {
                var originalGateWallCollider = walls.GetComponent<BoxCollider>();
                if (originalGateWallCollider != null) originalGateWallCollider.enabled = false;
                continue;
            }

            var wallCollider = walls.GetComponent<BoxCollider>();
            if (wallCollider == null)
            {
                wallCollider = Undo.AddComponent<BoxCollider>(walls.gameObject);
            }

            wallCollider.enabled = true;
            wallCollider.isTrigger = false;
        }
    }

    private static void UpgradeHallExterior(
        Transform hall,
        Material roof,
        Material roofSeam,
        Material roofRidge,
        Material wallGroove,
        Material window,
        Material frame,
        Material skylight,
        Material sideDoor,
        Material sideDoorRib)
    {
        if (hall == null)
        {
            return;
        }

        var walls = hall.Find("Walls");
        var wallRenderer = walls != null ? walls.GetComponent<Renderer>() : null;
        if (wallRenderer == null)
        {
            Debug.LogWarning($"Could not fit building details for {hall.name}: its Walls renderer is missing.");
            return;
        }

        // Bounds are in world space, so this also works after a user has moved
        // a hall root or resized its Walls child in the Unity editor.
        var wallBounds = wallRenderer.bounds;
        var position = wallBounds.center;
        var size = wallBounds.size;
        var ridgeAlongX = size.x >= size.z;
        // The side halls may have a different wall height from the main hall.
        // Their roof ridge is fitted to the same world-space height so the
        // three roofs meet at one consistent top level.
        var roofRise = GetRoofRiseForHall(hall, wallBounds, ridgeAlongX);
        // The side halls connect to the main roof with a short hip section.
        // Its tip lands on the main ridge instead of extending the side-hall
        // ridge straight through the main roof.
        var sideRoofConnectionDirection = GetSideRoofConnectionDirection(hall, wallBounds, ridgeAlongX);

        // Preserve every earlier generated revision for undo/history, but show
        // only the version fitted to the current hall geometry.
        for (var childIndex = 0; childIndex < hall.childCount; childIndex++)
        {
            var child = hall.GetChild(childIndex);
            if (child.name.StartsWith("Exterior_Upgrade"))
            {
                child.gameObject.SetActive(false);
            }
        }

        var originalRoof = hall.Find("Roof");
        if (originalRoof != null)
        {
            originalRoof.gameObject.SetActive(false);
        }

        var details = new GameObject("Exterior_Upgrade_CurrentFit").transform;
        details.SetParent(hall, false);
        CreateGabledRoof("Gabled_Roof", details, wallBounds, ridgeAlongX, roofRise, sideRoofConnectionDirection, roof);
        AddWallCorrugation(details, wallBounds, ridgeAlongX, wallGroove);
        AddRoofSurfaceDetails(details, wallBounds, ridgeAlongX, roofRise, roofSeam, roofRidge);

        if (ridgeAlongX)
        {
            AddFrontWindows(details, position, size, window, frame);
            AddMainRoofSkylights(details, hall.parent, wallBounds, roofRise, skylight, frame);
        }
        else
        {
            var inwardSide = position.x < 0f ? 1f : -1f;
            AddSideWindows(details, position, size, window, frame);
            AddSideRoofSkylights(details, wallBounds, roofRise, inwardSide, skylight, frame);
            AddSideHallFrontDoor(details, wallBounds, sideDoor, sideDoorRib, frame);

            if (sideRoofConnectionDirection == 1 && TryGetMainHallWallBounds(hall.parent, out var mainWallBounds))
            {
                AddSideRoofHipConnection(
                    details,
                    wallBounds,
                    mainWallBounds,
                    wallBounds.max.z + 0.32f,
                    roof,
                    roofRidge);
            }
        }
    }

    private static void CreateGabledRoof(string name, Transform parent, Bounds wallBounds, bool ridgeAlongX, float roofRise, int openGableEnd, Material material)
    {
        var halfX = wallBounds.extents.x + 0.32f;
        var halfZ = wallBounds.extents.z + 0.32f;
        // Keep the mesh local to an origin on the real top edge of the walls.
        // This avoids inheriting an outdated hall-root position or height.
        const float eaveY = 0f;
        var ridgeY = roofRise;
        var vertices = ridgeAlongX
            ? new[]
            {
                new Vector3(-halfX, eaveY, -halfZ), new Vector3(halfX, eaveY, -halfZ),
                new Vector3(-halfX, eaveY, halfZ), new Vector3(halfX, eaveY, halfZ),
                new Vector3(-halfX, ridgeY, 0f), new Vector3(halfX, ridgeY, 0f),
            }
            : new[]
            {
                new Vector3(-halfX, eaveY, -halfZ), new Vector3(halfX, eaveY, -halfZ),
                new Vector3(-halfX, eaveY, halfZ), new Vector3(halfX, eaveY, halfZ),
                new Vector3(0f, ridgeY, -halfZ), new Vector3(0f, ridgeY, halfZ),
            };

        // The original winding faced inward. With URP's normal back-face
        // culling that made the roof look transparent from above, revealing
        // skylight shadows through it. These triangles face outward.
        int[] triangles;
        if (ridgeAlongX)
        {
            triangles = new[] { 0, 5, 1, 0, 4, 5, 4, 3, 5, 4, 2, 3, 0, 2, 4, 1, 5, 3, 0, 3, 2, 0, 1, 3 };
        }
        else
        {
            // The normal side-gable roof is closed at both ends. When it
            // joins the main ridge, leave that end open for the hip connector
            // rather than retaining an unwanted vertical triangular cap.
            var sideTriangles = new List<int>
            {
                0, 5, 4, 0, 2, 5,
                4, 3, 1, 4, 5, 3,
            };
            if (openGableEnd != -1)
            {
                sideTriangles.AddRange(new[] { 0, 4, 1 });
            }

            if (openGableEnd != 1)
            {
                sideTriangles.AddRange(new[] { 2, 3, 5 });
            }

            sideTriangles.AddRange(new[] { 0, 3, 2, 0, 1, 3 });
            triangles = sideTriangles.ToArray();
        }

        var roof = new GameObject(name);
        roof.transform.SetParent(parent, false);
        roof.transform.position = new Vector3(wallBounds.center.x, wallBounds.max.y + 0.03f, wallBounds.center.z);
        var mesh = new Mesh { name = $"{name}_Mesh" };
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        roof.AddComponent<MeshFilter>().sharedMesh = mesh;
        roof.AddComponent<MeshRenderer>().sharedMaterial = material;
        var roofCollider = roof.AddComponent<MeshCollider>();
        roofCollider.sharedMesh = mesh;
        roofCollider.convex = false;
    }

    private static void AddWallCorrugation(Transform parent, Bounds wallBounds, bool ridgeAlongX, Material material)
    {
        var root = new GameObject("Wall_Corrugation").transform;
        root.SetParent(parent, false);
        const float spacing = 0.55f;
        const float stripWidth = 0.035f;
        var stripHeight = Mathf.Max(1f, wallBounds.size.y - 0.60f);
        var y = wallBounds.center.y;

        if (ridgeAlongX)
        {
            // The main façade faces the courtyard along -Z. Leave the truck
            // opening clear so the decorative ribs never cover the passage.
            var z = wallBounds.min.z - 0.018f;
            for (var x = wallBounds.min.x + 0.28f; x <= wallBounds.max.x - 0.28f; x += spacing)
            {
                if (Mathf.Abs(x - wallBounds.center.x) < 3.35f)
                {
                    continue;
                }

                CreateVisualCube("Wall_Rib", root, new Vector3(x, y, z), new Vector3(stripWidth, stripHeight, 0.035f), material);
            }
        }
        else
        {
            // On the side halls, dress only the faces looking into the yard.
            var inwardSide = wallBounds.center.x < 0f ? 1f : -1f;
            var x = inwardSide > 0f ? wallBounds.max.x + 0.018f : wallBounds.min.x - 0.018f;
            for (var z = wallBounds.min.z + 0.28f; z <= wallBounds.max.z - 0.28f; z += spacing)
            {
                CreateVisualCube("Wall_Rib", root, new Vector3(x, y, z), new Vector3(0.035f, stripHeight, stripWidth), material);
            }
        }
    }

    private static void AddRoofSurfaceDetails(Transform parent, Bounds wallBounds, bool ridgeAlongX, float roofRise, Material seamMaterial, Material ridgeMaterial)
    {
        var root = new GameObject("Roof_Corrugation").transform;
        root.SetParent(parent, false);
        var topY = wallBounds.max.y + 0.03f;

        if (ridgeAlongX)
        {
            var halfRun = wallBounds.extents.z + 0.32f;
            var halfX = wallBounds.extents.x + 0.32f;
            var seamLength = Mathf.Sqrt(halfRun * halfRun + roofRise * roofRise);
            var seamCount = Mathf.Clamp(Mathf.RoundToInt(wallBounds.size.x / 0.72f), 10, 58);
            for (var index = 1; index < seamCount; index++)
            {
                var x = Mathf.Lerp(wallBounds.min.x + 0.30f, wallBounds.max.x - 0.30f, index / (float)seamCount);
                AddRoofSeam(root, new Vector3(x, topY + roofRise * 0.5f + 0.025f, wallBounds.center.z - halfRun * 0.5f),
                    new Vector3(0f, -roofRise, -halfRun), seamLength, seamMaterial);
                AddRoofSeam(root, new Vector3(x, topY + roofRise * 0.5f + 0.025f, wallBounds.center.z + halfRun * 0.5f),
                    new Vector3(0f, -roofRise, halfRun), seamLength, seamMaterial);
            }

            CreateVisualCube("Ridge_Cap", root, new Vector3(wallBounds.center.x, topY + roofRise + 0.07f, wallBounds.center.z),
                new Vector3(wallBounds.size.x + 0.70f, 0.14f, 0.22f), ridgeMaterial);
            CreateVisualCube("Eave_Fascia_Front", root, new Vector3(wallBounds.center.x, topY + 0.06f, wallBounds.center.z - halfRun),
                new Vector3(wallBounds.size.x + 0.70f, 0.11f, 0.15f), ridgeMaterial);
            CreateVisualCube("Eave_Fascia_Back", root, new Vector3(wallBounds.center.x, topY + 0.06f, wallBounds.center.z + halfRun),
                new Vector3(wallBounds.size.x + 0.70f, 0.11f, 0.15f), ridgeMaterial);

            var edgeXLeft = wallBounds.center.x - halfX - 0.045f;
            var edgeXRight = wallBounds.center.x + halfX + 0.045f;
            var frontZ = wallBounds.center.z - halfRun;
            var backZ = wallBounds.center.z + halfRun;
            var eaveY = topY + 0.035f;
            var ridgeY = topY + roofRise + 0.035f;
            AddRoofEdgeTrim(root, "Gable_Trim_Left_Front", new Vector3(edgeXLeft, eaveY, frontZ), new Vector3(edgeXLeft, ridgeY, wallBounds.center.z), ridgeMaterial);
            AddRoofEdgeTrim(root, "Gable_Trim_Left_Back", new Vector3(edgeXLeft, ridgeY, wallBounds.center.z), new Vector3(edgeXLeft, eaveY, backZ), ridgeMaterial);
            AddRoofEdgeTrim(root, "Gable_Trim_Right_Front", new Vector3(edgeXRight, eaveY, frontZ), new Vector3(edgeXRight, ridgeY, wallBounds.center.z), ridgeMaterial);
            AddRoofEdgeTrim(root, "Gable_Trim_Right_Back", new Vector3(edgeXRight, ridgeY, wallBounds.center.z), new Vector3(edgeXRight, eaveY, backZ), ridgeMaterial);
        }
        else
        {
            var halfRun = wallBounds.extents.x + 0.32f;
            var halfZ = wallBounds.extents.z + 0.32f;
            var seamLength = Mathf.Sqrt(halfRun * halfRun + roofRise * roofRise);
            var seamCount = Mathf.Clamp(Mathf.RoundToInt(wallBounds.size.z / 0.72f), 10, 58);
            for (var index = 1; index < seamCount; index++)
            {
                var z = Mathf.Lerp(wallBounds.min.z + 0.30f, wallBounds.max.z - 0.30f, index / (float)seamCount);
                AddRoofSeam(root, new Vector3(wallBounds.center.x - halfRun * 0.5f, topY + roofRise * 0.5f + 0.025f, z),
                    new Vector3(-halfRun, -roofRise, 0f), seamLength, seamMaterial);
                AddRoofSeam(root, new Vector3(wallBounds.center.x + halfRun * 0.5f, topY + roofRise * 0.5f + 0.025f, z),
                    new Vector3(halfRun, -roofRise, 0f), seamLength, seamMaterial);
            }

            CreateVisualCube("Ridge_Cap", root, new Vector3(wallBounds.center.x, topY + roofRise + 0.07f, wallBounds.center.z),
                new Vector3(0.22f, 0.14f, wallBounds.size.z + 0.70f), ridgeMaterial);
            CreateVisualCube("Eave_Fascia_Left", root, new Vector3(wallBounds.center.x - halfRun, topY + 0.06f, wallBounds.center.z),
                new Vector3(0.20f, 0.12f, wallBounds.size.z + 0.70f), ridgeMaterial);
            CreateVisualCube("Eave_Fascia_Right", root, new Vector3(wallBounds.center.x + halfRun, topY + 0.06f, wallBounds.center.z),
                new Vector3(0.20f, 0.12f, wallBounds.size.z + 0.70f), ridgeMaterial);

            // The two gable ends of each side hall also receive a lower
            // fascia. Together with the sloping trims this closes the roof
            // outline without making the treatment too heavy.
            CreateVisualCube("Gable_Base_Trim_Front", root, new Vector3(wallBounds.center.x, topY + 0.055f, wallBounds.center.z - halfZ),
                new Vector3(wallBounds.size.x + 0.70f, 0.12f, 0.16f), ridgeMaterial);
            CreateVisualCube("Gable_Base_Trim_Back", root, new Vector3(wallBounds.center.x, topY + 0.055f, wallBounds.center.z + halfZ),
                new Vector3(wallBounds.size.x + 0.70f, 0.12f, 0.16f), ridgeMaterial);

            var leftX = wallBounds.center.x - halfRun;
            var rightX = wallBounds.center.x + halfRun;
            var frontZ = wallBounds.center.z - halfZ - 0.045f;
            var backZ = wallBounds.center.z + halfZ + 0.045f;
            var eaveY = topY + 0.035f;
            var ridgeY = topY + roofRise + 0.035f;
            AddRoofEdgeTrim(root, "Gable_Trim_Front_Left", new Vector3(leftX, eaveY, frontZ), new Vector3(wallBounds.center.x, ridgeY, frontZ), ridgeMaterial);
            AddRoofEdgeTrim(root, "Gable_Trim_Front_Right", new Vector3(wallBounds.center.x, ridgeY, frontZ), new Vector3(rightX, eaveY, frontZ), ridgeMaterial);
            AddRoofEdgeTrim(root, "Gable_Trim_Back_Left", new Vector3(leftX, eaveY, backZ), new Vector3(wallBounds.center.x, ridgeY, backZ), ridgeMaterial);
            AddRoofEdgeTrim(root, "Gable_Trim_Back_Right", new Vector3(wallBounds.center.x, ridgeY, backZ), new Vector3(rightX, eaveY, backZ), ridgeMaterial);
        }
    }

    private static void AddRoofSeam(Transform parent, Vector3 position, Vector3 direction, float length, Material material)
    {
        var seam = CreateVisualCube("Roof_Seam", parent, position, new Vector3(0.035f, 0.035f, length), material);
        seam.transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
    }

    private static void AddRoofEdgeTrim(Transform parent, string name, Vector3 start, Vector3 end, Material material)
    {
        var trim = CreateVisualCube(name, parent, (start + end) * 0.5f, Vector3.one, material);
        SetRoofEdgeTrimGeometry(trim.transform, start, end);
    }

    private static void SetRoofEdgeTrimGeometry(Transform trim, Vector3 start, Vector3 end)
    {
        if (trim == null)
        {
            return;
        }

        var direction = end - start;
        if (direction.sqrMagnitude < 0.0001f)
        {
            return;
        }

        trim.position = (start + end) * 0.5f;
        trim.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        trim.localScale = new Vector3(0.12f, 0.12f, direction.magnitude + 0.10f);
    }

    private static float GetRoofRiseForHall(Transform hall, Bounds wallBounds, bool ridgeAlongX)
    {
        if (ridgeAlongX || hall == null || hall.parent == null)
        {
            return GabledRoofRise;
        }

        var mainHall = hall.parent.Find("North_Main_Hall");
        var mainWalls = mainHall != null ? mainHall.Find("Walls") : null;
        var mainRenderer = mainWalls != null ? mainWalls.GetComponent<Renderer>() : null;
        if (mainRenderer == null)
        {
            return GabledRoofRise;
        }

        var mainRidgeY = mainRenderer.bounds.max.y + 0.03f + GabledRoofRise;
        return Mathf.Max(0.35f, mainRidgeY - wallBounds.max.y - 0.03f);
    }

    private static bool TryGetMainHallWallBounds(Transform environment, out Bounds mainWallBounds)
    {
        mainWallBounds = new Bounds();
        var mainHall = environment != null ? environment.Find("North_Main_Hall") : null;
        var mainWalls = mainHall != null ? mainHall.Find("Walls") : null;
        var mainRenderer = mainWalls != null ? mainWalls.GetComponent<Renderer>() : null;
        if (mainRenderer == null)
        {
            return false;
        }

        mainWallBounds = mainRenderer.bounds;
        return true;
    }

    private static int GetSideRoofConnectionDirection(Transform hall, Bounds wallBounds, bool ridgeAlongX)
    {
        if (ridgeAlongX || !TryGetMainHallWallBounds(hall != null ? hall.parent : null, out var mainWallBounds))
        {
            return 0;
        }

        const float roofOverhang = 0.32f;
        if (mainWallBounds.center.z > wallBounds.max.z + roofOverhang + 0.05f)
        {
            return 1;
        }

        if (mainWallBounds.center.z < wallBounds.min.z - roofOverhang - 0.05f)
        {
            return -1;
        }

        return 0;
    }

    private static void AddSideRoofHipConnection(
        Transform parent,
        Bounds sideBounds,
        Bounds mainWallBounds,
        float joinZ,
        Material roofMaterial,
        Material trimMaterial)
    {
        var mainRidgeZ = mainWallBounds.center.z;
        var mainPeakY = GetMainRoofSurfaceY(mainWallBounds, mainRidgeZ);
        if (mainRidgeZ <= joinZ + 0.05f)
        {
            return;
        }

        var halfX = sideBounds.extents.x + 0.32f;
        var baseY = sideBounds.max.y + 0.03f;
        var origin = new Vector3(sideBounds.center.x, baseY, joinZ);
        // The main roof has already risen above the side-hall eaves at this
        // Z position. Begin the connecting hip on that surface instead of
        // below it, then lift the flashing a little further. This prevents
        // the diagonal trim from disappearing inside the larger roof.
        var joinSurfaceY = Mathf.Max(baseY, GetMainRoofSurfaceY(mainWallBounds, joinZ) + SideRoofJunctionSurfaceLift);
        var ridgeY = mainPeakY;
        var tipY = mainPeakY + SideRoofJunctionSurfaceLift;
        var runZ = mainRidgeZ - joinZ;

        var connector = new GameObject("Side_Roof_Hip_Connection");
        connector.transform.SetParent(parent, false);
        connector.transform.position = origin;
        var mesh = new Mesh { name = "Side_Roof_Hip_Connection_Mesh" };
        mesh.vertices = new[]
        {
            new Vector3(-halfX, joinSurfaceY - baseY, 0f),
            new Vector3(0f, ridgeY - baseY, 0f),
            new Vector3(halfX, joinSurfaceY - baseY, 0f),
            new Vector3(0f, tipY - baseY, runZ),
        };
        mesh.triangles = new[] { 0, 3, 1, 1, 3, 2, 0, 2, 3 };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        connector.AddComponent<MeshFilter>().sharedMesh = mesh;
        connector.AddComponent<MeshRenderer>().sharedMaterial = roofMaterial;
        connector.AddComponent<MeshCollider>().sharedMesh = mesh;

        var left = origin + new Vector3(-halfX, joinSurfaceY - baseY + SideRoofJunctionTrimLift, 0f);
        var ridge = origin + new Vector3(0f, ridgeY - baseY + SideRoofJunctionTrimLift, 0f);
        var right = origin + new Vector3(halfX, joinSurfaceY - baseY + SideRoofJunctionTrimLift, 0f);
        var tip = origin + new Vector3(0f, tipY - baseY + SideRoofJunctionTrimLift, runZ);
        AddRoofEdgeTrim(connector.transform, "Hip_Trim_Left", left, tip, trimMaterial);
        AddRoofEdgeTrim(connector.transform, "Hip_Ridge_Trim", ridge, tip, trimMaterial);
        AddRoofEdgeTrim(connector.transform, "Hip_Trim_Right", right, tip, trimMaterial);
    }

    private static Transform FindActiveSideRoofHipConnection(Transform hall)
    {
        if (hall == null)
        {
            return null;
        }

        foreach (var candidate in hall.GetComponentsInChildren<Transform>(true))
        {
            if (candidate.name == "Side_Roof_Hip_Connection" && candidate.gameObject.activeInHierarchy)
            {
                return candidate;
            }
        }

        return null;
    }

    private static bool RefitSideRoofHipConnection(Transform connector, Bounds sideBounds, Bounds mainWallBounds)
    {
        var meshFilter = connector != null ? connector.GetComponent<MeshFilter>() : null;
        if (meshFilter == null || meshFilter.sharedMesh == null)
        {
            return false;
        }

        var joinZ = sideBounds.max.z + 0.32f;
        var mainRidgeZ = mainWallBounds.center.z;
        if (mainRidgeZ <= joinZ + 0.05f)
        {
            return false;
        }

        var halfX = sideBounds.extents.x + 0.32f;
        var baseY = sideBounds.max.y + 0.03f;
        var mainPeakY = GetMainRoofSurfaceY(mainWallBounds, mainRidgeZ);
        var joinSurfaceY = Mathf.Max(baseY, GetMainRoofSurfaceY(mainWallBounds, joinZ) + SideRoofJunctionSurfaceLift);
        var tipY = mainPeakY + SideRoofJunctionSurfaceLift;
        var runZ = mainRidgeZ - joinZ;
        var origin = new Vector3(sideBounds.center.x, baseY, joinZ);

        Undo.RecordObject(connector, "Refit Side Roof Junction");
        Undo.RecordObject(meshFilter.sharedMesh, "Refit Side Roof Junction");
        connector.position = origin;

        var mesh = meshFilter.sharedMesh;
        mesh.Clear();
        mesh.vertices = new[]
        {
            new Vector3(-halfX, joinSurfaceY - baseY, 0f),
            new Vector3(0f, mainPeakY - baseY, 0f),
            new Vector3(halfX, joinSurfaceY - baseY, 0f),
            new Vector3(0f, tipY - baseY, runZ),
        };
        mesh.triangles = new[] { 0, 3, 1, 1, 3, 2, 0, 2, 3 };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        EditorUtility.SetDirty(mesh);

        var collider = connector.GetComponent<MeshCollider>();
        if (collider != null)
        {
            Undo.RecordObject(collider, "Refit Side Roof Junction");
            collider.sharedMesh = null;
            collider.sharedMesh = mesh;
            EditorUtility.SetDirty(collider);
        }

        var left = origin + new Vector3(-halfX, joinSurfaceY - baseY + SideRoofJunctionTrimLift, 0f);
        var ridge = origin + new Vector3(0f, mainPeakY - baseY + SideRoofJunctionTrimLift, 0f);
        var right = origin + new Vector3(halfX, joinSurfaceY - baseY + SideRoofJunctionTrimLift, 0f);
        var tip = origin + new Vector3(0f, tipY - baseY + SideRoofJunctionTrimLift, runZ);
        RefitRoofEdgeTrim(connector.Find("Hip_Trim_Left"), left, tip);
        RefitRoofEdgeTrim(connector.Find("Hip_Ridge_Trim"), ridge, tip);
        RefitRoofEdgeTrim(connector.Find("Hip_Trim_Right"), right, tip);
        EditorUtility.SetDirty(connector);
        return true;
    }

    private static void RefitRoofEdgeTrim(Transform trim, Vector3 start, Vector3 end)
    {
        if (trim == null)
        {
            return;
        }

        Undo.RecordObject(trim, "Refit Side Roof Junction");
        SetRoofEdgeTrimGeometry(trim, start, end);
        EditorUtility.SetDirty(trim);
    }

    private static float GetMainRoofSurfaceY(Bounds mainWallBounds, float z)
    {
        var halfRun = Mathf.Max(0.01f, mainWallBounds.extents.z + 0.32f);
        var ridgeFactor = 1f - Mathf.Clamp01(Mathf.Abs(z - mainWallBounds.center.z) / halfRun);
        return mainWallBounds.max.y + 0.03f + GabledRoofRise * ridgeFactor;
    }

    private static void AddFrontWindows(Transform parent, Vector3 centre, Vector3 size, Material window, Material frame)
    {
        var z = centre.z - size.z * 0.5f - 0.055f;
        var panelWidth = Mathf.Min(3.15f, size.x * 0.085f);
        var panelHeight = Mathf.Min(1.2f, size.y * 0.135f);
        var windowY = centre.y + size.y * 0.14f;
        // The outer edge of each outer window aligns exactly with the outer
        // edge of the corresponding parcel-exit opening. This moves the two
        // window pairs closer to the central truck gate as set in the scene.
        foreach (var xOffset in GetMainFrontWindowOffsets(size, panelWidth))
        {
            // The main-hall front windows have four even panes: their third
            // vertical mullion is an extra divider requested for the central
            // building, while the side-hall windows keep their existing form.
            AddWindow("Front_Window", parent, new Vector3(centre.x + xOffset, windowY, z), new Vector3(panelWidth, panelHeight, 0.10f), window, frame, false, 3);
        }
    }

    private static float[] GetMainFrontWindowOffsets(Vector3 hallSize, float panelWidth)
    {
        const float parcelExitCentreX = 11.5f;
        const float parcelExitWidth = 2.45f;
        var outerWindowCentre = parcelExitCentreX + parcelExitWidth * 0.5f - panelWidth * 0.5f;
        var pairCentreSpacing = hallSize.x * 0.110f;
        return new[]
        {
            -outerWindowCentre,
            -outerWindowCentre + pairCentreSpacing,
            outerWindowCentre - pairCentreSpacing,
            outerWindowCentre,
        };
    }

    private static void AddSideWindows(Transform parent, Vector3 centre, Vector3 size, Material window, Material frame)
    {
        // West wall windows face +X and east wall windows face -X: both look
        // directly into the courtyard, not outward beyond the factory walls.
        var courtyardSide = centre.x < 0f ? 1f : -1f;
        var x = centre.x + courtyardSide * (size.x * 0.5f + 0.08f);
        var panelLength = Mathf.Min(3.5f, size.z * 0.117f);
        var panelHeight = Mathf.Min(1.2f, size.y * 0.135f);
        var windowY = centre.y + size.y * 0.117f;
        foreach (var zFactor in new[] { -0.333f, -0.117f, 0.117f, 0.333f })
        {
            // Match the main hall's four-pane appearance: the third mullion
            // is the requested vertical divider directly in the middle.
            AddWindow("Side_Window", parent, new Vector3(x, windowY, centre.z + size.z * zFactor), new Vector3(0.10f, panelHeight, panelLength), window, frame, true, 3);
        }
    }

    private static void AddSideHallFrontDoor(Transform parent, Bounds wallBounds, Material doorMaterial, Material ribMaterial, Material frameMaterial)
    {
        // The short front end of each side hall faces -Z, toward the player
        // and the open yard. Use current world bounds so the doors continue
        // to fit if either hall is moved or resized later.
        var root = new GameObject("Side_Hall_Front_Metal_Door").transform;
        root.SetParent(parent, false);

        // A narrow, human-sized leaf makes this read as a standard access
        // door instead of a loading gate.
        var doorWidth = Mathf.Min(wallBounds.size.x * 0.26f, 1.25f);
        var doorHeight = Mathf.Min(wallBounds.size.y * 0.29f, 2.45f);
        var doorBottom = wallBounds.min.y + 0.20f;
        var doorCentre = new Vector3(
            wallBounds.center.x,
            doorBottom + doorHeight * 0.5f,
            wallBounds.min.z - 0.055f);

        CreateVisualCube("Metal_Door_Panel", root, doorCentre, new Vector3(doorWidth, doorHeight, 0.10f), doorMaterial);

        const float frameThickness = 0.10f;
        const float frameDepth = 0.14f;
        CreateVisualCube("Door_Frame_Left", root,
            doorCentre + new Vector3(-doorWidth * 0.5f - frameThickness * 0.5f, 0f, -0.035f),
            new Vector3(frameThickness, doorHeight + frameThickness * 2f, frameDepth), frameMaterial);
        CreateVisualCube("Door_Frame_Right", root,
            doorCentre + new Vector3(doorWidth * 0.5f + frameThickness * 0.5f, 0f, -0.035f),
            new Vector3(frameThickness, doorHeight + frameThickness * 2f, frameDepth), frameMaterial);
        CreateVisualCube("Door_Frame_Top", root,
            doorCentre + new Vector3(0f, doorHeight * 0.5f + frameThickness * 0.5f, -0.035f),
            new Vector3(doorWidth + frameThickness * 2f, frameThickness, frameDepth), frameMaterial);
        CreateVisualCube("Door_Threshold", root,
            doorCentre - new Vector3(0f, doorHeight * 0.5f - 0.05f, 0.035f),
            new Vector3(doorWidth + frameThickness * 2f, 0.10f, frameDepth), frameMaterial);

        // Two shallow pressed-metal panels give the door detail without
        // turning it into a roller shutter or a double loading gate.
        var insetWidth = doorWidth * 0.68f;
        CreateVisualCube("Door_Upper_Inset", root,
            doorCentre + new Vector3(0f, doorHeight * 0.20f, -0.062f),
            new Vector3(insetWidth, doorHeight * 0.27f, 0.028f), ribMaterial);
        CreateVisualCube("Door_Lower_Inset", root,
            doorCentre + new Vector3(0f, -doorHeight * 0.23f, -0.062f),
            new Vector3(insetWidth, doorHeight * 0.31f, 0.028f), ribMaterial);

        // Hinges at the left edge and one horizontal handle on the right make
        // its opening direction immediately legible.
        var hingeX = doorCentre.x - doorWidth * 0.5f + 0.055f;
        foreach (var hingeHeight in new[] { -0.32f, 0f, 0.32f })
        {
            CreateVisualCube("Door_Hinge", root,
                new Vector3(hingeX, doorCentre.y + hingeHeight * doorHeight, doorCentre.z - 0.085f),
                new Vector3(0.10f, 0.095f, 0.045f), ribMaterial);
        }

        var handlePosition = new Vector3(
            doorCentre.x + doorWidth * 0.27f,
            doorCentre.y - doorHeight * 0.04f,
            doorCentre.z - 0.105f);
        CreateVisualCube("Door_Handle_Base", root, handlePosition + new Vector3(0f, 0f, 0.016f),
            new Vector3(0.11f, 0.15f, 0.035f), ribMaterial);
        CreateVisualCube("Door_Handle", root, handlePosition,
            new Vector3(0.24f, 0.050f, 0.060f), frameMaterial);
    }

    private static void AddWindow(string name, Transform parent, Vector3 centre, Vector3 panelSize, Material window, Material frame, bool alongZ, int verticalMullionCount = 2)
    {
        var root = new GameObject(name).transform;
        root.SetParent(parent);
        CreateCube("Glass", root, centre, panelSize, window);
        var depth = alongZ ? panelSize.x + 0.035f : panelSize.z + 0.035f;
        if (alongZ)
        {
            CreateCube("Frame_Top", root, centre + new Vector3(0f, panelSize.y * 0.5f, 0f), new Vector3(depth, 0.11f, panelSize.z + 0.16f), frame);
            CreateCube("Frame_Bottom", root, centre - new Vector3(0f, panelSize.y * 0.5f, 0f), new Vector3(depth, 0.11f, panelSize.z + 0.16f), frame);
            CreateCube("Frame_Left", root, centre - new Vector3(0f, 0f, panelSize.z * 0.5f), new Vector3(depth, panelSize.y + 0.14f, 0.10f), frame);
            CreateCube("Frame_Right", root, centre + new Vector3(0f, 0f, panelSize.z * 0.5f), new Vector3(depth, panelSize.y + 0.14f, 0.10f), frame);
            for (var index = 1; index <= verticalMullionCount; index++)
            {
                var offset = verticalMullionCount == 2
                    ? (index == 1 ? -1f / 3f : 1f / 3f)
                    : index / (float)(verticalMullionCount + 1) - 0.5f;
                CreateCube("Mullion", root, centre + new Vector3(0f, 0f, offset * panelSize.z), new Vector3(depth, panelSize.y + 0.14f, 0.075f), frame);
            }
        }
        else
        {
            CreateCube("Frame_Top", root, centre + new Vector3(0f, panelSize.y * 0.5f, 0f), new Vector3(panelSize.x + 0.16f, 0.11f, depth), frame);
            CreateCube("Frame_Bottom", root, centre - new Vector3(0f, panelSize.y * 0.5f, 0f), new Vector3(panelSize.x + 0.16f, 0.11f, depth), frame);
            CreateCube("Frame_Left", root, centre - new Vector3(panelSize.x * 0.5f, 0f, 0f), new Vector3(0.10f, panelSize.y + 0.14f, depth), frame);
            CreateCube("Frame_Right", root, centre + new Vector3(panelSize.x * 0.5f, 0f, 0f), new Vector3(0.10f, panelSize.y + 0.14f, depth), frame);
            for (var index = 1; index <= verticalMullionCount; index++)
            {
                var offset = verticalMullionCount == 2
                    ? (index == 1 ? -1f / 3f : 1f / 3f)
                    : index / (float)(verticalMullionCount + 1) - 0.5f;
                CreateCube("Mullion", root, centre + new Vector3(offset * panelSize.x, 0f, 0f), new Vector3(0.075f, panelSize.y + 0.14f, depth), frame);
            }
        }
    }

    private static void AddMainRoofSkylights(Transform parent, Transform environment, Bounds wallBounds, float roofRise, Material skylight, Material frame)
    {
        var halfZ = wallBounds.extents.z + 0.32f;
        var zOffset = -halfZ * 0.41f;
        var roofY = wallBounds.max.y + 0.03f + roofRise * (1f - Mathf.Abs(zOffset) / halfZ) + 0.08f;
        var slopeAngle = Mathf.Atan2(roofRise, halfZ) * Mathf.Rad2Deg;
        var lensSize = new Vector3(Mathf.Min(2.0f, wallBounds.size.x * 0.042f), 0.10f, Mathf.Min(1.05f, wallBounds.size.z * 0.21f));
        foreach (var xFactor in new[] { -0.375f, -0.229f, -0.083f, 0.083f, 0.229f, 0.375f })
        {
            var skylightPosition = new Vector3(wallBounds.center.x + wallBounds.size.x * xFactor, roofY, wallBounds.center.z + zOffset);
            // The two outer roof skylights intersect the new hip connections.
            // Omit only any unit that actually enters that roof footprint.
            if (DoesMainSkylightIntersectSideRoofConnection(environment, skylightPosition, lensSize))
            {
                continue;
            }

            AddSkylight(parent, skylightPosition, lensSize, Quaternion.Euler(-slopeAngle, 0f, 0f), skylight, frame);
        }
    }

    private static bool DoesMainSkylightIntersectSideRoofConnection(Transform environment, Vector3 skylightPosition, Vector3 skylightSize)
    {
        if (!TryGetMainHallWallBounds(environment, out var mainWallBounds))
        {
            return false;
        }

        const float roofOverhang = 0.32f;
        foreach (var sideName in new[] { "West_Hall", "East_Hall" })
        {
            var sideHall = environment.Find(sideName);
            var sideWalls = sideHall != null ? sideHall.Find("Walls") : null;
            var sideRenderer = sideWalls != null ? sideWalls.GetComponent<Renderer>() : null;
            if (sideRenderer == null)
            {
                continue;
            }

            var sideBounds = sideRenderer.bounds;
            if (GetSideRoofConnectionDirection(sideHall, sideBounds, false) != 1)
            {
                continue;
            }

            var joinZ = sideBounds.max.z + roofOverhang;
            var tipZ = mainWallBounds.center.z;
            var halfSkylightZ = skylightSize.z * 0.5f + 0.10f;
            if (skylightPosition.z + halfSkylightZ < joinZ || skylightPosition.z - halfSkylightZ > tipZ)
            {
                continue;
            }

            var alongConnection = Mathf.Clamp01((skylightPosition.z - joinZ) / (tipZ - joinZ));
            var halfConnectionWidth = (sideBounds.extents.x + roofOverhang) * (1f - alongConnection);
            var halfSkylightX = skylightSize.x * 0.5f + 0.12f;
            if (skylightPosition.x + halfSkylightX >= sideBounds.center.x - halfConnectionWidth &&
                skylightPosition.x - halfSkylightX <= sideBounds.center.x + halfConnectionWidth)
            {
                return true;
            }
        }

        return false;
    }

    private static void AddSideRoofSkylights(Transform parent, Bounds wallBounds, float roofRise, float inwardSide, Material skylight, Material frame)
    {
        var halfX = wallBounds.extents.x + 0.32f;
        var xOffset = inwardSide * halfX * 0.355f;
        var roofY = wallBounds.max.y + 0.03f + roofRise * (1f - Mathf.Abs(xOffset) / halfX) + 0.08f;
        var slopeAngle = Mathf.Atan2(roofRise, halfX) * Mathf.Rad2Deg;
        // The roof slopes down away from its central ridge.  The old sign here
        // pointed the side skylights into the roof instead of normal to it.
        var rotation = Quaternion.Euler(0f, 0f, inwardSide > 0f ? -slopeAngle : slopeAngle);
        var lensSize = new Vector3(Mathf.Min(1.05f, wallBounds.size.x * 0.21f), 0.10f, Mathf.Min(2.0f, wallBounds.size.z * 0.067f));
        foreach (var zFactor in new[] { -0.367f, -0.133f, 0.133f, 0.367f })
        {
            AddSkylight(parent, new Vector3(wallBounds.center.x + xOffset, roofY, wallBounds.center.z + wallBounds.size.z * zFactor), lensSize, rotation, skylight, frame);
        }
    }

    private static void AddSkylight(Transform parent, Vector3 position, Vector3 lensSize, Quaternion rotation, Material skylight, Material frame)
    {
        var root = new GameObject("Roof_Skylight").transform;
        root.SetParent(parent);
        root.position = position;
        root.rotation = rotation;
        var housing = CreateCube("Housing", root, position, lensSize + new Vector3(0.20f, 0.08f, 0.20f), frame);
        housing.transform.localPosition = Vector3.zero;
        housing.transform.localRotation = Quaternion.identity;
        var lens = CreateCube("Lens", root, position, lensSize, skylight);
        lens.transform.localPosition = new Vector3(0f, 0.075f, 0f);
        lens.transform.localRotation = Quaternion.identity;
    }

    private static void AddLoadingWindow(Transform parent, float x, Material dark, Material frame, Material hazard)
    {
        var loadingWindow = new GameObject(x < 0f ? "Loading_Window_Left" : "Loading_Window_Right").transform;
        loadingWindow.SetParent(parent);
        const float z = 14.86f;
        // This is a low loading slot, centred on the 2.04 m belt entry rather than a full-height door.
        const float openingCenterY = 2.18f;
        const float openingHeight = 1.12f;
        CreateCube("Opening", loadingWindow, new Vector3(x, openingCenterY, z), new Vector3(2.45f, openingHeight, 0.24f), dark);
        CreateCube("Top_Frame", loadingWindow, new Vector3(x, openingCenterY + openingHeight * 0.5f + 0.09f, z - 0.14f), new Vector3(2.72f, 0.16f, 0.16f), frame);
        CreateCube("Left_Frame", loadingWindow, new Vector3(x - 1.3f, openingCenterY, z - 0.14f), new Vector3(0.16f, openingHeight + 0.18f, 0.16f), frame);
        CreateCube("Right_Frame", loadingWindow, new Vector3(x + 1.3f, openingCenterY, z - 0.14f), new Vector3(0.16f, openingHeight + 0.18f, 0.16f), frame);
        CreateCube("Safety_Bumper", loadingWindow, new Vector3(x, openingCenterY - openingHeight * 0.5f - 0.11f, 14.62f), new Vector3(2.74f, 0.18f, 0.28f), hazard);
    }

    private static GameObject AddConveyor(GameObject prefab, string name, Transform parent, Vector3 position, bool mirrored)
    {
        if (!mirrored)
        {
            var conveyor = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            conveyor.name = name;
            conveyor.transform.localPosition = position;
            conveyor.transform.localRotation = ConveyorRotation;
            conveyor.transform.localScale = Vector3.one * ConveyorScale;
            return conveyor;
        }

        var mirror = new GameObject(name).transform;
        mirror.SetParent(parent);
        mirror.localPosition = position;
        mirror.localRotation = Quaternion.Euler(0f, 180f, 0f);
        mirror.localScale = new Vector3(-1f, 1f, 1f);

        var mirroredConveyor = (GameObject)PrefabUtility.InstantiatePrefab(prefab, mirror);
        mirroredConveyor.name = "Conveyor_Model";
        mirroredConveyor.transform.localPosition = Vector3.zero;
        mirroredConveyor.transform.localRotation = Quaternion.Euler(270f, 90f, 0f);
        mirroredConveyor.transform.localScale = Vector3.one * ConveyorScale;
        return mirror.gameObject;
    }

    private static void HideDemoPackages(GameObject conveyor)
    {
        foreach (var child in conveyor.GetComponentsInChildren<Transform>(true))
        {
            if (child.name.StartsWith("Package_"))
            {
                child.gameObject.SetActive(false);
            }
        }
    }

    private static void AddForklift(GameObject prefab, Transform parent)
    {
        var controllerRoot = new GameObject("Forklift_Controller");
        controllerRoot.transform.SetParent(parent, false);
        controllerRoot.transform.localPosition = new Vector3(0f, 0.03f, -10f);
        controllerRoot.transform.localRotation = Quaternion.identity;

        var forklift = (GameObject)PrefabUtility.InstantiatePrefab(prefab, controllerRoot.transform);
        forklift.name = "Forklift_Courtyard_Centre";
        forklift.transform.localPosition = Vector3.zero;
        forklift.transform.localRotation = Quaternion.Euler(270f, 180f, 0f);
        forklift.transform.localScale = Vector3.one * 50f;
        controllerRoot.AddComponent<ForkliftController>();
        EnsureForkliftCollision(controllerRoot);
        EnsureForkliftLoadArea(controllerRoot);
    }

    private static GameObject EnsureForkliftControllerRoot(GameObject forklift)
    {
        var existingParent = forklift.transform.parent;
        if (existingParent != null && existingParent.name == "Forklift_Controller")
        {
            return existingParent.gameObject;
        }

        var controllerRoot = new GameObject("Forklift_Controller");
        Undo.RegisterCreatedObjectUndo(controllerRoot, "Create forklift controller");
        controllerRoot.transform.SetParent(existingParent, false);
        controllerRoot.transform.position = forklift.transform.position;
        controllerRoot.transform.rotation = Quaternion.identity;
        controllerRoot.transform.localScale = Vector3.one;
        forklift.transform.SetParent(controllerRoot.transform, true);
        return controllerRoot;
    }

    private static void EnsureForkliftCollision(GameObject controllerRoot)
    {
        var collisionController = controllerRoot.GetComponent<CharacterController>();
        if (collisionController == null)
        {
            collisionController = Undo.AddComponent<CharacterController>(controllerRoot);
        }

        var visualModel = controllerRoot.transform.Find("Forklift_Courtyard_Centre");
        var renderers = visualModel != null
            ? visualModel.GetComponentsInChildren<Renderer>(true)
            : controllerRoot.GetComponentsInChildren<Renderer>(true);

        var hasBounds = false;
        var modelBounds = new Bounds();
        foreach (var renderer in renderers)
        {
            if (!renderer.enabled)
            {
                continue;
            }

            if (hasBounds)
            {
                modelBounds.Encapsulate(renderer.bounds);
            }
            else
            {
                modelBounds = renderer.bounds;
                hasBounds = true;
            }
        }

        if (hasBounds)
        {
            var localCentre = controllerRoot.transform.InverseTransformPoint(modelBounds.center);
            var footprint = Mathf.Min(modelBounds.size.x, modelBounds.size.z);
            collisionController.radius = Mathf.Clamp(footprint * 0.34f, 0.42f, 0.70f);
            collisionController.height = Mathf.Max(
                collisionController.radius * 2f + 0.02f,
                Mathf.Clamp(modelBounds.size.y * 0.90f, 1.60f, 2.60f));
            collisionController.center = localCentre;
        }
        else
        {
            collisionController.radius = 0.55f;
            collisionController.height = 2.0f;
            collisionController.center = new Vector3(0f, 1.0f, 0f);
        }

        collisionController.skinWidth = 0.04f;
        collisionController.stepOffset = Mathf.Min(0.25f, collisionController.height * 0.20f);
        collisionController.slopeLimit = 45f;
        collisionController.detectCollisions = true;
    }

    private static ForkliftLoadArea EnsureForkliftLoadArea(GameObject controllerRoot)
    {
        var anchor = controllerRoot.transform.Find("Forklift_Load_Area");
        if (anchor == null)
        {
            anchor = new GameObject("Forklift_Load_Area").transform;
            anchor.SetParent(controllerRoot.transform, false);
            // The imported forklift's visible front and fork tines point down
            // the controller's local -Z axis.
            anchor.localPosition = new Vector3(0f, 0.42f, -1.64f);
            anchor.localRotation = Quaternion.identity;
        }

        var loadArea = controllerRoot.GetComponent<ForkliftLoadArea>();
        if (loadArea == null)
        {
            loadArea = Undo.AddComponent<ForkliftLoadArea>(controllerRoot);
        }

        loadArea.loadAnchor = anchor;
        return loadArea;
    }

    private static void AddRuntimeBoxFlow(Transform root, Transform boxes, Material cardboard, Material tape)
    {
        var runtime = new GameObject("Runtime_Box_Flow").transform;
        runtime.SetParent(root);
        var dropCoordinator = runtime.gameObject.AddComponent<ConveyorDropCoordinator>();
        dropCoordinator.minimumDelayBetweenFalls = 4f;
        dropCoordinator.maximumDelayBetweenFalls = 7f;
        dropCoordinator.minimumDispatchGap = 0.55f;
        dropCoordinator.maximumPackagesPerRound = 30;
        var boxPrefab = CreateBoxPrefab(runtime, cardboard, tape);

        var leftDiverter = ConfigureDiverter(root.Find("Conveyors/Conveyor_Left"));
        var rightDiverter = ConfigureDiverter(root.Find("Conveyors/Conveyor_Right_Mirrored"));

        var leftA = CreatePath(runtime, "Left_Branch_A", new[]
        {
            LeftPoint(-7.85f, 0f, 2.23f),
            LeftPoint(-4.8f, 0f, 2.02f),
            LeftPoint(-2.2f, 0f, 1.89f),
            LeftPoint(-1.24f, 0.54f, 1.832f),
            LeftPoint(2.0f, 0.54f, 1.667f),
            LeftPoint(5.0f, 0.54f, 1.515f),
            LeftPoint(8.0f, 0.54f, 1.362f),
            LeftPoint(9.131f, 1.011f, 1.298f),
            LeftPoint(9.6f, 3f, 1.19f),
        });
        var leftB = CreatePath(runtime, "Left_Branch_B", new[]
        {
            LeftPoint(-7.85f, 0f, 2.23f),
            LeftPoint(-4.8f, 0f, 2.02f),
            LeftPoint(-2.2f, 0f, 1.89f),
            LeftPoint(-1.24f, -0.54f, 1.856f),
            LeftPoint(3.0f, -0.54f, 1.729f),
            LeftPoint(7.0f, -0.54f, 1.609f),
            LeftPoint(11.0f, -0.54f, 1.489f),
            LeftPoint(15.0f, -0.54f, 1.369f),
            LeftPoint(16.5f, -0.54f, 1.324f),
            LeftPoint(17.631f, -0.069f, 1.286f),
            LeftPoint(18.1f, 3f, 1.19f),
        });
        var rightA = CreatePath(runtime, "Right_Branch_A", new[]
        {
            RightPoint(-7.85f, 0f, 2.23f),
            RightPoint(-4.8f, 0f, 2.02f),
            RightPoint(-2.2f, 0f, 1.89f),
            RightPoint(-1.24f, 0.54f, 1.832f),
            RightPoint(2.0f, 0.54f, 1.667f),
            RightPoint(5.0f, 0.54f, 1.515f),
            RightPoint(8.0f, 0.54f, 1.362f),
            RightPoint(9.131f, 1.011f, 1.298f),
            RightPoint(9.6f, 3f, 1.19f),
        });
        var rightB = CreatePath(runtime, "Right_Branch_B", new[]
        {
            RightPoint(-7.85f, 0f, 2.23f),
            RightPoint(-4.8f, 0f, 2.02f),
            RightPoint(-2.2f, 0f, 1.89f),
            RightPoint(-1.24f, -0.54f, 1.856f),
            RightPoint(3.0f, -0.54f, 1.729f),
            RightPoint(7.0f, -0.54f, 1.609f),
            RightPoint(11.0f, -0.54f, 1.489f),
            RightPoint(15.0f, -0.54f, 1.369f),
            RightPoint(16.5f, -0.54f, 1.324f),
            RightPoint(17.631f, -0.069f, 1.286f),
            RightPoint(18.1f, 3f, 1.19f),
        });

        var leftSpawner = new GameObject("Box_Generator_Left").AddComponent<ConveyorBoxSpawner>();
        leftSpawner.transform.SetParent(runtime);
        leftSpawner.packagePrefab = boxPrefab;
        leftSpawner.branchPathA = leftA;
        leftSpawner.branchPathB = leftB;
        leftSpawner.packageParent = boxes;
        leftSpawner.dispatchCoordinator = dropCoordinator;
        leftSpawner.diverter = leftDiverter;

        var rightSpawner = new GameObject("Box_Generator_Right").AddComponent<ConveyorBoxSpawner>();
        rightSpawner.transform.SetParent(runtime);
        rightSpawner.packagePrefab = boxPrefab;
        rightSpawner.branchPathA = rightA;
        rightSpawner.branchPathB = rightB;
        rightSpawner.packageParent = boxes;
        rightSpawner.dispatchCoordinator = dropCoordinator;
        rightSpawner.diverter = rightDiverter;

        dropCoordinator.leftSpawner = leftSpawner;
        dropCoordinator.rightSpawner = rightSpawner;
        RebindDeliveryTruckCoordinator(root);
    }

    private static void RebindDeliveryTruckCoordinator(Transform root)
    {
        var truck = root != null ? root.Find("Delivery_Truck_Controller") : null;
        var coordinator = root != null ? root.Find("Runtime_Box_Flow")?.GetComponent<ConveyorDropCoordinator>() : null;
        var controller = truck != null ? truck.GetComponent<DeliveryTruckController>() : null;
        if (controller == null || coordinator == null)
        {
            return;
        }

        controller.coordinator = coordinator;
        EditorUtility.SetDirty(controller);
    }

    private static void EnsureLevelIntroOverlay(Transform root)
    {
        if (root == null)
        {
            return;
        }

        var intro = root.GetComponent<LevelIntroOverlay>();
        if (intro == null)
        {
            intro = Undo.AddComponent<LevelIntroOverlay>(root.gameObject);
        }

        intro.duration = 3f;
        intro.levelLabel = "Level 1";
        intro.objectiveLabel = "25/30\nto pass";
        EditorUtility.SetDirty(intro);
    }

    private static LevelOutcomeOverlay EnsureLevelOutcomeOverlay(Transform root)
    {
        if (root == null)
        {
            return null;
        }

        var outcome = root.GetComponent<LevelOutcomeOverlay>();
        if (outcome == null)
        {
            outcome = Undo.AddComponent<LevelOutcomeOverlay>(root.gameObject);
        }

        outcome.requiredLoadedPackages = 25;
        outcome.nextLevelPromptDelay = 2f;
        outcome.completedLabel = "Level completed";
        outcome.nextLevelLabel = "NEXT LEVEL";
        outcome.failedLabel = "You failed!";
        outcome.restartLabel = "RESTART";
        EditorUtility.SetDirty(outcome);
        return outcome;
    }

    private static GamePauseOverlay EnsureGamePauseOverlay(Transform root)
    {
        if (root == null)
        {
            return null;
        }

        var pause = root.GetComponent<GamePauseOverlay>();
        if (pause == null)
        {
            pause = Undo.AddComponent<GamePauseOverlay>(root.gameObject);
        }

        pause.pausedLabel = "Paused";
        pause.continueLabel = "Continue";
        pause.restartLabel = "Restart";
        EditorUtility.SetDirty(pause);
        return pause;
    }

    private static void EnsureFactoryYardInBuildSettings()
    {
        var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        for (var index = 0; index < scenes.Count; index++)
        {
            if (scenes[index].path != ScenePath)
            {
                continue;
            }

            if (!scenes[index].enabled)
            {
                scenes[index] = new EditorBuildSettingsScene(ScenePath, true);
                EditorBuildSettings.scenes = scenes.ToArray();
            }

            return;
        }

        scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }

    private static ConveyorDiverter ConfigureDiverter(Transform conveyor)
    {
        var pivot = FindDescendant(conveyor, "Diverter_Pivot");
        if (pivot == null)
        {
            Debug.LogWarning("The conveyor diverter pivot could not be found.");
            return null;
        }

        var diverter = pivot.GetComponent<ConveyorDiverter>();
        if (diverter == null)
        {
            diverter = pivot.gameObject.AddComponent<ConveyorDiverter>();
        }

        // Both imported pivots use the same local orientation. The parent of
        // the right conveyor is mirrored already, so its flap must keep the
        // same A/B local angles as the left one.
        diverter.branchALocalZ = -29f;
        diverter.branchBLocalZ = 29f;
        diverter.rotationSpeedDegrees = 360f;
        return diverter;
    }

    private static void EnsureConveyorCollision(GameObject conveyor)
    {
        if (conveyor == null || conveyor.transform.parent == null)
        {
            return;
        }

        var renderers = conveyor.GetComponentsInChildren<Renderer>(true);
        var hasBounds = false;
        var conveyorBounds = new Bounds();
        foreach (var renderer in renderers)
        {
            if (!renderer.enabled)
            {
                continue;
            }

            if (hasBounds)
            {
                conveyorBounds.Encapsulate(renderer.bounds);
            }
            else
            {
                conveyorBounds = renderer.bounds;
                hasBounds = true;
            }
        }

        if (!hasBounds)
        {
            return;
        }

        var collisionName = $"{conveyor.name}_Solid_Collider";
        var collisionRoot = conveyor.transform.parent.Find(collisionName);
        if (collisionRoot == null)
        {
            collisionRoot = new GameObject(collisionName).transform;
            collisionRoot.SetParent(conveyor.transform.parent, true);
        }

        collisionRoot.position = conveyorBounds.center;
        collisionRoot.rotation = Quaternion.identity;
        collisionRoot.localScale = Vector3.one;
        var boxCollider = collisionRoot.GetComponent<BoxCollider>();
        if (boxCollider == null)
        {
            boxCollider = collisionRoot.gameObject.AddComponent<BoxCollider>();
        }

        boxCollider.center = Vector3.zero;
        boxCollider.size = new Vector3(
            conveyorBounds.size.x + 0.08f,
            conveyorBounds.size.y + 0.08f,
            conveyorBounds.size.z + 0.08f);
        boxCollider.isTrigger = false;
    }

    private static void EnsureForkliftLoadCounter(Transform root, Material dark, Material frame)
    {
        var environment = root.Find("Environment");
        if (environment == null)
        {
            return;
        }

        var counterRoot = environment.Find("Forklift_Load_Counter");
        if (counterRoot == null)
        {
            counterRoot = new GameObject("Forklift_Load_Counter").transform;
            counterRoot.SetParent(environment, false);
        }

        // Position the board directly above the centre truck gate, on the
        // courtyard face of the north hall.
        var wallRenderer = environment.Find("North_Main_Hall/Walls")?.GetComponent<Renderer>();
        var facadeZ = wallRenderer != null ? wallRenderer.bounds.min.z - 0.16f : 14.78f;
        counterRoot.position = new Vector3(0f, 7.55f, facadeZ);
        counterRoot.rotation = Quaternion.identity;

        const float boardWidth = 3.1f;
        const float boardHeight = 1.30f;
        EnsureCounterBoardPiece(counterRoot, "Panel", Vector3.zero, new Vector3(boardWidth, boardHeight, 0.14f), dark);
        var trimDepth = 0.18f;
        var trimZ = -0.12f;
        EnsureCounterBoardPiece(counterRoot, "Frame_Top", new Vector3(0f, boardHeight * 0.5f, trimZ), new Vector3(boardWidth + 0.16f, 0.10f, trimDepth), frame);
        EnsureCounterBoardPiece(counterRoot, "Frame_Bottom", new Vector3(0f, -boardHeight * 0.5f, trimZ), new Vector3(boardWidth + 0.16f, 0.10f, trimDepth), frame);
        EnsureCounterBoardPiece(counterRoot, "Frame_Left", new Vector3(-boardWidth * 0.5f, 0f, trimZ), new Vector3(0.10f, boardHeight + 0.16f, trimDepth), frame);
        EnsureCounterBoardPiece(counterRoot, "Frame_Right", new Vector3(boardWidth * 0.5f, 0f, trimZ), new Vector3(0.10f, boardHeight + 0.16f, trimDepth), frame);

        var textTransform = counterRoot.Find("Text");
        if (textTransform == null)
        {
            textTransform = new GameObject("Text").transform;
            textTransform.SetParent(counterRoot, false);
        }

        textTransform.localPosition = new Vector3(0f, 0f, -0.16f);
        textTransform.localRotation = Quaternion.identity;
        var textMesh = textTransform.GetComponent<TextMesh>();
        if (textMesh == null)
        {
            textMesh = textTransform.gameObject.AddComponent<TextMesh>();
        }

        textMesh.text = "00";
        textMesh.anchor = TextAnchor.MiddleCenter;
        textMesh.alignment = TextAlignment.Center;
        textMesh.characterSize = 0.20f;
        textMesh.fontSize = 84;
        // Dark red resembles a classic hockey score display without looking
        // like an orange warning label.
        textMesh.color = new Color(0.62f, 0.018f, 0.008f);
        textMesh.fontStyle = FontStyle.Bold;

        var counter = textTransform.GetComponent<ForkliftLoadCounter>();
        if (counter == null)
        {
            counter = textTransform.gameObject.AddComponent<ForkliftLoadCounter>();
        }

        counter.counterText = textMesh;
    }

    private static void EnsureCounterBoardPiece(Transform parent, string name, Vector3 localPosition, Vector3 size, Material material)
    {
        var piece = parent.Find(name);
        if (piece == null)
        {
            piece = CreateCube(name, parent, parent.TransformPoint(localPosition), size, material).transform;
        }

        piece.localPosition = localPosition;
        piece.localRotation = Quaternion.identity;
        piece.localScale = size;
        var renderer = piece.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = material;
        }
    }

    private static Transform FindDescendant(Transform parent, string name)
    {
        if (parent == null)
        {
            return null;
        }

        foreach (var child in parent.GetComponentsInChildren<Transform>(true))
        {
            if (child.name == name)
            {
                return child;
            }
        }

        return null;
    }

    private static GameObject CreateBoxPrefab(Transform parent, Material cardboard, Material tape)
    {
        var root = new GameObject("Generated_Box_Prefab");
        root.transform.SetParent(parent);
        root.transform.localPosition = new Vector3(0f, -10f, 0f);

        var body = CreateCube("Cardboard_Box", root.transform, root.transform.position, new Vector3(0.62f, 0.36f, 0.44f), cardboard);
        body.transform.localPosition = Vector3.zero;
        var tapeStrip = CreateCube("Packing_Tape", root.transform, root.transform.position, new Vector3(0.12f, 0.018f, 0.46f), tape);
        tapeStrip.transform.localPosition = new Vector3(0f, 0.19f, 0f);
        root.SetActive(false);
        return root;
    }

    private static Transform[] CreatePath(Transform parent, string name, Vector3[] points)
    {
        var pathRoot = new GameObject(name).transform;
        pathRoot.SetParent(parent);
        var result = new Transform[points.Length];

        for (var index = 0; index < points.Length; index++)
        {
            var point = new GameObject($"Point_{index + 1:00}").transform;
            point.SetParent(pathRoot);
            point.position = points[index];
            result[index] = point;
        }

        return result;
    }

    private static Vector3 LeftPoint(float localX, float localY, float height)
    {
        return new Vector3(LeftConveyorOrigin.x + localY, height, LeftConveyorOrigin.z - localX);
    }

    private static Vector3 RightPoint(float localX, float localY, float height)
    {
        return new Vector3(RightConveyorOrigin.x - localY, height, RightConveyorOrigin.z - localX);
    }

    private static void AddLightingAndCamera(Transform parent)
    {
        var sun = new GameObject("Directional_Light").AddComponent<Light>();
        sun.transform.SetParent(parent);
        sun.type = LightType.Directional;
        sun.intensity = 1.15f;
        sun.shadows = LightShadows.Soft;
        sun.transform.rotation = Quaternion.Euler(52f, -28f, 0f);

        var cameraObject = new GameObject("Main Camera");
        cameraObject.transform.SetParent(parent);
        cameraObject.tag = "MainCamera";
        cameraObject.transform.position = new Vector3(0f, 28f, -31f);
        cameraObject.transform.rotation = Quaternion.LookRotation(new Vector3(0f, 2.4f, 4f) - cameraObject.transform.position);
        var camera = cameraObject.AddComponent<Camera>();
        camera.fieldOfView = 52f;
        camera.clearFlags = CameraClearFlags.Skybox;
        EnsureCameraZoom(camera, GetConveyorEndTargets(parent));

        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.62f, 0.67f, 0.73f);
    }

    private static CameraZoomController EnsureCameraZoom(Transform root)
    {
        if (root == null)
        {
            return null;
        }

        return EnsureCameraZoom(
            root.GetComponentInChildren<Camera>(true),
            GetConveyorEndTargets(root));
    }

    private static CameraZoomController EnsureCameraZoom(Camera camera, Transform[] conveyorEnds = null)
    {
        if (camera == null)
        {
            return null;
        }

        // The truck is spawned behind the north hall and then crosses the
        // doorway. Keeping occlusion culling off avoids an out-of-date baked
        // hall occluder hiding this dynamic vehicle during its approach.
        camera.useOcclusionCulling = false;

        var zoom = camera.GetComponent<CameraZoomController>();
        if (zoom == null)
        {
            zoom = camera.gameObject.AddComponent<CameraZoomController>();
        }

        var focus = GetCurrentCameraFocus(camera);
        zoom.ConfigureFraming(
            focus,
            conveyorEnds ?? zoom.framingTargets,
            18f,
            Vector3.Distance(camera.transform.position, focus));
        zoom.closeViewPitchOffset = 8f;
        zoom.closeViewFieldOfView = 60f;
        zoom.maximumFieldOfView = 66f;
        zoom.framingFill = 0.88f;

        return zoom;
    }

    private static Transform[] GetConveyorEndTargets(Transform root)
    {
        if (root == null)
        {
            return new Transform[0];
        }

        var runtime = root.Find("Runtime_Box_Flow");
        if (runtime == null)
        {
            return new Transform[0];
        }

        var targetPaths = new[]
        {
            "Left_Branch_A/Point_09",
            "Left_Branch_B/Point_11",
            "Right_Branch_A/Point_09",
            "Right_Branch_B/Point_11",
        };
        var targets = new System.Collections.Generic.List<Transform>(targetPaths.Length);
        foreach (var path in targetPaths)
        {
            var target = runtime.Find(path);
            if (target != null)
            {
                targets.Add(target);
            }
        }

        return targets.ToArray();
    }

    private static Vector3 GetCurrentCameraFocus(Camera camera)
    {
        if (camera == null || Mathf.Abs(camera.transform.forward.y) < 0.001f)
        {
            return CameraFocusPoint;
        }

        const float focusHeight = 2.4f;
        var distance = (focusHeight - camera.transform.position.y) / camera.transform.forward.y;
        return distance > 0f
            ? camera.transform.position + camera.transform.forward * distance
            : CameraFocusPoint;
    }

    private static GameObject CreateCube(string name, Transform parent, Vector3 position, Vector3 size, Material material)
    {
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = name;
        cube.transform.SetParent(parent);
        cube.transform.position = position;
        cube.transform.localScale = size;
        cube.GetComponent<MeshRenderer>().sharedMaterial = material;
        return cube;
    }

    // Visual trim deliberately has no collider: it improves the façade without
    // changing the truck or forklift's existing collision paths.
    private static GameObject CreateVisualCube(string name, Transform parent, Vector3 position, Vector3 size, Material material)
    {
        var cube = CreateCube(name, parent, position, size, material);
        var collider = cube.GetComponent<Collider>();
        if (collider != null)
        {
            Object.DestroyImmediate(collider);
        }

        return cube;
    }

    private static Material GetMaterial(string name, Color color, float smoothness, float metallic)
    {
        var path = $"{MaterialFolder}/{name}.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            material = new Material(shader) { name = name };
            AssetDatabase.CreateAsset(material, path);
        }

        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }
        else if (material.HasProperty("_Color"))
        {
            material.color = color;
        }

        if (material.HasProperty("_Smoothness"))
        {
            material.SetFloat("_Smoothness", smoothness);
        }

        if (material.HasProperty("_Metallic"))
        {
            material.SetFloat("_Metallic", metallic);
        }

        EditorUtility.SetDirty(material);
        return material;
    }

    // The roof is intentionally unaffected by directional lighting.  Apart
    // from keeping the requested light-grey colour consistent over both
    // slopes, this prevents the corrugation from making an unintended broad
    // dark-to-light gradient.
    private static Material GetUniformRoofMaterial(string name, Color color)
    {
        var path = $"{MaterialFolder}/{name}.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");

        if (material == null)
        {
            material = new Material(shader ?? Shader.Find("Standard")) { name = name };
            AssetDatabase.CreateAsset(material, path);
        }
        else if (shader != null && material.shader != shader)
        {
            material.shader = shader;
        }

        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }
        else if (material.HasProperty("_Color"))
        {
            material.color = color;
        }

        EnsureOpaqueBuildingMaterial(material);
        EditorUtility.SetDirty(material);
        return material;
    }

    private static void EnsureOpaqueBuildingMaterial(Material material)
    {
        if (material == null)
        {
            return;
        }

        if (material.HasProperty("_BaseColor"))
        {
            var color = material.GetColor("_BaseColor");
            color.a = 1f;
            material.SetColor("_BaseColor", color);
        }
        else if (material.HasProperty("_Color"))
        {
            var color = material.color;
            color.a = 1f;
            material.color = color;
        }

        // These are URP's opaque-material settings. The property checks keep
        // the method compatible with the Standard shader as a fallback.
        if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 0f);
        if (material.HasProperty("_Blend")) material.SetFloat("_Blend", 0f);
        if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 1f);
        material.SetOverrideTag("RenderType", "Opaque");
        material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.EnableKeyword("_SURFACE_TYPE_OPAQUE");
        material.renderQueue = (int)RenderQueue.Geometry;
        EditorUtility.SetDirty(material);
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
        {
            return;
        }

        var parent = System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/');
        var folderName = System.IO.Path.GetFileName(path);
        if (!string.IsNullOrEmpty(parent) && !string.IsNullOrEmpty(folderName))
        {
            AssetDatabase.CreateFolder(parent, folderName);
        }
    }
}
