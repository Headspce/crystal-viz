using UnityEngine;
using UnityEngine.EventSystems; // Required for UI detection
using TMPro; // Add this namespace for TextMeshPro
using System.Collections;

public class DuckCharacterController : MonoBehaviour
{
    public float walkSpeed = 2f;
    public float runSpeed = 5f;
    public float jumpForce = 7f;
    public float doubleJumpForce = 7f;
    public float glideGravity = 2f;
    public float glideDuration = 2f;
    public Camera mainCamera; // Reference to the main camera

    [Header("Sprite Settings")]
    public Sprite frontSprite;       // Sprite for facing front
    public Sprite backSprite;        // Sprite for facing back
    public Sprite leftSprite;        // Sprite for facing left
    public Sprite rightSprite;       // Sprite for facing right
    public Sprite frontLeftSprite;   // Sprite for facing front-left
    public Sprite frontRightSprite;  // Sprite for facing front-right
    public Sprite backLeftSprite;    // Sprite for facing back-left
    public Sprite backRightSprite;   // Sprite for facing back-right

    [Header("Pecking Animation - Forward (S)")]
    public Sprite[] forwardPeckingSprites = new Sprite[3]; // 3 frames for forward pecking
    public float delayBetweenForwardSprites = 0.2f; // Delay between forward sprites

    [Header("Pecking Animation - Backward (W)")]
    public Sprite[] backwardPeckingSprites = new Sprite[3]; // 3 frames for backward pecking
    public float delayBetweenBackwardSprites = 0.2f; // Delay between backward sprites

    [Header("Pecking Animation - Left (A)")]
    public Sprite[] leftPeckingSprites = new Sprite[3]; // 3 frames for left pecking
    public float delayBetweenLeftSprites = 0.2f; // Delay between left sprites

    [Header("Pecking Animation - Right (D)")]
    public Sprite[] rightPeckingSprites = new Sprite[3]; // 3 frames for right pecking
    public float delayBetweenRightSprites = 0.2f; // Delay between right sprites

    [Header("UI Counter")]
    public TextMeshProUGUI peckCounterText; // TextMeshPro UI element to display the peck counter
    private int peckCounter = 0; // Counter for pecks

    [Header("Object Interaction")]
    public GameObject objectInFrontOfDuck; // The game object in front of the duck
    public float interactionRange = 1f; // Range within which the duck can interact with objects

    [Header("Counters")]
    public TextMeshProUGUI coinCounterText;
    public TextMeshProUGUI gemCounterText;
    public TextMeshProUGUI shellCounterText;
    public TextMeshProUGUI fishCounterText;

    private int coinCounter = 0;
    private int gemCounter = 0;
    private int shellCounter = 0;
    private int fishCounter = 0;

    [Header("Customizable Triggers")]
    public GameObject coinTriggerPrefab;
    public GameObject gemTriggerPrefab;
    public GameObject shellTriggerPrefab;
    public GameObject fishTriggerPrefab;

    [Header("Trigger Display Duration")]
    public float coinDisplayDuration = 2f;
    public float gemDisplayDuration = 2f;
    public float shellDisplayDuration = 2f;
    public float fishDisplayDuration = 2f;

    [Header("Object Layers")]
    public LayerMask coinLayer;
    public LayerMask gemLayer;
    public LayerMask shellLayer;
    public LayerMask fishLayer;

    private SpriteRenderer spriteRenderer;
    private Rigidbody rb;
    private bool isGrounded = true;
    private bool canDoubleJump = false;
    private bool isGliding = false;
    private bool isPecking = false; // Flag to track if the duck is pecking
    private float glideTime = 0f;
    private Sprite originalSprite; // Stores the original sprite before pecking
    private string lastMovementDirection = "S"; // Tracks the last movement direction (W, A, S, D)

    void Start()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        rb = GetComponent<Rigidbody>();
        UpdatePeckCounterUI(); // Initialize the UI counter
    }

    void Update()
    {
        if (!isPecking)
        {
            HandleMovement();
            HandleJumping();
            HandleGliding();
        }

        // Check for left click (not on UI)
        if (Input.GetMouseButtonDown(0))
        {
            Debug.Log("Left mouse button clicked."); // Debug log for left click
            if (!IsPointerOverUI())
            {
                // Start the pecking animation
                StartPeckingAnimation();

                // Check for overlapping objects and handle interactions
                HandleObjectInteraction();
            }
        }
    }

    void HandleMovement()
    {
        // Get camera's forward and right vectors
        Vector3 cameraForward = mainCamera.transform.forward;
        Vector3 cameraRight = mainCamera.transform.right;

        // Project the camera vectors on the ground plane (assuming y is up)
        cameraForward.y = 0;
        cameraRight.y = 0;
        cameraForward.Normalize();
        cameraRight.Normalize();

        // Get input
        float moveHorizontal = Input.GetAxis("Horizontal");
        float moveVertical = Input.GetAxis("Vertical");

        // Calculate movement direction
        Vector3 moveDirection = (cameraForward * moveVertical + cameraRight * moveHorizontal).normalized;

        // Apply movement
        if (moveDirection != Vector3.zero)
        {
            // Move the character using Rigidbody velocity
            float speed = Input.GetKey(KeyCode.LeftShift) ? runSpeed : walkSpeed;
            rb.linearVelocity = new Vector3(moveDirection.x * speed, rb.linearVelocity.y, moveDirection.z * speed);

            // Change sprite based on movement direction
            UpdateSpriteBasedOnDirection(moveDirection);

            // Track the last movement direction
            if (Input.GetKey(KeyCode.W)) lastMovementDirection = "W";
            if (Input.GetKey(KeyCode.A)) lastMovementDirection = "A";
            if (Input.GetKey(KeyCode.S)) lastMovementDirection = "S";
            if (Input.GetKey(KeyCode.D)) lastMovementDirection = "D";
        }
        else
        {
            // Stop movement if no input
            rb.linearVelocity = new Vector3(0, rb.linearVelocity.y, 0);
        }
    }

    void UpdateSpriteBasedOnDirection(Vector3 moveDirection)
    {
        // Calculate the angle between the movement direction and the camera's forward direction
        float angle = Vector3.SignedAngle(moveDirection, mainCamera.transform.forward, Vector3.up);

        // Determine which sprite to use based on the angle
        if (angle >= -22.5f && angle < 22.5f)
        {
            spriteRenderer.sprite = backSprite; // Facing back
        }
        else if (angle >= 22.5f && angle < 67.5f)
        {
            spriteRenderer.sprite = backRightSprite; // Facing back-right
        }
        else if (angle >= 67.5f && angle < 112.5f)
        {
            spriteRenderer.sprite = rightSprite; // Facing right
        }
        else if (angle >= 112.5f && angle < 157.5f)
        {
            spriteRenderer.sprite = frontRightSprite; // Facing front-right
        }
        else if (angle >= 157.5f || angle < -157.5f)
        {
            spriteRenderer.sprite = frontSprite; // Facing front
        }
        else if (angle >= -157.5f && angle < -112.5f)
        {
            spriteRenderer.sprite = frontLeftSprite; // Facing front-left
        }
        else if (angle >= -112.5f && angle < -67.5f)
        {
            spriteRenderer.sprite = leftSprite; // Facing left
        }
        else if (angle >= -67.5f && angle < -22.5f)
        {
            spriteRenderer.sprite = backLeftSprite; // Facing back-left
        }
    }

    void HandleJumping()
    {
        if (Input.GetButtonDown("Jump"))
        {
            if (isGrounded)
            {
                rb.linearVelocity = new Vector3(rb.linearVelocity.x, jumpForce, rb.linearVelocity.z);
                isGrounded = false;
                canDoubleJump = true;
            }
            else if (canDoubleJump)
            {
                rb.linearVelocity = new Vector3(rb.linearVelocity.x, doubleJumpForce, rb.linearVelocity.z);
                canDoubleJump = false;
            }
        }
    }

    void HandleGliding()
    {
        if (!isGrounded && Input.GetButton("Jump") && glideTime < glideDuration)
        {
            rb.linearVelocity = new Vector3(rb.linearVelocity.x, -glideGravity, rb.linearVelocity.z);
            isGliding = true;
            glideTime += Time.deltaTime;
        }
        else
        {
            isGliding = false;
            glideTime = 0f;
        }
    }

    void OnCollisionEnter(Collision collision)
    {
        if (collision.gameObject.CompareTag("Ground"))
        {
            isGrounded = true;
        }
    }

    void StartPeckingAnimation()
    {
        if (!isPecking)
        {
            originalSprite = spriteRenderer.sprite; // Store the original sprite
            StartCoroutine(PlayPeckingAnimation());
        }
    }

    System.Collections.IEnumerator PlayPeckingAnimation()
    {
        isPecking = true;

        // Determine which set of sprites to use based on the last movement direction
        Sprite[] peckingSprites = null;
        float delay = 0f;

        switch (lastMovementDirection)
        {
            case "W": // Backward
                peckingSprites = backwardPeckingSprites;
                delay = delayBetweenBackwardSprites;
                break;
            case "A": // Left
                peckingSprites = leftPeckingSprites;
                delay = delayBetweenLeftSprites;
                break;
            case "S": // Forward
                peckingSprites = forwardPeckingSprites;
                delay = delayBetweenForwardSprites;
                break;
            case "D": // Right
                peckingSprites = rightPeckingSprites;
                delay = delayBetweenRightSprites;
                break;
        }

        // Play the pecking animation
        for (int i = 0; i < peckingSprites.Length; i++)
        {
            spriteRenderer.sprite = peckingSprites[i];
            yield return new WaitForSeconds(delay);
        }

        // Reset to the original sprite
        spriteRenderer.sprite = originalSprite;
        isPecking = false;

        // Increase the peck counter and update the UI
        peckCounter++;
        UpdatePeckCounterUI();
    }

    void UpdatePeckCounterUI()
    {
        // Update the TextMeshPro UI text with the current peck count
        if (peckCounterText != null)
        {
            peckCounterText.text = peckCounter.ToString(); // Only display the numeric value
        }
    }

    void HandleObjectInteraction()
    {
        // Check if the object in front of the duck is active and enabled
        if (objectInFrontOfDuck == null || !objectInFrontOfDuck.activeInHierarchy)
        {
            Debug.Log("Object in front of duck is disabled or null. Skipping interaction.");
            return; // Exit if the object is disabled or null
        }

        // Combine all interaction layers into a single layer mask
        LayerMask interactionLayers = coinLayer | gemLayer | shellLayer | fishLayer;

        // Check if the object in front of the duck is overlapping any of the interaction layers
        Collider[] overlaps = Physics.OverlapSphere(objectInFrontOfDuck.transform.position, interactionRange, interactionLayers);

        if (overlaps.Length == 0)
        {
            Debug.Log("No overlapping objects detected."); // Debug log for no overlaps
            return; // Exit if no overlaps are detected
        }

        // Handle coin overlaps
        Collider[] coinOverlaps = Physics.OverlapSphere(objectInFrontOfDuck.transform.position, interactionRange, coinLayer);
        if (coinOverlaps.Length > 0)
        {
            GameObject coin = coinOverlaps[0].gameObject;
            coinCounter++;
            UpdateCounterUI(coinCounterText, coinCounter);
            TriggerCustomizableObject(coinTriggerPrefab, coinDisplayDuration);
            Destroy(coin); // Destroy the coin immediately
        }

        // Handle gem overlaps
        Collider[] gemOverlaps = Physics.OverlapSphere(objectInFrontOfDuck.transform.position, interactionRange, gemLayer);
        if (gemOverlaps.Length > 0)
        {
            GameObject gem = gemOverlaps[0].gameObject;
            gemCounter++;
            UpdateCounterUI(gemCounterText, gemCounter);
            TriggerCustomizableObject(gemTriggerPrefab, gemDisplayDuration);
            Destroy(gem); // Destroy the gem immediately
        }

        // Handle shell overlaps
        Collider[] shellOverlaps = Physics.OverlapSphere(objectInFrontOfDuck.transform.position, interactionRange, shellLayer);
        if (shellOverlaps.Length > 0)
        {
            GameObject shell = shellOverlaps[0].gameObject;
            shellCounter++;
            UpdateCounterUI(shellCounterText, shellCounter);
            TriggerCustomizableObject(shellTriggerPrefab, shellDisplayDuration);
            Destroy(shell); // Destroy the shell immediately
        }

        // Handle fish overlaps
        Collider[] fishOverlaps = Physics.OverlapSphere(objectInFrontOfDuck.transform.position, interactionRange, fishLayer);
        if (fishOverlaps.Length > 0)
        {
            GameObject fish = fishOverlaps[0].gameObject;
            fishCounter++;
            UpdateCounterUI(fishCounterText, fishCounter);
            TriggerCustomizableObject(fishTriggerPrefab, fishDisplayDuration);
            Destroy(fish); // Destroy the fish immediately
        }
    }

    void UpdateCounterUI(TextMeshProUGUI counterText, int counterValue)
    {
        if (counterText != null)
        {
            counterText.text = counterValue.ToString(); // Only display the numeric value
        }
    }

    void TriggerCustomizableObject(GameObject triggerPrefab, float displayDuration)
    {
        if (triggerPrefab != null)
        {
            // Instantiate the object above the player's head
            GameObject triggerObject = Instantiate(triggerPrefab, transform.position + Vector3.up * 2f, Quaternion.identity);
            StartCoroutine(DestroyAfterDelay(triggerObject, displayDuration));
        }
    }

    IEnumerator DestroyAfterDelay(GameObject obj, float delay)
    {
        yield return new WaitForSeconds(delay);
        Destroy(obj);
    }

    bool IsPointerOverUI()
    {
        // Check if the mouse is over a UI element
        return EventSystem.current.IsPointerOverGameObject();
    }
}