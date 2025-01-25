using UnityEngine;

public class GrassWindAnimator : MonoBehaviour
{
    public Material grassMaterial; // Assign the material in the Inspector
    public float windSpeed = 1.0f; // Customize wind speed in the Inspector

    void Update()
    {
        float customTime = Time.time * windSpeed;
        grassMaterial.SetFloat("_CustomTime", customTime);
    }
}
