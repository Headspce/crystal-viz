using UnityEngine;

public class SlideArrows : MonoBehaviour
{
    public GameObject leftArrow;
    public GameObject rightArrow;
    public GameObject[] levelOptions; // Array of level option GameObjects
    public float slideDistance = 10f; // Distance to slide
    public float slideSpeed = 1f;     // Speed of the slide
    public float scaleFactor = 1.2f;  // Scale factor for the size increase
    public float scaleSpeed = 5f;     // Speed of the scaling animation
    public float pauseDuration = 0.5f; // Pause duration after the scale animation
    public float cooldownDuration = 0.5f; // Cooldown duration to prevent spamming
    public float slideAwayDistance = 500f; // Distance to slide the level options away
    private Vector3 leftArrowOriginalPosition;
    private Vector3 rightArrowOriginalPosition;
    private Vector3 leftArrowOriginalScale;
    private Vector3 rightArrowOriginalScale;
    private bool isScaling = false;
    private bool isInCooldown = false;
    private GameObject currentArrow = null;
    private Coroutine leftArrowCoroutine;
    private Coroutine rightArrowCoroutine;
    private int currentIndex = 0;

    void Start()
    {
        leftArrowOriginalPosition = leftArrow.transform.localPosition;
        rightArrowOriginalPosition = rightArrow.transform.localPosition;
        leftArrowOriginalScale = leftArrow.transform.localScale;
        rightArrowOriginalScale = rightArrow.transform.localScale;

        // Initialize the first level option to be active
        foreach (var level in levelOptions)
        {
            level.SetActive(false);
        }
        levelOptions[currentIndex].SetActive(true);
    }

    void Update()
    {
        if (!isInCooldown)
        {
            if (Input.GetKeyDown(KeyCode.RightArrow))
            {
                if (leftArrowCoroutine != null)
                {
                    StopCoroutine(leftArrowCoroutine);
                    ResetPosition(leftArrow, leftArrowOriginalPosition);
                }
                if (rightArrowCoroutine != null)
                {
                    StopCoroutine(rightArrowCoroutine);
                    ResetPosition(rightArrow, rightArrowOriginalPosition);
                }
                currentArrow = rightArrow;
                isScaling = true;
                StartCoroutine(Cooldown());
            }
            else if (Input.GetKeyDown(KeyCode.LeftArrow))
            {
                if (rightArrowCoroutine != null)
                {
                    StopCoroutine(rightArrowCoroutine);
                    ResetPosition(rightArrow, rightArrowOriginalPosition);
                }
                if (leftArrowCoroutine != null)
                {
                    StopCoroutine(leftArrowCoroutine);
                    ResetPosition(leftArrow, leftArrowOriginalPosition);
                }
                currentArrow = leftArrow;
                isScaling = true;
                StartCoroutine(Cooldown());
            }
        }

        if (isScaling)
        {
            ScaleArrow(currentArrow);
        }

        if (Input.GetKeyDown(KeyCode.Return))
        {
            if (currentArrow == rightArrow)
            {
                StartCoroutine(SlideAwayAndIn(currentIndex, -slideAwayDistance, true));
            }
            else if (currentArrow == leftArrow)
            {
                StartCoroutine(SlideAwayAndIn(currentIndex, slideAwayDistance, false));
            }
        }
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

    System.Collections.IEnumerator SlideAwayAndIn(int currentIndex, float distance, bool forward)
    {
        GameObject currentObject = levelOptions[currentIndex];
        int nextIndex = forward ? (currentIndex + 1) % levelOptions.Length : (currentIndex - 1 + levelOptions.Length) % levelOptions.Length;
        GameObject nextObject = levelOptions[nextIndex];

        Vector3 startPosition = currentObject.transform.localPosition;
        Vector3 endPosition = startPosition + new Vector3(distance, 0, 0);
        Vector3 startScale = currentObject.transform.localScale;
        Vector3 endScale = Vector3.zero; // Scale down to zero to simulate fading away
        float elapsedTime = 0f;
        float duration = 1f; // Duration of the slide animation

        // Fade out the current object
        while (elapsedTime < duration)
        {
            currentObject.transform.localPosition = Vector3.Lerp(startPosition, endPosition, (elapsedTime / duration));
            currentObject.transform.localScale = Vector3.Lerp(startScale, endScale, (elapsedTime / duration));
            elapsedTime += Time.deltaTime;
            yield return null;
        }

        currentObject.transform.localPosition = endPosition;
        currentObject.SetActive(false);

        // Prepare the next object to slide in
        nextObject.transform.localPosition = startPosition - new Vector3(distance, 0, 0);
        nextObject.transform.localScale = Vector3.zero; // Start the next object at scale zero for fading in
        nextObject.SetActive(true);

        elapsedTime = 0f;
        Vector3 endScaleNext = Vector3.one; // Scale to one to simulate fading in
        startPosition = nextObject.transform.localPosition;
        endPosition = startPosition + new Vector3(distance, 0, 0);

        // Fade in the next object
        while (elapsedTime < duration)
        {
            nextObject.transform.localPosition = Vector3.Lerp(startPosition, endPosition, (elapsedTime / duration));
            nextObject.transform.localScale = Vector3.Lerp(Vector3.zero, endScaleNext, (elapsedTime / duration));
            elapsedTime += Time.deltaTime;
            yield return null;
        }

        nextObject.transform.localPosition = endPosition;
        nextObject.transform.localScale = endScaleNext;

        this.currentIndex = nextIndex;
    }

    void ResetPosition(GameObject arrow, Vector3 originalPosition)
    {
        arrow.transform.localPosition = originalPosition;
    }
}
