#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using static Proselyte.Persistence.PersistentIdLogger;

namespace Proselyte.Persistence
{
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
            m_SerializedObject.Update();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Persistent ID System Configuration", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            // Registry assignment field
            var previousRegistry = m_RegistryProperty.objectReferenceValue as PersistentIdRegistrySO;
            EditorGUILayout.PropertyField(m_RegistryProperty, new GUIContent("ID Registry", "The Persistent ID Registry asset that will track all IDs in the project"));

            ShowRegistryInfo();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Debugging Configuration", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            // Log severity handled separately to avoid triggering domain reload
            var currentSeverity = PersistentIdProjectSettings.instance.LogSeverity;
            var newSeverity = (PersistentIdLogger.LogSeverity)EditorGUILayout.EnumPopup(
                new GUIContent("Log Severity", "Controls the minimum severity level for Persistent ID system logs"),
                currentSeverity
            );

            if(newSeverity != currentSeverity)
            {
                PersistentIdProjectSettings.instance.LogSeverity = newSeverity;
                PersistentIdProjectSettings.instance.ApplyLoggingSettings();
                LogInfo($"[PersistentIdSettings] Log severity changed to: {newSeverity}");
            }

            EditorGUILayout.Space();

            // Only registry changes are tracked for reload
            using(var check = new EditorGUI.ChangeCheckScope())
            {
                if(check.changed)
                {
                    var newRegistry = m_RegistryProperty.objectReferenceValue as PersistentIdRegistrySO;

                    if(ShouldShowConfirmation(previousRegistry, newRegistry))
                    {
                        if(ShowRegistryChangeConfirmation(previousRegistry, newRegistry))
                        {
                            m_SerializedObject.ApplyModifiedProperties();
                            PersistentIdProjectSettings.instance.SaveRegistryRef();
                            PersistentIdRegistrar.ClearTrackingData();
                            EditorUtility.RequestScriptReload();

                            LogInfo($"[PersistentIdSettings] Registry changed to: {(newRegistry ? newRegistry.name : "None")}");
                        }
                        else
                        {
                            m_RegistryProperty.objectReferenceValue = previousRegistry;
                        }
                    }
                    else
                    {
                        m_SerializedObject.ApplyModifiedProperties();
                        PersistentIdProjectSettings.instance.SaveRegistryRef();
                        EditorUtility.RequestScriptReload();
                    }
                }
            }

            EditorGUILayout.Space();
        }

        private bool ShouldShowConfirmation(PersistentIdRegistrySO oldRegistry, PersistentIdRegistrySO newRegistry)
        {
            return oldRegistry != newRegistry;
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
            } else
            {
                EditorGUILayout.HelpBox(
                    $"Total IDs Registered: {currentRegistry.RegisteredCount}",
                    MessageType.Info);
            }
        }
    }
}
#endif