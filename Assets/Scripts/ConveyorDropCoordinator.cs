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
    // Accounts for frame quantisation while keeping the actual fall gap safely
    // above the requested 3.5 seconds.
    private const float FallSafetyMargin = 0.25f;
    private const int MaximumConsecutivePackagesPerBranch = 3;
    private const int MaximumConsecutivePackagesPerSide = 5;

    [Tooltip("Minimum time between the start of any two parcel falls in the whole yard.")]
    [Min(3.5f)] public float minimumDelayBetweenFalls = 3.5f;

    [Tooltip("Maximum time allowed between the start of any two parcel falls in the whole yard.")]
    [Min(3.5f)] public float maximumDelayBetweenFalls = 7f;

    [Tooltip("Minimum spacing between departures from either loading window. This prevents simultaneous starts while allowing unequal path lengths to meet the fall deadline.")]
    [Min(0.5f)] public float minimumDispatchGap = 0.55f;

    [Tooltip("Maximum number of parcels dispatched in one play-through. Generation stops after this amount.")]
    [Min(1)] public int maximumPackagesPerRound = 30;

    public ConveyorBoxSpawner leftSpawner;
    public ConveyorBoxSpawner rightSpawner;

    private readonly Queue<ScheduledPackage> scheduledPackages = new Queue<ScheduledPackage>();
    private bool hasSchedule;
    private float lastScheduledDispatchTime;
    private float lastScheduledFallTime;
    private ConveyorBoxSpawner lastScheduledSpawner;
    private ConveyorBranch lastScheduledBranch;
    private int scheduledSideRun;
    private int scheduledRouteRun;
    private int reservedPackageCount;
    public int DispatchedPackageCount { get; private set; }

    /// <summary>
    /// Number of parcels which have completed their conveyor journey. A parcel
    /// counts here only after it reaches a landing zone or is caught by the
    /// forklift, never merely when it leaves its loading window.
    /// </summary>
    public int DeliveredPackageCount { get; private set; }

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
        while (scheduledPackages.Count > 0 && Time.time >= scheduledPackages.Peek().dispatchTime)
        {
            var package = scheduledPackages.Dequeue();
            package.spawner.SpawnReservedPackage(package.path, package.branch, package.packageNumber);
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
            ? validChoices[Random.Range(0, validChoices.Count)]
            : FindShortestRoute(validChoices);
        if (!choice.spawner.TryReservePath(choice.branch, out var path, out var packageNumber))
        {
            return false;
        }

        scheduledPackages.Enqueue(new ScheduledPackage(
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
        lastScheduledDispatchTime = choice.dispatchTime;
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
            dispatchTime = Time.time + InitialDispatchDelay;
            fallTime = dispatchTime + travelTime;
        }
        else
        {
            var minimumFallDelay = Mathf.Max(3.5f, minimumDelayBetweenFalls) + FallSafetyMargin;
            var maximumFallDelay = Mathf.Max(minimumFallDelay, maximumDelayBetweenFalls);
            var earliestFall = lastScheduledFallTime + minimumFallDelay;
            dispatchTime = Mathf.Max(
                earliestFall - travelTime,
                lastScheduledDispatchTime + Mathf.Max(0.5f, minimumDispatchGap));
            fallTime = dispatchTime + travelTime;

            if (fallTime > lastScheduledFallTime + maximumFallDelay)
            {
                return;
            }
        }

        choices.Add(new RouteChoice(spawner, branch, dispatchTime, fallTime));
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
