using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Defines the usable loading area above the forklift tines. Parcels that
/// fall into a discharge zone are snapped into a 2 x 3 x 5 stack when this
/// area is directly above that zone.
/// </summary>
[DisallowMultipleComponent]
public sealed class ForkliftLoadArea : MonoBehaviour
{
    private const int Rows = 2;
    private const int Columns = 3;
    private const int Layers = 5;
    private const int PackagesPerLayer = Rows * Columns;

    [Tooltip("Empty child centred over the fork tines. Loaded parcels become its children.")]
    public Transform loadAnchor;

    [Header("Truck loading lift")]
    [Tooltip("The moving fork carriage in the imported model. It is found automatically when left empty.")]
    public Transform forkCarriage;
    [Tooltip("How far the tines and their parcel stack rise above their normal carrying height for truck loading.")]
    [Min(0f)] public float truckLiftHeight = 1.05f;
    [Tooltip("Duration of both the lift and the return stroke during truck loading.")]
    [Min(0.01f)] public float truckLiftDuration = 1f;

    [Header("Full-load reward")]
    [Tooltip("Colour used for the forklift's orange body panels after all 30 boxes are captured.")]
    public Color fullLoadRewardColor = new Color(0.08f, 0.65f, 0.15f, 1f);

    [Header("Capture footprint")]
    [Min(0.01f)] public float captureWidth = 1.40f;
    [Min(0.01f)] public float captureLength = 1.50f;

    [Header("2 rows x 3 fork positions x 5 layers")]
    [Min(0.01f)] public float rowSpacing = 0.64f;
    [Min(0.01f)] public float columnSpacing = 0.50f;
    [Min(0.01f)] public float layerSpacing = 0.38f;

    public const int Capacity = Rows * Columns * Layers;

    public int LoadedPackageCount => loadAnchor == null ? 0 : loadAnchor.childCount;
    public bool IsFull => LoadedPackageCount >= Capacity;
    public bool IsAtTruckLoadingHeight =>
        liftTarget > 0.001f && Mathf.Abs(currentLiftHeight - liftTarget) < 0.01f;

    private Vector3 loadAnchorRestLocalPosition;
    private Vector3 forkCarriageRestControllerLocalPosition;
    private bool hasLiftRestPose;
    private float currentLiftHeight;
    private float liftStartHeight;
    private float liftTarget;
    private float liftElapsed;
    private bool fullLoadRewardApplied;
    private bool fullLoadRewardReady;
    private ForkliftFullLoadReward fullLoadRewardEffect;
    private readonly List<OrangeMaterialSlot> orangeMaterialSlots = new();

    private readonly struct OrangeMaterialSlot
    {
        public readonly Renderer Renderer;
        public readonly int MaterialIndex;

        public OrangeMaterialSlot(Renderer renderer, int materialIndex)
        {
            Renderer = renderer;
            MaterialIndex = materialIndex;
        }
    }

    private void Awake()
    {
        EnsureLoadAnchor();
        CacheLiftRestPose();

        // A reward unlocked in an earlier level belongs to the whole current
        // play session, so restore the green body when this level is loaded.
        if (LevelProgression.HasFullLoadForkliftReward)
        {
            ApplyFullLoadReward();
        }
    }

    private void Reset()
    {
        EnsureLoadAnchor();
        CacheLiftRestPose();
    }

    private void Update()
    {
        if (!hasLiftRestPose)
        {
            CacheLiftRestPose();
        }

        if (!hasLiftRestPose || Mathf.Abs(currentLiftHeight - liftTarget) < 0.0001f)
        {
            return;
        }

        liftElapsed = Mathf.Min(truckLiftDuration, liftElapsed + Time.deltaTime);
        var progress = Mathf.Clamp01(liftElapsed / Mathf.Max(0.01f, truckLiftDuration));
        currentLiftHeight = Mathf.Lerp(liftStartHeight, liftTarget, Mathf.SmoothStep(0f, 1f, progress));
        ApplyTruckLoadingLift();
    }

    /// <summary>
    /// Raises the physical fork carriage and the package anchor over one
    /// second when the forklift enters the truck-loading area. Lowering uses
    /// the same smooth one-second stroke after the packages are transferred.
    /// </summary>
    public void SetTruckLoadingLift(bool raised)
    {
        EnsureLoadAnchor();
        if (!hasLiftRestPose)
        {
            CacheLiftRestPose();
        }

        if (!hasLiftRestPose)
        {
            return;
        }

        var requestedHeight = raised ? Mathf.Max(0f, truckLiftHeight) : 0f;
        if (Mathf.Abs(requestedHeight - liftTarget) < 0.0001f)
        {
            return;
        }

        liftStartHeight = currentLiftHeight;
        liftTarget = requestedHeight;
        liftElapsed = 0f;

        if (Mathf.Abs(liftStartHeight - liftTarget) < 0.0001f)
        {
            currentLiftHeight = liftTarget;
            ApplyTruckLoadingLift();
        }
    }

    /// <summary>
    /// Checks whether the fork footprint covers the parcel's discharge point.
    /// When it does, reparents the parcel to the next free stack position.
    /// </summary>
    public bool TryCatch(GameObject package, Vector3 dischargePoint)
    {
        if (package == null || loadAnchor == null || IsFull || !Covers(dischargePoint))
        {
            return false;
        }

        var slot = LoadedPackageCount;
        package.transform.SetParent(loadAnchor, false);
        package.transform.localPosition = GetSlotPosition(slot);
        package.transform.localRotation = Quaternion.identity;
        package.transform.localScale = Vector3.one;
        package.name = $"Forklift_Load_{slot + 1:00}";

        // Capturing the thirtieth package qualifies for the reward, but the
        // visual reward is deliberately delayed until those packages have
        // really been transferred into the truck.
        fullLoadRewardReady = IsFull;

        FactoryAudioController.PlayPackageCaught();

        return true;
    }

    /// <summary>
    /// Called only after the physical package transfer from the forks to the
    /// cargo bed. The burst is therefore never shown merely for catching the
    /// final box.
    /// </summary>
    public void CelebrateFullLoadAfterTransfer()
    {
        var shouldCelebrate = fullLoadRewardReady && !LevelProgression.HasFullLoadForkliftReward;
        fullLoadRewardReady = false;
        if (!shouldCelebrate)
        {
            return;
        }

        if (fullLoadRewardEffect == null)
        {
            fullLoadRewardEffect = GetComponent<ForkliftFullLoadReward>();
            if (fullLoadRewardEffect == null)
            {
                fullLoadRewardEffect = gameObject.AddComponent<ForkliftFullLoadReward>();
            }
        }

        fullLoadRewardEffect.Play(() =>
        {
            LevelProgression.UnlockFullLoadForkliftReward();
            ApplyFullLoadReward();
        });
    }

    /// <summary>
    /// Lets a falling conveyor parcel query the active forklift without a
    /// direct scene reference. The yard currently contains one forklift.
    /// </summary>
    public static bool TryCatchFallingPackage(GameObject package, Vector3 dischargePoint)
    {
        // The yard has one active forklift. FindAny avoids Unity's obsolete
        // instance-order dependent lookup and is sufficient for this query.
        var loadArea = Object.FindAnyObjectByType<ForkliftLoadArea>();
        return loadArea != null && loadArea.TryCatch(package, dischargePoint);
    }

    private bool Covers(Vector3 dischargePoint)
    {
        var localPoint = loadAnchor.InverseTransformPoint(dischargePoint);
        return Mathf.Abs(localPoint.x) <= captureWidth * 0.5f &&
               Mathf.Abs(localPoint.z) <= captureLength * 0.5f;
    }

    private Vector3 GetSlotPosition(int slot)
    {
        var layer = slot / PackagesPerLayer;
        var layerSlot = slot % PackagesPerLayer;
        // Fill each layer as pairs across both fork tines: first the middle
        // position, then the position nearest the truck, and finally the tips.
        // The forklift model's forks point along local -Z, so +Z is nearer the
        // truck and -Z is the end of the forks.
        var row = layerSlot % Rows;
        var pair = layerSlot / Rows;
        var column = pair == 0 ? 1 : pair == 1 ? 2 : 0;

        return new Vector3(
            (row - 0.5f) * rowSpacing,
            layer * layerSpacing,
            (column - 1f) * columnSpacing);
    }

    private void EnsureLoadAnchor()
    {
        if (loadAnchor != null)
        {
            return;
        }

        loadAnchor = transform.Find("Forklift_Load_Area");
        if (loadAnchor != null)
        {
            return;
        }

        var anchor = new GameObject("Forklift_Load_Area").transform;
        anchor.SetParent(transform, false);
        // The source forklift faces local -Z. This position centres the stack
        // above its two tines, with the first package resting on their top.
        anchor.localPosition = new Vector3(0f, 0.42f, -1.64f);
        anchor.localRotation = Quaternion.identity;
        loadAnchor = anchor;
    }

    private void CacheLiftRestPose()
    {
        if (loadAnchor == null)
        {
            return;
        }

        if (forkCarriage == null)
        {
            forkCarriage = FindDescendant(transform, "ForkCarriage_Pivot");
        }

        loadAnchorRestLocalPosition = loadAnchor.localPosition;
        if (forkCarriage != null)
        {
            forkCarriageRestControllerLocalPosition = transform.InverseTransformPoint(forkCarriage.position);
        }

        currentLiftHeight = 0f;
        liftStartHeight = 0f;
        liftTarget = 0f;
        liftElapsed = 0f;
        hasLiftRestPose = true;
    }

    private void ApplyTruckLoadingLift()
    {
        if (loadAnchor != null)
        {
            loadAnchor.localPosition = loadAnchorRestLocalPosition + Vector3.up * currentLiftHeight;
        }

        if (forkCarriage != null)
        {
            // The FBX has its own nested axes, so position the carriage from
            // the controller-root reference and lift it in world-up. This
            // stays vertical while the player turns the forklift.
            forkCarriage.position = transform.TransformPoint(
                forkCarriageRestControllerLocalPosition + Vector3.up * currentLiftHeight);
        }
    }

    private void ApplyFullLoadReward()
    {
        if (fullLoadRewardApplied)
        {
            return;
        }

        CacheOrangeBodyMaterialSlots();
        foreach (var slot in orangeMaterialSlots)
        {
            if (slot.Renderer == null)
            {
                continue;
            }

            var block = new MaterialPropertyBlock();
            slot.Renderer.GetPropertyBlock(block, slot.MaterialIndex);
            var material = slot.Renderer.sharedMaterials[slot.MaterialIndex];
            if (material != null && material.HasProperty("_BaseColor"))
            {
                block.SetColor("_BaseColor", fullLoadRewardColor);
            }

            if (material != null && material.HasProperty("_Color"))
            {
                block.SetColor("_Color", fullLoadRewardColor);
            }

            slot.Renderer.SetPropertyBlock(block, slot.MaterialIndex);
        }

        fullLoadRewardApplied = true;
    }

    private void CacheOrangeBodyMaterialSlots()
    {
        if (orangeMaterialSlots.Count > 0)
        {
            return;
        }

        foreach (var renderer in GetComponentsInChildren<Renderer>(true))
        {
            var materials = renderer.sharedMaterials;
            for (var index = 0; index < materials.Length; index++)
            {
                var material = materials[index];
                if (material != null && material.name.IndexOf("orange", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    orangeMaterialSlots.Add(new OrangeMaterialSlot(renderer, index));
                }
            }
        }
    }

    private static Transform FindDescendant(Transform root, string targetName)
    {
        if (root == null)
        {
            return null;
        }

        foreach (var candidate in root.GetComponentsInChildren<Transform>(true))
        {
            if (candidate.name == targetName)
            {
                return candidate;
            }
        }

        return null;
    }

    private void OnDrawGizmosSelected()
    {
        if (loadAnchor == null)
        {
            return;
        }

        var previousMatrix = Gizmos.matrix;
        Gizmos.matrix = loadAnchor.localToWorldMatrix;
        Gizmos.color = new Color(0.1f, 0.9f, 1f, 0.9f);
        Gizmos.DrawWireCube(Vector3.zero, new Vector3(captureWidth, 0.03f, captureLength));
        Gizmos.DrawWireCube(
            new Vector3(0f, (Layers - 1) * layerSpacing * 0.5f, 0f),
            new Vector3(captureWidth, Layers * layerSpacing, captureLength));
        Gizmos.matrix = previousMatrix;
    }
}
