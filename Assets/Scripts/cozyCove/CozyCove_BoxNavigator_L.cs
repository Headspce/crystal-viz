using UnityEngine;
using UnityEngine.UI;
using System.Collections;

public class CozyCove_BoxNavigator_L : MonoBehaviour
{
    public Camera targetCamera; // Public variable to assign the camera GameObject
    public Transform playerCharacter; // Reference to the player character
    public Transform[] targetPositions; // Array of positions in front of the camera (0: North, 1: East, 2: South, 3: West)
    public Material duckMaterial; // Reference to the duck's material with the shader

    private float fadeDuration = 1.0f; // Duration of the fade
    private bool isInitialized = false; // Track if the initialization has been done
    private int currentDirection = 0; // 0: North, 1: East, 2: South, 3: West

    void Start()
    {
        // Get the button components attached to the same GameObject
        Button rightButton = transform.Find("RightButton").GetComponent<Button>();
        Button leftButton = transform.Find("LeftButton").GetComponent<Button>();

        if (rightButton != null)
        {
            // Add a listener to the right button's onClick event
            rightButton.onClick.AddListener(() => RotateCamera(true));
        }
        else
        {
            Debug.LogError("Right button component not found on this GameObject.");
        }

        if (leftButton != null)
        {
            // Add a listener to the left button's onClick event
            leftButton.onClick.AddListener(() => RotateCamera(false));
        }
        else
        {
            Debug.LogError("Left button component not found on this GameObject.");
        }

        // Initialize material fade value and position
        InitializeDuckState();
    }

    void InitializeDuckState()
    {
        if (duckMaterial != null)
        {
            duckMaterial.SetFloat("_Fade", 1.0f); // Ensure the duck is fully visible
        }
        else
        {
            Debug.LogError("Duck material not assigned in Inspector.");
        }

        if (playerCharacter != null && targetPositions.Length > 0)
        {
            playerCharacter.position = targetPositions[0].position; // Set initial position to North
            currentDirection = 0;
        }
        else
        {
            Debug.LogError("Player character or target positions not assigned in Inspector.");
        }

        isInitialized = true;
    }

    void RotateCamera(bool rotateRight)
    {
        Debug.Log("Button clicked to rotate camera");

        // Ensure initialization has been done
        if (!isInitialized)
        {
            InitializeDuckState();
        }

        // Determine the new direction based on the current direction
        if (rotateRight)
        {
            currentDirection = (currentDirection + 1) % 4;
        }
        else
        {
            currentDirection = (currentDirection + 3) % 4; // Rotate left by subtracting 1 mod 4
        }

        // Rotate the camera and move the player character
        if (targetCamera != null)
        {
            StartCoroutine(FadeOutAndMovePlayer());
        }
        else
        {
            Debug.LogError("Target camera not assigned in Inspector.");
        }

        // Print the current direction
        Debug.Log("Current Direction: " + currentDirection);
    }

    IEnumerator FadeOutAndMovePlayer()
    {
        // Fade out the duck completely
        yield return StartCoroutine(Fade(0.0f));

        // Rotate the camera
        targetCamera.transform.Rotate(0, -90, 0);

        // Ensure targetPosition is correctly set
        if (targetPositions.Length > currentDirection)
        {
            // Move the duck to the new position
            playerCharacter.position = targetPositions[currentDirection].position;
        }
        else
        {
            Debug.LogError("Target position not assigned in Inspector.");
        }

        // Fade in the duck completely
        yield return StartCoroutine(Fade(1.0f));
    }

    IEnumerator Fade(float targetAlpha)
    {
        float elapsedTime = 0.0f;
        float startAlpha = duckMaterial.GetFloat("_Fade");

        while (elapsedTime < fadeDuration)
        {
            elapsedTime += Time.deltaTime;
            float newAlpha = Mathf.Lerp(startAlpha, targetAlpha, elapsedTime / fadeDuration);
            duckMaterial.SetFloat("_Fade", newAlpha);
            yield return null;
        }

        // Ensure the final alpha value is set
        duckMaterial.SetFloat("_Fade", targetAlpha);
    }
}
