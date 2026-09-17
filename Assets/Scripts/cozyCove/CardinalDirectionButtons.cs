using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Collections;

public class CameraDirectionController : MonoBehaviour
{
    public Camera targetCamera; // Reference to the camera
    public Button northButton; // Reference to the North button
    public Button eastButton; // Reference to the East button
    public Button southButton; // Reference to the South button
    public Button westButton; // Reference to the West button
    public float doubleClickThreshold = 0.3f; // Time threshold to detect double-click
    public float forwardMoveAmount = 5f; // Amount to move forward on double-click

    public GameObject northButtonHoverImage; // Hover image for North button
    public GameObject eastButtonHoverImage; // Hover image for East button
    public GameObject southButtonHoverImage; // Hover image for South button
    public GameObject westButtonHoverImage; // Hover image for West button

    public float exitDelay = 0.5f; // Delay time for button exit
    public float rotationSpeed = 5f; // Speed of rotation (lerping)

    public GameObject character; // Reference to the character to teleport
    public Transform teleportLocation; // Transform of the GameObject specifying the teleport location

    [Header("Reappear Sprites")]
    public Sprite reappearSpriteNorthDoubleClick; // Sprite for North direction (double click)
    public Sprite reappearSpriteEast;  // Sprite for East direction
    public Sprite reappearSpriteSouth; // Sprite for South direction
    public Sprite reappearSpriteWest;  // Sprite for West direction

    [Header("Simulated Key Press")]
    public float simulatedKeyPressDuration = 0.1f; // Duration of the simulated key press

    private float lastClickTime = 0; // Time of the last click
    private Button lastClickedButton = null; // Track the last clicked button
    private Quaternion targetRotation; // Target rotation for smooth lerping
    private bool isRotating = false; // Flag to check if the camera is currently rotating
    private string lastDirection = "South"; // Track the last direction (North, East, South, West)
    private bool isDoubleClick = false; // Flag to track if the North button was double-clicked

    private CameraControl cameraControl; // Reference to the CameraControl script
    private Rigidbody characterRigidbody; // Reference to the character's Rigidbody (if it exists)
    private CharacterController characterController; // Reference to the character's CharacterController (if it exists)
    private SpriteRenderer characterSpriteRenderer; // Reference to the character's SpriteRenderer (if it exists)

    void Start()
    {

        cameraControl = targetCamera.GetComponent<CameraControl>(); // Get the CameraControl script reference

        // Get references to the character's Rigidbody, CharacterController, and SpriteRenderer (if they exist)
        if (character != null)
        {
            characterRigidbody = character.GetComponent<Rigidbody>();
            characterController = character.GetComponent<CharacterController>();
            characterSpriteRenderer = character.GetComponent<SpriteRenderer>();
        }

        if (northButton != null)
        {
            northButton.onClick.AddListener(() => OnButtonClick(northButton, 0, "North", KeyCode.W)); // Rotate to North
            AddHoverListeners(northButton, northButtonHoverImage);
        }

        if (eastButton != null)
        {
            eastButton.onClick.AddListener(() => OnButtonClick(eastButton, 90, "East", KeyCode.D)); // Rotate to East
            AddHoverListeners(eastButton, eastButtonHoverImage);
        }

        if (southButton != null)
        {
            southButton.onClick.AddListener(() => OnButtonClick(southButton, 180, "South", KeyCode.S)); // Rotate to South
            AddHoverListeners(southButton, southButtonHoverImage);
        }

        if (westButton != null)
        {
            westButton.onClick.AddListener(() => OnButtonClick(westButton, -90, "West", KeyCode.A)); // Rotate to West
            AddHoverListeners(westButton, westButtonHoverImage);
        }

        // Initialize target rotation to the camera's current rotation
        if (targetCamera != null)
        {
            targetRotation = targetCamera.transform.rotation;
        }
    }

    void Update()
    {
        // Smoothly rotate the camera towards the target rotation
        if (isRotating && targetCamera != null)
        {
            targetCamera.transform.rotation = Quaternion.Slerp(
                targetCamera.transform.rotation,
                targetRotation,
                rotationSpeed * Time.deltaTime
            );

            // Stop rotating when the camera is close to the target rotation
            if (Quaternion.Angle(targetCamera.transform.rotation, targetRotation) < 0.1f)
            {
                isRotating = false;
                cameraControl.enabled = true; // Re-enable the CameraControl script

                // Teleport the character to the specified location (except for North double-click)
                if (lastDirection != "North" || !isDoubleClick)
                {
                    TeleportCharacterToLocation();
                }
            }
        }

        // Ensure the character's "front" text is always readable
        if (character != null && targetCamera != null)
        {
            AlignTextWithCamera();
        }
    }

    void OnButtonClick(Button button, float angle, string direction, KeyCode keyCode)
    {
        // Prevent overlapping rotations
        if (isRotating)
        {
            return;
        }

        float timeSinceLastClick = Time.time - lastClickTime;

        // Handle double-click for North button
        if (button == northButton && button == lastClickedButton && timeSinceLastClick <= doubleClickThreshold)
        {
            isDoubleClick = true; // Set double-click flag
            MoveCameraForward();
            TeleportCharacterToLocation(); // Teleport only on double-click
        }
        else
        {
            isDoubleClick = false; // Reset double-click flag
            RotateCamera(angle);

            // Teleport for single clicks (except North)
            if (direction != "North")
            {
                TeleportCharacterToLocation();
            }
        }

        // Update last click time and button
        lastClickTime = Time.time;
        lastClickedButton = button;
        lastDirection = direction; // Update the last direction

        // Set the sprite only for double-click on North button
        if (isDoubleClick && lastDirection == "North")
        {
            SetReappearSprite();
        }

        // Simulate a brief key press for the corresponding direction
        StartCoroutine(SimulateKeyPress(keyCode));
    }

    IEnumerator SimulateKeyPress(KeyCode keyCode)
    {
        // Simulate key down
        InputSimulator.PressKey(keyCode, true);

        // Wait for the specified duration
        yield return new WaitForSeconds(simulatedKeyPressDuration);

        // Simulate key up
        InputSimulator.PressKey(keyCode, false);
    }

    void RotateCamera(float angle)
    {
        if (targetCamera != null)
        {
            // Calculate the target rotation
            targetRotation = Quaternion.Euler(0, angle, 0) * targetCamera.transform.rotation;
            isRotating = true; // Start rotating
            cameraControl.enabled = false; // Disable the CameraControl script
        }
    }

    void MoveCameraForward()
    {
        if (targetCamera != null)
        {
            Vector3 initialPosition = targetCamera.transform.position; // Log initial position
            targetCamera.transform.Translate(Vector3.forward * forwardMoveAmount);
            Vector3 finalPosition = targetCamera.transform.position; // Log final position
            Debug.Log("Camera moved from " + initialPosition + " to " + finalPosition + " by " + forwardMoveAmount + " units.");
            cameraControl.enabled = true; // Re-enable the CameraControl script
        }
        else
        {
            Debug.LogError("Target camera not assigned in Inspector.");
        }
    }

    void TeleportCharacterToLocation()
    {
        if (character != null && teleportLocation != null)
        {
            // Teleport the character to the specified location
            if (characterRigidbody != null)
            {
                // Use Rigidbody.MovePosition for physics-based characters
                characterRigidbody.MovePosition(teleportLocation.position);
            }
            else if (characterController != null)
            {
                // Use CharacterController.Move for CharacterController-based characters
                characterController.enabled = false; // Disable the CharacterController temporarily
                character.transform.position = teleportLocation.position;
                characterController.enabled = true; // Re-enable the CharacterController
            }
            else
            {
                // Directly set the position for non-physics characters
                character.transform.position = teleportLocation.position;
            }

            Debug.Log("Character teleported to " + teleportLocation.position);

            // Ensure the character's "front" text is readable
            AlignTextWithCamera();
        }
        else
        {
            Debug.LogError("Character or teleport location not assigned in Inspector.");
        }
    }

    void SetReappearSprite()
    {
        if (characterSpriteRenderer != null)
        {
            if (lastDirection == "North" && isDoubleClick && reappearSpriteNorthDoubleClick != null)
            {
                // Use the double-click sprite for North
                characterSpriteRenderer.sprite = reappearSpriteNorthDoubleClick;
                Debug.Log("Character sprite set to North double-click reappear sprite.");
            }
        }
    }

    void AlignTextWithCamera()
    {
        if (character != null && targetCamera != null)
        {
            // Calculate the direction from the character to the camera
            Vector3 directionToCamera = targetCamera.transform.position - character.transform.position;
            directionToCamera.y = 0; // Ignore vertical difference to keep the character upright
            directionToCamera.Normalize();

            // Calculate the rotation needed to make the text face the camera
            Quaternion targetRotation = Quaternion.LookRotation(-directionToCamera);

            // Apply the rotation to the character
            character.transform.rotation = targetRotation;

            Debug.Log("Character's 'front' text aligned with the camera.");
        }
    }

    void AddHoverListeners(Button button, GameObject hoverImage)
    {
        EventTrigger eventTrigger = button.gameObject.AddComponent<EventTrigger>();

        EventTrigger.Entry pointerEnterEntry = new EventTrigger.Entry
        {
            eventID = EventTriggerType.PointerEnter
        };
        pointerEnterEntry.callback.AddListener((data) => { OnPointerEnter(hoverImage); });
        eventTrigger.triggers.Add(pointerEnterEntry);

        EventTrigger.Entry pointerExitEntry = new EventTrigger.Entry
        {
            eventID = EventTriggerType.PointerExit
        };
        pointerExitEntry.callback.AddListener((data) => { StartCoroutine(DelayedExit(hoverImage)); });
        eventTrigger.triggers.Add(pointerExitEntry);
    }

    void OnPointerEnter(GameObject hoverImage)
    {
        if (hoverImage != null)
        {
            hoverImage.SetActive(true);
        }
    }

    IEnumerator DelayedExit(GameObject hoverImage)
    {
        yield return new WaitForSeconds(exitDelay);
        if (hoverImage != null)
        {
            hoverImage.SetActive(false);
            Debug.Log("Button exit delay completed");
        }
    }
}

// Helper class to simulate key presses
public static class InputSimulator
{
    public static void PressKey(KeyCode keyCode, bool isPressed)
    {
        // Simulate key press using Unity's Input system
        if (isPressed)
        {
            Input.GetKeyDown(keyCode);
        }
        else
        {
            Input.GetKeyUp(keyCode);
        }
    }
}