using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Zooms the yard camera while keeping the four conveyor discharge ends in
/// frame. As the view moves closer, it eases toward their centre, tilts down
/// slightly and widens the field of view only as much as needed.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
public sealed class CameraZoomController : MonoBehaviour
{
    [Header("Wide view")]
    [Tooltip("Point at the centre of the normal, fully zoomed-out view.")]
    public Vector3 focusPoint = new Vector3(0f, 2.4f, 4f);

    [Tooltip("Closest permitted camera distance. It is chosen to keep all conveyor ends visible.")]
    [Min(1f)] public float minimumDistance = 18f;

    [Tooltip("Furthest permitted distance. It is set to the scene's current camera view.")]
    [Min(1f)] public float maximumDistance;

    [Tooltip("Distance moved by one mouse-wheel step.")]
    [Min(0.1f)] public float zoomSpeed = 4f;

    [Header("Conveyor-end framing")]
    [Tooltip("The four discharge endpoints that must remain visible while zooming.")]
    public Transform[] framingTargets;

    [Tooltip("Additional downward tilt, in degrees, at the closest zoom.")]
    [Range(0f, 20f)] public float closeViewPitchOffset = 8f;

    [Tooltip("Field of view used at the closest zoom before automatic framing is applied.")]
    [Range(1f, 120f)] public float closeViewFieldOfView = 60f;

    [Tooltip("Maximum field of view that automatic framing may use.")]
    [Range(1f, 120f)] public float maximumFieldOfView = 66f;

    [Tooltip("Free border kept around every discharge end in the view.")]
    [Range(0.5f, 0.98f)] public float framingFill = 0.88f;

    private Camera sceneCamera;
    private Vector3 farPosition;
    private Quaternion farRotation;
    private Vector3 farOffsetDirection;
    private float farFieldOfView;
    private float currentDistance;

    // SceneManager.LoadScene creates a new camera component, but these values
    // intentionally survive that reload. They are cleared only when a brand
    // new Play session starts, so the authored wide view remains the default.
    private static bool hasPersistentView;
    private static Vector3 persistentPosition;
    private static Quaternion persistentRotation;
    private static float persistentFieldOfView;
    private static float persistentDistance;

    private void Awake()
    {
        sceneCamera = GetComponent<Camera>();
        // The delivery truck moves through the main-hall doorway. Prevent a
        // previously baked building occluder from hiding this dynamic object
        // while it is crossing the opening.
        sceneCamera.useOcclusionCulling = false;
        EnsureMaximumDistance();
        CaptureWideView();
        currentDistance = maximumDistance;
        RestorePersistentView();
    }

    private void Update()
    {
        var mouse = Mouse.current;
        if (mouse == null)
        {
            return;
        }

        var scroll = mouse.scroll.ReadValue().y;
        if (Mathf.Abs(scroll) < 0.001f)
        {
            return;
        }

        EnsureMaximumDistance();
        var wheelSteps = scroll / 120f;
        currentDistance = Mathf.Clamp(
            currentDistance - wheelSteps * zoomSpeed,
            minimumDistance,
            maximumDistance);
        ApplyFramedView();
        SavePersistentView();
    }

    private void LateUpdate()
    {
        // Keep the exact live pose as well as the logical zoom distance. The
        // latter is needed for the next wheel step; the former makes a reload
        // visually seamless even if another camera behaviour adjusted it.
        SavePersistentView();
    }

    /// <summary>
    /// Configures the current view as the zoom-out limit and assigns the four
    /// conveyor endpoints used for automatic framing.
    /// </summary>
    public void ConfigureFraming(
        Vector3 wideFocusPoint,
        Transform[] dischargeEnds,
        float minDistance,
        float maxDistance)
    {
        focusPoint = wideFocusPoint;
        framingTargets = dischargeEnds;
        minimumDistance = Mathf.Max(1f, minDistance);
        maximumDistance = Mathf.Max(minimumDistance, maxDistance);

        if (sceneCamera == null)
        {
            sceneCamera = GetComponent<Camera>();
        }

        CaptureWideView();
        currentDistance = maximumDistance;
        RestorePersistentView();
    }

    private void ApplyFramedView()
    {
        var closeFocus = GetFramingCenter();
        if (closeFocus == focusPoint)
        {
            ApplySimpleZoom();
            return;
        }

        var zoomT = Mathf.InverseLerp(maximumDistance, minimumDistance, currentDistance);
        if (zoomT <= 0.0001f)
        {
            transform.SetPositionAndRotation(farPosition, farRotation);
            sceneCamera.fieldOfView = farFieldOfView;
            return;
        }

        var closeDirection = Quaternion.AngleAxis(closeViewPitchOffset, Vector3.right) * farOffsetDirection;
        var closePosition = closeFocus + closeDirection * minimumDistance;
        var closeRotation = Quaternion.LookRotation(closeFocus - closePosition, Vector3.up);

        transform.position = Vector3.Lerp(farPosition, closePosition, zoomT);
        transform.rotation = Quaternion.Slerp(farRotation, closeRotation, zoomT);
        sceneCamera.fieldOfView = Mathf.Lerp(farFieldOfView, closeViewFieldOfView, zoomT);
        FitDischargeEndsInView();
    }

    private void ApplySimpleZoom()
    {
        var offset = farOffsetDirection * currentDistance;
        transform.position = focusPoint + offset;
        transform.rotation = Quaternion.LookRotation(focusPoint - transform.position, Vector3.up);
        sceneCamera.fieldOfView = farFieldOfView;
    }

    private Vector3 GetFramingCenter()
    {
        if (framingTargets == null || framingTargets.Length == 0)
        {
            return focusPoint;
        }

        var sum = Vector3.zero;
        var count = 0;
        foreach (var target in framingTargets)
        {
            if (target == null)
            {
                continue;
            }

            sum += target.position;
            count++;
        }

        return count > 0 ? sum / count : focusPoint;
    }

    private void FitDischargeEndsInView()
    {
        if (framingTargets == null || framingTargets.Length == 0 || sceneCamera == null)
        {
            return;
        }

        var requiredTangent = 0f;
        foreach (var target in framingTargets)
        {
            if (target == null)
            {
                continue;
            }

            var local = transform.InverseTransformPoint(target.position);
            if (local.z <= 0.01f)
            {
                sceneCamera.fieldOfView = maximumFieldOfView;
                return;
            }

            requiredTangent = Mathf.Max(
                requiredTangent,
                Mathf.Abs(local.y) / (local.z * framingFill),
                Mathf.Abs(local.x) / (local.z * sceneCamera.aspect * framingFill));
        }

        var requiredFieldOfView = 2f * Mathf.Atan(requiredTangent) * Mathf.Rad2Deg;
        sceneCamera.fieldOfView = Mathf.Clamp(
            Mathf.Max(sceneCamera.fieldOfView, requiredFieldOfView),
            farFieldOfView,
            maximumFieldOfView);
    }

    private void CaptureWideView()
    {
        if (sceneCamera == null)
        {
            sceneCamera = GetComponent<Camera>();
        }

        farPosition = transform.position;
        farRotation = transform.rotation;
        farFieldOfView = sceneCamera != null ? sceneCamera.fieldOfView : 52f;

        var offset = farPosition - focusPoint;
        farOffsetDirection = offset.sqrMagnitude > 0.0001f
            ? offset.normalized
            : -transform.forward;
    }

    private void EnsureMaximumDistance()
    {
        if (maximumDistance <= 0f)
        {
            maximumDistance = Mathf.Max(minimumDistance, Vector3.Distance(transform.position, focusPoint));
        }
    }

    private void RestorePersistentView()
    {
        if (!hasPersistentView || sceneCamera == null)
        {
            return;
        }

        currentDistance = Mathf.Clamp(persistentDistance, minimumDistance, maximumDistance);
        transform.SetPositionAndRotation(persistentPosition, persistentRotation);
        sceneCamera.fieldOfView = persistentFieldOfView;
    }

    private void SavePersistentView()
    {
        if (sceneCamera == null)
        {
            return;
        }

        persistentPosition = transform.position;
        persistentRotation = transform.rotation;
        persistentFieldOfView = sceneCamera.fieldOfView;
        persistentDistance = currentDistance;
        hasPersistentView = true;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetPersistentViewForNewPlaySession()
    {
        hasPersistentView = false;
        persistentPosition = Vector3.zero;
        persistentRotation = Quaternion.identity;
        persistentFieldOfView = 0f;
        persistentDistance = 0f;
    }
}
