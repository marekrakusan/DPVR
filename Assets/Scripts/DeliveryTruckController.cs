using UnityEngine;

/// <summary>
/// Runs the single delivery cycle for the yard: only after the final parcel
/// arrives at its landing zone, the gate opens, the truck enters, receives
/// the forklift load and leaves.
/// </summary>
public sealed class DeliveryTruckController : MonoBehaviour
{
    public ConveyorDropCoordinator coordinator;
    public ForkliftLoadArea forkliftLoad;
    public Transform truckVisual;
    public Transform cargoAnchor;
    public GameObject cargoCover;
    public Transform gate;
    public float approachZ = 9.5f;
    // The rear of the north hall ends around Z=20.  Starting further north
    // keeps the whole vehicle outside the hall until the gate has opened.
    public float exitZ = 25.5f;
    public float driveSpeed = 3.5f;
    public float gateSpeed = 2.2f;
    public float loadingDistance = 3.1f;

    private Vector3 gateClosedScale;
    private Vector3 gateClosedPosition;
    private float gateTopY;
    private Collider gateCollider;
    private State state;
    private float loadingTimer;

    private enum State { Waiting, Opening, Arriving, WaitingForForklift, Loading, Leaving, Closing, Finished }

    private void Awake()
    {
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
                    state = State.Opening;
                }
                break;
            case State.Opening:
                if (SetGateOpen(true)) state = State.Arriving;
                break;
            case State.Arriving:
                EnsureTruckVisible();
                if (MoveTruckTo(approachZ)) state = State.WaitingForForklift;
                break;
            case State.WaitingForForklift:
                if (CanLoadForklift())
                {
                    if (cargoCover != null) cargoCover.SetActive(false);
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
                    state = State.Leaving;
                }
                break;
            case State.Leaving:
                if (MoveTruckTo(exitZ)) state = State.Closing;
                break;
            case State.Closing:
                if (SetGateOpen(false)) state = State.Finished;
                break;
        }
    }

    private bool MoveTruckTo(float targetZ)
    {
        var position = transform.position;
        position.z = Mathf.MoveTowards(position.z, targetZ, driveSpeed * Time.deltaTime);
        transform.position = position;
        return Mathf.Abs(position.z - targetZ) < 0.01f;
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
