#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;
using static Proselyte.Persistence.PersistentIdLogger;
using static Proselyte.Persistence.PersistentIdManager;

namespace Proselyte.Persistence
{
    // Data Layer - Scriptable Singleton
    [System.Serializable]
    [InitializeOnLoad]
    [FilePath(PERSISTENCE_PROJECT_SETTINGS_PATH + "/" + PROJECT_SETTINGS_ASSET_FILE_NAME, FilePathAttribute.Location.ProjectFolder)]
    public class PersistentIdProjectSettings : ScriptableSingleton<PersistentIdProjectSettings>
    {
        [SerializeField]
        public PersistentIdRegistrySO registry;

        [SerializeField]
        public PersistentIdLogger.LogSeverity logSeverity = PersistentIdLogger.LogSeverity.Warning;

        void OnEnable()
        {
            Debug.Log("On Enable Persistent Id Project Settings. [Creating Path]");
            string fullPath = Path.Combine(Application.dataPath, "../" + PERSISTENCE_PROJECT_SETTINGS_PATH);
            if(!Directory.Exists(fullPath))
            {
                LogDebug("On Enable Project Settings Creating missing directory at: " + fullPath);
                Directory.CreateDirectory(fullPath);
            }
        }

        public void ApplyLoggingSettings()
        {
            PersistentIdLogger.MinimumSeverity = logSeverity;
        }

        public static bool RehydrateRegistryReference()
        {
            LogDebug("Rehydrating Project Settings data");
            var settings = PersistentIdProjectSettings.instance;
            settings.ApplyLoggingSettings();

            if(settings.registry == null)
            {
                // Try to find the registry asset by GUID
                string registryPath = EditorPrefs.GetString(DEFAULT_REGISTRY_GUID);
                if(string.IsNullOrEmpty(registryPath)) return false;
                
                var registryAsset = AssetDatabase.LoadAssetAtPath<PersistentIdRegistrySO>(registryPath);

                if(registryAsset != null)
                {
                    settings.registry = registryAsset;
                    settings.Save(true); // Save the registry reference back to the project settings asset
                    LogDebug("Registry reference restored after editor reload.");
                    return true;
                }
            }

            return true;
        }

        public void SaveRegistryRef()
        {
            LogDebug("Saving PersistentIdProjectSettings registry.");

            // Ensure directory exists
            string directory = System.IO.Path.GetDirectoryName(GetFilePath());
            if(!System.IO.Directory.Exists(directory))
            {
                System.IO.Directory.CreateDirectory(directory);
            }

            LogDebug($"Saving to path: {GetFilePath()}");
            Save(true);

            // Verify the file exists after save
            if(System.IO.File.Exists(GetFilePath()))
            {
                LogDebug("File saved successfully!");
            }
            else
            {
                LogError("File was not saved!");
            }
        }
    }
}

#endif //UNITY_EDITOR
