using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Repairs only the clipped outer marking of the east parking area. It is
/// deliberately separate from FactoryYardBuilder, so running it preserves
/// every hand-edited building, road, fence and gameplay object in the scene.
/// </summary>
public static class ParkingStripeAlignmentInstaller
{
    private const float SurfaceMargin = 0.45f;

    [MenuItem("Factory/Fix Parking/Align Right Parking Stripe")]
    private static void AlignRightParkingStripe()
    {
        if (Application.isPlaying)
        {
            EditorUtility.DisplayDialog("Parking", "Stop Play mode before aligning the parking stripe.", "OK");
            return;
        }

        var parkingAreas = GameObject.Find("Parking_Areas")?.transform;
        var eastParking = parkingAreas != null ? parkingAreas.Find("Parking_East") : null;
        var surface = eastParking != null ? eastParking.Find("Parking_Surface") : null;
        if (eastParking == null || surface == null)
        {
            EditorUtility.DisplayDialog(
                "Parking",
                "Open FactoryYard with the east parking area first.",
                "OK");
            return;
        }

        var stripes = new List<Transform>();
        for (var index = 0; index < eastParking.childCount; index++)
        {
            var child = eastParking.GetChild(index);
            if (child.name == "Diagonal_Bay")
            {
                stripes.Add(child);
            }
        }

        if (stripes.Count < 3)
        {
            EditorUtility.DisplayDialog(
                "Parking",
                "The east parking area needs at least three diagonal markings.",
                "OK");
            return;
        }

        stripes.Sort((left, right) => left.localPosition.x.CompareTo(right.localPosition.x));
        var previous = stripes[stripes.Count - 3];
        var reference = stripes[stripes.Count - 2];
        var rightmost = stripes[stripes.Count - 1];

        // Reuse the preceding bay exactly, then move it by the existing bay
        // spacing. This fixes the final clipped line without changing the
        // appearance or orientation of any other parking marking.
        var spacing = reference.localPosition.x - previous.localPosition.x;
        if (spacing <= 0.01f)
        {
            EditorUtility.DisplayDialog("Parking", "Could not determine the parking-bay spacing.", "OK");
            return;
        }

        Undo.RecordObject(rightmost, "Align right parking stripe");
        rightmost.localPosition = reference.localPosition + Vector3.right * spacing;
        rightmost.localRotation = reference.localRotation;
        rightmost.localScale = reference.localScale;

        // Keep the full stripe on asphalt, extending only the outer/right
        // edge when the formerly clipped line needs a little more room.
        var stripeRenderer = rightmost.GetComponent<Renderer>();
        if (stripeRenderer != null)
        {
            var desiredWorldRightEdge = stripeRenderer.bounds.max.x + SurfaceMargin;
            var desiredLocalRightEdge = eastParking.InverseTransformPoint(
                new Vector3(desiredWorldRightEdge, stripeRenderer.bounds.center.y, stripeRenderer.bounds.center.z)).x;
            var currentLocalRightEdge = surface.localPosition.x + surface.localScale.x * 0.5f;
            var extension = desiredLocalRightEdge - currentLocalRightEdge;

            if (extension > 0.001f)
            {
                Undo.RecordObject(surface, "Extend east parking surface");
                surface.localPosition += Vector3.right * (extension * 0.5f);
                surface.localScale += Vector3.right * extension;
                EditorUtility.SetDirty(surface);
            }
        }

        EditorUtility.SetDirty(rightmost);
        EditorSceneManager.MarkSceneDirty(eastParking.gameObject.scene);
        EditorSceneManager.SaveScene(eastParking.gameObject.scene);
        Selection.activeGameObject = rightmost.gameObject;
        Debug.Log("Aligned the rightmost east parking stripe and extended only its asphalt edge when needed.");
    }
}
