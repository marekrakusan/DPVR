using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Drives the forklift across the factory yard with WASD. Movement uses the
/// world XZ ground plane and turns the visible front of the forklift toward
/// the direction of travel. Attach this component to Forklift_Controller,
/// the empty wrapper around the imported FBX model.
/// </summary>
[DisallowMultipleComponent]
public sealed class ForkliftController : MonoBehaviour
{
    [Min(0f)] public float movementSpeed = 4f;
    [Min(0f)] public float turnSpeedDegreesPerSecond = 720f;

    private float lockedHeight;

    private void Awake()
    {
        lockedHeight = transform.position.y;
    }

    private void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return;
        }

        var input = Vector2.zero;
        if (keyboard.wKey.isPressed) input.y += 1f;
        if (keyboard.sKey.isPressed) input.y -= 1f;
        if (keyboard.aKey.isPressed) input.x -= 1f;
        if (keyboard.dKey.isPressed) input.x += 1f;

        if (input.sqrMagnitude < 0.0001f)
        {
            return;
        }

        var direction = new Vector3(input.x, 0f, input.y).normalized;
        TurnFrontToward(direction);

        var position = transform.position + direction * (movementSpeed * Time.deltaTime);
        position.y = lockedHeight;
        transform.position = position;
    }

    private void TurnFrontToward(Vector3 direction)
    {
        // In the wrapper's local coordinates, the visible forks/front point
        // along -Z. Therefore the wrapper's +Z must face opposite to the
        // requested travel direction.
        var targetRotation = Quaternion.LookRotation(-direction, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(
            transform.rotation,
            targetRotation,
            turnSpeedDegreesPerSecond * Time.deltaTime);
    }
}
