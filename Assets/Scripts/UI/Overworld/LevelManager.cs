using UnityEngine;

public class LevelManager : MonoBehaviour
{
    public GameObject[] levels; // Array of level GameObjects
    public GameObject duckPrefab; // Prefab of the duck
    private GameObject currentDuck; // Instance of the duck
    private int currentLevel = 0;
    private int currentBoardSpace = 0;
    private bool isLocked = true; // Initially, the hidden board space is locked

    void Start()
    {
        ShowCurrentBoardSpace();
    }

    void ShowCurrentBoardSpace()
    {
        // Ensure all levels and spaces are initially inactive
        foreach (GameObject level in levels)
        {
            foreach (Transform child in level.transform)
            {
                child.gameObject.SetActive(false);
            }
        }

        // Activate the current level and board space
        GameObject currentLevelObject = levels[currentLevel];
        currentLevelObject.transform.GetChild(currentBoardSpace).gameObject.SetActive(true);

        // Spawn the duck on the current board space
        if (currentDuck != null)
        {
            Destroy(currentDuck);
        }
        Vector3 duckPosition = currentLevelObject.transform.GetChild(currentBoardSpace).position + new Vector3(0, 1, 0); // Adjust the position as needed
        currentDuck = Instantiate(duckPrefab, duckPosition, Quaternion.identity);
    }

    public void NextBoardSpace()
    {
        if (currentBoardSpace < 2 || (!isLocked && currentBoardSpace == 2))
        {
            currentBoardSpace++;
            ShowCurrentBoardSpace();
        }
        else if (currentBoardSpace == 2 && isLocked)
        {
            Debug.Log("This board space is locked. Complete the first level to unlock it.");
        }
        else if (currentBoardSpace == 3 && currentLevel < levels.Length - 1)
        {
            currentBoardSpace = 0;
            currentLevel++;
            ShowCurrentBoardSpace();
        }
    }

    public void UnlockHiddenSpace()
    {
        isLocked = false;
        ShowCurrentBoardSpace();
    }
}
