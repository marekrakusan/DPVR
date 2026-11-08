using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Drives the forklift across the factory yard with WASD or the arrow keys.
/// Movement uses the world XZ ground plane and turns the visible front of the
/// forklift toward the direction of travel. Attach this component to
/// Forklift_Controller, the empty wrapper around the imported FBX model.
/// </summary>
[DisallowMultipleComponent]
public sealed class ForkliftController : MonoBehaviour
{
    [Min(0f)] public float movementSpeed = 4f;
    [Min(0f)] public float turnSpeedDegreesPerSecond = 720f;

    private float lockedHeight;
    private CharacterController collisionController;

    private void Awake()
    {
        // Level 2 keeps the same handling but covers ground 20% faster.
        movementSpeed *= LevelProgression.ForkliftSpeedMultiplier;
        lockedHeight = transform.position.y;
        collisionController = GetComponent<CharacterController>();
    }

    private void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return;
        }

        var input = Vector2.zero;
        // Keep WASD and arrow keys equivalent, including diagonal movement
        // when one horizontal and one vertical key are held together.
        if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) input.y += 1f;
        if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) input.y -= 1f;
        if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) input.x -= 1f;
        if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) input.x += 1f;

        if (input.sqrMagnitude < 0.0001f)
        {
            return;
        }

        var direction = new Vector3(input.x, 0f, input.y).normalized;
        TurnFrontToward(direction);

        var movement = direction * (movementSpeed * Time.deltaTime);
        if (collisionController != null && collisionController.enabled)
        {
            // CharacterController.Move performs a swept collision check, so
            // the forklift stops at the solid hall walls and roof geometry.
            collisionController.Move(movement);
        }
        else
        {
            transform.position += movement;
        }

        var position = transform.position;
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
