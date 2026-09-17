using UnityEngine;

public class MouseClickLogger : MonoBehaviour
{
    void Update()
    {
        if (Input.GetMouseButtonDown(0)) // 0 is the left mouse button
        {
            Debug.Log("Mouse click detected at position: " + Input.mousePosition);

            // Create a ray from the camera to the mouse position
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            RaycastHit hit;

            // Perform the raycast
            if (Physics.Raycast(ray, out hit))
            {
                Debug.Log("GameObject detected: " + hit.collider.gameObject.name);
            }
        }
    }
}
