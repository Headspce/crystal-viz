using UnityEngine;

public class randomDotMovement : MonoBehaviour
{
    public GameObject fishPrefab;
    public int fishCount = 100;
    public float areaSize = 10f;
    public float innerRadius = 2f; // Inner radius to prevent spawning inside this area
    public float speed = 2f;
    private GameObject[] fishArray;

    void Start()
    {
        fishArray = new GameObject[fishCount];
        for (int i = 0; i < fishCount; i++)
        {
            Vector3 position = GetRandomPosition();
            fishArray[i] = Instantiate(fishPrefab, position, Quaternion.identity);
        }
    }

    void Update()
    {
        foreach (GameObject fish in fishArray)
        {
            Vector3 direction = new Vector3(
                Random.Range(-1f, 1f),
                Random.Range(-1f, 1f),
                Random.Range(-1f, 1f)
            );
            fish.transform.Translate(direction * speed * Time.deltaTime);
        }
    }

    void OnEnable()
    {
        if (fishArray != null)
        {
            foreach (GameObject fish in fishArray)
            {
                fish.SetActive(true);
            }
        }
    }

    void OnDisable()
    {
        if (fishArray != null)
        {
            foreach (GameObject fish in fishArray)
            {
                fish.SetActive(false);
            }
        }
    }

    Vector3 GetRandomPosition()
    {
        Vector3 position;
        do
        {
            position = new Vector3(
                Random.Range(-areaSize, areaSize),
                Random.Range(-areaSize, areaSize),
                Random.Range(-areaSize, areaSize)
            );
        } while (position.magnitude < innerRadius);

        return position;
    }
}
