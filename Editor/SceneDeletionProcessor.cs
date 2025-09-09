#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using static Proselyte.PersistentIdSystem.PersistentIdLogger;

namespace Proselyte.PersistentIdSystem
{
    /// <summary>
    /// Handles removing any registered persistent ids that correspond 
    /// to a scene deleted while the editor is open.
    /// </summary>
    internal class SceneDeletionProcessor : AssetPostprocessor
    {
        static PersistentIdRegistrySO Registry => PersistentIdProjectSettings.instance.registry;

        static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets,
            string[] movedAssets, string[] movedFromAssetPaths)
        {
            if(Registry == null)
            {
                PersistentIdLogger.LogWarning("Missing Persistent Id Registry scriptable object on scene deletion. " +
                    "Scene ids may be out of sync.");
                return;
            }

            foreach(var asset in importedAssets )
            {
                if(asset.EndsWith(".unity"))
                {
                    LogDebug("scene file processed: " + asset);
                }
            }

            // Remove scenes from registry asset which are about to be deleted
            foreach(string deletedAsset in deletedAssets)
            {
                if(deletedAsset.EndsWith(".unity")) // Scene file
                {
                    string sceneGuid = AssetDatabase.AssetPathToGUID(deletedAsset);
                    if(!string.IsNullOrEmpty(sceneGuid))
                    {
                        Registry.RemoveScene(sceneGuid);
                        LogDebug("Removed scene file which was located at : " + deletedAsset);
                    }
                }
            }
        }
    }
}
#endif
