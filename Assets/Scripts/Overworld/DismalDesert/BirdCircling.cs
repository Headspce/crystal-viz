using UnityEngine;

public class BirdCircling : MonoBehaviour
{
    public Bird[] objectsToRotate;

    void Start()
    {
        foreach (Bird settings in objectsToRotate)
        {
            settings.rotationCenter = settings.useCustomCenter ? settings.customCenter : transform.position;
        }
    }

    void Update()
    {
        foreach (Bird settings in objectsToRotate)
        {
            if (settings.useCustomCenter)
            {
                settings.rotationCenter = settings.customCenter;
            }

            float angle = settings.rotationSpeed * Time.time;
            float nextAngle = angle + settings.rotationSpeed * Time.deltaTime;

            Vector3 offset = new Vector3(
                Mathf.Cos(angle) * settings.rotationRadius,
                0,
                Mathf.Sin(angle) * settings.rotationRadius
            );

            Vector3 nextOffset = new Vector3(
                Mathf.Cos(nextAngle) * settings.rotationRadius,
                0,
                Mathf.Sin(nextAngle) * settings.rotationRadius
            );

            Vector3 rotatedOffset = settings.platform.rotation * offset;
            Vector3 nextRotatedOffset = settings.platform.rotation * nextOffset;

            settings.objectToRotate.transform.position = settings.rotationCenter + rotatedOffset;

            Vector3 direction = (nextRotatedOffset - rotatedOffset).normalized;
            if (direction != Vector3.zero)
                settings.objectToRotate.transform.forward = -direction;

            // Apply local rotation to add variation
            settings.objectToRotate.transform.Rotate(settings.localRotationAngle, Space.Self);
        }
    }

    public void SetRotationAxis(Vector3 axis)
    {
        foreach (Bird settings in objectsToRotate)
        {
            settings.rotationAxis = axis;
        }
    }

    public void SetCustomCenter(Vector3 center, bool useCustom)
    {
        foreach (Bird settings in objectsToRotate)
        {
            settings.customCenter = center;
            settings.useCustomCenter = useCustom;
        }
    }

    public void SetPlatform(Transform platformTransform)
    {
        foreach (Bird settings in objectsToRotate)
        {
            settings.platform = platformTransform;
        }
    }
}
