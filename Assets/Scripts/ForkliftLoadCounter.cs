using UnityEngine;

/// <summary>
/// Keeps the world-space counter above the main gate in sync with the number
/// of parcels currently caught on the forklift tines.
/// </summary>
[DisallowMultipleComponent]
public sealed class ForkliftLoadCounter : MonoBehaviour
{
    public TextMesh counterText;

    private ForkliftLoadArea loadArea;
    private DeliveryTruckController deliveryTruck;
    private int lastCount = -1;

    private void OnEnable()
    {
        Refresh();
    }

    private void Update()
    {
        Refresh();
    }

    private void Refresh()
    {
        if (counterText == null)
        {
            counterText = GetComponent<TextMesh>();
        }

        if (counterText == null)
        {
            return;
        }

        if (loadArea == null)
        {
            loadArea = Object.FindAnyObjectByType<ForkliftLoadArea>();
        }

        if (deliveryTruck == null)
        {
            deliveryTruck = Object.FindAnyObjectByType<DeliveryTruckController>();
        }

        // Once boxes are transferred to the truck, the forklift anchor is
        // empty. Keep the recorded number illuminated above the gate for the
        // rest of this level, including the truck's departure and outcome.
        var count = deliveryTruck != null && deliveryTruck.ShouldHoldLoadCounterUntilLevelEnds
            ? deliveryTruck.LoadedPackageCount
            : loadArea != null ? loadArea.LoadedPackageCount : 0;
        if (count == lastCount)
        {
            return;
        }

        lastCount = count;
        // Two red digits mimic a simple hockey-style scoreboard.
        counterText.text = count.ToString("00");
    }
}
