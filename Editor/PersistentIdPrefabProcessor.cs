#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public class PersistentIdPrefabProcessor : AssetPostprocessor
{
    //static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets,
    //    string[] movedAssets, string[] movedFromAssetPaths)
    //{
    //    foreach(var path in importedAssets)
    //    {
    //        var obj = AssetDatabase.LoadAssetAtPath<GameObject>(path);
    //        if(obj != null && PrefabUtility.IsPartOfPrefabAsset(obj))
    //        {
    //            foreach(var comp in obj.GetComponentsInChildren<MonoBehaviour>(true))
    //            {
    //                if(comp != null)
    //                {
    //                    var so = new SerializedObject(comp);
    //                    PersistentIdManager.ForceClearIdsFromPrefabAsset(so);
    //                }
    //            }
    //        }
    //    }
    //}
}
#endif
