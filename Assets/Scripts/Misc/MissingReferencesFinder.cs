using UnityEditor;
using UnityEngine;

public class MissingReferencesFinder : EditorWindow
{
    [MenuItem("Window/Find Missing References")]
    public static void ShowWindow()
    {
        GetWindow<MissingReferencesFinder>("Missing References Finder");
    }

    private void OnGUI()
    {
        if (GUILayout.Button("Find Missing References in Scene"))
        {
            FindMissingReferencesInScene();
        }

        if (GUILayout.Button("Find Missing References in Assets"))
        {
            FindMissingReferencesInAssets();
        }
    }

    private static void FindMissingReferencesInScene()
    {
        GameObject[] sceneObjects = GameObject.FindObjectsOfType<GameObject>();
        foreach (GameObject obj in sceneObjects)
        {
            Component[] components = obj.GetComponents<Component>();
            foreach (Component component in components)
            {
                SerializedObject so = new SerializedObject(component);
                SerializedProperty sp = so.GetIterator();
                while (sp.NextVisible(true))
                {
                    if (sp.propertyType == SerializedPropertyType.ObjectReference && sp.objectReferenceValue == null && sp.objectReferenceInstanceIDValue != 0)
                    {
                        Debug.LogWarning("Missing reference found in " + obj.name + " (" + component.GetType().Name + ")", obj);
                    }
                }
            }
        }
    }

    private static void FindMissingReferencesInAssets()
    {
        string[] allAssetPaths = AssetDatabase.GetAllAssetPaths();
        foreach (string path in allAssetPaths)
        {
            GameObject obj = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (obj != null)
            {
                Component[] components = obj.GetComponentsInChildren<Component>(true);
                foreach (Component component in components)
                {
                    SerializedObject so = new SerializedObject(component);
                    SerializedProperty sp = so.GetIterator();
                    while (sp.NextVisible(true))
                    {
                        if (sp.propertyType == SerializedPropertyType.ObjectReference && sp.objectReferenceValue == null && sp.objectReferenceInstanceIDValue != 0)
                        {
                            Debug.LogWarning("Missing reference found in asset: " + path + " (" + component.GetType().Name + ")", obj);
                        }
                    }
                }
            }
        }
    }
}
