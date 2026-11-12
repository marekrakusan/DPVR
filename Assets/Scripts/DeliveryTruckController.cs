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
    [Tooltip("Tarp ribs and side logos that are hidden with the tarp while the forklift loads the truck.")]
    public GameObject[] cargoPresentation;
    [Tooltip("The accessible side of the cargo bed. The forklift only has to bring its fork load close to this point.")]
    public Transform loadingPoint;
    public Transform gate;
    [Header("Route through the yard")]
    [Tooltip("First point south of the opened gate. The truck reaches this point straight ahead.")]
    public Vector3 gateEntryPosition = new Vector3(0f, 0f, 10.7f);

    [Tooltip("The loading stop is intentionally left of the gate and angled toward the courtyard.")]
    public Vector3 loadingStopPosition = new Vector3(-3f, 0f, 6.5f);

    [Tooltip("Root yaw at the loading stop. The vehicle model's cab faces local -Z.")]
    public float loadingStopYaw = 50f;

    [Tooltip("The truck reaches this point while reversing to the right, then faces the exit.")]
    public Vector3 reverseTurnPosition = new Vector3(3f, 0f, 8.2f);

    [Tooltip("Centre line in front of the open gate. The truck reaches it on a smooth forward curve before exiting.")]
    public Vector3 gateExitPosition = new Vector3(0.2f, 0f, 14.1f);

    [Tooltip("Position outside the north hall after the forward departure.")]
    public Vector3 exitPosition = new Vector3(0.2f, 0f, 25.5f);

    public float driveSpeed = 3.5f;
    public float reverseSpeed = 2.4f;
    public float turnSpeed = 105f;
    public float gateSpeed = 2.2f;
    [Tooltip("Maximum horizontal distance of the forklift's load from the accessible side of the cargo bed.")]
    public float loadingDistance = 1f;

    private Vector3 gateClosedScale;
    private Vector3 gateClosedPosition;
    private float gateTopY;
    private Collider gateCollider;
    private State state;
    private float loadingTimer;
    private RouteCurve activeCurve;
    private Vector3 curveStart;
    private Vector3 curveControlA;
    private Vector3 curveControlB;
    private Vector3 curveEnd;
    private Quaternion curveStartRotation;
    private Quaternion curveEndRotation;
    private float curveElapsed;
    private float curveDuration;

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
        ExitingCourtyard,
        Leaving,
        Closing,
        Finished
    }

    private enum RouteCurve
    {
        None,
        Arrival,
        Reverse,
        Exit
    }

    private void Awake()
    {
        // Only the automatic delivery truck is sped up here. The forklift is
        // controlled by its own level-based movement system.
        driveSpeed *= VehicleSpeedMultiplier;
        reverseSpeed *= VehicleSpeedMultiplier;
        EnsureTruckVisible();
        SetCargoPresentation(true);

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
                    // The gate must remain shut unless this level's full
                    // minimum objective was met. Checking the progression
                    // directly makes the rule apply equally to Levels 1–5
                    // (25, 26, 27, 28 and 29 packages), rather than only
                    // checking whether at least one package was caught.
                    var requiredPackages = LevelProgression.RequiredLoadedPackages;
                    if (forkliftLoad == null || forkliftLoad.LoadedPackageCount < requiredPackages)
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
                if (MoveAlongCurve(
                        RouteCurve.Arrival,
                        gateEntryPosition + Vector3.back * 1.35f,
                        loadingStopPosition - CabDirectionForYaw(loadingStopYaw) * 1.35f,
                        loadingStopPosition,
                        loadingStopYaw,
                        driveSpeed,
                        true))
                {
                    // The open cargo bed is visible as soon as the vehicle
                    // stops, so the player can place the fork load beside it.
                    SetCargoPresentation(false);
                    state = State.WaitingForForklift;
                }
                break;
            case State.WaitingForForklift:
                if (CanLoadForklift())
                {
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
                    SetCargoPresentation(true);
                    state = State.ReversingTurn;
                }
                break;
            case State.ReversingTurn:
                // Moving from the left parking stop toward +X/+Z is backward
                // for the initially angled cab. Simultaneously turn the root
                // so the cab ultimately faces north (+Z) for the forward exit.
                if (MoveAlongCurve(
                        RouteCurve.Reverse,
                        Vector3.Lerp(loadingStopPosition, reverseTurnPosition, 0.24f) + new Vector3(0f, 0f, -0.55f),
                        Vector3.Lerp(loadingStopPosition, reverseTurnPosition, 0.76f) + new Vector3(0f, 0f, 0.15f),
                        reverseTurnPosition,
                        180f,
                        reverseSpeed,
                        false))
                {
                    state = State.ExitingCourtyard;
                }
                break;
            case State.ExitingCourtyard:
                // The final green arc moves back to the centre of the gate
                // before the truck drives out in a straight, safe line.
                if (MoveAlongCurve(
                        RouteCurve.Exit,
                        reverseTurnPosition + new Vector3(-0.15f, 0f, 1.8f),
                        gateExitPosition + Vector3.back * 1.3f,
                        gateExitPosition,
                        180f,
                        driveSpeed,
                        true))
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

    /// <summary>
    /// Travels a cubic Bezier segment. Arrival and departure follow the curve
    /// tangent; the yellow segment turns while reversing, which keeps the
    /// reverse manoeuvre believable rather than sliding through the corner.
    /// </summary>
    private bool MoveAlongCurve(
        RouteCurve curve,
        Vector3 controlA,
        Vector3 controlB,
        Vector3 targetPosition,
        float targetYaw,
        float speed,
        bool followForwardTangent)
    {
        if (activeCurve != curve)
        {
            activeCurve = curve;
            curveStart = transform.position;
            curveControlA = controlA;
            curveControlB = controlB;
            curveEnd = targetPosition;
            curveStartRotation = transform.rotation;
            curveEndRotation = Quaternion.Euler(0f, targetYaw, 0f);
            curveElapsed = 0f;
            curveDuration = Mathf.Max(0.08f, EstimateCurveLength(curveStart, curveControlA, curveControlB, curveEnd) /
                                                 Mathf.Max(0.01f, speed));
        }

        curveElapsed = Mathf.Min(curveDuration, curveElapsed + Time.deltaTime);
        var progress = Mathf.Clamp01(curveElapsed / curveDuration);
        // Ease only very slightly at the join points so each manoeuvre starts
        // and finishes without a visual jerk.
        var smoothProgress = Mathf.SmoothStep(0f, 1f, progress);
        transform.position = EvaluateCurve(curveStart, curveControlA, curveControlB, curveEnd, smoothProgress);

        if (followForwardTangent)
        {
            var tangent = EvaluateCurveTangent(curveStart, curveControlA, curveControlB, curveEnd, smoothProgress);
            if (tangent.sqrMagnitude > 0.0001f)
            {
                // The imported truck's cab looks along local -Z.
                var desiredRotation = Quaternion.LookRotation(-tangent.normalized, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation,
                    desiredRotation,
                    turnSpeed * Time.deltaTime);
            }
        }
        else
        {
            transform.rotation = Quaternion.Slerp(curveStartRotation, curveEndRotation, smoothProgress);
        }

        if (progress < 1f)
        {
            return false;
        }

        transform.position = curveEnd;
        transform.rotation = curveEndRotation;
        activeCurve = RouteCurve.None;
        return true;
    }

    private static Vector3 EvaluateCurve(Vector3 start, Vector3 controlA, Vector3 controlB, Vector3 end, float t)
    {
        var inverse = 1f - t;
        return inverse * inverse * inverse * start +
               3f * inverse * inverse * t * controlA +
               3f * inverse * t * t * controlB +
               t * t * t * end;
    }

    private static Vector3 EvaluateCurveTangent(Vector3 start, Vector3 controlA, Vector3 controlB, Vector3 end, float t)
    {
        var inverse = 1f - t;
        return 3f * inverse * inverse * (controlA - start) +
               6f * inverse * t * (controlB - controlA) +
               3f * t * t * (end - controlB);
    }

    private static float EstimateCurveLength(Vector3 start, Vector3 controlA, Vector3 controlB, Vector3 end)
    {
        const int samples = 20;
        var length = 0f;
        var previous = start;
        for (var index = 1; index <= samples; index++)
        {
            var point = EvaluateCurve(start, controlA, controlB, end, index / (float)samples);
            length += Vector3.Distance(previous, point);
            previous = point;
        }

        return length;
    }

    private static Vector3 CabDirectionForYaw(float yaw)
    {
        return Quaternion.Euler(0f, yaw, 0f) * Vector3.back;
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

    private void SetCargoPresentation(bool visible)
    {
        if (cargoCover != null)
        {
            cargoCover.SetActive(visible);
        }

        if (cargoPresentation == null)
        {
            return;
        }

        foreach (var part in cargoPresentation)
        {
            if (part != null)
            {
                part.SetActive(visible);
            }
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
        if (forkliftLoad == null || forkliftLoad.LoadedPackageCount <= 0)
        {
            return false;
        }

        var forkLoadPosition = forkliftLoad.loadAnchor != null
            ? forkliftLoad.loadAnchor.position
            : forkliftLoad.transform.position;
        var cargoSidePosition = loadingPoint != null
            ? loadingPoint.position
            : cargoAnchor != null ? cargoAnchor.position : transform.position;

        forkLoadPosition.y = 0f;
        cargoSidePosition.y = 0f;
        return Vector3.Distance(forkLoadPosition, cargoSidePosition) <= loadingDistance;
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

    private void OnDrawGizmosSelected()
    {
        // Editor-only route preview: red arrival, yellow reverse, green exit.
        var arrivalStart = transform.position;
        Gizmos.color = new Color(1f, 0.18f, 0.08f, 0.95f);
        Gizmos.DrawLine(arrivalStart, gateEntryPosition);
        DrawCurve(
            gateEntryPosition,
            gateEntryPosition + Vector3.back * 1.35f,
            loadingStopPosition - CabDirectionForYaw(loadingStopYaw) * 1.35f,
            loadingStopPosition);

        Gizmos.color = new Color(1f, 0.9f, 0.08f, 0.95f);
        DrawCurve(
            loadingStopPosition,
            Vector3.Lerp(loadingStopPosition, reverseTurnPosition, 0.24f) + new Vector3(0f, 0f, -0.55f),
            Vector3.Lerp(loadingStopPosition, reverseTurnPosition, 0.76f) + new Vector3(0f, 0f, 0.15f),
            reverseTurnPosition);

        Gizmos.color = new Color(0.1f, 0.95f, 0.15f, 0.95f);
        DrawCurve(
            reverseTurnPosition,
            reverseTurnPosition + new Vector3(-0.15f, 0f, 1.8f),
            gateExitPosition + Vector3.back * 1.3f,
            gateExitPosition);
        Gizmos.DrawLine(gateExitPosition, exitPosition);

        Gizmos.color = Color.cyan;
        var point = loadingPoint != null ? loadingPoint.position : cargoAnchor != null ? cargoAnchor.position : transform.position;
        Gizmos.DrawWireSphere(point, loadingDistance);
    }

    private static void DrawCurve(Vector3 start, Vector3 controlA, Vector3 controlB, Vector3 end)
    {
        const int samples = 24;
        var previous = start;
        for (var index = 1; index <= samples; index++)
        {
            var point = EvaluateCurve(start, controlA, controlB, end, index / (float)samples);
            Gizmos.DrawLine(previous, point);
            previous = point;
        }
    }
}
