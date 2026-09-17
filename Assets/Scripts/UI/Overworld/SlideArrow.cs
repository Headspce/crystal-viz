using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.SceneManagement;

public class SlideArrows : MonoBehaviour
{
    // Variables for left and right arrows
    public GameObject leftArrow;
    public GameObject rightArrow;
    public GameObject[] levelOptions; // Array of level option GameObjects
    public List<string> LevelLoad; // List of scene names for each level option (customizable)
    public float slideDistance = 10f; // Distance to slide
    public float slideSpeed = 1f;     // Speed of the slide
    public float scaleFactor = 1.2f;  // Scale factor for the size increase
    public float scaleSpeed = 5f;     // Speed of the scaling animation
    public float pauseDuration = 0.5f; // Pause duration after the scale animation
    public float cooldownDuration = 0.5f; // Cooldown duration to prevent spamming
    public float slideAwayDistance = 500f; // Distance to slide the level options away
    public float arrowSlideDistance = 50f; // Distance for the arrow to slide back and forth
    public float arrowSlideSpeed = 1f; // Speed of the slide
    public float arrowSlideDuration = 1f; // Duration of the slide
    public float arrowMovementStopTime = 3f; // Time after which movement should stop

    // Variables for middleSpark
    public GameObject middleSpark; // The new GameObject to appear
    public float sparkScaleFactor = 1.5f;  // Scale factor for middleSpark
    public float sparkScaleSpeed = 5f;     // Speed of the scaling animation for middleSpark
    public float sparkAppearDuration = 1f; // Duration middleSpark remains visible
    public float sparkDisappearDuration = 1f; // Duration of the disappearing animation for middleSpark
    public float sparkResetDuration = 2f; // Time after which middleSpark can be reused
    public float sparkAppearDelay = 0.5f; // Delay before middleSpark appears

    // Customizable options for level options behavior
    public float levelOptionDisappear = 1f; // Duration of the disappearing animation for level options
    public float levelOptionReappear = 1f; // Duration of the appearing animation for level options

    private Vector3 leftArrowOriginalPosition;
    private Vector3 rightArrowOriginalPosition;
    private Vector3 leftArrowOriginalScale;
    private Vector3 rightArrowOriginalScale;
    private Vector3 middleSparkOriginalScale; // Added for middleSpark reset
    private bool isScaling = false;
    private bool isInCooldown = false;
    private GameObject currentArrow = null;
    private Coroutine leftArrowCoroutine;
    private Coroutine rightArrowCoroutine;
    private int currentIndex = 0;
    private bool[] originalActiveStates;
    private bool isSparkResetting = false; // Flag to check if middleSpark is resetting

    public int CurrentIndex
    {
        get { return currentIndex; }
    }

    void Start()
    {
        leftArrowOriginalPosition = leftArrow.transform.localPosition;
        rightArrowOriginalPosition = rightArrow.transform.localPosition;
        leftArrowOriginalScale = leftArrow.transform.localScale;
        rightArrowOriginalScale = rightArrow.transform.localScale;
        middleSparkOriginalScale = middleSpark.transform.localScale; // Added for middleSpark reset

        // Store original active states
        originalActiveStates = new bool[levelOptions.Length];
        for (int i = 0; i < levelOptions.Length; i++)
        {
            originalActiveStates[i] = levelOptions[i].activeSelf;
        }

        // Initialize the first level option to be active
        foreach (var level in levelOptions)
        {
            level.SetActive(false);
        }
        levelOptions[currentIndex].SetActive(true);

        // Move unused level options to the side
        MoveUnusedLevelOptions();

        // Ensure middleSpark is initially inactive
        middleSpark.SetActive(false);
    }

    void OnEnable()
    {
        // Restore the original active states when the script is enabled
        if (originalActiveStates != null)
        {
            for (int i = 0; i < levelOptions.Length; i++)
            {
                levelOptions[i].SetActive(originalActiveStates[i]);
            }
        }
    }

    void OnDisable()
    {
        // Store the current active states when the script is disabled
        for (int i = 0; i < levelOptions.Length; i++)
        {
            originalActiveStates[i] = levelOptions[i].activeSelf;
        }
    }
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Return))
        {
            Debug.Log("Enter key pressed.");
            LoadLevelScene();
            return;
        }

        if (!isInCooldown && !isSparkResetting)
        {
            if (Input.GetKeyDown(KeyCode.RightArrow))
            {
                StartCoroutine(DeactivateAndScaleDownOption(currentIndex, (currentIndex + 1) % levelOptions.Length));
                StartCoroutine(MoveArrowBackAndForth(rightArrow));
                StartCoroutine(HandleMiddleSpark());
            }
            else if (Input.GetKeyDown(KeyCode.LeftArrow))
            {
                StartCoroutine(DeactivateAndScaleDownOption(currentIndex, (currentIndex - 1 + levelOptions.Length) % levelOptions.Length));
                StartCoroutine(MoveArrowBackAndForth(leftArrow));
                StartCoroutine(HandleMiddleSpark());
            }
        }

        if (isScaling)
        {
            ScaleArrow(currentArrow);
        }
    }

    void ScaleArrow(GameObject arrow)
    {
        arrow.transform.localScale = Vector3.Lerp(
            arrow.transform.localScale,
            arrow == leftArrow ? leftArrowOriginalScale * scaleFactor : rightArrowOriginalScale * scaleFactor,
            Time.deltaTime * scaleSpeed
        );

        if (Mathf.Abs(arrow.transform.localScale.x - (arrow == leftArrow ? leftArrowOriginalScale.x : rightArrowOriginalScale.x) * scaleFactor) < 0.01f)
        {
            isScaling = false;
            StartCoroutine(PauseAfterScale(arrow));
        }
    }

    System.Collections.IEnumerator PauseAfterScale(GameObject arrow)
    {
        yield return new WaitForSeconds(pauseDuration);
        StartCoroutine(ResetScale(arrow));
    }

    System.Collections.IEnumerator HandleMiddleSpark()
    {
        isSparkResetting = true;

        // Wait for the delay before middleSpark appears
        yield return new WaitForSeconds(sparkAppearDelay);

        // Ensure middleSpark is active
        middleSpark.SetActive(true);

        // Scale up middleSpark
        Vector3 startScale = Vector3.zero;
        Vector3 endScale = middleSparkOriginalScale * sparkScaleFactor; // Updated for middleSpark reset
        float elapsedTime = 0f;

        while (elapsedTime < sparkAppearDuration)
        {
            middleSpark.transform.localScale = Vector3.Lerp(startScale, endScale, (elapsedTime / sparkAppearDuration));
            elapsedTime += Time.deltaTime;
            yield return null;
        }

        middleSpark.transform.localScale = endScale;

        // Wait for some time
        yield return new WaitForSeconds(sparkAppearDuration);

        // Scale down and deactivate middleSpark
        elapsedTime = 0f;
        startScale = middleSpark.transform.localScale;
        endScale = Vector3.zero;

        while (elapsedTime < sparkDisappearDuration)
        {
            middleSpark.transform.localScale = Vector3.Lerp(startScale, endScale, (elapsedTime / sparkDisappearDuration));
            elapsedTime += Time.deltaTime;
            yield return null;
        }

        middleSpark.transform.localScale = endScale;
        middleSpark.SetActive(false);

        // Wait for the reset duration
        yield return new WaitForSeconds(sparkResetDuration);

        // Reset the scale of middleSpark
        middleSpark.transform.localScale = middleSparkOriginalScale;

        isSparkResetting = false;
    }

    void LoadLevelScene()
    {
        Debug.Log("LoadLevelScene called. Current index: " + currentIndex);
        if (currentIndex >= 0 && currentIndex < LevelLoad.Count)
        {
            string sceneName = LevelLoad[currentIndex];
            Debug.Log("Loading scene: " + sceneName);
            SceneManager.LoadScene(sceneName);
        }
        else
        {
            Debug.Log("Invalid level index or LevelLoad list is not set up correctly.");
        }
    }
    System.Collections.IEnumerator ResetScale(GameObject arrow)
    {
        while (Mathf.Abs(arrow.transform.localScale.x - (arrow == leftArrow ? leftArrowOriginalScale.x : rightArrowOriginalScale.x)) > 0.01f)
        {
            arrow.transform.localScale = Vector3.Lerp(
                arrow.transform.localScale,
                arrow == leftArrow ? leftArrowOriginalScale : rightArrowOriginalScale,
                Time.deltaTime * scaleSpeed
            );
            yield return null;
        }

        arrow.transform.localScale = arrow == leftArrow ? leftArrowOriginalScale : rightArrowOriginalScale;

        if (arrow == rightArrow)
        {
            rightArrowCoroutine = StartCoroutine(StartSliding(rightArrow, rightArrowOriginalPosition, SlideRightAndBack));
        }
        else if (arrow == leftArrow)
        {
            leftArrowCoroutine = StartCoroutine(StartSliding(leftArrow, leftArrowOriginalPosition, SlideLeftAndBack));
        }
    }

    System.Collections.IEnumerator DeactivateAndScaleDownOption(int currentIndex, int nextIndex)
    {
        GameObject currentOption = levelOptions[currentIndex];

        Vector3 startScale = currentOption.transform.localScale;
        Vector3 endScale = Vector3.zero; // Scale down to zero to simulate fading away
        float elapsedTime = 0f;
        float duration = levelOptionDisappear; // Customizable duration for disappearing

        // Scale down the current option
        while (elapsedTime < duration)
        {
            currentOption.transform.localScale = Vector3.Lerp(startScale, endScale, (elapsedTime / duration));
            elapsedTime += Time.deltaTime;
            yield return null;
        }

        currentOption.transform.localScale = endScale;
        currentOption.SetActive(false);

        // Move all level options to the side
        MoveUnusedLevelOptions();

        // Activate the next option and scale it up
        StartCoroutine(ScaleUpAndActivateOption(nextIndex));

        this.currentIndex = nextIndex;
    }

    void MoveUnusedLevelOptions()
    {
        for (int i = 0; i < levelOptions.Length; i++)
        {
            if (i != currentIndex)
            {
                levelOptions[i].transform.localPosition = new Vector3(slideAwayDistance, levelOptions[i].transform.localPosition.y, levelOptions[i].transform.localPosition.z);
            }
        }
    }

    System.Collections.IEnumerator ScaleUpAndActivateOption(int index)
    {
        GameObject nextOption = levelOptions[index];
        nextOption.SetActive(true);

        Vector3 startScale = Vector3.zero;
        Vector3 endScale = Vector3.one;
        float elapsedTime = 0f;
        float duration = levelOptionReappear; // Customizable duration for appearing

        // Scale up the next option
        while (elapsedTime < duration)
        {
            nextOption.transform.localScale = Vector3.Lerp(startScale, endScale, (elapsedTime / duration));
            elapsedTime += Time.deltaTime;
            yield return null;
        }

        nextOption.transform.localScale = endScale;
    }

    System.Collections.IEnumerator StartSliding(GameObject arrow, Vector3 originalPosition, System.Action<GameObject, Vector3> slideFunction)
    {
        yield return null; // Short delay to ensure the scaling is completed
        while (true)
        {
            slideFunction(arrow, originalPosition);
            yield return null;
        }
    }

    System.Collections.IEnumerator Cooldown()
    {
        isInCooldown = true;
        yield return new WaitForSeconds(cooldownDuration);
        isInCooldown = false;
    }

    void SlideRightAndBack(GameObject arrow, Vector3 originalPosition)
    {
        float offset = Mathf.PingPong(Time.time * slideSpeed, slideDistance);
        arrow.transform.localPosition = originalPosition + new Vector3(offset, 0, 0);
    }

    void SlideLeftAndBack(GameObject arrow, Vector3 originalPosition)
    {
        float offset = Mathf.PingPong(Time.time * slideSpeed, slideDistance);
        arrow.transform.localPosition = originalPosition - new Vector3(offset, 0, 0);
    }

    void ResetPosition(GameObject arrow, Vector3 originalPosition)
    {
        arrow.transform.localPosition = originalPosition;
    }

    System.Collections.IEnumerator MoveArrowBackAndForth(GameObject arrow)
    {
        float elapsedTime = 0f;
        Vector3 startPosition = arrow.transform.localPosition;
        Vector3 endPosition = startPosition + new Vector3(arrowSlideDistance, 0, 0);

        while (elapsedTime < arrowMovementStopTime)
        {
            float t = Mathf.PingPong(Time.time * arrowSlideSpeed, arrowSlideDuration) / arrowSlideDuration;
            arrow.transform.localPosition = Vector3.Lerp(startPosition, endPosition, t);
            elapsedTime += Time.deltaTime;
            yield return null;
        }

        // Reset position after movement stop time
        arrow.transform.localPosition = startPosition;
    }

    private IEnumerator DoublePressCooldown(KeyCode arrowKey)
    {
        yield return new WaitForSeconds(0.5f); // Adjust the delay as needed
    }
}
