using System.Collections;
using UnityEngine;

public class MeteorSpawner : MonoBehaviour
{
    public GameObject meteorPrefab;
    public Vector3 spawnCenter; // Center location in world space
    public float spawnRadius = 5f; // Radius for the spawn area
    public float minSpawnInterval = 1f;
    public float maxSpawnInterval = 3f;
    public SlideArrows slideArrows; // Reference to the SlideArrows script
    public int activeLevelIndex; // Index of the level option to check if active

    void Start()
    {
        StartCoroutine(SpawnMeteorRoutine());
    }

    IEnumerator SpawnMeteorRoutine()
    {
        while (true)
        {
            float interval = Random.Range(minSpawnInterval, maxSpawnInterval);
            yield return new WaitForSeconds(interval);

            // Check if the specified level option is active before spawning
            if (slideArrows.levelOptions[activeLevelIndex].activeSelf)
            {
                Vector3 randomOffset = Random.insideUnitSphere * spawnRadius;
                randomOffset.y = Mathf.Abs(randomOffset.y); // Ensure meteors spawn above the center
                Vector3 spawnPosition = spawnCenter + randomOffset;

                Instantiate(meteorPrefab, spawnPosition, Quaternion.identity);
            }
        }
    }
}
