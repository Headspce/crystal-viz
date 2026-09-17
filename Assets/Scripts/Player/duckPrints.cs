using UnityEngine;

public class duckPrints : MonoBehaviour
{
    [System.Serializable]
    public class DirectionSettings
    {
        public GameObject footprintPrefab; // Footprint prefab for this direction
        public Vector3 spawnOffset; // Offset for this direction
        [Tooltip("Time delay between spawning footprints for this direction.")]
        public float spawnInterval = 0.5f; // Spawn interval for this direction (publicly editable)
        [Tooltip("Additional delay before footprints start spawning for this direction.")]
        public float initialDelay = 0f; // Additional delay for this direction (publicly editable)
        public float disappearTime = 2f; // Disappear time for this direction
        [HideInInspector] public float timer; // Timer for this direction (not editable in Inspector)
        [HideInInspector] public bool isDelayed; // Track if the initial delay has passed
    }

    // Inspector sections for each direction
    public DirectionSettings wSettings; // W (Up)
    public DirectionSettings aSettings; // A (Left)
    public DirectionSettings sSettings; // S (Down)
    public DirectionSettings dSettings; // D (Right)

    private GameObject lastFootprint; // Track the last spawned footprint

    void Start()
    {
        // Initialize timers with their respective spawn intervals
        wSettings.timer = wSettings.initialDelay; // Start with the initial delay
        aSettings.timer = aSettings.initialDelay;
        sSettings.timer = sSettings.initialDelay;
        dSettings.timer = dSettings.initialDelay;

        // Set initial delay flags
        wSettings.isDelayed = wSettings.initialDelay > 0;
        aSettings.isDelayed = aSettings.initialDelay > 0;
        sSettings.isDelayed = sSettings.initialDelay > 0;
        dSettings.isDelayed = dSettings.initialDelay > 0;
    }

    void Update()
    {
        // Check if the player is moving (using input or velocity)
        bool isMoving = IsPlayerMoving();

        // Update timers only if the player is moving
        if (isMoving)
        {
            // Update timers for each direction
            UpdateDirectionTimer(wSettings);
            UpdateDirectionTimer(aSettings);
            UpdateDirectionTimer(sSettings);
            UpdateDirectionTimer(dSettings);

            // Check for input and spawn footprints based on direction-specific timers
            if (Input.GetKey(KeyCode.W)) // W (Up)
            {
                if (wSettings.timer <= 0f && !wSettings.isDelayed)
                {
                    SpawnFootprint(wSettings);
                    wSettings.timer = wSettings.spawnInterval; // Reset the timer
                }
            }
            else if (Input.GetKey(KeyCode.S)) // S (Down)
            {
                if (sSettings.timer <= 0f && !sSettings.isDelayed)
                {
                    SpawnFootprint(sSettings);
                    sSettings.timer = sSettings.spawnInterval; // Reset the timer
                }
            }
            else if (Input.GetKey(KeyCode.A)) // A (Left)
            {
                if (aSettings.timer <= 0f && !aSettings.isDelayed)
                {
                    SpawnFootprint(aSettings);
                    aSettings.timer = aSettings.spawnInterval; // Reset the timer
                }
            }
            else if (Input.GetKey(KeyCode.D)) // D (Right)
            {
                if (dSettings.timer <= 0f && !dSettings.isDelayed)
                {
                    SpawnFootprint(dSettings);
                    dSettings.timer = dSettings.spawnInterval; // Reset the timer
                }
            }
        }
    }

    void UpdateDirectionTimer(DirectionSettings settings)
    {
        if (settings.isDelayed)
        {
            // Count down the initial delay
            settings.timer -= Time.deltaTime;
            if (settings.timer <= 0f)
            {
                settings.isDelayed = false; // Initial delay has passed
                settings.timer = settings.spawnInterval; // Reset the timer to the spawn interval
            }
        }
        else
        {
            // Count down the spawn interval
            settings.timer -= Time.deltaTime;
        }
    }

    bool IsPlayerMoving()
    {
        // Check if the player is providing movement input (WASD or arrow keys)
        float horizontalInput = Input.GetAxis("Horizontal");
        float verticalInput = Input.GetAxis("Vertical");

        // Return true if there is any input
        return Mathf.Abs(horizontalInput) > 0.1f || Mathf.Abs(verticalInput) > 0.1f;
    }

    void SpawnFootprint(DirectionSettings settings)
    {
        // Spawn the footprint using the selected direction's settings
        if (settings.footprintPrefab != null)
        {
            // Calculate the spawn position with the offset
            Vector3 spawnPosition = transform.position + transform.TransformDirection(settings.spawnOffset);

            // Instantiate the footprint at the calculated position and rotation of the parent GameObject
            lastFootprint = Instantiate(settings.footprintPrefab, spawnPosition, transform.rotation);
            lastFootprint.transform.localScale = transform.localScale; // Match the player's scale

            Debug.Log("Footprint spawned at: " + spawnPosition + " for direction: " + settings.footprintPrefab.name);

            // Start the disappear cycle for the footprint
            StartCoroutine(DisappearCycle(lastFootprint, settings.disappearTime));
        }
        else
        {
            Debug.LogError("Footprint prefab is not assigned for the current direction.");
        }
    }

    System.Collections.IEnumerator DisappearCycle(GameObject footprint, float disappearTime)
    {
        if (footprint == null)
        {
            Debug.LogWarning("Footprint is null. Coroutine will not run.");
            yield break; // Exit if the footprint is already destroyed
        }

        Debug.Log("Coroutine started for footprint at: " + footprint.transform.position);

        // Wait for the disappear time
        yield return new WaitForSeconds(disappearTime);

        Debug.Log("Disappear time reached for footprint at: " + footprint.transform.position);

        // Destroy the footprint
        if (footprint != null)
        {
            Destroy(footprint);
            Debug.Log("Footprint destroyed at: " + footprint.transform.position);
        }
        else
        {
            Debug.LogWarning("Footprint is null during disappear.");
        }
    }
}