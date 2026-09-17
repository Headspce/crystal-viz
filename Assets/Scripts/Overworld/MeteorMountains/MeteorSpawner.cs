using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MeteorSpawner : MonoBehaviour
{
    public GameObject meteorPrefab; // Prefab of the meteor
    public float spawnRadius = 10f; // Radius within which meteors will spawn
    public float spawnAngle = 45f;  // Angle within which meteors will spawn
    public float spawnRate = 2f;    // Time between spawns
    public Gradient colorGradient;  // Gradient for randomizing meteor colors
    public float minDestructionTime = 3f; // Minimum time before destruction
    public float maxDestructionTime = 5f; // Maximum time before destruction

    [Header("SlideArrows Settings")]
    public SlideArrows slideArrowsScript; // Reference to the SlideArrows script
    public int triggerIndex; // Index that triggers meteor spawning

    [Header("Spawn Settings")]
    public Transform spawnLocationObject; // Reference to the GameObject for the spawn location

    private void Start()
    {
        InvokeRepeating("SpawnMeteor", 0f, spawnRate);
    }

    private void SpawnMeteor()
    {
        if (slideArrowsScript != null && slideArrowsScript.CurrentIndex == triggerIndex && spawnLocationObject != null)
        {
            // Randomize the spawn position within the radius and angle
            float angle = Random.Range(-spawnAngle, spawnAngle);
            Vector3 spawnPosition = Quaternion.Euler(0, angle, 0) * Vector3.forward * Random.Range(0, spawnRadius);
            spawnPosition += spawnLocationObject.position; // Use the position of the specified GameObject

            // Instantiate the meteor
            GameObject meteor = Instantiate(meteorPrefab, spawnPosition, Quaternion.identity);

            // Randomize the meteor color
            MeshRenderer renderer = meteor.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.material.color = colorGradient.Evaluate(Random.value);
            }

            // Set the destruction time
            Meteor meteorScript = meteor.GetComponent<Meteor>();
            if (meteorScript != null)
            {
                meteorScript.minDestructionTime = minDestructionTime;
                meteorScript.maxDestructionTime = maxDestructionTime;
            }
        }
    }
}
