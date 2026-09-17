using UnityEngine;

public class RippleController : MonoBehaviour
{
    public Material waterMaterial;  // Assign your water material here
    [Range(0, 1)]
    public float rippleCenterX = 0.5f; // Manually set X position of the ripple center (UV space)
    [Range(0, 1)]
    public float rippleCenterY = 0.5f; // Manually set Y position of the ripple center (UV space)

    void Update()
    {
        // Pass the manually set UV coordinates to the shader
        waterMaterial.SetVector("_RippleCenter", new Vector4(rippleCenterX, rippleCenterY, 0, 0));
    }
}
