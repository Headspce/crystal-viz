using UnityEngine;
using System.Collections.Generic;

public class CozyCove_CardinalTracker : MonoBehaviour
{
    private Vector3 initialPosition;
    private Vector3 lastPosition;
    private List<string> cardinalDirections = new List<string>();

    void Start()
    {
        initialPosition = transform.position;
        lastPosition = initialPosition;
        Debug.Log("Initial position: " + initialPosition);
    }

    void Update()
    {
        Vector3 currentPosition = transform.position;

        if (currentPosition != lastPosition)
        {
            string direction = GetCardinalDirection(lastPosition, currentPosition);
            if (!string.IsNullOrEmpty(direction))
            {
                cardinalDirections.Add(direction);
                Debug.Log("Moved " + direction + " to position: " + currentPosition);
            }
            lastPosition = currentPosition;
        }
    }

    string GetCardinalDirection(Vector3 from, Vector3 to)
    {
        Vector3 direction = (to - from).normalized;

        if (Vector3.Dot(direction, Vector3.forward) > 0.5f)
        {
            return "North";
        }
        else if (Vector3.Dot(direction, Vector3.back) > 0.5f)
        {
            return "South";
        }
        else if (Vector3.Dot(direction, Vector3.right) > 0.5f)
        {
            return "East";
        }
        else if (Vector3.Dot(direction, Vector3.left) > 0.5f)
        {
            return "West";
        }
        return null;
    }

    public List<string> GetCardinalDirections()
    {
        return new List<string>(cardinalDirections);
    }
}
