#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

public class CustomMikeObjectMenu
{
    [MenuItem("GameObject/Create Mike Object", false, 0)]
    private static void MikeObject(MenuCommand menuCommand)
    {
        GameObject go = new GameObject("MikeObject");
        go.AddComponent<ExampleSaveableComponent>();
        Undo.RegisterCreatedObjectUndo(go, "Added Mike Object");

        EditorApplication.delayCall += () =>
        {
            Selection.activeGameObject = go;
        };

        Debug.Log("Created Mike!");
    }
}
#endif