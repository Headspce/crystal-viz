using UnityEngine;
using UnityEngine.UI;
using System.Collections;

public class CozyCove_BoxNavigator_R : MonoBehaviour
{
    public Camera targetCamera; // Public variable to assign the camera GameObject
    public Button rotateButton; // Public variable to assign the button
    public GameObject targetObject; // Public variable to assign the GameObject to be moved
    public GameObject spawnPointObject; // Public variable to assign the GameObject for the spawn position
    public float moveSpeed = 1.0f; // Speed at which the object will move
    public float delayBeforeMove = 2.0f; // Delay before the object starts moving
    public Material targetMaterial; // Reference to the material of the target object
    public float fadeDuration = 1.0f; // Duration of the fade-out and fade-in effect

    private bool isMoving = false; // Flag to check if the object is moving

    void Update()
    {
        if (targetObject != null)
        {
            Debug.Log("Target object's current position: " + targetObject.transform.position);

            if (isMoving)
            {
                // Smoothly move the object towards the spawn point
                targetObject.transform.position = Vector3.MoveTowards(targetObject.transform.position, spawnPointObject.transform.position, Time.deltaTime * moveSpeed);

                // Check if the object has reached the spawn point
                if (Vector3.Distance(targetObject.transform.position, spawnPointObject.transform.position) < 0.01f)
                {
                    isMoving = false; // Stop moving
                    StartCoroutine(FadeIn()); // Start fade-in effect
                    Debug.Log("Target object reached the spawn point.");
                }
            }
        }
    }

    void Start()
    {
        if (rotateButton != null)
        {
            // Add a listener to the button's onClick event
            rotateButton.onClick.AddListener(OnButtonClick);
            Debug.Log("Button listener added.");
        }
        else
        {
            Debug.LogError("Rotate button not assigned in Inspector.");
        }
    }

    void OnButtonClick()
    {
        Debug.Log("Button clicked.");
        StartCoroutine(RotateAndMove());
    }

    IEnumerator RotateAndMove()
    {
        RotateCamera90Degrees();
        yield return new WaitForSeconds(delayBeforeMove);
        StartCoroutine(FadeOut()); // Start fade-out effect
        yield return new WaitForSeconds(fadeDuration); // Wait for fade-out to complete
        StartMovingObject();
    }

    void RotateCamera90Degrees()
    {
        if (targetCamera != null)
        {
            // Rotate the camera +90 degrees on the Y axis
            targetCamera.transform.Rotate(0, 90, 0);
            Debug.Log("Camera rotated +90 degrees along the Y axis.");
        }
        else
        {
            Debug.LogError("Target camera not assigned in Inspector.");
        }
    }

    void StartMovingObject()
    {
        if (targetObject != null && spawnPointObject != null)
        {
            Debug.Log("Initial position: " + targetObject.transform.position);
            isMoving = true; // Start moving the object
            Debug.Log("Started moving the target object towards the spawn point.");
        }
        else
        {
            if (targetObject == null)
            {
                Debug.LogError("Target object not assigned in Inspector.");
            }
            if (spawnPointObject == null)
            {
                Debug.LogError("Spawn point object not assigned in Inspector.");
            }
        }
    }

    IEnumerator FadeOut()
    {
        float elapsedTime = 0;
        while (elapsedTime < fadeDuration)
        {
            elapsedTime += Time.deltaTime;
            float alpha = Mathf.Lerp(1, 0, elapsedTime / fadeDuration);
            targetMaterial.SetFloat("_Fade", alpha);
            yield return null;
        }
    }

    IEnumerator FadeIn()
    {
        float elapsedTime = 0;
        while (elapsedTime < fadeDuration)
        {
            elapsedTime += Time.deltaTime;
            float alpha = Mathf.Lerp(0, 1, elapsedTime / fadeDuration);
            targetMaterial.SetFloat("_Fade", alpha);
            yield return null;
        }
    }
}
