using UnityEngine;
using UnityEngine.UI;
using System.Collections;

public class CozyCove_BoxNavigator_F : MonoBehaviour
{
    public Camera targetCamera; // Public variable to assign the camera GameObject
    public Transform playerCharacter; // Reference to the player character
    public Transform targetPosition; // Target position in front of the camera
    public Animator playerAnimator; // Reference to the player character's animator
    public float moveSpeed = 3.0f; // Speed at which the player character moves
    public Button forwardArrowButton; // UI Button for the forward arrow
    public string targetTag = "TargetTag"; // Tag to identify target objects
    private GameObject currentHitObject; // To keep track of the current hit object
    private bool firstMove = true; // To track if it's the first move
    private Rigidbody playerRigidbody; // Reference to the player's Rigidbody

    void Start()
    {
        // Ensure the button click listener is assigned
        if (forwardArrowButton != null)
        {
            forwardArrowButton.onClick.AddListener(OnButtonClick);
            Debug.Log("Forward Arrow Button listener assigned");
        }
        else
        {
            Debug.LogError("ForwardArrowButton not assigned in Inspector.");
        }

        // Ensure the player character and animator are assigned
        if (playerCharacter == null)
        {
            Debug.LogError("Player character not assigned in Inspector.");
        }
        else
        {
            playerRigidbody = playerCharacter.GetComponent<Rigidbody>();

            if (playerRigidbody == null)
            {
                Debug.LogError("Rigidbody component not found on player character.");
            }
        }

        if (playerAnimator == null)
        {
            Debug.LogError("Player animator not assigned in Inspector.");
        }
    }

    void OnButtonClick()
    {
        Debug.Log("Forward Arrow button clicked");
        ShootRaycast();
    }

    void ShootRaycast()
    {
        // Get the center of the screen
        Vector3 screenCenter = new Vector3(Screen.width / 2, Screen.height / 2, 0);

        // Convert screen point to ray
        Ray ray = targetCamera.ScreenPointToRay(screenCenter);
        RaycastHit hit;

        // Check if the ray hits an object with the specified tag
        if (Physics.Raycast(ray, out hit, Mathf.Infinity))
        {
            GameObject hitObject = hit.collider.gameObject;
            Debug.Log("Hit Object: " + hitObject.name);

            // Check if the hit object has the specified tag
            if (hitObject.CompareTag(targetTag))
            {
                Debug.Log("Specific target found: " + hitObject.name);
                MoveCameraToTargetLocation(hitObject);
            }
            else
            {
                Debug.Log("Hit object does not have the specified tag.");
            }
        }
        else
        {
            Debug.Log("No target found. Check if the GameObject has a Box Collider and the correct tag.");
        }
    }

    void MoveCameraToTargetLocation(GameObject hitObject)
    {
        // Retrieve the script attached to the GameObject
        TargetLocations targetLocations = hitObject.GetComponent<TargetLocations>();

        if (targetLocations != null)
        {
            // Move the camera towards the first location in the list
            if (targetLocations.worldLocations.Count > 0)
            {
                Vector3 location = targetLocations.worldLocations[0];
                targetCamera.transform.position = location;
                Debug.Log("Camera moved to location: " + location);

                // Disable the hit object if it's not the first move
                if (!firstMove)
                {
                    hitObject.SetActive(false);
                    currentHitObject = hitObject;
                    StartCoroutine(CheckCameraDistance(location, hitObject));
                }
                else
                {
                    firstMove = false;
                }

                // Move the player character to the target position and trigger walking animation
                StartCoroutine(MovePlayerToTargetPosition(location));
            }
            else
            {
                Debug.Log("No world locations found in the TargetLocations script.");
            }
        }
        else
        {
            Debug.LogError("TargetLocations script not found on the specified GameObject.");
        }
    }

    IEnumerator MovePlayerToTargetPosition(Vector3 location)
    {
        Vector3 direction = (targetPosition.position - playerCharacter.position).normalized;

        // Trigger walking animation
        if (playerAnimator != null)
        {
            Debug.Log("Triggering walking animation");
            playerAnimator.SetBool("isWalking", true);
        }

        // Calculate target rotation facing the right side of the camera
        Vector3 rightSideDirection = targetCamera.transform.right;
        Quaternion targetRotation = Quaternion.LookRotation(rightSideDirection);

        // Move the player character towards the target position using Rigidbody
        while (Vector3.Distance(playerCharacter.position, targetPosition.position) > 0.1f)
        {
            playerRigidbody.MovePosition(playerCharacter.position + direction * moveSpeed * Time.deltaTime);
            playerCharacter.rotation = Quaternion.Lerp(playerCharacter.rotation, targetRotation, Time.deltaTime * moveSpeed);
            yield return null;
        }

        playerCharacter.position = targetPosition.position;
        playerCharacter.rotation = targetRotation;

        // Stop walking animation
        if (playerAnimator != null)
        {
            Debug.Log("Stopping walking animation");
            playerAnimator.SetBool("isWalking", false);
        }
    }

    IEnumerator CheckCameraDistance(Vector3 location, GameObject hitObject)
    {
        while (true)
        {
            yield return new WaitForSeconds(0.5f);

            // Check the distance between the camera and the target location
            float distance = Vector3.Distance(targetCamera.transform.position, location);
            if (distance > 1.0f)
            {
                // Re-enable the hit object if the camera has moved away
                hitObject.SetActive(true);
                currentHitObject = null;
                yield break;
            }
        }
    }
}
