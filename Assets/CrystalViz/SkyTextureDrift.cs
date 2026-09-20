using UnityEngine;

/// <summary>
/// Ultra-slow horizontal pan of the sky dome texture (one full cycle takes
/// about 30 minutes), echoing the old scrolling sky plane. Update() never
/// runs in the CI edit-mode screenshot path, so captures stay deterministic.
/// </summary>
public class SkyTextureDrift : MonoBehaviour
{
    public Material skyMaterial;
    const float CyclesPerSecond = 1f / 1800f; // ~30-minute cycle

    void Update()
    {
        if (skyMaterial == null) return;
        var off = skyMaterial.mainTextureOffset;
        off.x = (off.x + CyclesPerSecond * Time.deltaTime) % 1f;
        skyMaterial.mainTextureOffset = off;
    }
}
