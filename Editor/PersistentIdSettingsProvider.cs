#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Proselyte.PersistentIdSystem
{
    // Data Layer - Scriptable Singleton
    [FilePath("ProjectSettings/Packages/com.proselyte/PersistentIdSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    public class PersistentIdProjectSettings : ScriptableSingleton<PersistentIdProjectSettings>
    {
        [SerializeField]
        public PersistentIdRegistrySO registry;
    }

    // UI Layer - Settings Provider
    public class PersistentIdSettingsProvider : SettingsProvider
    {
        private SerializedObject m_SerializedObject;
        private SerializedProperty m_RegistryProperty;

        public PersistentIdSettingsProvider(string path, SettingsScope scopes)
            : base(path, scopes) { }

        [SettingsProvider]
        public static SettingsProvider CreateProvider()
        {
            var provider = new PersistentIdSettingsProvider("Project/Persistent IDs", SettingsScope.Project);
            provider.keywords = new[] { "persistent", "id", "registry", "serialization" };
            return provider;
        }

        public override void OnActivate(string searchContext, UnityEngine.UIElements.VisualElement rootElement)
        {
            var settings = PersistentIdProjectSettings.instance;
            m_SerializedObject = new SerializedObject(settings);
            m_RegistryProperty = m_SerializedObject.FindProperty(nameof(PersistentIdProjectSettings.registry));
        }

        public override void OnGUI(string searchContext)
        {
            using(var check = new EditorGUI.ChangeCheckScope())
            {
                m_SerializedObject.Update();

                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Persistent ID System Configuration", EditorStyles.boldLabel);
                EditorGUILayout.Space();

                // Registry assignment field
                var previousRegistry = m_RegistryProperty.objectReferenceValue as PersistentIdRegistrySO;
                EditorGUILayout.PropertyField(m_RegistryProperty, new GUIContent("ID Registry",
                    "The Persistent ID Registry asset that will track all IDs in the project"));

                if(check.changed)
                {
                    var newRegistry = m_RegistryProperty.objectReferenceValue as PersistentIdRegistrySO;

                    // Show confirmation dialog for significant changes
                    if(ShouldShowConfirmation(previousRegistry, newRegistry))
                    {
                        if(ShowRegistryChangeConfirmation(previousRegistry, newRegistry))
                        {
                            // Apply the change and reinitialize
                            m_SerializedObject.ApplyModifiedProperties();

                            // Reinitialize the manager with new registry
                            EditorUtility.RequestScriptReload();

                            Debug.Log($"[PersistentIdSettings] Registry changed to: {(newRegistry ? newRegistry.name : "None")}");
                        }
                        else
                        {
                            // User cancelled - revert the property
                            m_RegistryProperty.objectReferenceValue = previousRegistry;
                        }
                    }
                    else
                    {
                        // No confirmation needed - apply directly
                        m_SerializedObject.ApplyModifiedProperties();
                        EditorUtility.RequestScriptReload();
                    }
                }

                EditorGUILayout.Space();
                ShowRegistryInfo();
            }
        }

        private bool ShouldShowConfirmation(PersistentIdRegistrySO oldRegistry, PersistentIdRegistrySO newRegistry)
        {
            // Show confirmation when:
            // 1. Changing from one populated registry to another
            // 2. Removing a registry that has registered IDs

            if(oldRegistry != null && oldRegistry.RegisteredCount > 0)
                return true;

            if(oldRegistry != newRegistry && newRegistry != null)
                return true;

            return false;
        }

        private bool ShowRegistryChangeConfirmation(PersistentIdRegistrySO oldRegistry, PersistentIdRegistrySO newRegistry)
        {
            string message;

            if(oldRegistry == null && newRegistry != null)
            {
                message = "This will initialize the Persistent ID system with the selected registry.";
            }
            else if(oldRegistry != null && newRegistry == null)
            {
                message = $"This will disable the Persistent ID system and stop tracking {oldRegistry.RegisteredCount} registered IDs.\n\n" +
                         "Existing IDs in scenes will remain unchanged but no new IDs will be generated.";
            }
            else if(oldRegistry != newRegistry)
            {
                int oldCount = oldRegistry?.RegisteredCount ?? 0;
                int newCount = newRegistry?.RegisteredCount ?? 0;
                message = $"This will switch from '{oldRegistry?.name ?? "None"}' ({oldCount} IDs) to " +
                         $"'{newRegistry?.name ?? "None"}' ({newCount} IDs).\n\n" +
                         "This may cause ID conflicts or system inconsistencies.";
            }
            else
            {
                return true; // No change
            }

            return EditorUtility.DisplayDialog(
                "Persistent ID Registry Change",
                message + "\n\nDo you want to continue?",
                "Continue", "Cancel");
        }

        private void ShowRegistryInfo()
        {
            var currentRegistry = PersistentIdProjectSettings.instance.registry;

            if(currentRegistry == null)
            {
                EditorGUILayout.HelpBox(
                    "No registry assigned. The Persistent ID system is disabled.\n" +
                    "Create a PersistentIdRegistry asset and assign it above to enable the system.",
                    MessageType.Warning);
            }
        }
    }

    // Extension methods for accessing registry info (add these to PersistentIdRegistrySO)
    public static class RegistryExtensions
    {
        public static int GetRegisteredCount(this PersistentIdRegistrySO registry)
        {
            if(registry == null) return 0;
            // Add public property or method to access registered count
            return 0; // Replace with actual implementation
        }

        public static int GetRegisteredSceneCount(this PersistentIdRegistrySO registry)
        {
            if(registry == null) return 0;
            // Add public property or method to access scene count
            return 0; // Replace with actual implementation
        }
    }
}
#endif