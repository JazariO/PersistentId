using UnityEditor;
using UnityEngine;

public class TestBlankComponent : MonoBehaviour
{
    [SerializeField] string Name;
    [SerializeField] string Name2;
}

[CustomEditor(typeof(TestBlankComponent))]
public class TestBlankComponentEditor : Editor
{
    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();

        TestBlankComponent tbc = (TestBlankComponent)target;

        if(GUILayout.Button("Detect Prefab State"))
        {
            if(PrefabUtility.IsPartOfPrefabInstance(tbc.gameObject))
            {
                Debug.Log($"{tbc.gameObject.name} Is Part of Prefab Instance");
            }

            if(PrefabUtility.IsPartOfNonAssetPrefabInstance(tbc.gameObject))
            {
                Debug.Log($"{tbc.gameObject.name} Is Part of Non-Asset Prefab Instance");
            }
        }
    }
}
