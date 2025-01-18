using System.Collections;
using UnityEngine;

public class RevealAndHide : MonoBehaviour
{
    public GameObject leftArrowGameObject;
    public GameObject rightArrowGameObject;
    public GameObject extraGameObject;
    public float leftArrowRevealTime = 2f;
    public float leftArrowHideTime = 2f;
    public float rightArrowRevealTime = 2f;
    public float rightArrowHideTime = 2f;
    public float extraRevealTime = 2f;      // Reveal time for extra object
    public float extraHideTime = 2f;        // Hide time for extra object
    public float extraAppearDelay = 1f;     // Time delay for extra object

    private Coroutine leftArrowCoroutine;
    private Coroutine rightArrowCoroutine;
    private Coroutine extraCoroutine;

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.LeftArrow))
        {
            if (leftArrowCoroutine != null)
                StopCoroutine(leftArrowCoroutine);
            leftArrowCoroutine = StartCoroutine(RevealAndHideCoroutine(leftArrowGameObject, leftArrowRevealTime, leftArrowHideTime));
        }
        if (Input.GetKeyDown(KeyCode.RightArrow))
        {
            if (rightArrowCoroutine != null)
                StopCoroutine(rightArrowCoroutine);
            rightArrowCoroutine = StartCoroutine(RevealAndHideCoroutine(rightArrowGameObject, rightArrowRevealTime, rightArrowHideTime));
        }
        if (Input.GetKeyDown(KeyCode.Return))
        {
            if (extraCoroutine != null)
                StopCoroutine(extraCoroutine);
            extraCoroutine = StartCoroutine(RevealAndHideExtraCoroutine(extraGameObject, extraRevealTime, extraHideTime, extraAppearDelay));
        }
    }

    private IEnumerator RevealAndHideCoroutine(GameObject obj, float revealTime, float hideTime)
    {
        obj.SetActive(true);
        yield return new WaitForSeconds(revealTime);
        obj.SetActive(false);
        yield return new WaitForSeconds(hideTime);
        obj.SetActive(false);  // Ensure the final state is hidden
    }

    private IEnumerator RevealAndHideExtraCoroutine(GameObject obj, float revealTime, float hideTime, float appearDelay)
    {
        yield return new WaitForSeconds(appearDelay);
        obj.SetActive(true);
        yield return new WaitForSeconds(revealTime);
        obj.SetActive(false);
        yield return new WaitForSeconds(hideTime);
        obj.SetActive(false);  // Ensure the final state is hidden
    }
}
