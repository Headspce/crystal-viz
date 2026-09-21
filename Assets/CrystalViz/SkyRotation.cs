using UnityEngine;

/// <summary>
/// Ultra-slow rotation of the sky dome around the viewer's vertical axis
/// (one full revolution per ~20 minutes), so the painted clouds visibly
/// drift across the sky. The dome is centered on the camera, so this reads
/// as the sky itself turning overhead. Update() never runs in the CI
/// edit-mode screenshot path, so captures stay deterministic. Replaces the
/// old texture-offset pan: rotating the transform moves the whole panorama
/// rigidly, with no UV seam and no swimming artifacts.
/// </summary>
public class SkyRotation : MonoBehaviour
{
    const float DegreesPerSecond = 360f / 1200f; // ~20-minute revolution

    void Update()
    {
        transform.Rotate(0f, DegreesPerSecond * Time.deltaTime, 0f);
    }
}
