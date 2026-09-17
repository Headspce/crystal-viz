using UnityEngine;
using System.Collections.Generic;

public class RockSpawner : MonoBehaviour
{
    public List<GameObject> objectPrefabs; // List of prefabs to spawn
    public List<Vector3> spawnPositions; // List of positions for each prefab
    public int maxObjects = 100; // Maximum number of objects that can spawn
    public Vector2 sizeRange = new Vector2(0.5f, 1.5f); // Min and max size of the objects
    public Vector2 rotationRange = new Vector2(0f, 360f); // Min and max rotation angle for the objects

    void Start()
    {
        SpawnObjects();
    }

    void SpawnObjects()
    {
        if (objectPrefabs.Count != spawnPositions.Count)
        {
            Debug.LogError("The number of prefabs and spawn positions must be the same.");
            return;
        }

        int objectsSpawned = 0;

        for (int i = 0; i < objectPrefabs.Count; i++)
        {
            if (objectsSpawned >= maxObjects)
                break;

            // Get the position for the current prefab
            Vector3 spawnPosition = spawnPositions[i];

            // Instantiate object at the specified position
            GameObject obj = Instantiate(objectPrefabs[i], spawnPosition, Quaternion.identity);

            // Randomize size of the object within sizeRange
            float randomSize = Random.Range(sizeRange.x, sizeRange.y);
            obj.transform.localScale = new Vector3(randomSize, randomSize, randomSize);

            // Randomize rotation of the object within rotationRange
            float randomRotationX = Random.Range(rotationRange.x, rotationRange.y);
            float randomRotationY = Random.Range(rotationRange.x, rotationRange.y);
            float randomRotationZ = Random.Range(rotationRange.x, rotationRange.y);
            obj.transform.rotation = Quaternion.Euler(randomRotationX, randomRotationY, randomRotationZ);

            objectsSpawned++;
        }
    }

    // Draw the box area in the scene view for visualization
    void OnDrawGizmos()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawWireCube(transform.position, new Vector3(maxObjects, maxObjects, maxObjects));
    }
}
