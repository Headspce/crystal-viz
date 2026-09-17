using UnityEngine;
using UnityEngine.UI;

public class buttonTest : MonoBehaviour
{
    public Button leftArrowButton;
    public Button rightArrowButton;

    void Start()
    {
        // Assign button click listeners
        if (leftArrowButton != null)
        {
            leftArrowButton.onClick.AddListener(OnLeftArrowClick);
            Debug.Log("Left Arrow Button listener assigned");
        }
        else
        {
            Debug.LogError("Left Arrow Button not assigned in Inspector");
        }

        if (rightArrowButton != null)
        {
            rightArrowButton.onClick.AddListener(OnRightArrowClick);
            Debug.Log("Right Arrow Button listener assigned");
        }
        else
        {
            Debug.LogError("Right Arrow Button not assigned in Inspector");
        }
    }

    void OnLeftArrowClick()
    {
        Debug.Log("Left Arrow Clicked");
        // Quit game for testing
        Application.Quit();
    }

    void OnRightArrowClick()
    {
        Debug.Log("Right Arrow Clicked");
        // Quit game for testing
        Application.Quit();
    }
}
