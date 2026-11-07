using UnityEngine;

/// <summary>
/// Moves a generated parcel along an editor-defined conveyor path, then drops
/// it vertically onto the floor below the discharge point.
/// </summary>
public sealed class ConveyorPackageMover : MonoBehaviour
{
    private Transform[] waypoints;
    private int nextWaypoint;
    private float speed;
    private float groundY;
    private ConveyorDiverter diverter;
    private ConveyorDropCoordinator dispatchCoordinator;
    private ConveyorBranch branch;
    private int diverterPreparationWaypoint;
    private bool diverterPrepared;
    private bool isDropping;
    private Vector3 dropStart;
    private float dropProgress;

    public void Initialize(
        Transform[] path,
        float movementSpeed,
        float floorHeight,
        ConveyorDiverter assignedDiverter,
        ConveyorBranch selectedBranch,
        int preparationWaypoint,
        ConveyorDropCoordinator assignedCoordinator)
    {
        waypoints = path;
        speed = movementSpeed;
        groundY = floorHeight;
        diverter = assignedDiverter;
        dispatchCoordinator = assignedCoordinator;
        branch = selectedBranch;
        nextWaypoint = 1;
        diverterPreparationWaypoint = Mathf.Max(1, preparationWaypoint);
        diverterPrepared = false;

        if (waypoints != null && waypoints.Length > 0)
        {
            transform.position = waypoints[0].position;
        }

        PrepareDiverterIfNeeded();
    }

    private void Update()
    {
        if (waypoints == null || waypoints.Length < 2)
        {
            return;
        }

        if (isDropping)
        {
            dropProgress += Time.deltaTime / 0.42f;
            var target = new Vector3(dropStart.x, groundY, dropStart.z);
            transform.position = Vector3.Lerp(dropStart, target, Mathf.SmoothStep(0f, 1f, dropProgress));

            // The forklift may drive beneath the discharge zone at any time
            // during the fall. If its fork footprint covers this point, the
            // same parcel becomes part of the load instead of reaching ground.
            if (ForkliftLoadArea.TryCatchFallingPackage(gameObject, dropStart))
            {
                NotifyArrival();
                enabled = false;
                return;
            }

            if (dropProgress >= 1f)
            {
                NotifyArrival();
                Destroy(gameObject, 5f);
                enabled = false;
            }

            return;
        }

        var targetWaypoint = waypoints[nextWaypoint].position;
        transform.position = Vector3.MoveTowards(transform.position, targetWaypoint, speed * Time.deltaTime);

        if ((transform.position - targetWaypoint).sqrMagnitude > 0.0001f)
        {
            return;
        }

        if (nextWaypoint < waypoints.Length - 1)
        {
            nextWaypoint++;
            PrepareDiverterIfNeeded();
            return;
        }

        dropStart = transform.position;
        isDropping = true;
    }

    private void NotifyArrival()
    {
        if (dispatchCoordinator != null)
        {
            dispatchCoordinator.NotifyPackageArrived();
            dispatchCoordinator = null;
        }
    }

    private void PrepareDiverterIfNeeded()
    {
        if (diverterPrepared || diverter == null || nextWaypoint < diverterPreparationWaypoint)
        {
            return;
        }

        diverter.SelectBranch(branch);
        diverterPrepared = true;
    }
}
