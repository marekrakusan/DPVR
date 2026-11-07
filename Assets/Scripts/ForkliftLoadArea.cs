using UnityEngine;

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

    private void Awake()
    {
        EnsureLoadAnchor();
    }

    private void Reset()
    {
        EnsureLoadAnchor();
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
        return true;
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
