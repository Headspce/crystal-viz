using UnityEngine;

public class MeteorCollisions : MonoBehaviour
{
    public GameObject targetObject; // Set this to the object the meteor should collide with

    void OnCollisionEnter(Collision collision)
    {
        // Check if the collided object is the target object
        if (collision.gameObject == targetObject)
        {
            Debug.Log("Meteor has collided with the target object!");
        }
    }
}
