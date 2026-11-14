using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Keeps the world-space counter above the main gate in sync with the number
/// of parcels currently caught on the forklift tines.
/// </summary>
[DisallowMultipleComponent]
public sealed class ForkliftLoadCounter : MonoBehaviour
{
    public TextMesh counterText;

    [Header("Zoom visibility")]
    [Tooltip("Keeps the complete counter frame visible at the top of the view when camera zoom would move the gate out of frame.")]
    public bool keepCounterVisibleWhileZoomed = true;
    [Tooltip("Vertical viewport position used by the counter while it is pinned to the upper edge of the view.")]
    [Range(0.5f, 0.98f)] public float pinnedViewportY = 0.90f;
    [Tooltip("Free horizontal border retained when the counter is pinned to the view.")]
    [Range(0f, 0.25f)] public float pinnedViewportHorizontalMargin = 0.06f;
    [Tooltip("The counter is moved no farther than this distance from the camera, keeping its red digits legible.")]
    [Min(1f)] public float pinnedMaximumDistance = 20f;

    private ForkliftLoadArea loadArea;
    private DeliveryTruckController deliveryTruck;
    private int lastCount = -1;
    private Transform counterFrame;
    private Vector3 counterFrameRestPosition;
    private Quaternion counterFrameRestRotation;
    private bool hasCounterFrameRestPose;
    private Renderer[] counterFrameRenderers;
    private ShadowCastingMode[] counterFrameRestShadowModes;
    private bool counterFramePinned;

    private void OnEnable()
    {
        CacheCounterFrameRestPose();
        Refresh();
    }

    private void Update()
    {
        Refresh();
    }

    private void LateUpdate()
    {
        KeepCounterInView();
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

    private void CacheCounterFrameRestPose()
    {
        // This script sits on the Text child. Its parent is the complete dark
        // scoreboard frame, so moving that parent keeps both the frame and
        // digits together rather than leaving a floating number behind.
        counterFrame = transform.parent != null ? transform.parent : transform;
        counterFrameRestPosition = counterFrame.position;
        counterFrameRestRotation = counterFrame.rotation;
        hasCounterFrameRestPose = true;
    }

    private void KeepCounterInView()
    {
        if (!keepCounterVisibleWhileZoomed)
        {
            RestoreCounterFrameRestPose();
            return;
        }

        if (!hasCounterFrameRestPose || counterFrame == null)
        {
            CacheCounterFrameRestPose();
        }

        var sceneCamera = Camera.main;
        if (sceneCamera == null || counterFrame == null)
        {
            return;
        }

        // While the delivery vehicle uses the gate and courtyard, leave the
        // physical board on its facade. It must never float in front of, or
        // visually cover, the truck from the player's current camera view.
        if (deliveryTruck != null && deliveryTruck.IsCounterPinningSuppressed)
        {
            RestoreCounterFrameRestPose();
            return;
        }

        // Always project the original board position. Projecting its current
        // pinned position would make the counter creep further across the
        // screen on every frame.
        var restViewportPosition = sceneCamera.WorldToViewportPoint(counterFrameRestPosition);
        var shouldPin = restViewportPosition.z <= 0.01f ||
                         restViewportPosition.y > pinnedViewportY;
        if (!shouldPin)
        {
            RestoreCounterFrameRestPose();
            return;
        }

        var horizontalMargin = Mathf.Clamp(pinnedViewportHorizontalMargin, 0f, 0.25f);
        var pinnedX = Mathf.Clamp(
            restViewportPosition.x,
            horizontalMargin,
            1f - horizontalMargin);
        var pinnedDepth = Mathf.Clamp(
            restViewportPosition.z,
            1f,
            Mathf.Max(1f, pinnedMaximumDistance));
        var pinnedPosition = sceneCamera.ViewportToWorldPoint(
            new Vector3(pinnedX, Mathf.Clamp(pinnedViewportY, 0.5f, 0.98f), pinnedDepth));

        counterFrame.SetPositionAndRotation(
            pinnedPosition,
            // TextMesh is drawn toward its local -Z side. Keep that face and
            // the frame's front toward the camera after pinning it.
            Quaternion.LookRotation(pinnedPosition - sceneCamera.transform.position, sceneCamera.transform.up));
        SetCounterFramePinned(true);
    }

    private void RestoreCounterFrameRestPose()
    {
        SetCounterFramePinned(false);
        if (hasCounterFrameRestPose && counterFrame != null)
        {
            counterFrame.SetPositionAndRotation(counterFrameRestPosition, counterFrameRestRotation);
        }
    }

    private void SetCounterFramePinned(bool pinned)
    {
        if (counterFramePinned == pinned) return;
        CacheCounterFrameRenderers();
        if (counterFrameRenderers == null) return;

        for (var index = 0; index < counterFrameRenderers.Length; index++)
        {
            var renderer = counterFrameRenderers[index];
            if (renderer == null) continue;
            renderer.shadowCastingMode = pinned
                ? ShadowCastingMode.Off
                : counterFrameRestShadowModes[index];
        }

        counterFramePinned = pinned;
    }

    private void CacheCounterFrameRenderers()
    {
        if (counterFrame == null || counterFrameRenderers != null) return;

        counterFrameRenderers = counterFrame.GetComponentsInChildren<Renderer>(true);
        counterFrameRestShadowModes = new ShadowCastingMode[counterFrameRenderers.Length];
        for (var index = 0; index < counterFrameRenderers.Length; index++)
        {
            counterFrameRestShadowModes[index] = counterFrameRenderers[index].shadowCastingMode;
        }
    }
}
