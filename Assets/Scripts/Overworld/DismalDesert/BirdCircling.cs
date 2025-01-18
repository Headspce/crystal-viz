using UnityEngine;

public class BirdCircling : MonoBehaviour
{
    public GameObject[] objectsToRotate; // Array of game objects to rotate
    public Vector3 rotationAxis = Vector3.up; // Axis of rotation (default is Y-axis)
    public float rotationRadius = 10f; // Radius of the circular path
    public float rotationSpeed = 10f; // Speed of rotation
    private Vector3 rotationCenter;
    public bool useCustomCenter; // Use custom center point
    public Vector3 customCenter;
    public Transform platform; // Reference to the platform

    void Start()
    {
        // Set the initial rotation center to this GameObject's position if not using custom center
        rotationCenter = useCustomCenter ? customCenter : transform.position;
    }

    void Update()
    {
        // Update the rotation center if custom center is toggled
        if (useCustomCenter)
        {
            rotationCenter = customCenter;
        }

        // Loop through each object in the array
        foreach (GameObject obj in objectsToRotate)
        {
            float angle = rotationSpeed * Time.time;
            float nextAngle = angle + rotationSpeed * Time.deltaTime; // Calculate next angle for forward direction

            // Calculate the new position based on the specified axis
            Vector3 offset = new Vector3(
                Mathf.Cos(angle) * rotationRadius,
                0,
                Mathf.Sin(angle) * rotationRadius
            );

            Vector3 nextOffset = new Vector3(
                Mathf.Cos(nextAngle) * rotationRadius,
                0,
                Mathf.Sin(nextAngle) * rotationRadius
            );

            // Rotate the offset vector to align with the platform's orientation
            Vector3 rotatedOffset = platform.rotation * offset;
            Vector3 nextRotatedOffset = platform.rotation * nextOffset;

            // Update the position
            obj.transform.position = rotationCenter + rotatedOffset;

            // Update rotation to look in the forward direction of the flight path
            Vector3 direction = (nextRotatedOffset - rotatedOffset).normalized;
            if (direction != Vector3.zero) // Avoid setting rotation to an illegal value
                obj.transform.forward = -direction; // Invert the direction to make the bird face forward
        }
    }

    // Optional manual controls to adjust radius, speed, and center
    public void SetRotationAxis(Vector3 axis)
    {
        rotationAxis = axis;
    }

    public void SetRotationRadius(float radius)
    {
        rotationRadius = radius;
    }

    public void SetRotationSpeed(float speed)
    {
        rotationSpeed = speed;
    }

    public void SetCustomCenter(Vector3 center, bool useCustom)
    {
        customCenter = center;
        useCustomCenter = useCustom;
    }

    public void SetPlatform(Transform platformTransform)
    {
        platform = platformTransform;
    }
}
