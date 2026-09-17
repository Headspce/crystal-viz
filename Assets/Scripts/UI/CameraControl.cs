using UnityEngine;
using System.Collections;

public class CameraControl : MonoBehaviour
{
    public float rotationSpeed = 100.0f; // Adjust this value to change the rotation speed
    public float rotationLimit = 60.0f;
    private Quaternion originalRotation;
    private bool isRotating = false;
    private Vector3 rotation;
    private Vector3 lastMousePosition; // Track the last mouse position

    void Start()
    {
        originalRotation = transform.rotation;
        rotation = transform.localEulerAngles; // Initialize the rotation
        lastMousePosition = Input.mousePosition; // Initialize the last mouse position
    }

    void Update()
    {
        if (Input.GetMouseButtonDown(1))
        {
            isRotating = true;
            originalRotation = transform.rotation; // Update the original rotation to current rotation
            rotation = transform.localEulerAngles; // Ensure rotation is in sync with current camera orientation
            lastMousePosition = Input.mousePosition; // Update the last mouse position when starting to rotate
        }

        if (Input.GetMouseButtonUp(1))
        {
            isRotating = false;
            StartCoroutine(LerpBackToOriginalPosition());
        }

        if (isRotating)
        {
            Vector3 mouseDelta = Input.mousePosition - lastMousePosition; // Calculate the mouse movement

            if (mouseDelta.sqrMagnitude > 0) // Check if the mouse has moved
            {
                float mouseX = mouseDelta.x * rotationSpeed * Time.deltaTime;
                float mouseY = -mouseDelta.y * rotationSpeed * Time.deltaTime;

                rotation.y += mouseX;
                rotation.x = Mathf.Clamp(rotation.x + mouseY, -rotationLimit, rotationLimit); // Clamp the rotation on the X axis

                transform.localEulerAngles = rotation;
                lastMousePosition = Input.mousePosition; // Update the last mouse position
            }
        }
    }

    private IEnumerator LerpBackToOriginalPosition()
    {
        float duration = 0.5f;
        float time = 0;
        Quaternion startRotation = transform.rotation;

        while (time < duration)
        {
            transform.rotation = Quaternion.Lerp(startRotation, originalRotation, time / duration);
            time += Time.deltaTime;
            yield return null;
        }

        transform.rotation = originalRotation;
        rotation = originalRotation.eulerAngles; // Ensure the rotation vector is updated to match the original rotation
    }

    // Public method to adjust the rotation speed
    public void SetRotationSpeed(float newSpeed)
    {
        rotationSpeed = newSpeed;
    }
}
