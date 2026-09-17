using UnityEngine;
using System.Collections; // Ensure the System.Collections namespace is included

public class MeteorCollision : MonoBehaviour
{
    public GameObject targetObject; // Set this to the object the meteor should collide with
    public GameObject particleEffectPrefab; // Set this to the particle effect prefab
    public float initialDelay = 3f; // Customizable delay in seconds
    private bool delayCompleted = false;

    void Start()
    {
        StartCoroutine(InitialDelayCoroutine());
    }

    void OnCollisionEnter(Collision collision)
    {
        if (!delayCompleted) return; // Skip collision handling if delay is not completed

        Debug.Log("Collision detected with: " + collision.gameObject.name);

        // Check if the collided object is the target object
        if (collision.gameObject == targetObject)
        {
            // Spawn the particle effect at the collision point
            Instantiate(particleEffectPrefab, collision.contacts[0].point, Quaternion.identity);

            // Print confirmation message
            Debug.Log("Meteor has collided with the target object and spawned a particle effect!");
        }
    }

    private IEnumerator InitialDelayCoroutine()
    {
        yield return new WaitForSeconds(initialDelay);
        delayCompleted = true;
        Debug.Log("Initial delay completed. Collision detection is now active.");
    }
}
