using System.Collections;
using UnityEngine;

// This script can be attached to the meteor or the invisible trigger
public class Meteor : MonoBehaviour
{
    public float fallSpeed = 5f;
    public float fadeDuration = 1f;
    public Vector3 fallDirection = Vector3.down; // Set the fall direction

    [Header("Destruction Time Range")]
    public float minDestructionTime = 3f; // Minimum time before destruction
    public float maxDestructionTime = 5f; // Maximum time before destruction

    void Start()
    {
        // Start the destruction timer
        float destructionTime = Random.Range(minDestructionTime, maxDestructionTime);
        Invoke("StartFadeAndDestroy", destructionTime);
    }

    void Update()
    {
        transform.Translate(fallDirection.normalized * fallSpeed * Time.deltaTime);
    }

    /*
    void StartFadeAndDestroy()
    {
        StartCoroutine(FadeAndDestroy());
    }
    

    /* IEnumerator FadeAndDestroy()
    {
        MeshRenderer renderer = GetComponent<MeshRenderer>();
        Color initialColor = renderer.material.color;

        for (float t = 0; t < fadeDuration; t += Time.deltaTime)
        {
            Color newColor = initialColor;
            newColor.a = Mathf.Lerp(initialColor.a, 0, t / fadeDuration);
            renderer.material.color = newColor;
            yield return null;
        }
    */

        //Destroy(gameObject);
}

