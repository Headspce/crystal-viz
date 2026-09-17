using UnityEngine;

public class RaycastFromCamera : MonoBehaviour
{
    public Camera mainCamera; // Reference to the main camera
    public LayerMask targetLayerMask; // LayerMask to specify which objects to detect

    void Update()
    {
        // Check for mouse button click (left mouse button)
        if (Input.GetMouseButtonDown(0))
        {
            ShootRaycast();
        }
    }

    void ShootRaycast()
    {
        // Get the center of the screen
        Vector3 screenCenter = new Vector3(Screen.width / 2, Screen.height / 2, 0);

        // Convert screen point to ray
        Ray ray = mainCamera.ScreenPointToRay(screenCenter);
        RaycastHit hit;

        // Check if the ray hits an object in the specified layer
        if (Physics.Raycast(ray, out hit, Mathf.Infinity, targetLayerMask))
        {
            GameObject hitObject = hit.collider.gameObject;
            Debug.Log("Hit Object: " + hitObject.name);

            // Check if the hit object has a specific tag
            if (hitObject.CompareTag("BoxNavigator"))
            {
                Debug.Log("Specific target found: " + hitObject.name);
            }
            else
            {
                Debug.Log("Hit object does not have the TargetTag");
            }
        }
        else
        {
            Debug.Log("No target found. Check if the GameObject has a Box Collider and is on the correct layer.");
        }
    }
}
