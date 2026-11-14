using UnityEngine;

/// <summary>
/// Creates parcels at a loading window and chooses one of its two discharge
/// routes at random for every parcel.
/// </summary>
public enum ConveyorBranch
{
    A,
    B,
}

public sealed class ConveyorBoxSpawner : MonoBehaviour
{
    public GameObject packagePrefab;
    public Transform[] branchPathA;
    public Transform[] branchPathB;
    public Transform packageParent;
    public float intervalSeconds = 7f;
    public float beltSpeed = 1.55f;
    public float floorHeight = 0.19f;
    public ConveyorDropCoordinator dispatchCoordinator;

    [Header("Discharge layout")]
    [Tooltip("Which of this conveyor's two physical branches finishes at the lower discharge zone.")]
    public ConveyorBranch lowerBranch = ConveyorBranch.B;

    [Header("Diverter")]
    [Tooltip("The physical flap which opens the selected route.")]
    public ConveyorDiverter diverter;

    [Tooltip("Waypoint at which the flap is prepared. The next segment leads into the split.")]
    [Min(1)] public int diverterPreparationWaypoint = 2;

    private int packageIndex;

    private void Awake()
    {
        // Scale the belt before the shared coordinator calculates route travel
        // times in Start. The fallback interval is adjusted as well so this
        // component behaves consistently in a standalone scene.
        beltSpeed *= LevelProgression.ConveyorSpeedMultiplier;
        intervalSeconds *= LevelProgression.FallDelayMultiplier;
    }

    private void Start()
    {
        // The factory scene assigns a shared dispatcher, which calculates
        // departures from the different branch lengths. The fallback keeps
        // this component useful in a standalone scene.
        if (dispatchCoordinator == null)
        {
            InvokeRepeating(nameof(SpawnPackage), 0.6f, intervalSeconds);
        }
    }

    private void OnDisable()
    {
        CancelInvoke();
    }

    private void SpawnPackage()
    {
        if (TryReserveRandomPath(out var path, out var branch, out var packageNumber))
        {
            SpawnReservedPackage(path, branch, packageNumber);
        }
    }

    /// <summary>
    /// Reserves a random valid branch for the shared dispatcher.
    /// </summary>
    public bool TryReserveRandomPath(
        out Transform[] path,
        out ConveyorBranch branch,
        out int packageNumber)
    {
        var canUseA = IsValidPath(branchPathA);
        var canUseB = IsValidPath(branchPathB);
        branch = ConveyorBranch.A;
        path = null;
        packageNumber = 0;
        if (packagePrefab == null || (!canUseA && !canUseB))
        {
            return false;
        }

        if (canUseA && canUseB)
        {
            branch = Random.value < 0.5f ? ConveyorBranch.A : ConveyorBranch.B;
        }
        else
        {
            branch = canUseA ? ConveyorBranch.A : ConveyorBranch.B;
        }

        return TryReservePath(branch, out path, out packageNumber);
    }

    /// <summary>
    /// Reserves one explicitly selected branch. The shared coordinator uses
    /// this method after it has checked the yard-wide routing rules.
    /// </summary>
    public bool TryReservePath(
        ConveyorBranch branch,
        out Transform[] path,
        out int packageNumber)
    {
        path = null;
        packageNumber = 0;
        if (packagePrefab == null || !HasValidPath(branch))
        {
            return false;
        }

        path = branch == ConveyorBranch.A ? branchPathA : branchPathB;
        packageIndex++;
        packageNumber = packageIndex;
        return true;
    }

    /// <summary>
    /// The shared coordinator may discard a tentative whole-round plan before
    /// any package is spawned. Reset only the display-number reservation so
    /// the accepted plan still begins at package one on this conveyor.
    /// </summary>
    public void ResetPackageReservations()
    {
        packageIndex = 0;
    }

    /// <summary>
    /// Lets the coordinator evaluate a route before reserving it.
    /// </summary>
    public bool HasValidPath(ConveyorBranch branch)
    {
        return IsValidPath(branch == ConveyorBranch.A ? branchPathA : branchPathB);
    }

    /// <summary>
    /// The coordinator uses the authored layout rather than assuming that a
    /// particular A/B label always means the lower physical conveyor.
    /// </summary>
    public bool IsLowerBranch(ConveyorBranch branch)
    {
        return branch == lowerBranch;
    }

    /// <summary>
    /// Calculates the uninterrupted belt travel time for a branch.
    /// </summary>
    public float GetTravelTime(Transform[] path)
    {
        if (path == null || path.Length < 2 || beltSpeed <= 0f)
        {
            return 0f;
        }

        var distance = 0f;
        for (var index = 1; index < path.Length; index++)
        {
            distance += Vector3.Distance(path[index - 1].position, path[index].position);
        }

        return distance / beltSpeed;
    }

    /// <summary>
    /// Starts a package on an already reserved path. It never waits at an end
    /// waypoint: its calculated departure time is what creates the fall gap.
    /// </summary>
    public void SpawnReservedPackage(
        Transform[] path,
        ConveyorBranch branch,
        int packageNumber)
    {
        if (packagePrefab == null || path == null || path.Length < 2)
        {
            return;
        }

        var package = Instantiate(packagePrefab, path[0].position, Quaternion.identity, packageParent);
        package.name = $"Generated_Box_{packageNumber:00}";
        package.SetActive(true);

        var mover = package.GetComponent<ConveyorPackageMover>();
        if (mover == null)
        {
            mover = package.AddComponent<ConveyorPackageMover>();
        }

        mover.Initialize(
            path,
            beltSpeed,
            floorHeight,
            diverter,
            branch,
            diverterPreparationWaypoint,
            dispatchCoordinator);
    }

    private static bool IsValidPath(Transform[] path)
    {
        return path != null && path.Length >= 2;
    }
}
