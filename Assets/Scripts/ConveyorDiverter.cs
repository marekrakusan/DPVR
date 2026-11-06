using UnityEngine;

/// <summary>
/// Turns the physical flap of one conveyor to the branch selected for the
/// approaching parcel. It preserves the imported pivot's X and Y rotation
/// and changes only its local Z angle.
/// </summary>
[DisallowMultipleComponent]
public sealed class ConveyorDiverter : MonoBehaviour
{
    [Tooltip("Target local Z rotation that opens branch A.")]
    public float branchALocalZ = -29f;

    [Tooltip("Target local Z rotation that opens branch B.")]
    public float branchBLocalZ = 29f;

    [Tooltip("How quickly the visible flap turns to the selected route.")]
    [Min(1f)] public float rotationSpeedDegrees = 360f;

    private Vector2 preservedLocalRotation;
    private Quaternion targetRotation;
    private bool initialized;

    private void Awake()
    {
        EnsureInitialized();
        targetRotation = transform.localRotation;
    }

    private void Update()
    {
        EnsureInitialized();
        transform.localRotation = Quaternion.RotateTowards(
            transform.localRotation,
            targetRotation,
            rotationSpeedDegrees * Time.deltaTime);
    }

    /// <summary>
    /// Starts rotating the flap so that the selected branch is open before
    /// the parcel reaches the split.
    /// </summary>
    public void SelectBranch(ConveyorBranch branch)
    {
        EnsureInitialized();
        var z = branch == ConveyorBranch.A ? branchALocalZ : branchBLocalZ;
        targetRotation = Quaternion.Euler(preservedLocalRotation.x, preservedLocalRotation.y, z);
    }

    private void EnsureInitialized()
    {
        if (initialized)
        {
            return;
        }

        var localEuler = transform.localEulerAngles;
        preservedLocalRotation = new Vector2(localEuler.x, localEuler.y);
        targetRotation = transform.localRotation;
        initialized = true;
    }
}
