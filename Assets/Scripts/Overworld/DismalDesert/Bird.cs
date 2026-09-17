using UnityEngine;

[System.Serializable]
public class Bird
{
    public GameObject objectToRotate;
    public Vector3 rotationAxis = Vector3.up;
    public float rotationRadius = 10f;
    public float rotationSpeed = 10f;
    public bool useCustomCenter;
    public Vector3 customCenter;
    [HideInInspector]
    public Vector3 rotationCenter; // Add this property
    public Transform platform;
    public Vector3 localRotationAngle; // Add this property
}
