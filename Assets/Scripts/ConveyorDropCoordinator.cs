using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Plans package departures before they enter either conveyor. Each departure
/// is calculated from the length of its selected branch, so parcels keep
/// moving continuously and their actual falls remain safely separated.
/// </summary>
[DisallowMultipleComponent]
public sealed class ConveyorDropCoordinator : MonoBehaviour
{
    private const float DispatchSchedulingTolerance = 0.02f;
    // Accounts for frame quantisation while keeping the actual fall gap safely
    // above the requested 4 seconds.
    private const float FallSafetyMargin = 0.25f;
    private const int MaximumConsecutivePackagesPerBranch = 2;
    private const int MaximumConsecutivePackagesPerSide = 5;
    private const int FirstLevelMaximumSameRoutePairs = 7;
    private const int PhysicalBranchCount = 4;
    private const float MinimumPackagesPerBranchShare = 0.20f;
    private const float MaximumPackagesPerBranchShare = 0.30f;
    // This is deliberately separate from the normal 3.5–7 second cadence:
    // it gives the scheduler room to reserve the penguin's landing window
    // while never asking a parcel already on a belt to stop.
    private const float PenguinPauseSchedulingSlack = 3f;
    // A complete round is planned before the first package leaves its
    // loading window. Retrying a random plan here prevents a locally valid
    // choice from later leaving no legal route for package 30.
    private const int FullRoundPlanningAttempts = 512;

    [Tooltip("Minimum time between the start of any two parcel falls in the whole yard.")]
    [Min(0.1f)] public float minimumDelayBetweenFalls = 4f;

    [Tooltip("Maximum time allowed between the start of any two parcel falls in the whole yard.")]
    [Min(0.1f)] public float maximumDelayBetweenFalls = 7f;

    [Tooltip("Minimum spacing between departures from either loading window. This prevents simultaneous starts while allowing unequal path lengths to meet the fall deadline.")]
    [Min(0.5f)] public float minimumDispatchGap = 0.55f;

    [Tooltip("Maximum number of parcels dispatched in one play-through. Generation stops after this amount.")]
    [Min(1)] public int maximumPackagesPerRound = 30;

    public ConveyorBoxSpawner leftSpawner;
    public ConveyorBoxSpawner rightSpawner;

    // Packages are planned in their fall order, but stored here in their real
    // departure order. A long route can therefore start before a short route
    // and still arrive after it without either package waiting on a belt.
    private readonly List<ScheduledPackage> scheduledPackages = new List<ScheduledPackage>();
    private readonly Dictionary<int, float> expectedArrivalTimes = new Dictionary<int, float>();
    private bool hasSchedule;
    private float lastScheduledFallTime;
    private float lastActualDispatchTime = float.NegativeInfinity;
    private ConveyorBoxSpawner lastScheduledSpawner;
    private ConveyorBranch lastScheduledBranch;
    private int scheduledSideRun;
    private int scheduledRouteRun;
    private int scheduledSameRoutePairCount;
    private int maximumSameRoutePairCount;
    private int reservedPackageCount;
    private readonly int[] reservedPackagesPerPhysicalBranch = new int[PhysicalBranchCount];
    private int minimumPackagesPerPhysicalBranch;
    private int maximumPackagesPerPhysicalBranch;
    private bool enforcePhysicalBranchQuotas;
    private bool penguinPauseConfigured;
    private int penguinPauseTriggerPackageCount;
    private float penguinLandingDelayAfterTrigger;
    private float penguinLowerConveyorPause;
    private float penguinUpperConveyorPause;
    private bool penguinPauseReserved;
    private int penguinPausePackageOrdinal;
    private float penguinPauseExpectedFallTime;
    private float penguinPauseDuration;
    private bool allowRecoveryTiming;
    public int DispatchedPackageCount { get; private set; }
    public float PenguinQuietUntil { get; private set; } = float.NegativeInfinity;

    /// <summary>
    /// Number of parcels which have completed their conveyor journey. A parcel
    /// counts here only after it reaches a landing zone or is caught by the
    /// forklift, never merely when it leaves its loading window.
    /// </summary>
    public int DeliveredPackageCount { get; private set; }

    /// <summary>
    /// Raised on the frame an individual parcel really reaches its discharge
    /// zone (or is caught by the forklift). The first argument is the
    /// zero-free order in which parcels arrived during the round.
    /// </summary>
    public event System.Action<int, float> PackageArrived;

    private void Awake()
    {
        // The active level shortens the fall spacing before Start plans the
        // first route, so every scheduled departure uses its own cadence.
        var delayMultiplier = LevelProgression.FallDelayMultiplier;
        minimumDelayBetweenFalls *= delayMultiplier;
        maximumDelayBetweenFalls *= delayMultiplier;
        // Level 1 allows seven pairs of consecutive packages on one
        // physical route. Every next level lowers that total by one.
        maximumSameRoutePairCount = Mathf.Max(
            0,
            FirstLevelMaximumSameRoutePairs - (LevelProgression.CurrentLevel - 1));
        ConfigurePhysicalBranchQuotas();
    }

    private struct ScheduledPackage
    {
        public readonly ConveyorBoxSpawner spawner;
        public readonly Transform[] path;
        public readonly ConveyorBranch branch;
        public readonly int packageNumber;
        public readonly float dispatchTime;
        public readonly float fallTime;

        public ScheduledPackage(
            ConveyorBoxSpawner spawner,
            Transform[] path,
            ConveyorBranch branch,
            int packageNumber,
            float dispatchTime,
            float fallTime)
        {
            this.spawner = spawner;
            this.path = path;
            this.branch = branch;
            this.packageNumber = packageNumber;
            this.dispatchTime = dispatchTime;
            this.fallTime = fallTime;
        }
    }

    private struct RouteChoice
    {
        public readonly ConveyorBoxSpawner spawner;
        public readonly ConveyorBranch branch;
        public readonly float dispatchTime;
        public readonly float fallTime;

        public RouteChoice(
            ConveyorBoxSpawner spawner,
            ConveyorBranch branch,
            float dispatchTime,
            float fallTime)
        {
            this.spawner = spawner;
            this.branch = branch;
            this.dispatchTime = dispatchTime;
            this.fallTime = fallTime;
        }
    }

    private void Start()
    {
        if (TryPlanEntireRound(false))
        {
            return;
        }

        // The normal planner preserves the level-specific maximum fall gap.
        // In the very rare case that four uneven paths leave no mathematical
        // slot, keep the game finishable rather than permanently stopping
        // before package 30. The recovery still keeps every route quota,
        // branch run and minimum fall separation intact.
        if (!TryPlanEntireRound(true))
        {
            Debug.LogError("Unable to build a complete conveyor round.");
        }
        else
        {
            Debug.LogWarning("Conveyor timing used one recovery interval to complete this round.");
        }
    }

    /// <summary>
    /// Reserves the one exceptional gap surrounding the penguin encounter.
    /// It is configured in the encounter's Awake method, before this
    /// coordinator begins preplanning departures in Start.
    /// </summary>
    public void ConfigurePenguinLandingPause(
        int triggerDeliveredPackageCount,
        float landingDelayAfterTrigger,
        float lowerConveyorPause,
        float upperConveyorPause)
    {
        if (hasSchedule && !penguinPauseReserved)
        {
            Debug.LogWarning("Penguin landing pause was configured after conveyor scheduling had already started.");
        }

        penguinPauseConfigured = true;
        penguinPauseTriggerPackageCount = Mathf.Max(1, triggerDeliveredPackageCount);
        penguinLandingDelayAfterTrigger = Mathf.Max(0f, landingDelayAfterTrigger);
        penguinLowerConveyorPause = Mathf.Max(0f, lowerConveyorPause);
        penguinUpperConveyorPause = Mathf.Max(0f, upperConveyorPause);
    }

    /// <summary>
    /// Supplies the predicted physical arrival of a globally ordered parcel.
    /// The penguin uses this to begin its warning and descent before package
    /// fifteen reaches the floor, then corrects to the real arrival event.
    /// </summary>
    public bool TryGetExpectedArrivalTime(int packageOrdinal, out float arrivalTime)
    {
        return expectedArrivalTimes.TryGetValue(packageOrdinal, out arrivalTime);
    }

    /// <summary>
    /// Starts the promised quiet interval from the actual landing frame. The
    /// following fall was reserved before dispatch, so no active parcel needs
    /// to be slowed or stopped to honour this moment.
    /// </summary>
    public void NotifyPenguinLanded(float landedAt)
    {
        if (!penguinPauseReserved)
        {
            Debug.LogWarning("Penguin landed without a reserved conveyor pause.");
            return;
        }

        PenguinQuietUntil = landedAt + penguinPauseDuration;
        if (penguinPauseExpectedFallTime + DispatchSchedulingTolerance < PenguinQuietUntil)
        {
            Debug.LogWarning(
                $"Penguin pause reservation for parcel {penguinPausePackageOrdinal} is too short. " +
                $"Expected fall {penguinPauseExpectedFallTime:F2}, quiet until {PenguinQuietUntil:F2}.");
        }
    }

    private void Update()
    {
        while (scheduledPackages.Count > 0 && Time.time >= scheduledPackages[0].dispatchTime)
        {
            var package = scheduledPackages[0];
            scheduledPackages.RemoveAt(0);
            package.spawner.SpawnReservedPackage(package.path, package.branch, package.packageNumber);
            lastActualDispatchTime = Time.time;
            DispatchedPackageCount++;
        }

    }

    private bool TryPlanEntireRound(bool useRecoveryTiming)
    {
        for (var attempt = 0; attempt < FullRoundPlanningAttempts; attempt++)
        {
            ResetRoundPlan();
            allowRecoveryTiming = useRecoveryTiming;
            var planCompleted = true;
            while (reservedPackageCount < maximumPackagesPerRound)
            {
                if (TryScheduleNextPackage(false)) continue;
                planCompleted = false;
                break;
            }

            if (planCompleted)
            {
                return true;
            }
        }

        ResetRoundPlan();
        allowRecoveryTiming = false;
        return false;
    }

    private void ResetRoundPlan()
    {
        scheduledPackages.Clear();
        expectedArrivalTimes.Clear();
        hasSchedule = false;
        lastScheduledFallTime = 0f;
        lastActualDispatchTime = float.NegativeInfinity;
        lastScheduledSpawner = null;
        lastScheduledBranch = ConveyorBranch.A;
        scheduledSideRun = 0;
        scheduledRouteRun = 0;
        scheduledSameRoutePairCount = 0;
        reservedPackageCount = 0;
        System.Array.Clear(reservedPackagesPerPhysicalBranch, 0, reservedPackagesPerPhysicalBranch.Length);
        penguinPauseReserved = false;
        penguinPausePackageOrdinal = 0;
        penguinPauseExpectedFallTime = 0f;
        penguinPauseDuration = 0f;
        DispatchedPackageCount = 0;
        PenguinQuietUntil = float.NegativeInfinity;
        leftSpawner?.ResetPackageReservations();
        rightSpawner?.ResetPackageReservations();
    }

    private bool TryScheduleNextPackage(bool logFailure)
    {
        if (reservedPackageCount >= maximumPackagesPerRound)
        {
            return false;
        }

        var validChoices = new List<RouteChoice>(4);
        AddValidChoices(leftSpawner, validChoices);
        AddValidChoices(rightSpawner, validChoices);
        if (validChoices.Count == 0)
        {
            if (logFailure)
            {
                Debug.LogWarning("No valid conveyor route could meet the routing and fall-timing rules.");
            }
            return false;
        }

        var isFirstPostPenguinPackage = IsSchedulingFirstPostPenguinPackage();

        // The first parcel of a round always takes the shortest available
        // route. Later parcels retain the requested random routing rules.
        var choice = hasSchedule
            ? FindBalancedRandomRoute(validChoices)
            : FindShortestRoute(validChoices);
        if (!choice.spawner.TryReservePath(choice.branch, out var path, out var packageNumber))
        {
            return false;
        }

        RegisterPhysicalBranchReservation(choice.spawner, choice.branch);

        var roundPackageOrdinal = reservedPackageCount + 1;
        InsertScheduledPackage(new ScheduledPackage(
            choice.spawner,
            path,
            choice.branch,
            packageNumber,
            choice.dispatchTime,
            choice.fallTime));
        expectedArrivalTimes[roundPackageOrdinal] = choice.fallTime + ConveyorPackageMover.DropDuration;

        if (isFirstPostPenguinPackage)
        {
            penguinPauseReserved = true;
            penguinPausePackageOrdinal = reservedPackageCount + 1;
            penguinPauseExpectedFallTime = choice.fallTime;
            penguinPauseDuration = GetPenguinPauseDuration(choice.spawner, choice.branch);
        }

        var hadSchedule = hasSchedule;
        var createdSameRoutePair = hadSchedule &&
                                   choice.spawner == lastScheduledSpawner &&
                                   choice.branch == lastScheduledBranch;
        if (createdSameRoutePair)
        {
            scheduledSameRoutePairCount++;
        }
        scheduledSideRun = hadSchedule && choice.spawner == lastScheduledSpawner
            ? scheduledSideRun + 1
            : 1;
        scheduledRouteRun = hadSchedule && choice.spawner == lastScheduledSpawner && choice.branch == lastScheduledBranch
            ? scheduledRouteRun + 1
            : 1;
        reservedPackageCount++;

        hasSchedule = true;
        lastScheduledSpawner = choice.spawner;
        lastScheduledBranch = choice.branch;
        lastScheduledFallTime = choice.fallTime;
        return true;
    }

    private static RouteChoice FindShortestRoute(List<RouteChoice> choices)
    {
        var shortestChoice = choices[0];
        var shortestTravelTime = GetTravelTime(shortestChoice);
        for (var index = 1; index < choices.Count; index++)
        {
            var candidate = choices[index];
            var candidateTravelTime = GetTravelTime(candidate);
            if (candidateTravelTime < shortestTravelTime)
            {
                shortestChoice = candidate;
                shortestTravelTime = candidateTravelTime;
            }
        }

        return shortestChoice;
    }

    /// <summary>
    /// Selects from every timing- and quota-valid route. The feasibility
    /// checks still guarantee the 20–30% share for all four physical routes,
    /// but no artificial A/B alternation makes the visible flow predictable.
    /// </summary>
    private RouteChoice FindBalancedRandomRoute(List<RouteChoice> choices)
    {
        return choices[Random.Range(0, choices.Count)];
    }

    private int GetReservedPhysicalBranchCount(RouteChoice choice)
    {
        if (!enforcePhysicalBranchQuotas)
        {
            return 0;
        }

        var routeIndex = GetPhysicalBranchIndex(choice.spawner, choice.branch);
        return routeIndex >= 0 ? reservedPackagesPerPhysicalBranch[routeIndex] : int.MaxValue;
    }

    private static float GetTravelTime(RouteChoice choice)
    {
        if (choice.spawner == null)
        {
            return float.PositiveInfinity;
        }

        var path = choice.branch == ConveyorBranch.A
            ? choice.spawner.branchPathA
            : choice.spawner.branchPathB;
        return choice.spawner.GetTravelTime(path);
    }

    private void AddValidChoices(ConveyorBoxSpawner spawner, List<RouteChoice> choices)
    {
        AddValidChoice(spawner, ConveyorBranch.A, choices);
        AddValidChoice(spawner, ConveyorBranch.B, choices);
    }

    private void AddValidChoice(
        ConveyorBoxSpawner spawner,
        ConveyorBranch branch,
        List<RouteChoice> choices)
    {
        if (spawner == null || !spawner.HasValidPath(branch))
        {
            return;
        }

        if (!CanReservePhysicalBranch(spawner, branch))
        {
            return;
        }

        if (hasSchedule && spawner == lastScheduledSpawner && scheduledSideRun >= MaximumConsecutivePackagesPerSide)
        {
            return;
        }

        // A branch is specific to one physical conveyor. For example,
        // left-A and right-A are separate branches and may follow each other.
        var repeatsPreviousPhysicalRoute = hasSchedule &&
                                           spawner == lastScheduledSpawner &&
                                           branch == lastScheduledBranch;
        if (repeatsPreviousPhysicalRoute && scheduledRouteRun >= MaximumConsecutivePackagesPerBranch)
        {
            return;
        }

        // A route may form a pair but not a triple. Count those pairs over
        // the whole round so Level 1 permits at most seven, Level 2 six,
        // down to three in Level 5.
        if (repeatsPreviousPhysicalRoute && scheduledRouteRun == 1 &&
            scheduledSameRoutePairCount >= maximumSameRoutePairCount)
        {
            return;
        }

        var path = branch == ConveyorBranch.A ? spawner.branchPathA : spawner.branchPathB;
        var travelTime = spawner.GetTravelTime(path);
        if (travelTime <= 0f)
        {
            return;
        }

        float dispatchTime;
        float fallTime;
        if (!hasSchedule)
        {
            // The title overlay starts in Awake, before this coordinator
            // reaches Start. Send the first (short-route) parcel immediately
            // so it begins moving while the introductory title is visible.
            dispatchTime = Time.time;
            fallTime = dispatchTime + travelTime;
        }
        else
        {
            // Keep the small scheduling margin proportional to the level as
            // well: Level 2 therefore shortens the actual fall cadence by a
            // full 20%, rather than only shortening the visible setting.
            var safetyMargin = FallSafetyMargin * LevelProgression.FallDelayMultiplier;
            var minimumFallDelay = Mathf.Max(0.1f, minimumDelayBetweenFalls) + safetyMargin;
            var maximumFallDelay = Mathf.Max(minimumFallDelay, maximumDelayBetweenFalls);
            var earliestFall = lastScheduledFallTime + minimumFallDelay;
            var latestFall = lastScheduledFallTime + maximumFallDelay;

            // The special interval is based on the future parcel's actual
            // physical route. The penguin lands four seconds after parcel
            // fifteen arrives; from that landing there are five seconds on
            // either conveyor before this next fall.
            if (IsSchedulingFirstPostPenguinPackage())
            {
                var pauseDuration = GetPenguinPauseDuration(spawner, branch);
                var requiredGap = ConveyorPackageMover.DropDuration +
                                  penguinLandingDelayAfterTrigger +
                                  pauseDuration +
                                  FallSafetyMargin;
                earliestFall = lastScheduledFallTime + Mathf.Max(minimumFallDelay, requiredGap);
                latestFall = earliestFall + PenguinPauseSchedulingSlack;
            }

            // The departure can be between the two route-specific bounds.
            // Search the global, time-sorted dispatch list for a free slot so
            // no two loading windows start a box at the same time.
            var earliestDispatch = earliestFall - travelTime;
            var latestDispatch = latestFall - travelTime;
            if (!TryFindAvailableDispatchTime(
                    spawner,
                    earliestDispatch,
                    latestDispatch,
                    out dispatchTime))
            {
                // This branch is used only after many complete normal-plan
                // attempts have failed. It prevents a single unlucky route
                // combination from leaving the level without an ending.
                if (!allowRecoveryTiming || !TryFindAvailableDispatchTime(
                        spawner,
                        earliestDispatch,
                        float.PositiveInfinity,
                        out dispatchTime))
                {
                    return;
                }
            }

            fallTime = dispatchTime + travelTime;
        }

        choices.Add(new RouteChoice(spawner, branch, dispatchTime, fallTime));
    }

    /// <summary>
    /// Inserts a reservation by its real departure time. Fall-order state is
    /// kept separately, because the long paths need to leave ahead of shorter
    /// paths that are due to fall first.
    /// </summary>
    private void InsertScheduledPackage(ScheduledPackage package)
    {
        var insertIndex = scheduledPackages.Count;
        for (var index = 0; index < scheduledPackages.Count; index++)
        {
            if (package.dispatchTime < scheduledPackages[index].dispatchTime)
            {
                insertIndex = index;
                break;
            }
        }

        scheduledPackages.Insert(insertIndex, package);
    }

    private bool IsSchedulingFirstPostPenguinPackage()
    {
        return penguinPauseConfigured &&
               !penguinPauseReserved &&
               reservedPackageCount == penguinPauseTriggerPackageCount;
    }

    private float GetPenguinPauseDuration(ConveyorBoxSpawner spawner, ConveyorBranch branch)
    {
        return spawner != null && spawner.IsLowerBranch(branch)
            ? penguinLowerConveyorPause
            : penguinUpperConveyorPause;
    }

    /// <summary>
    /// Finds the earliest usable start inside a parcel's allowed fall-time
    /// window. Existing reservations are ordered by departure time, so a
    /// candidate may be safely inserted before a previously planned short
    /// route when its own route is longer.
    /// </summary>
    private bool TryFindAvailableDispatchTime(
        ConveyorBoxSpawner spawner,
        float earliestDispatchTime,
        float latestDispatchTime,
        out float dispatchTime)
    {
        var dispatchGap = Mathf.Max(0.5f, minimumDispatchGap);
        var candidate = Mathf.Max(
            earliestDispatchTime,
            Time.time + DispatchSchedulingTolerance,
            lastActualDispatchTime + dispatchGap);

        // Packages sharing a conveyor must preserve their departure order.
        // Otherwise a following parcel could move that conveyor's physical
        // flap before the preceding parcel has passed the branch split.
        for (var index = 0; index < scheduledPackages.Count; index++)
        {
            var reservation = scheduledPackages[index];
            if (reservation.spawner == spawner)
            {
                candidate = Mathf.Max(candidate, reservation.dispatchTime + dispatchGap);
            }
        }

        for (var index = 0; index < scheduledPackages.Count; index++)
        {
            var reservedTime = scheduledPackages[index].dispatchTime;
            if (candidate <= reservedTime - dispatchGap)
            {
                break;
            }

            candidate = Mathf.Max(candidate, reservedTime + dispatchGap);
        }

        if (candidate > latestDispatchTime + DispatchSchedulingTolerance)
        {
            dispatchTime = 0f;
            return false;
        }

        dispatchTime = candidate;
        return true;
    }

    /// <summary>
    /// Enforces the requested 20–30% share for each physical route: left-A,
    /// left-B, right-A and right-B. With the 30-parcel round this is 6–9
    /// parcels per route. The lower-bound feasibility test keeps enough
    /// future parcels available for every route that is still below its
    /// minimum while preserving the existing random selection among choices.
    /// </summary>
    private void ConfigurePhysicalBranchQuotas()
    {
        minimumPackagesPerPhysicalBranch = Mathf.CeilToInt(
            maximumPackagesPerRound * MinimumPackagesPerBranchShare);
        maximumPackagesPerPhysicalBranch = Mathf.FloorToInt(
            maximumPackagesPerRound * MaximumPackagesPerBranchShare);

        var hasFourRoutes =
            leftSpawner != null && leftSpawner.HasValidPath(ConveyorBranch.A) && leftSpawner.HasValidPath(ConveyorBranch.B) &&
            rightSpawner != null && rightSpawner.HasValidPath(ConveyorBranch.A) && rightSpawner.HasValidPath(ConveyorBranch.B);
        enforcePhysicalBranchQuotas = hasFourRoutes &&
            minimumPackagesPerPhysicalBranch * PhysicalBranchCount <= maximumPackagesPerRound &&
            maximumPackagesPerPhysicalBranch * PhysicalBranchCount >= maximumPackagesPerRound;

        if (!enforcePhysicalBranchQuotas)
        {
            Debug.LogWarning("The 20–30% branch quota needs four valid conveyor routes and a compatible round size.");
        }
    }

    private bool CanReservePhysicalBranch(ConveyorBoxSpawner spawner, ConveyorBranch branch)
    {
        if (!enforcePhysicalBranchQuotas)
        {
            return true;
        }

        var routeIndex = GetPhysicalBranchIndex(spawner, branch);
        if (routeIndex < 0 || reservedPackagesPerPhysicalBranch[routeIndex] >= maximumPackagesPerPhysicalBranch)
        {
            return false;
        }

        var remainingPackagesAfterReservation = maximumPackagesPerRound - (reservedPackageCount + 1);
        var remainingMinimumDeficit = 0;
        for (var index = 0; index < PhysicalBranchCount; index++)
        {
            var prospectiveCount = reservedPackagesPerPhysicalBranch[index] + (index == routeIndex ? 1 : 0);
            remainingMinimumDeficit += Mathf.Max(0, minimumPackagesPerPhysicalBranch - prospectiveCount);
        }

        return remainingMinimumDeficit <= remainingPackagesAfterReservation;
    }

    private void RegisterPhysicalBranchReservation(ConveyorBoxSpawner spawner, ConveyorBranch branch)
    {
        if (!enforcePhysicalBranchQuotas)
        {
            return;
        }

        var routeIndex = GetPhysicalBranchIndex(spawner, branch);
        if (routeIndex >= 0)
        {
            reservedPackagesPerPhysicalBranch[routeIndex]++;
        }
    }

    private int GetPhysicalBranchIndex(ConveyorBoxSpawner spawner, ConveyorBranch branch)
    {
        if (spawner == leftSpawner)
        {
            return branch == ConveyorBranch.A ? 0 : 1;
        }

        if (spawner == rightSpawner)
        {
            return branch == ConveyorBranch.A ? 2 : 3;
        }

        return -1;
    }

    /// <summary>
    /// Called by a package mover once the parcel actually arrives at a
    /// discharge zone. The truck watches this value, so its gate cannot open
    /// while the final parcel is still travelling on a conveyor.
    /// </summary>
    public void NotifyPackageArrived()
    {
        if (DeliveredPackageCount < maximumPackagesPerRound)
        {
            DeliveredPackageCount++;
            PackageArrived?.Invoke(DeliveredPackageCount, Time.time);
        }
    }
}
