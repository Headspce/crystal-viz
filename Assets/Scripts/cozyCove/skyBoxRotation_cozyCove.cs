using UnityEngine;

public class CustomAxisRotation : MonoBehaviour
{
    // This field allows you to set the rotation axis in the Editor
    public Vector3 rotationAxis = Vector3.up;

    // Speed of rotation
    public float rotationSpeed = 100.0f;

    void Update()
    {
        // Rotate the object around the specified axis at the specified speed
        transform.Rotate(rotationAxis * rotationSpeed * Time.deltaTime);
    }
}
