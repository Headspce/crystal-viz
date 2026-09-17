using UnityEngine;

public class Meteor : MonoBehaviour
{
    public GameObject targetObject; // Set this to the object the meteor should collide with
    public GameObject particleEffectPrefab; // Set this to the particle effect prefab
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

        // Disable gravity for the Rigidbody
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.useGravity = false;
        }
    }

    void Update()
    {
        transform.Translate(fallDirection.normalized * fallSpeed * Time.deltaTime);
    }

    void OnCollisionEnter(Collision collision)
    {
        Debug.Log("Collision detected with: " + collision.gameObject.name);

        // Check if the collided object is the target object
        if (collision.gameObject == targetObject)
        {
            Debug.Log("Collision with target object confirmed.");

            // Spawn the particle effect at the collision point
            Instantiate(particleEffectPrefab, collision.contacts[0].point, Quaternion.identity);

            // Print confirmation message
            Debug.Log("Meteor has collided with the target object and spawned a particle effect!");
        }
        else
        {
            Debug.Log("Collision with non-target object.");
        }
    }

    /*
    void StartFadeAndDestroy()
    {
        StartCoroutine(FadeAndDestroy());
    }
    
    IEnumerator FadeAndDestroy()
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

        Destroy(gameObject);
    }
    */
}
