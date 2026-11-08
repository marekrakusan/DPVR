using UnityEngine;

/// <summary>
/// Runs the single delivery cycle for the yard: only after the final parcel
/// arrives at its landing zone, the gate opens, the truck enters, receives
/// the forklift load and leaves.
/// </summary>
public sealed class DeliveryTruckController : MonoBehaviour
{
    private const float VehicleSpeedMultiplier = 1.5f;

    public ConveyorDropCoordinator coordinator;
    public ForkliftLoadArea forkliftLoad;
    public LevelOutcomeOverlay outcomeOverlay;
    public Transform truckVisual;
    public Transform cargoAnchor;
    public GameObject cargoCover;
    public Transform gate;
    [Header("Route through the yard")]
    [Tooltip("First point south of the opened gate. The truck reaches this point straight ahead.")]
    public Vector3 gateEntryPosition = new Vector3(0f, 0f, 10.7f);

    [Tooltip("The loading stop is intentionally left of the gate and angled toward the courtyard.")]
    public Vector3 loadingStopPosition = new Vector3(-3f, 0f, 8.5f);

    [Tooltip("Root yaw at the loading stop. The vehicle model's cab faces local -Z.")]
    public float loadingStopYaw = 50f;

    [Tooltip("The truck reaches this point while reversing to the right, then faces the exit.")]
    public Vector3 reverseTurnPosition = new Vector3(0.2f, 0f, 11.2f);

    [Tooltip("Position outside the north hall after the forward departure.")]
    public Vector3 exitPosition = new Vector3(0.2f, 0f, 25.5f);

    public float driveSpeed = 3.5f;
    public float reverseSpeed = 2.4f;
    public float turnSpeed = 105f;
    public float gateSpeed = 2.2f;
    public float loadingDistance = 3.1f;

    private Vector3 gateClosedScale;
    private Vector3 gateClosedPosition;
    private float gateTopY;
    private Collider gateCollider;
    private State state;
    private float loadingTimer;

    /// <summary>
    /// The packages are re-parented from the forks to the truck when loading
    /// starts. Keep this snapshot so the completed round can still be judged
    /// once the truck has departed and the gate is shut.
    /// </summary>
    public int LoadedPackageCount { get; private set; }

    /// <summary>
    /// Lets the board over the gate keep showing the transferred load until
    /// the whole level ends. A scene reload for the next level or a restart
    /// then creates a fresh controller and resets the board naturally.
    /// </summary>
    public bool ShouldHoldLoadCounterUntilLevelEnds => LoadedPackageCount > 0;

    private enum State
    {
        Waiting,
        Opening,
        EnteringGate,
        Parking,
        WaitingForForklift,
        Loading,
        ReversingTurn,
        Leaving,
        Closing,
        Finished
    }

    private void Awake()
    {
        // Only the automatic delivery truck is sped up here. The forklift is
        // controlled by its own level-based movement system.
        driveSpeed *= VehicleSpeedMultiplier;
        reverseSpeed *= VehicleSpeedMultiplier;
        EnsureTruckVisible();

        if (gate != null)
        {
            gateClosedScale = gate.localScale;
            gateClosedPosition = gate.position;
            gateTopY = gate.position.y + gate.lossyScale.y * 0.5f;
            gateCollider = gate.GetComponent<Collider>();
        }
    }

    private void Update()
    {
        switch (state)
        {
            case State.Waiting:
                // The conveyor layout can be refreshed in the editor after the
                // truck has been added. Recover the new coordinator instead of
                // leaving this delivery cycle attached to the destroyed one.
                if (coordinator == null)
                {
                    coordinator = Object.FindAnyObjectByType<ConveyorDropCoordinator>();
                }

                if (coordinator != null &&
                    coordinator.DeliveredPackageCount >= Mathf.Max(1, coordinator.maximumPackagesPerRound))
                {
                    // The last parcel has already completed its fall here.
                    // If none of the parcels was caught on the forks, there
                    // is no load to deliver: keep the gate closed and show
                    // the failed-round flow immediately.
                    if (forkliftLoad == null || forkliftLoad.LoadedPackageCount == 0)
                    {
                        state = State.Finished;
                        outcomeOverlay?.ShowResult(false);
                    }
                    else
                    {
                        state = State.Opening;
                    }
                }
                break;
            case State.Opening:
                if (SetGateOpen(true)) state = State.EnteringGate;
                break;
            case State.EnteringGate:
                EnsureTruckVisible();
                // The cab faces local -Z at yaw 0, so this is a forward drive
                // directly through the gate before the vehicle begins its turn.
                if (MoveAndRotate(gateEntryPosition, 0f, driveSpeed)) state = State.Parking;
                break;
            case State.Parking:
                // Stop a little left of the opening, with the cab angled into
                // the courtyard to leave an easy approach for the forklift.
                if (MoveAndRotate(loadingStopPosition, loadingStopYaw, driveSpeed))
                {
                    state = State.WaitingForForklift;
                }
                break;
            case State.WaitingForForklift:
                if (CanLoadForklift())
                {
                    if (cargoCover != null) cargoCover.SetActive(false);
                    LoadedPackageCount = forkliftLoad.LoadedPackageCount;
                    LoadForkliftPackages();
                    loadingTimer = 2.0f;
                    state = State.Loading;
                }
                break;
            case State.Loading:
                loadingTimer -= Time.deltaTime;
                if (loadingTimer <= 0f)
                {
                    if (cargoCover != null) cargoCover.SetActive(true);
                    state = State.ReversingTurn;
                }
                break;
            case State.ReversingTurn:
                // Moving from the left parking stop toward +X/+Z is backward
                // for the initially angled cab. Simultaneously turn the root
                // so the cab ultimately faces north (+Z) for the forward exit.
                if (MoveAndRotate(reverseTurnPosition, 180f, reverseSpeed))
                {
                    state = State.Leaving;
                }
                break;
            case State.Leaving:
                // At yaw 180 the model's local -Z cab points along world +Z,
                // therefore this is a forward departure through the gate.
                if (MoveAndRotate(exitPosition, 180f, driveSpeed)) state = State.Closing;
                break;
            case State.Closing:
                if (SetGateOpen(false))
                {
                    state = State.Finished;
                    if (outcomeOverlay != null)
                    {
                        outcomeOverlay.ShowResult(LoadedPackageCount >= outcomeOverlay.requiredLoadedPackages);
                    }
                }
                break;
        }
    }

    private bool MoveAndRotate(Vector3 targetPosition, float targetYaw, float speed)
    {
        transform.position = Vector3.MoveTowards(transform.position, targetPosition, speed * Time.deltaTime);
        var targetRotation = Quaternion.Euler(0f, targetYaw, 0f);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, turnSpeed * Time.deltaTime);

        return Vector3.SqrMagnitude(transform.position - targetPosition) < 0.0001f &&
               Quaternion.Angle(transform.rotation, targetRotation) < 0.1f;
    }

    private void EnsureTruckVisible()
    {
        if (truckVisual == null)
        {
            return;
        }

        truckVisual.gameObject.SetActive(true);
        foreach (var renderer in truckVisual.GetComponentsInChildren<Renderer>(true))
        {
            renderer.enabled = true;
        }
    }

    private bool SetGateOpen(bool opening)
    {
        if (gate == null) return true;
        var targetHeightScale = opening ? gateClosedScale.y * 0.04f : gateClosedScale.y;
        var scale = gate.localScale;
        scale.y = Mathf.MoveTowards(scale.y, targetHeightScale, gateSpeed * Time.deltaTime);
        gate.localScale = scale;
        var currentWorldHeight = gate.lossyScale.y;
        gate.position = new Vector3(gateClosedPosition.x, gateTopY - currentWorldHeight * 0.5f, gateClosedPosition.z);
        if (gateCollider != null) gateCollider.enabled = !opening;
        return Mathf.Abs(scale.y - targetHeightScale) < 0.001f;
    }

    private bool CanLoadForklift()
    {
        return forkliftLoad != null && forkliftLoad.LoadedPackageCount > 0 &&
               Vector3.Distance(new Vector3(forkliftLoad.transform.position.x, 0f, forkliftLoad.transform.position.z),
                                new Vector3(transform.position.x, 0f, transform.position.z)) <= loadingDistance;
    }

    private void LoadForkliftPackages()
    {
        if (forkliftLoad == null || forkliftLoad.loadAnchor == null || cargoAnchor == null) return;
        var count = forkliftLoad.loadAnchor.childCount;
        for (var index = count - 1; index >= 0; index--)
        {
            var package = forkliftLoad.loadAnchor.GetChild(index);
            package.SetParent(cargoAnchor, false);
            var column = index % 3;
            var row = (index / 3) % 2;
            var layer = index / 6;
            package.localPosition = new Vector3((column - 1) * 0.58f, 0.18f + layer * 0.38f, (row - 0.5f) * 0.78f);
            package.localRotation = Quaternion.identity;
        }
    }
}
