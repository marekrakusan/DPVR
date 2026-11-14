using UnityEngine;

/// <summary>
/// Creates one penguin encounter from Level 2 onward. Its descent is planned
/// from the fifteenth parcel's expected arrival, then aligned to the real
/// arrival without interrupting the visible descent.
/// </summary>
[DisallowMultipleComponent]
public sealed class PenguinEncounterController : MonoBehaviour
{
    private const int FirstPenguinLevel = 2;
    private const float PerLevelTimingMultiplier = 0.8f;

    private enum State
    {
        Waiting,
        Target,
        Descending,
        Landed,
        Boarding,
        Escaping,
        EscapeJump,
        EscapeGlide,
        Finished
    }

    [Header("Scene references")]
    public ConveyorDropCoordinator coordinator;
    public ForkliftController forklift;
    public GameObject penguinPrefab;
    public Transform[] landingPoints;
    public Transform aPillarAnchor;
    public Transform driverSeatAnchor;

    [Header("Encounter timing")]
    [Min(1)] public int triggerDeliveredPackageCount = 15;
    [Min(0f)] public float targetLeadTime = 5f;
    [Tooltip("The penguin touches down this many seconds after package fifteen actually arrives.")]
    [Min(0f)] public float landingDelayAfterTriggerPackage = 2f;
    [Min(0.1f)] public float descentDuration = 8.5f;
    [Min(0.1f)] public float landedLifetime = 1f;
    [Min(0.1f)] public float boardingDuration = 0.82f;
    [Min(0.01f)] public float escapeArrivalTolerance = 0.08f;
    [Tooltip("No parcel may fall for this long after the penguin lands.")]
    [Min(0f)] public float lowerConveyorLandingPause = 5f;
    [Tooltip("Kept for both conveyor branches so the post-landing interval stays five seconds.")]
    [Min(0f)] public float upperConveyorLandingPause = 5f;

    [Header("Warning")]
    [Tooltip("Blinking facade text shown before the penguin starts descending.")]
    public string warningLabel = "Tučňák landing!";
    [Tooltip("Slow blink rate of the advance-warning text, in complete flashes per second.")]
    [Min(0.1f)] public float warningBlinkFrequency = 1.25f;

    [Header("Movement")]
    [Min(0.1f)] public float parachuteStartHeight = 30f;
    [Min(0.1f)] public float forkliftHitDistance = 1.7f;
    [Min(0.1f)] public float minimumLandingDistanceFromForklift = 2.4f;
    [Min(0.1f)] public float escapeSpeed = 6.5f;
    [Min(0.01f)] public float escapeWallClearance = 0.16f;
    [Min(0.01f)] public float groundClearance = 0.01f;
    [Min(0.01f)] public float parachuteSwayAmplitude = 0.36f;
    [Min(0.01f)] public float parachuteSwayFrequency = 1.1f;
    [Min(0.01f)] public float escapeSwayAngleDegrees = 8f;
    [Min(0.01f)] public float escapeSwayFrequency = 8f;
    [Min(0.1f)] public float escapeJumpDuration = 1.05f;
    [Min(0.1f)] public float escapeGlideDuration = 4.25f;
    [Min(1f)] public float forkliftSpeedBonusMultiplier = 1.10f;
    [Tooltip("Scales the penguin and its parachute together without changing the Blender source model.")]
    [Min(0.01f)] public float visualScaleMultiplier = 2.5f;

    private State state;
    private Transform landingPoint;
    private GameObject targetMarker;
    private GameObject warningSign;
    private TextMesh warningText;
    private Renderer warningRenderer;
    private GameObject penguin;
    private Vector3 landingPosition;
    private Vector3 descentStart;
    private Vector3 groundedRootPosition;
    private Vector3 boardStart;
    private Vector3 boardOffset;
    private Vector3 escapeDestination;
    private Vector3 escapeFootPosition;
    private Vector3 escapeJumpStart;
    private Vector3 escapeJumpApex;
    private Vector3 escapeGlideStart;
    private Vector3 escapeGlideDestination;
    private Camera gameplayCamera;
    private ForkliftLoadArea forkliftLoadArea;
    private Quaternion penguinBaseRotation;
    private Quaternion boardingStartRotation;
    private Transform escapeFootPivot;
    private GameObject speedBonusSign;
    private float stateElapsed;
    private float speedBonusSignExpiry;
    private float descentStartsAt;
    private float descentLandingAt;
    private bool speedBonusGranted;
    private bool penguinPauseConfigured;
    private bool escapeRunFinishesEncounter;
    private bool triggerPackageArrivalObserved;
    private ConveyorDropCoordinator arrivalEventCoordinator;

    private bool IsEncounterEnabledForCurrentLevel => LevelProgression.CurrentLevel >= FirstPenguinLevel;

    // Level 2 is the baseline for the new encounter. Every later level
    // shortens both the touchdown delay and the following quiet interval by
    // exactly twenty percent, independently of belt-speed multipliers.
    private float EncounterTimingMultiplier => Mathf.Pow(
        PerLevelTimingMultiplier,
        Mathf.Max(0, LevelProgression.CurrentLevel - FirstPenguinLevel));
    private float EffectiveLandingDelayAfterTrigger =>
        landingDelayAfterTriggerPackage * EncounterTimingMultiplier;
    private float EffectiveLowerConveyorLandingPause =>
        lowerConveyorLandingPause * EncounterTimingMultiplier;
    private float EffectiveUpperConveyorLandingPause =>
        upperConveyorLandingPause * EncounterTimingMultiplier;

    private void Awake()
    {
        // Level 1 has no penguin at all. In particular, do not subscribe to
        // the package event or reserve the hidden conveyor pause there.
        if (!IsEncounterEnabledForCurrentLevel)
        {
            state = State.Finished;
            enabled = false;
            return;
        }

        ResolveReferences();
        ConfigurePenguinLandingPause();
    }

    private void OnEnable()
    {
        if (!IsEncounterEnabledForCurrentLevel)
        {
            state = State.Finished;
            enabled = false;
            return;
        }

        ResolveReferences();
    }

    private void Start()
    {
        if (!IsEncounterEnabledForCurrentLevel) return;

        ResolveReferences();
        ConfigurePenguinLandingPause();
    }

    private void Update()
    {
        if (!IsEncounterEnabledForCurrentLevel) return;

        ResolveReferences();
        ConfigurePenguinLandingPause();
        UpdateSpeedBonusSign();
        switch (state)
        {
            case State.Waiting:
                TryBeginPlannedEncounter();
                break;
            case State.Target: UpdateTarget(); break;
            case State.Descending: UpdateDescent(); break;
            case State.Landed: UpdateLanded(); break;
            case State.Boarding: UpdateBoarding(); break;
            case State.Escaping: UpdateEscaping(); break;
            case State.EscapeJump: UpdateEscapeJump(); break;
            case State.EscapeGlide: UpdateEscapeGlide(); break;
        }
    }

    private void OnDestroy()
    {
        UnsubscribeFromPackageArrivals();
        DestroyRuntime(targetMarker);
        DestroyRuntime(warningSign);
        DestroyRuntime(speedBonusSign);
        if (escapeFootPivot != null)
        {
            DestroyRuntime(escapeFootPivot.gameObject);
        }
        else if (state != State.Boarding)
        {
            DestroyRuntime(penguin);
        }
    }

    private void OnDisable()
    {
        UnsubscribeFromPackageArrivals();
    }

    private void TryBeginPlannedEncounter()
    {
        if (coordinator == null)
        {
            return;
        }

        if (coordinator.TryGetExpectedArrivalTime(triggerDeliveredPackageCount, out var expectedArrivalAt))
        {
            var plannedLandingAt = expectedArrivalAt + EffectiveLandingDelayAfterTrigger;
            var plannedDescentAt = plannedLandingAt - descentDuration;
            var warningStartAt = plannedDescentAt - targetLeadTime;
            if (Time.time >= warningStartAt)
            {
                BeginTarget(plannedDescentAt, plannedLandingAt);
            }
            return;
        }

        // A fallback only matters if a scene is configured without the
        // shared scheduler. In the authored yard, the schedule is available
        // long before package fifteen reaches its discharge point.
        if (coordinator.DeliveredPackageCount >= triggerDeliveredPackageCount)
        {
            BeginTarget(Time.time, Time.time + EffectiveLandingDelayAfterTrigger);
            SpawnPenguin();
        }
    }

    private void BeginTarget(float plannedDescentAt, float plannedLandingAt)
    {
        landingPoint = FindLandingPoint();
        if (landingPoint == null || penguinPrefab == null)
        {
            Debug.LogWarning("Penguin encounter needs a model and at least one landing point.");
            state = State.Finished;
            return;
        }

        landingPosition = landingPoint.position;
        descentStartsAt = plannedDescentAt;
        descentLandingAt = Mathf.Max(plannedDescentAt + 0.1f, plannedLandingAt);
        CreateWarningSign();
        stateElapsed = 0f;
        state = State.Target;
    }

    private void UpdateTarget()
    {
        stateElapsed += Time.deltaTime;
        UpdateWarningSign();

        if (Time.time >= descentStartsAt) SpawnPenguin();
    }

    private void SpawnPenguin()
    {
        DestroyRuntime(warningSign);
        warningSign = null;
        warningText = null;
        warningRenderer = null;

        // The warning owns the courtyard until it fades away. Creating the
        // marker only now guarantees it cannot blink underneath the text.
        if (targetMarker == null)
        {
            targetMarker = CreateTargetMarker(landingPosition);
        }

        // Do not pass Quaternion.identity here: the imported FBX has a baked
        // -90 degree axis conversion. Keeping its source rotation is what
        // keeps the penguin upright and the canopy directly above it.
        penguin = Instantiate(penguinPrefab);
        penguinBaseRotation = penguin.transform.rotation;
        penguin.transform.position = landingPosition + Vector3.up * parachuteStartHeight;
        penguin.name = "Penguin_Parachute_Encounter";
        penguin.transform.localScale *= visualScaleMultiplier;
        foreach (var collider in penguin.GetComponentsInChildren<Collider>(true)) collider.enabled = false;

        FacePenguinToCamera();
        groundedRootPosition = GetGroundedRootPosition(landingPosition);
        penguin.transform.position = groundedRootPosition + Vector3.up * parachuteStartHeight;
        descentStart = penguin.transform.position;
        descentStartsAt = Time.time;
        if (descentLandingAt <= descentStartsAt + 0.05f)
        {
            descentLandingAt = descentStartsAt + Mathf.Max(0.1f, descentDuration);
        }
        stateElapsed = 0f;
        state = State.Descending;
    }

    private void UpdateDescent()
    {
        if (penguin == null) { state = State.Finished; return; }
        stateElapsed += Time.deltaTime;
        UpdateTargetMarker();
        var descentTime = Mathf.Max(0.01f, descentLandingAt - descentStartsAt);
        var progress = Mathf.Clamp01((Time.time - descentStartsAt) / descentTime);
        var eased = Mathf.SmoothStep(0f, 1f, progress);
        var position = Vector3.Lerp(descentStart, groundedRootPosition, eased);
        var swayEnvelope = Mathf.Sin(progress * Mathf.PI);
        position += GetCameraRight() * (Mathf.Sin(stateElapsed * parachuteSwayFrequency) * parachuteSwayAmplitude * swayEnvelope);
        penguin.transform.position = position;
        FacePenguinToCamera();
        if (progress < 1f) return;

        penguin.transform.position = groundedRootPosition;
        FacePenguinToCamera();
        SetParachuteVisible(penguin.transform, false);
        KeepPenguinAboveGround(landingPosition.y);
        DestroyRuntime(targetMarker);
        targetMarker = null;
        // The coordinator reserved this interval while planning, but the
        // quiet time itself begins here – on the frame the penguin reaches
        // the concrete rather than when parcel fifteen reached its zone.
        coordinator?.NotifyPenguinLanded(Time.time);
        stateElapsed = 0f;
        state = State.Landed;
    }

    private void UpdateLanded()
    {
        if (penguin == null) { state = State.Finished; return; }
        KeepPenguinAboveGround(landingPosition.y);
        if (TouchesForklift()) { BeginBoarding(); return; }
        stateElapsed += Time.deltaTime;
        if (stateElapsed >= landedLifetime) BeginEscape();
    }

    private void BeginBoarding()
    {
        if (forklift == null || penguin == null) { state = State.Finished; return; }

        boardStart = penguin.transform.position;
        var pivot = aPillarAnchor != null ? aPillarAnchor : forklift.transform;
        boardOffset = boardStart - pivot.position;
        boardOffset.y = 0f;
        if (boardOffset.sqrMagnitude < 0.04f) boardOffset = -forklift.transform.forward * 0.75f;
        var seat = driverSeatAnchor != null ? driverSeatAnchor : forklift.transform;
        var seatedRotation = GetSeatedPenguinRotation(seat);
        // Begin precisely opposite to the final driving direction so the
        // visible 180-degree turn happens while the penguin swings inside.
        boardingStartRotation = seatedRotation * Quaternion.Euler(0f, 180f, 0f);
        penguin.transform.rotation = boardingStartRotation;
        stateElapsed = 0f;
        state = State.Boarding;
    }

    private void UpdateBoarding()
    {
        if (penguin == null || forklift == null) { state = State.Finished; return; }
        stateElapsed += Time.deltaTime;
        var progress = Mathf.Clamp01(stateElapsed / boardingDuration);
        var smooth = Mathf.SmoothStep(0f, 1f, progress);
        var pivot = aPillarAnchor != null ? aPillarAnchor : forklift.transform;
        var seat = driverSeatAnchor != null ? driverSeatAnchor : forklift.transform;
        var radius = Mathf.Max(0.28f, boardOffset.magnitude);
        var offset = Quaternion.AngleAxis(-205f * smooth, Vector3.up) * boardOffset.normalized;
        var arcPosition = pivot.position + offset * Mathf.Lerp(radius, 0.22f, smooth);
        arcPosition.y = Mathf.Lerp(boardStart.y, seat.position.y + 0.16f, smooth) + Mathf.Sin(progress * Mathf.PI) * 0.60f;
        penguin.transform.position = Vector3.Lerp(arcPosition, seat.position, smooth * smooth);
        penguin.transform.rotation = Quaternion.Slerp(
            boardingStartRotation,
            GetSeatedPenguinRotation(seat),
            smooth);
        if (progress < 1f) return;

        penguin.transform.SetParent(seat, true);
        penguin.transform.localPosition = Vector3.zero;
        penguin.transform.localRotation = Quaternion.Euler(0f, 180f, 0f) * penguinBaseRotation;
        if (!speedBonusGranted)
        {
            forklift.ApplyPenguinSpeedBonus(forkliftSpeedBonusMultiplier);
            speedBonusGranted = true;
            ShowSpeedBonusSign();
        }
        state = State.Finished;
    }

    private void BeginEscape()
    {
        if (penguin == null) { state = State.Finished; return; }
        BeginGroundEscape(FindOppositeConveyorPosition(penguin.transform.position), false);
    }

    private void BeginGroundEscape(Vector3 destination, bool finishesEncounter)
    {
        if (penguin == null) { state = State.Finished; return; }
        escapeDestination = destination;
        escapeRunFinishesEncounter = finishesEncounter;
        var runDirection = escapeDestination - penguin.transform.position;
        runDirection.y = 0f;
        if (runDirection.sqrMagnitude < 0.0001f) runDirection = Vector3.right;

        var footPosition = GetPenguinFootPosition();
        footPosition.y = landingPosition.y + groundClearance + 0.10f;
        escapeFootPosition = footPosition;
        // The opposite-conveyor target uses the visual model's root height.
        // The run pivot instead travels on the concrete at the feet height.
        escapeDestination.y = escapeFootPosition.y;
        escapeFootPivot = new GameObject("Penguin_Run_Foot_Pivot")
        {
            hideFlags = HideFlags.DontSave
        }.transform;
        escapeFootPivot.position = footPosition;
        escapeFootPivot.rotation = Quaternion.LookRotation(runDirection.normalized, Vector3.up);
        penguin.transform.SetParent(escapeFootPivot, true);
        penguin.transform.rotation = escapeFootPivot.rotation * penguinBaseRotation;

        // Give the feet a tiny clearance once before the run begins. After
        // that only the foot pivot moves; nothing is snapped upward each
        // frame, so the penguin cannot appear to hop.
        KeepPenguinAboveGround(landingPosition.y);
        stateElapsed = 0f;
        state = State.Escaping;
    }

    private void UpdateEscaping()
    {
        if (penguin == null || escapeFootPivot == null) { state = State.Finished; return; }
        stateElapsed += Time.deltaTime;
        var toDestination = escapeDestination - escapeFootPosition;
        toDestination.y = 0f;
        if (toDestination.sqrMagnitude > escapeArrivalTolerance * escapeArrivalTolerance)
        {
            var travelDirection = toDestination.normalized;
            escapeFootPosition = Vector3.MoveTowards(
                escapeFootPosition,
                escapeDestination,
                escapeSpeed * Time.deltaTime);
            escapeFootPivot.position = escapeFootPosition;

            // Rotate around the feet rather than translating the body from
            // side to side. The pitch axis is world X, making the head sway
            // visibly along Z while the grounded point remains fixed.
            var sway = Mathf.Sin(stateElapsed * escapeSwayFrequency) * escapeSwayAngleDegrees;
            escapeFootPivot.rotation =
                Quaternion.AngleAxis(sway, Vector3.right) *
                Quaternion.LookRotation(travelDirection, Vector3.up);
            return;
        }

        if (escapeRunFinishesEncounter)
        {
            FinishPenguinEscape();
        }
        else
        {
            BeginEscapeJump();
        }
    }

    private void BeginEscapeJump()
    {
        if (penguin == null) { state = State.Finished; return; }

        // The pivot owns the model during the grounded run. Detach before
        // flying so the foot point remains fixed only during that run.
        penguin.transform.SetParent(null, true);
        DestroyRuntime(escapeFootPivot.gameObject);
        escapeFootPivot = null;

        escapeJumpStart = penguin.transform.position;
        var side = Mathf.Abs(escapeDestination.x) > 0.01f
            ? Mathf.Sign(escapeDestination.x)
            : Mathf.Sign(escapeJumpStart.x);
        if (Mathf.Abs(side) < 0.01f) side = 1f;

        var feetToRootHeight = GetRootHeightAboveFeet();
        var roofClearance = FindHighestHallRoofY() + 1.50f + feetToRootHeight;
        escapeJumpApex = new Vector3(
            escapeJumpStart.x + side * 3.2f,
            Mathf.Max(escapeJumpStart.y + 2.4f, roofClearance),
            escapeJumpStart.z);
        SetParachuteVisible(penguin.transform, false);
        escapeGlideDestination = GetEscapeGlideDestination();
        FacePenguinAlong(GetEscapeFlightDirection());
        stateElapsed = 0f;
        state = State.EscapeJump;
    }

    private void UpdateEscapeJump()
    {
        if (penguin == null) { state = State.Finished; return; }
        stateElapsed += Time.deltaTime;
        var progress = Mathf.Clamp01(stateElapsed / escapeJumpDuration);
        penguin.transform.position = Vector3.Lerp(
            escapeJumpStart,
            escapeJumpApex,
            Mathf.SmoothStep(0f, 1f, progress));
        FacePenguinAlong(GetEscapeFlightDirection());
        if (progress < 1f) return;

        SetParachuteVisible(penguin.transform, true);
        escapeGlideStart = penguin.transform.position;
        stateElapsed = 0f;
        state = State.EscapeGlide;
    }

    private void UpdateEscapeGlide()
    {
        if (penguin == null) { state = State.Finished; return; }
        stateElapsed += Time.deltaTime;
        var progress = Mathf.Clamp01(stateElapsed / escapeGlideDuration);
        var horizontalProgress = Mathf.SmoothStep(0f, 1f, progress);
        var position = Vector3.Lerp(escapeGlideStart, escapeGlideDestination, horizontalProgress);
        // The slow start keeps the parachute and penguin above the halls
        // until they have passed the side wall and fence.
        position.y = Mathf.Lerp(
            escapeGlideStart.y,
            escapeGlideDestination.y,
            progress * progress * progress);
        penguin.transform.position = position;
        FacePenguinAlong(GetEscapeFlightDirection());
        if (progress < 1f) return;

        penguin.transform.position = escapeGlideDestination;
        SetParachuteVisible(penguin.transform, false);
        KeepPenguinAboveGround(landingPosition.y);
        BeginGroundEscape(GetGreenEdgeDestination(), true);
    }

    private void CreateWarningSign()
    {
        if (string.IsNullOrWhiteSpace(warningLabel)) return;

        warningSign = new GameObject("Penguin_Approach_Warning") { hideFlags = HideFlags.DontSave };
        warningText = warningSign.AddComponent<TextMesh>();
        warningText.text = warningLabel;
        warningText.anchor = TextAnchor.MiddleCenter;
        warningText.alignment = TextAlignment.Center;
        warningText.fontStyle = FontStyle.Bold;
        warningText.fontSize = 128;
        warningText.characterSize = 0.11f;
        warningText.color = new Color(0.08f, 0.95f, 1f, 1f);

        warningRenderer = warningText.GetComponent<Renderer>();
        if (warningRenderer != null)
        {
            warningRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            warningRenderer.receiveShadows = false;
        }

        UpdateWarningSign();
    }

    private void UpdateWarningSign()
    {
        if (warningSign == null || warningText == null) return;

        var camera = GetGameplayCamera();
        if (camera == null) return;

        var facadePosition = GetWarningYardPosition();
        warningSign.transform.SetPositionAndRotation(
            facadePosition,
            // A TextMesh draws its readable side along local -Z.
            Quaternion.LookRotation(facadePosition - camera.transform.position, camera.transform.up));

        var visible = !GamePauseOverlay.IsPaused &&
                      Mathf.Repeat(Time.time * warningBlinkFrequency, 1f) < 0.64f;
        if (warningRenderer != null) warningRenderer.enabled = visible;
    }

    private static Vector3 GetWarningYardPosition()
    {
        // Move five metres in positive Z, toward the gate. The spot remains
        // on clear central concrete and below the closest screen-pinned counter.
        return new Vector3(0f, 0.25f, 5.6f);
    }

    private void UpdateTargetMarker()
    {
        if (targetMarker == null) return;

        var pulse = 1.04f + 0.14f * Mathf.Sin(Time.time * 3.6f);
        targetMarker.transform.localScale = new Vector3(pulse, 1f, pulse);
        var visible = Mathf.Repeat(Time.time * 1.2f, 1f) < 0.64f;
        foreach (var renderer in targetMarker.GetComponentsInChildren<Renderer>(true)) renderer.enabled = visible;
    }

    private void ConfigurePenguinLandingPause()
    {
        if (!IsEncounterEnabledForCurrentLevel || penguinPauseConfigured || coordinator == null) return;

        coordinator.ConfigurePenguinLandingPause(
            triggerDeliveredPackageCount,
            EffectiveLandingDelayAfterTrigger,
            EffectiveLowerConveyorLandingPause,
            EffectiveUpperConveyorLandingPause);
        penguinPauseConfigured = true;
    }

    private void SubscribeToPackageArrivals()
    {
        if (arrivalEventCoordinator == coordinator || coordinator == null)
        {
            return;
        }

        UnsubscribeFromPackageArrivals();
        coordinator.PackageArrived += OnPackageArrived;
        arrivalEventCoordinator = coordinator;
    }

    private void UnsubscribeFromPackageArrivals()
    {
        if (arrivalEventCoordinator == null)
        {
            return;
        }

        arrivalEventCoordinator.PackageArrived -= OnPackageArrived;
        arrivalEventCoordinator = null;
    }

    private void OnPackageArrived(int packageOrdinal, float arrivedAt)
    {
        if (packageOrdinal != triggerDeliveredPackageCount || triggerPackageArrivalObserved)
        {
            return;
        }

        triggerPackageArrivalObserved = true;
        var targetLandingAt = arrivedAt + EffectiveLandingDelayAfterTrigger;
        descentLandingAt = targetLandingAt;

        // The expected time begins the descent early. The actual event only
        // retimes the endpoint: restarting descent from the current pose
        // would visibly brake the penguin for a frame when box 15 arrives.
        switch (state)
        {
            case State.Waiting:
                BeginTarget(arrivedAt, targetLandingAt);
                SpawnPenguin();
                break;
            case State.Target:
                descentStartsAt = Time.time;
                SpawnPenguin();
                break;
            case State.Descending:
                break;
        }
    }

    private Quaternion GetSeatedPenguinRotation(Transform seat)
    {
        return seat.rotation * Quaternion.Euler(0f, 180f, 0f) * penguinBaseRotation;
    }

    private void ShowSpeedBonusSign()
    {
        DestroyRuntime(speedBonusSign);
        if (forklift == null) return;

        speedBonusSign = new GameObject("Penguin_Speed_Bonus")
        {
            hideFlags = HideFlags.DontSave
        };
        speedBonusSign.transform.SetParent(forklift.transform, false);
        speedBonusSign.transform.localPosition = new Vector3(2.05f, 2.35f, -0.25f);

        var text = speedBonusSign.AddComponent<TextMesh>();
        text.text = "Speed +10";
        text.anchor = TextAnchor.MiddleCenter;
        text.alignment = TextAlignment.Center;
        text.fontStyle = FontStyle.Bold;
        text.fontSize = 108;
        text.characterSize = 0.085f;
        text.color = new Color(0.35f, 1f, 0.34f, 1f);
        var renderer = text.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        speedBonusSignExpiry = Time.time + 2.25f;
        UpdateSpeedBonusSign();
    }

    private void UpdateSpeedBonusSign()
    {
        if (speedBonusSign == null) return;
        if (Time.time >= speedBonusSignExpiry)
        {
            DestroyRuntime(speedBonusSign);
            speedBonusSign = null;
            return;
        }

        var camera = GetGameplayCamera();
        if (camera == null) return;
        var toCamera = speedBonusSign.transform.position - camera.transform.position;
        if (toCamera.sqrMagnitude > 0.0001f)
        {
            // TextMesh is readable from local -Z.
            speedBonusSign.transform.rotation = Quaternion.LookRotation(toCamera, camera.transform.up);
        }
    }

    private void FacePenguinToCamera()
    {
        if (penguin == null) return;
        var camera = GetGameplayCamera();
        if (camera == null) return;

        var toCamera = camera.transform.position - penguin.transform.position;
        toCamera.y = 0f;
        if (toCamera.sqrMagnitude < 0.0001f)
        {
            toCamera = -camera.transform.forward;
            toCamera.y = 0f;
        }
        FacePenguinAlong(toCamera);
    }

    private void FacePenguinAlong(Vector3 direction)
    {
        if (penguin == null) return;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f) return;

        // The exported penguin's beak points along its local +Z axis after
        // the FBX axis conversion. Reapply that conversion after the desired
        // yaw so no pitch/roll is introduced into the vertical parachute rig.
        penguin.transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up) * penguinBaseRotation;
    }

    private Vector3 GetCameraRight()
    {
        var camera = GetGameplayCamera();
        if (camera == null) return Vector3.right;

        var right = camera.transform.right;
        right.y = 0f;
        return right.sqrMagnitude > 0.0001f ? right.normalized : Vector3.right;
    }

    private Camera GetGameplayCamera()
    {
        if (gameplayCamera != null && gameplayCamera.isActiveAndEnabled) return gameplayCamera;
        gameplayCamera = Camera.main;
        if (gameplayCamera == null) gameplayCamera = UnityEngine.Object.FindAnyObjectByType<Camera>();
        return gameplayCamera;
    }

    private Vector3 GetGroundedRootPosition(Vector3 groundPosition)
    {
        var groundedPosition = groundPosition;
        if (!TryGetActiveRendererBounds(penguin.transform, out var bounds))
        {
            groundedPosition.y += groundClearance;
            return groundedPosition;
        }

        var rootToLowestVisiblePoint = bounds.min.y - penguin.transform.position.y;
        groundedPosition.y = groundPosition.y + groundClearance - rootToLowestVisiblePoint;
        return groundedPosition;
    }

    private void KeepPenguinAboveGround(float groundY)
    {
        if (penguin == null || !TryGetActiveRendererBounds(penguin.transform, out var bounds)) return;
        var correction = groundY + groundClearance - bounds.min.y;
        if (correction > 0f) penguin.transform.position += Vector3.up * correction;
    }

    private Vector3 FindOppositeConveyorPosition(Vector3 from)
    {
        var landingSide = Mathf.Sign(landingPosition.x);
        if (Mathf.Abs(landingSide) < 0.01f) landingSide = Mathf.Sign(from.x);
        if (Mathf.Abs(landingSide) < 0.01f) landingSide = 1f;
        var oppositeSide = -landingSide;

        var closest = new Vector3(oppositeSide * 8f, groundedRootPosition.y, from.z);
        var closestDistance = float.PositiveInfinity;
        var conveyorNames = new[] { "Conveyor_Left", "Conveyor_Right_Mirrored" };
        foreach (var conveyorName in conveyorNames)
        {
            var conveyor = GameObject.Find(conveyorName);
            if (conveyor == null || !TryGetActiveRendererBounds(conveyor.transform, out var bounds)) continue;
            if (Mathf.Sign(bounds.center.x) != oppositeSide) continue;

            var conveyorPoint = bounds.ClosestPoint(from);
            var awayFromConveyor = from - conveyorPoint;
            awayFromConveyor.y = 0f;
            if (awayFromConveyor.sqrMagnitude < 0.0001f) continue;
            var candidate = conveyorPoint + awayFromConveyor.normalized * escapeWallClearance;
            candidate.y = groundedRootPosition.y;
            var horizontalDistance = HorizontalDistanceSquared(from, candidate);
            if (horizontalDistance >= closestDistance) continue;

            closest = candidate;
            closestDistance = horizontalDistance;
        }

        return closest;
    }

    private Vector3 GetPenguinFootPosition()
    {
        if (penguin != null && TryGetActiveRendererBounds(penguin.transform, out var bounds))
        {
            return new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
        }

        return penguin != null ? penguin.transform.position : landingPosition;
    }

    private float GetRootHeightAboveFeet()
    {
        return penguin == null ? 0f : Mathf.Max(0f, penguin.transform.position.y - GetPenguinFootPosition().y);
    }

    private float FindHighestHallRoofY()
    {
        var roofTop = 10.8f;
        foreach (var hallName in new[] { "North_Main_Hall", "West_Hall", "East_Hall" })
        {
            var hall = GameObject.Find(hallName);
            if (hall != null && TryGetActiveRendererBounds(hall.transform, out var bounds))
            {
                roofTop = Mathf.Max(roofTop, bounds.max.y);
            }
        }

        return roofTop;
    }

    private Vector3 GetEscapeFlightDirection()
    {
        // Use the planned glide line for both the leap and the parachute
        // descent. The penguin is therefore seen in profile, facing the
        // direction in which it escapes instead of turning back to camera.
        var direction = escapeGlideDestination - escapeJumpStart;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f)
        {
            var side = Mathf.Sign(escapeDestination.x);
            if (Mathf.Abs(side) < 0.01f) side = 1f;
            direction = new Vector3(side, 0f, 0.25f);
        }

        return direction.normalized;
    }

    private Vector3 GetGreenEdgeDestination()
    {
        // Continue the escape across the grass after landing beyond the
        // fence. The visible grass mesh defines the actual outer edge, so
        // this also follows future hand-adjustments to the yard size.
        var side = Mathf.Sign(escapeGlideDestination.x);
        if (Mathf.Abs(side) < 0.01f) side = Mathf.Sign(escapeDestination.x);
        if (Mathf.Abs(side) < 0.01f) side = 1f;

        var edgeX = side * 98f;
        var grass = GameObject.Find("Exterior_Grass_Ground");
        if (grass != null && TryGetActiveRendererBounds(grass.transform, out var grassBounds))
        {
            edgeX = side > 0f ? grassBounds.max.x - 1.5f : grassBounds.min.x + 1.5f;
        }

        return new Vector3(edgeX, landingPosition.y, escapeGlideDestination.z);
    }

    private void FinishPenguinEscape()
    {
        if (escapeFootPivot != null)
        {
            DestroyRuntime(escapeFootPivot.gameObject);
            escapeFootPivot = null;
            penguin = null;
        }
        else
        {
            DestroyRuntime(penguin);
            penguin = null;
        }

        state = State.Finished;
    }

    private Vector3 GetEscapeGlideDestination()
    {
        var side = Mathf.Abs(escapeDestination.x) > 0.01f
            ? Mathf.Sign(escapeDestination.x)
            : Mathf.Sign(escapeJumpStart.x);
        if (Mathf.Abs(side) < 0.01f) side = 1f;

        var fence = GameObject.Find(side > 0f ? "Fence_East" : "Fence_West");
        var outsideX = side * 42.5f;
        if (fence != null && TryGetActiveRendererBounds(fence.transform, out var fenceBounds))
        {
            outsideX = side > 0f ? fenceBounds.max.x + 2.5f : fenceBounds.min.x - 2.5f;
        }

        // A small Z drift keeps the descent visibly diagonal even from the
        // high camera, while the sideward path carries the penguin past the
        // fence and away from the production yard.
        var targetZ = Mathf.Clamp(escapeDestination.z + 4.2f, -26f, 18f);
        var destinationFoot = new Vector3(outsideX, landingPosition.y, targetZ);
        return GetGroundedRootPosition(destinationFoot);
    }

    private Transform FindLandingPoint()
    {
        if (landingPoints == null || landingPoints.Length == 0) return null;
        var start = Random.Range(0, landingPoints.Length);
        for (var offset = 0; offset < landingPoints.Length; offset++)
        {
            var candidate = landingPoints[(start + offset) % landingPoints.Length];
            if (candidate == null) continue;
            if (forklift == null) return candidate;
            var delta = candidate.position - forklift.transform.position;
            delta.y = 0f;
            if (delta.sqrMagnitude >= minimumLandingDistanceFromForklift * minimumLandingDistanceFromForklift) return candidate;
        }

        return landingPoints[start];
    }

    private bool TouchesForklift()
    {
        if (forklift == null || penguin == null) return false;
        if (forkliftLoadArea != null && forkliftLoadArea.loadAnchor != null)
        {
            // The load anchor is centred directly over the two tines, so this
            // gives the visible forks their own reliable capture footprint.
            var localPoint = forkliftLoadArea.loadAnchor.InverseTransformPoint(penguin.transform.position);
            var forkWidth = forkliftLoadArea.captureWidth * 0.5f + forkliftHitDistance * 0.35f;
            var forkLength = forkliftLoadArea.captureLength * 0.5f + forkliftHitDistance * 0.45f;
            if (Mathf.Abs(localPoint.x) <= forkWidth && Mathf.Abs(localPoint.z) <= forkLength) return true;
        }

        foreach (var collider in forklift.GetComponentsInChildren<Collider>(true))
        {
            if (!collider.enabled || !collider.gameObject.activeInHierarchy) continue;
            if (IsWithinHorizontalDistance(collider.ClosestPoint(penguin.transform.position), penguin.transform.position, forkliftHitDistance)) return true;
        }

        // Accept contact with either the vehicle body or its forward fork
        // footprint if a visual part has no collider. The visible forklift
        // front points along local -Z.
        var bodyCentre = forklift.transform.position;
        var forkCentre = bodyCentre - forklift.transform.forward * 1.15f;
        return IsWithinHorizontalDistance(bodyCentre, penguin.transform.position, forkliftHitDistance) ||
               IsWithinHorizontalDistance(forkCentre, penguin.transform.position, forkliftHitDistance);
    }

    private void ResolveReferences()
    {
        if (coordinator == null) coordinator = UnityEngine.Object.FindAnyObjectByType<ConveyorDropCoordinator>();
        if (forklift == null) forklift = UnityEngine.Object.FindAnyObjectByType<ForkliftController>();
        if (forkliftLoadArea == null) forkliftLoadArea = UnityEngine.Object.FindAnyObjectByType<ForkliftLoadArea>();
        SubscribeToPackageArrivals();
    }

    private static bool IsWithinHorizontalDistance(Vector3 first, Vector3 second, float distance)
    {
        return HorizontalDistanceSquared(first, second) <= distance * distance;
    }

    private static float HorizontalDistanceSquared(Vector3 first, Vector3 second)
    {
        var delta = first - second;
        delta.y = 0f;
        return delta.sqrMagnitude;
    }

    private static bool TryGetActiveRendererBounds(Transform root, out Bounds bounds)
    {
        var hasBounds = false;
        bounds = default;
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
            if (!hasBounds)
            {
                bounds = renderer.bounds;
                hasBounds = true;
            }
            else bounds.Encapsulate(renderer.bounds);
        }

        return hasBounds;
    }

    private static void SetParachuteVisible(Transform root, bool visible)
    {
        foreach (var part in root.GetComponentsInChildren<Transform>(true))
        {
            if (part == root) continue;
            var name = part.name.ToLowerInvariant();
            if (name.Contains("parachute") || name.Contains("canopy") || name.Contains("line")) part.gameObject.SetActive(visible);
        }
    }

    private static GameObject CreateTargetMarker(Vector3 position)
    {
        var root = new GameObject("Penguin_Landing_Target") { hideFlags = HideFlags.DontSave };
        root.transform.position = position + Vector3.up * 0.012f;
        CreateMarkerDisc("Outer_Ring", root.transform, 0f, 1.35f, new Color(1f, 0.12f, 0.06f));
        CreateMarkerDisc("Inner_Ring", root.transform, 0.01f, 0.82f, new Color(1f, 0.94f, 0.64f));
        CreateMarkerDisc("Bullseye", root.transform, 0.02f, 0.29f, new Color(1f, 0.12f, 0.06f));
        return root;
    }

    private static void CreateMarkerDisc(string name, Transform parent, float height, float diameter, Color color)
    {
        var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        disc.name = name;
        disc.hideFlags = HideFlags.DontSave;
        disc.transform.SetParent(parent, false);
        disc.transform.localPosition = Vector3.up * height;
        disc.transform.localScale = new Vector3(diameter, 0.01f, diameter);
        var collider = disc.GetComponent<Collider>();
        if (collider != null) UnityEngine.Object.Destroy(collider);
        var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Standard");
        var material = new Material(shader) { hideFlags = HideFlags.DontSave };
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        else if (material.HasProperty("_Color")) material.color = color;
        disc.GetComponent<Renderer>().sharedMaterial = material;
    }

    private static void DestroyRuntime(UnityEngine.Object runtimeObject)
    {
        if (runtimeObject != null) UnityEngine.Object.Destroy(runtimeObject);
    }
}
