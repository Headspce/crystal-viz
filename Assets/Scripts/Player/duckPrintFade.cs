using UnityEngine;

public class duckPrintFade : MonoBehaviour
{
    public float fadeDuration = 5f; // Duration for the footprint to fade out

    private Material footprintMaterial;
    private Color originalColor;
    private float fadeStartTime;

    void Start()
    {
        footprintMaterial = GetComponent<Renderer>().material;
        originalColor = footprintMaterial.color;
        fadeStartTime = Time.time;
    }

    void Update()
    {
        float elapsed = Time.time - fadeStartTime;
        float alpha = Mathf.Lerp(originalColor.a, 0, elapsed / fadeDuration);

        Color newColor = new Color(originalColor.r, originalColor.g, originalColor.b, alpha);
        footprintMaterial.color = newColor;

        if (alpha <= 0)
        {
            Destroy(gameObject); // Destroy the footprint when it becomes fully transparent
        }
    }
}
