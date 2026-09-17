#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;
using UnityEngine.SceneManagement;

public class TitleSelection : MonoBehaviour
{
    public GameObject startScreen;
    public GameObject quitScreen;
    public string LevelName;

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.UpArrow))
        {
            startScreen.SetActive(true);
            quitScreen.SetActive(false);
            Debug.Log("Start screen selected");
        }
        else if (Input.GetKeyDown(KeyCode.DownArrow))
        {
            startScreen.SetActive(false);
            quitScreen.SetActive(true);
            Debug.Log("Quit screen selected");
        }

        // Check if the Enter key is pressed
        if (Input.GetKeyDown(KeyCode.Return))
        {
            if (startScreen.activeSelf)
            {
                // Load the new level when startScreen is active and Enter is pressed
                Debug.Log("Loading new level: " + LevelName);
                SceneManager.LoadScene(LevelName);
            }
            else if (quitScreen.activeSelf)
            {
                // Quit the application when quitScreen is active and Enter is pressed
                Debug.Log("Quitting application");
#if UNITY_EDITOR
                EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
            }
        }
    }
}
