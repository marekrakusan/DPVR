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
    private const float InitialDispatchDelay = 0.6f;
    private const float SchedulingHorizon = 45f;
    private const float DispatchSchedulingTolerance = 0.02f;
    // Accounts for frame quantisation while keeping the actual fall gap safely
    // above the requested 4 seconds.
    private const float FallSafetyMargin = 0.25f;
    private const int MaximumConsecutivePackagesPerBranch = 3;
    private const int MaximumConsecutivePackagesPerSide = 5;
    private const int PhysicalBranchCount = 4;
    private const float MinimumPackagesPerBranchShare = 0.20f;
    private const float MaximumPackagesPerBranchShare = 0.30f;

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
    private bool hasSchedule;
    private float lastScheduledFallTime;
    private float lastActualDispatchTime = float.NegativeInfinity;
    private ConveyorBoxSpawner lastScheduledSpawner;
    private ConveyorBranch lastScheduledBranch;
    private int scheduledSideRun;
    private int scheduledRouteRun;
    private int reservedPackageCount;
    private readonly int[] reservedPackagesPerPhysicalBranch = new int[PhysicalBranchCount];
    private int minimumPackagesPerPhysicalBranch;
    private int maximumPackagesPerPhysicalBranch;
    private bool enforcePhysicalBranchQuotas;
    public int DispatchedPackageCount { get; private set; }

    /// <summary>
    /// Number of parcels which have completed their conveyor journey. A parcel
    /// counts here only after it reaches a landing zone or is caught by the
    /// forklift, never merely when it leaves its loading window.
    /// </summary>
    public int DeliveredPackageCount { get; private set; }

    private void Awake()
    {
        // The active level shortens the fall spacing before Start plans the
        // first route, so every scheduled departure uses its own cadence.
        var delayMultiplier = LevelProgression.FallDelayMultiplier;
        minimumDelayBetweenFalls *= delayMultiplier;
        maximumDelayBetweenFalls *= delayMultiplier;
        ConfigurePhysicalBranchQuotas();
    }

    private struct ScheduledPackage
    {
        public readonly ConveyorBoxSpawner spawner;
        public readonly Transform[] path;
        public readonly ConveyorBranch branch;
        public readonly int packageNumber;
        public readonly float dispatchTime;

        public ScheduledPackage(
            ConveyorBoxSpawner spawner,
            Transform[] path,
            ConveyorBranch branch,
            int packageNumber,
            float dispatchTime)
        {
            this.spawner = spawner;
            this.path = path;
            this.branch = branch;
            this.packageNumber = packageNumber;
            this.dispatchTime = dispatchTime;
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
        PlanUntil(Time.time + SchedulingHorizon);
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

        if (!hasSchedule || lastScheduledFallTime < Time.time + SchedulingHorizon)
        {
            PlanUntil(Time.time + SchedulingHorizon);
        }
    }

    private void PlanUntil(float targetFallTime)
    {
        while (reservedPackageCount < maximumPackagesPerRound &&
               (!hasSchedule || lastScheduledFallTime < targetFallTime))
        {
            if (!TryScheduleNextPackage())
            {
                return;
            }
        }
    }

    private bool TryScheduleNextPackage()
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
            Debug.LogWarning("No valid conveyor route could meet the routing and fall-timing rules.");
            return false;
        }

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

        InsertScheduledPackage(new ScheduledPackage(
            choice.spawner,
            path,
            choice.branch,
            packageNumber,
            choice.dispatchTime));

        var hadSchedule = hasSchedule;
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
    /// Keeps the physical routes random while preferring the opposite branch
    /// type to the preceding falling parcel. Alternating short and long
    /// branches gives the long paths enough lead time to start early, which
    /// keeps every fall inside its requested time window. Among those choices
    /// the least-used physical route is selected at random, so all four
    /// routes remain in the required 20–30% range instead of one route being
    /// saved until the end of the round.
    /// </summary>
    private RouteChoice FindBalancedRandomRoute(List<RouteChoice> choices)
    {
        var preferredChoices = new List<RouteChoice>(choices.Count);
        for (var index = 0; index < choices.Count; index++)
        {
            var candidate = choices[index];
            if (candidate.branch != lastScheduledBranch)
            {
                preferredChoices.Add(candidate);
            }
        }

        if (preferredChoices.Count == 0)
        {
            preferredChoices.AddRange(choices);
        }

        var lowestReservationCount = int.MaxValue;
        for (var index = 0; index < preferredChoices.Count; index++)
        {
            lowestReservationCount = Mathf.Min(
                lowestReservationCount,
                GetReservedPhysicalBranchCount(preferredChoices[index]));
        }

        var balancedChoices = new List<RouteChoice>(preferredChoices.Count);
        for (var index = 0; index < preferredChoices.Count; index++)
        {
            var candidate = preferredChoices[index];
            if (GetReservedPhysicalBranchCount(candidate) == lowestReservationCount)
            {
                balancedChoices.Add(candidate);
            }
        }

        return balancedChoices[Random.Range(0, balancedChoices.Count)];
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
        if (hasSchedule && spawner == lastScheduledSpawner && branch == lastScheduledBranch &&
            scheduledRouteRun >= MaximumConsecutivePackagesPerBranch)
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
            // Reserve enough lead time for a longer route to depart before
            // this first, short-route parcel when it must arrive later. The
            // first parcel to *fall* is still on the shorter route.
            fallTime = Time.time + GetLongestValidRouteTravelTime() + InitialDispatchDelay;
            dispatchTime = fallTime - travelTime;
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

            // The departure can be between the two route-specific bounds.
            // Search the global, time-sorted dispatch list for a free slot so
            // no two loading windows start a box at the same time.
            if (!TryFindAvailableDispatchTime(
                    spawner,
                    earliestFall - travelTime,
                    latestFall - travelTime,
                    out dispatchTime))
            {
                return;
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

    private float GetLongestValidRouteTravelTime()
    {
        var longestTravelTime = 0f;
        AddLongestTravelTime(leftSpawner, ConveyorBranch.A, ref longestTravelTime);
        AddLongestTravelTime(leftSpawner, ConveyorBranch.B, ref longestTravelTime);
        AddLongestTravelTime(rightSpawner, ConveyorBranch.A, ref longestTravelTime);
        AddLongestTravelTime(rightSpawner, ConveyorBranch.B, ref longestTravelTime);
        return longestTravelTime;
    }

    private static void AddLongestTravelTime(
        ConveyorBoxSpawner spawner,
        ConveyorBranch branch,
        ref float longestTravelTime)
    {
        if (spawner == null || !spawner.HasValidPath(branch))
        {
            return;
        }

        var path = branch == ConveyorBranch.A ? spawner.branchPathA : spawner.branchPathB;
        longestTravelTime = Mathf.Max(longestTravelTime, spawner.GetTravelTime(path));
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
        }
    }
}
