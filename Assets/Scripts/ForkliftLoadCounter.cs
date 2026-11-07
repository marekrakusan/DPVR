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

        var count = loadArea != null ? loadArea.LoadedPackageCount : 0;
        if (count == lastCount)
        {
            return;
        }

        lastCount = count;
        // Two red digits mimic a simple hockey-style scoreboard.
        counterText.text = count.ToString("00");
    }
}
