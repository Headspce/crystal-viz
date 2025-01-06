using UnityEngine;

public class TitleSelection : MonoBehaviour
{
    public GameObject startScreen;
    public GameObject quitScreen;

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.UpArrow))
        {
            startScreen.SetActive(true);
            quitScreen.SetActive(false);
        }
        else if (Input.GetKeyDown(KeyCode.DownArrow))
        {
            startScreen.SetActive(false);
            quitScreen.SetActive(true);
        }
    }
}
