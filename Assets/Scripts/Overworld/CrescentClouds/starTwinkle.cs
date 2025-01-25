using UnityEngine;

public class starTwinkle : MonoBehaviour
{
    private Renderer starRenderer;
    public float maxOffset = 10f; // Adjust this value as needed

    void Start()
    {
        starRenderer = GetComponent<Renderer>();
        float randomOffset = Random.Range(0f, maxOffset);
        starRenderer.material.SetFloat("_TimeOffset", randomOffset);
    }
}
