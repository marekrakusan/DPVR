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
        var hallYellow = GetMaterial("Factory_Yellow", new Color(0.92f, 0.57f, 0.06f), 0.42f, 0.05f);
        var roofGray = GetMaterial("Roof_Gray", new Color(0.20f, 0.23f, 0.27f), 0.6f, 0.2f);
        var dark = GetMaterial("Door_And_Window", new Color(0.035f, 0.045f, 0.055f), 0.30f, 0.1f);
        var frame = GetMaterial("Window_Frame", new Color(0.47f, 0.51f, 0.55f), 0.35f, 0.45f);
        var skylight = GetMaterial("Skylight_Glass", new Color(0.52f, 0.68f, 0.78f), 0.88f, 0.12f);
        var hazard = GetMaterial("Safety_Yellow", new Color(0.97f, 0.65f, 0.05f), 0.35f, 0.1f);
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
        var roofGray = GetMaterial("Roof_Gray", new Color(0.20f, 0.23f, 0.27f), 0.6f, 0.2f);
        var dark = GetMaterial("Door_And_Window", new Color(0.035f, 0.045f, 0.055f), 0.30f, 0.1f);
        var frame = GetMaterial("Window_Frame", new Color(0.47f, 0.51f, 0.55f), 0.35f, 0.45f);
        var skylight = GetMaterial("Skylight_Glass", new Color(0.52f, 0.68f, 0.78f), 0.88f, 0.12f);

        AddBuildingDetails(environment, roofGray, dark, frame, skylight);
        EditorSceneManager.MarkSceneDirty(root.scene);
        EditorSceneManager.SaveScene(root.scene);
        Selection.activeGameObject = environment.gameObject;
        Debug.Log("Upgraded only the factory roofs, wall windows and roof skylights.");
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
        var hallYellow = GetMaterial("Factory_Yellow", new Color(0.92f, 0.57f, 0.06f), 0.42f, 0.05f);
        var roofGray = GetMaterial("Roof_Gray", new Color(0.20f, 0.23f, 0.27f), 0.6f, 0.2f);
        var dark = GetMaterial("Door_And_Window", new Color(0.035f, 0.045f, 0.055f), 0.30f, 0.1f);
        var frame = GetMaterial("Window_Frame", new Color(0.47f, 0.51f, 0.55f), 0.35f, 0.45f);
        var skylight = GetMaterial("Skylight_Glass", new Color(0.52f, 0.68f, 0.78f), 0.88f, 0.12f);

        EnsureOpaqueBuildingMaterial(hallYellow);
        EnsureOpaqueBuildingMaterial(roofGray);
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
        var roofGray = GetMaterial("Roof_Gray", new Color(0.20f, 0.23f, 0.27f), 0.6f, 0.2f);
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

        var hallYellow = GetMaterial("Factory_Yellow", new Color(0.92f, 0.57f, 0.06f), 0.42f, 0.05f);
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
        controller.cargoCover = FindDescendant(controller.truckVisual, "Cargo_Tarp")?.gameObject;
        controller.gate = gate;
        controller.gateEntryPosition = new Vector3(0f, 0f, 10.7f);
        controller.loadingStopPosition = new Vector3(-3f, 0f, 8.5f);
        controller.loadingStopYaw = 50f;
        controller.reverseTurnPosition = new Vector3(0.2f, 0f, 11.2f);
        controller.exitPosition = new Vector3(0.2f, 0f, truckStartZ);

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
        UpgradeHallExterior(environment.Find("North_Main_Hall"), roof, window, frame, skylight);
        UpgradeHallExterior(environment.Find("West_Hall"), roof, window, frame, skylight);
        UpgradeHallExterior(environment.Find("East_Hall"), roof, window, frame, skylight);
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

    private static void UpgradeHallExterior(Transform hall, Material roof, Material window, Material frame, Material skylight)
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
        CreateGabledRoof("Gabled_Roof", details, wallBounds, ridgeAlongX, roof);

        if (ridgeAlongX)
        {
            AddFrontWindows(details, position, size, window, frame);
            AddMainRoofSkylights(details, wallBounds, skylight, frame);
        }
        else
        {
            var inwardSide = position.x < 0f ? 1f : -1f;
            AddSideWindows(details, position, size, window, frame);
            AddSideRoofSkylights(details, wallBounds, inwardSide, skylight, frame);
        }
    }

    private static void CreateGabledRoof(string name, Transform parent, Bounds wallBounds, bool ridgeAlongX, Material material)
    {
        var halfX = wallBounds.extents.x + 0.32f;
        var halfZ = wallBounds.extents.z + 0.32f;
        // Keep the mesh local to an origin on the real top edge of the walls.
        // This avoids inheriting an outdated hall-root position or height.
        const float eaveY = 0f;
        const float ridgeY = 1.35f;
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

        var triangles = ridgeAlongX
            ? new[] { 0, 1, 5, 0, 5, 4, 4, 5, 3, 4, 3, 2, 0, 4, 2, 1, 3, 5, 0, 2, 3, 0, 3, 1 }
            : new[] { 0, 4, 5, 0, 5, 2, 4, 1, 3, 4, 3, 5, 0, 1, 4, 2, 5, 3, 0, 2, 3, 0, 3, 1 };

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

    private static void AddFrontWindows(Transform parent, Vector3 centre, Vector3 size, Material window, Material frame)
    {
        var z = centre.z - size.z * 0.5f - 0.055f;
        // Keep the two windows on each side clear of the adjacent halls and
        // distribute them around the central truck gate instead of clustering
        // the inner pair close to the centre.
        var panelWidth = Mathf.Min(3.15f, size.x * 0.085f);
        var panelHeight = Mathf.Min(1.2f, size.y * 0.135f);
        var windowY = centre.y + size.y * 0.14f;
        foreach (var xFactor in new[] { -0.315f, -0.205f, 0.205f, 0.315f })
        {
            // The main-hall front windows have four even panes: their third
            // vertical mullion is an extra divider requested for the central
            // building, while the side-hall windows keep their existing form.
            AddWindow("Front_Window", parent, new Vector3(centre.x + size.x * xFactor, windowY, z), new Vector3(panelWidth, panelHeight, 0.10f), window, frame, false, 3);
        }
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
            AddWindow("Side_Window", parent, new Vector3(x, windowY, centre.z + size.z * zFactor), new Vector3(0.10f, panelHeight, panelLength), window, frame, true);
        }
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

    private static void AddMainRoofSkylights(Transform parent, Bounds wallBounds, Material skylight, Material frame)
    {
        const float roofRise = 1.35f;
        var halfZ = wallBounds.extents.z + 0.32f;
        var zOffset = -halfZ * 0.41f;
        var roofY = wallBounds.max.y + 0.03f + roofRise * (1f - Mathf.Abs(zOffset) / halfZ) + 0.08f;
        var slopeAngle = Mathf.Atan2(roofRise, halfZ) * Mathf.Rad2Deg;
        var lensSize = new Vector3(Mathf.Min(2.0f, wallBounds.size.x * 0.042f), 0.10f, Mathf.Min(1.05f, wallBounds.size.z * 0.21f));
        foreach (var xFactor in new[] { -0.375f, -0.229f, -0.083f, 0.083f, 0.229f, 0.375f })
        {
            AddSkylight(parent, new Vector3(wallBounds.center.x + wallBounds.size.x * xFactor, roofY, wallBounds.center.z + zOffset), lensSize, Quaternion.Euler(-slopeAngle, 0f, 0f), skylight, frame);
        }
    }

    private static void AddSideRoofSkylights(Transform parent, Bounds wallBounds, float inwardSide, Material skylight, Material frame)
    {
        const float roofRise = 1.35f;
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
