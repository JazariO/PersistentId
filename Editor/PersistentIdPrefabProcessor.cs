#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public class PersistentIdPrefabProcessor : AssetPostprocessor
{
    void OnPostprocessPrefab(GameObject prefab)
    {
        Debug.Log("Prefab post processing!");
        bool modified = false;

        // Iterate all MonoBehaviours in the prefab
        foreach(var comp in prefab.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if(comp == null) continue;

            var so = new SerializedObject(comp);
            var sp = so.GetIterator();

            while(sp.NextVisible(true))
            {
                if(sp.propertyType == SerializedPropertyType.Generic &&
                    sp.type == nameof(PersistentId))
                {
                    // Found a PersistentId field
                    var idProp = sp.FindPropertyRelative("id");
                    if(idProp != null && idProp.uintValue != 0)
                    {
                        idProp.uintValue = 0; // Clear it
                        so.ApplyModifiedProperties();
                        EditorUtility.SetDirty(comp);
                        modified = true;
                    }
                }
            }
        }

        if(modified)
        {
            Debug.Log($"[PersistentIdPrefabProcessor] Cleared IDs on prefab: {prefab.name}");
        }
    }
}
#endif
