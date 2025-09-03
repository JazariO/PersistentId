#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using System.Linq;
using System.IO;

namespace Proselyte.PersistentIdSystem
{
    public static class PersistentIdManager
    {
        private static PersistentIdRegistrySO registry;
        private const string REGISTRY_SEARCH_FILTER = "t:" + nameof(PersistentIdRegistrySO);
        private const string REGISTRY_PATH = "Assets/PersistentIdRegistry.asset";

        // NOTE(Jazz): Tracks components by instanceId that contain PersistentIds, keeps a hashset of all unique PersistentId values.
        // Requires clearing on scene open/close in editor.
        private static TrackedComponentIds trackedComponentIds;
        private const string TRACKED_COMPONENT_IDS_JSON_PATH = "ProjectSettings/Packages/com.proselyte/";
        private const string TRACKED_COMPONENT_IDS_JSON_FILE_NAME = "PersistentIdSettings.json";
        
        // NOTE(Jazz): This acts purely as a speed boost to component processing as an early-out guard
        // to avoid reprocessing components. Will be cleared with each domain reload.
        private static HashSet<int> processedComponentsThisDomainCycle = new HashSet<int>();

        [MenuItem("Tools/Persistent Id/Print Processed Component Ids", false, 0)]
        public static void PrintProcessedComponentIds()
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine($"==== Processed Component Instance Ids [{processedComponentsThisDomainCycle.Count}] =====");
            foreach(var componentInstanceID in processedComponentsThisDomainCycle)
            {
                if(!trackedComponentIds.trackedComponentIds.ContainsKey(componentInstanceID)) continue;
                sb.Append("\nInstanceID: " + componentInstanceID.ToString() + " ");

                int count = 0;
                foreach(var persistentId in trackedComponentIds.trackedComponentIds[componentInstanceID])
                {
                    sb.Append($"ID {count:D2}: 0x{persistentId:X8}, ");
                    count++;
                }
            }
            Debug.Log(sb.ToString());
        }

        [MenuItem("Tools/Persistent Id/Remove Processed Components Hashset", false, 0)]
        public static void RemoveProcessedComponentIds()
        {
            processedComponentsThisDomainCycle.Clear();
            Debug.Log("Processed Components This Session Cleared.");
        }

        [InitializeOnLoadMethod]
        private static void Initialize()
        {
            Debug.Log($"[{nameof(PersistentIdManager)}] Initializing()");

            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
            AssemblyReloadEvents.afterAssemblyReload -= OnAfterAssemblyReload;
            AssemblyReloadEvents.afterAssemblyReload += OnAfterAssemblyReload;


            // find or create trackedComponentId settings file from ../ProjectSettings directory
            string fullPath = Path.Combine(Application.dataPath, "../" + TRACKED_COMPONENT_IDS_JSON_PATH, TRACKED_COMPONENT_IDS_JSON_FILE_NAME);
            string directoryPath = Path.Combine(Application.dataPath, "../" + TRACKED_COMPONENT_IDS_JSON_PATH);
            string sessionStateKey = "sessionActive";
            if(!SessionState.GetBool(sessionStateKey, false))
            {
                SessionState.SetBool(sessionStateKey, true);
                Debug.LogWarning($"{nameof(TrackedComponentIds)} not found in project. Creating new {nameof(TrackedComponentIds)} at: " + fullPath);

                if(!Directory.Exists(directoryPath))
                    Directory.CreateDirectory(directoryPath);

                try
                {
                    var wrapper = new TrackedComponentIdsWrapper();
                    string json = JsonUtility.ToJson(wrapper, true);
                    System.IO.File.WriteAllText(fullPath, json);
                    Debug.Log($"Created new JSON file at {fullPath}");
                }
                catch(System.Exception ex)
                {
                    Debug.LogError($"Failed to write to {fullPath}: {ex.Message}");
                }
            }
            ReadTrackedIdDataFromJSON();


            EditorApplication.delayCall += () =>
            {
                InitializeRegistry();
                SubscribeToCallbacks();
            };
        }


        private static void ReadTrackedIdDataFromJSON()
        {
            string fullPath = Path.Combine(Application.dataPath, "../" + TRACKED_COMPONENT_IDS_JSON_PATH, TRACKED_COMPONENT_IDS_JSON_FILE_NAME);
            string directoryPath = Path.Combine(Application.dataPath, "../" + TRACKED_COMPONENT_IDS_JSON_PATH);

            if(System.IO.File.Exists(fullPath))
            {
                Debug.Log("Tracked Components JSON found during Read Op!");

                try
                {
                    string json = System.IO.File.ReadAllText(fullPath);
                    var wrapper = JsonUtility.FromJson<TrackedComponentIdsWrapper>(json);

                    if(wrapper != null)
                    {
                        trackedComponentIds ??= new TrackedComponentIds();

                        foreach(var entry in wrapper.trackedComponentEntries)
                        {
                            trackedComponentIds.trackedComponentIds[entry.instanceId] = new HashSet<uint>(entry.persistentIds);
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"Failed to deserialize wrapper. Creating empty dictionary.");
                        trackedComponentIds.trackedComponentIds.Clear();
                    }
                }
                catch(System.Exception ex)
                {
                    Debug.LogError($"Error reading from {TRACKED_COMPONENT_IDS_JSON_FILE_NAME}: {ex.Message}");
                    trackedComponentIds.trackedComponentIds.Clear();
                }
            }
            else
            {
                Debug.LogWarning($"{nameof(TrackedComponentIds)} not found in project. Creating new {nameof(TrackedComponentIds)} at: " + fullPath);

                if(!Directory.Exists(directoryPath))
                    Directory.CreateDirectory(directoryPath);

                try
                {
                    var wrapper = new TrackedComponentIdsWrapper();
                    string json = JsonUtility.ToJson(wrapper, true);
                    System.IO.File.WriteAllText(fullPath, json);
                    Debug.Log($"Created new JSON file at {fullPath}");
                }
                catch(System.Exception ex)
                {
                    Debug.LogError($"Failed to write to {fullPath}: {ex.Message}");
                }
            }
        }

        private static void InitializeRegistry()
        {
            if(registry != null) return;

            var guids = AssetDatabase.FindAssets(REGISTRY_SEARCH_FILTER);
            if(guids.Length > 0)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[0]);
                registry = AssetDatabase.LoadAssetAtPath<PersistentIdRegistrySO>(path);

                if(guids.Length > 1)
                {
                    Debug.LogWarning($"Multiple {nameof(PersistentIdRegistrySO)} assets found. Using: {path}");
                }
            }

            if(registry == null)
            {
                Debug.LogError($"{nameof(PersistentIdRegistrySO)} not found in project. Creating new {nameof(PersistentIdRegistrySO)} at: " + REGISTRY_PATH);

                registry = ScriptableObject.CreateInstance<PersistentIdRegistrySO>();

                var directory = System.IO.Path.GetDirectoryName(REGISTRY_PATH);
                if(!AssetDatabase.IsValidFolder(directory))
                {
                    AssetDatabase.CreateFolder("Assets", System.IO.Path.GetFileName(directory));
                }

                AssetDatabase.CreateAsset(registry, REGISTRY_PATH);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
        }

        private static void SubscribeToCallbacks()
        {
            ObjectChangeEvents.changesPublished -= OnObjectChangesPublished;
            ObjectChangeEvents.changesPublished += OnObjectChangesPublished;
            Undo.undoRedoPerformed -= OnUndoRedoPerformed;
            Undo.undoRedoPerformed += OnUndoRedoPerformed;
            Undo.postprocessModifications -= OnPostProcessModifications;
            Undo.postprocessModifications += OnPostProcessModifications;
            EditorSceneManager.sceneOpened -= OnSceneOpened;
            EditorSceneManager.sceneOpened += OnSceneOpened;
            EditorSceneManager.sceneClosing -= OnSceneClosing;
            EditorSceneManager.sceneClosing += OnSceneClosing;
        }

        private static void OnBeforeAssemblyReload()
        {
            Debug.Log("Before Domain Reload.");

            // NOTE(Jazz): We can't trust that previously processed components have been updated correctly,
            // since the user could make a change to a script, like adding another PersistentId field.
            // In that case we'd need to re-process each component to generate a unqiue id for any new fields.


            // Write the tracked components ids to ../ProjectSettings and overwrite its value with the current trackedComponentsIds dictionary
            { 
                string fullPath = Path.Combine(Application.dataPath, "../" + TRACKED_COMPONENT_IDS_JSON_PATH, TRACKED_COMPONENT_IDS_JSON_FILE_NAME);
                Debug.Log("Writing tracked component ids data to JSON file at: " + fullPath);

                // create settings json file if it doesn't exist
                trackedComponentIds ??= new TrackedComponentIds();

                try
                {
                    var wrapper = new TrackedComponentIdsWrapper();
                    foreach(var kvp in trackedComponentIds.trackedComponentIds)
                    {
                        wrapper.trackedComponentEntries.Add(new TrackedComponentEntry
                        {
                            instanceId = kvp.Key,
                            persistentIds = new List<uint>(kvp.Value)
                        });
                    }

                    string json = JsonUtility.ToJson(wrapper, true);
                    System.IO.File.WriteAllText(fullPath, json);
                    Debug.Log($"Updated JSON file at {fullPath}");
                }
                catch(System.Exception ex)
                {
                    Debug.LogError($"Error writing to {TRACKED_COMPONENT_IDS_JSON_FILE_NAME}: {ex.Message}");
                }
            }

            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine("---- Tracked Component Ids Before Domain Reload ----");
            foreach(var trackedComponent in trackedComponentIds.trackedComponentIds)
            {
                sb.Append($"Instance Id: {trackedComponent.Key.ToString()}, {nameof(PersistentId)}: ");
                foreach(var persistentId in trackedComponent.Value)
                {
                    sb.Append($"0x{persistentId:X8}, ");
                }
                sb.Append("\n");
            }
            Debug.Log(sb.ToString());
        }

        [MenuItem("Tools/PersistentId/Print Tracked Component Ids")]
        private static void PrintTrackedComponentIds()
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine("---- Tracked Component Ids ----");
            foreach(var trackedComponent in trackedComponentIds.trackedComponentIds)
            {
                sb.Append($"Instance Id: {trackedComponent.ToString()}, ");
                foreach(var persistentId in trackedComponent.Value)
                {
                    sb.Append($"{persistentId.ToString()}, ");
                }
                sb.Append("\n");
            }
            Debug.Log(sb.ToString());
        }

        private static void OnAfterAssemblyReload()
        {
            Debug.Log("After Domain Reload: performing ");

            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine("---- Tracked Component Ids After Domain Reload ----");
            foreach(var trackedComponent in trackedComponentIds.trackedComponentIds)
            {
                sb.Append($"Instance Id: {trackedComponent.Key.ToString()}, {nameof(PersistentId)}: ");
                foreach(var persistentId in trackedComponent.Value)
                {
                    sb.Append($"0x{persistentId:X8}, ");
                }
                sb.Append("\n");
            }
            Debug.Log(sb.ToString());
        }

        public static void OnSceneOpened(Scene scene, OpenSceneMode mode)
        {
            Debug.Log("Editor Scene Opened.");
            foreach(var root in scene.GetRootGameObjects()) // NOTE(Jazz): Big heavy scan, but only on scene opened in editor.
            {
                var components = root.GetComponentsInChildren<MonoBehaviour>(true);
                foreach(var comp in components)
                {
                    if(comp == null) continue;

                    var componentInstanceId = comp.GetInstanceID();
                    TrackComponentIds(comp);

                    // Register existing IDs
                    var so = new SerializedObject(comp);
                    var iterator = so.GetIterator();

                    while(iterator.NextVisible(true))
                    {
                        if(!(iterator.propertyType == SerializedPropertyType.Generic && 
                                iterator.type == nameof(PersistentId))) continue; // NAND
                            
                        var idProp = iterator.FindPropertyRelative(nameof(PersistentId.id));
                        if(!(idProp != null && idProp.uintValue != 0)) continue; // NAND
                            
                        if(!IsIdRegistered(idProp.uintValue))
                            RegisterId(idProp.uintValue);
                    }

                    processedComponentsThisDomainCycle.Add(componentInstanceId);
                }
            }

            Debug.Log($"Rehydrated {nameof(PersistentId)} tracking for scene '{scene.name}'");
        }

        public static void OnSceneClosing(Scene scene, bool removingScene)
        {
            Debug.Log("Editor Scene Closing.");
            var component_ids_to_remove = new List<int>();
            foreach(var root in scene.GetRootGameObjects()) // NOTE(Jazz): Another big heavy scan, but only on scene close in editor
            {
                var components = root.GetComponentsInChildren<MonoBehaviour>(true);
                foreach(var comp in components)
                {
                    if(comp == null) continue;

                    var componentInstanceId = comp.GetInstanceID();
                    component_ids_to_remove.Add(componentInstanceId);
                }
            }

            for(int componentIndex = component_ids_to_remove.Count - 1;
                componentIndex >= 0;
                componentIndex--)
            {
                var curr_comp = component_ids_to_remove[componentIndex];
                if(trackedComponentIds.trackedComponentIds.ContainsKey(curr_comp))
                {
                    trackedComponentIds.trackedComponentIds.Remove(curr_comp);
                }
            }
        }

        private static UndoPropertyModification[] OnPostProcessModifications(UndoPropertyModification[] modifications)
        {
            Debug.Log("Making a modification.");
            for(int i = 0; i < modifications.Length; i++)
            {
                var mod = modifications[i];

                if(mod.currentValue.target is MonoBehaviour component &&
                    PrefabUtility.IsPartOfPrefabInstance(component))
                {
                    var path = mod.currentValue.propertyPath;

                    if(path.EndsWith($".{nameof(PersistentId.id)}"))
                    {
                        var so = new SerializedObject(component);
                        var fieldName = path.Substring(0, path.Length - $".{nameof(PersistentId.id)}".Length);
                        var idProp = so.FindProperty(fieldName)?.FindPropertyRelative(nameof(PersistentId.id));

                        if(idProp != null &&
                            uint.TryParse(mod.currentValue.value, out var newValue) &&
                            newValue == 0 &&
                            uint.TryParse(mod.previousValue.value, out var previousId) &&
                            previousId != 0)
                        {
                            idProp.uintValue = previousId;
                            so.ApplyModifiedProperties();
                            EditorUtility.SetDirty(component);
                            modifications[i].keepPrefabOverride = true;

                            if(!IsIdRegistered(idProp.uintValue))
                                RegisterId(idProp.uintValue);

                            Debug.Log($"Restored reverted {nameof(PersistentId)} from previousValue: 0x{previousId:X8} on '{component.name}'");
                        }
                    }
                }
            }

            return modifications;
        }

        public static bool RegisterId(uint id)
        {
            return registry != null && registry.RegisterId(id);
        }

        public static bool UnregisterId(uint id)
        {
            return registry != null && registry.UnregisterId(id);
        }

        public static bool IsIdRegistered(uint id)
        {
            return registry != null && registry.IsIdRegistered(id);
        }

        public static uint GenerateUniqueId()
        {
            if(registry == null)
            {
                Debug.LogError("Registry not found, generating invalid id.");
                return 0;
            }
            return registry.GenerateUniqueId();
        }

        private static void OnObjectChangesPublished(ref ObjectChangeEventStream stream)
        {
            for(int eventIndex = 0; eventIndex < stream.length; eventIndex++)
            {
                var eventType = stream.GetEventType(eventIndex);

                switch(eventType)
                {
                    case ObjectChangeKind.CreateGameObjectHierarchy:
                    {
                        Debug.Log("ObjectChangeKind.CreateGameObjectHierarchy");
                        stream.GetCreateGameObjectHierarchyEvent(eventIndex, out var createEvent);
                        var obj = EditorUtility.InstanceIDToObject(createEvent.instanceId);
                        if(obj is GameObject go)
                        {
                            foreach(var comp in go.GetComponents<MonoBehaviour>())
                            {
                                if(comp != null)
                                {
                                    var componentInstanceId = comp.GetInstanceID();
                                    if(!processedComponentsThisDomainCycle.Contains(componentInstanceId))
                                    {
                                        ProcessComponentForPersistentIds(comp);
                                    }
                                }
                            }
                        }
                        else if(obj is Component comp && comp is MonoBehaviour monoBehaviour)
                        {
                            var componentInstanceId = monoBehaviour.GetInstanceID();
                            if(!processedComponentsThisDomainCycle.Contains(componentInstanceId))
                            {
                                ProcessComponentForPersistentIds(monoBehaviour);
                            }
                        }
                    }
                    break;

                    case ObjectChangeKind.DestroyGameObjectHierarchy:
                    {
                        Debug.Log("ObjectChangeKind.DestroyGameObjectHierarchy");
                        stream.GetDestroyGameObjectHierarchyEvent(eventIndex, out var destroyEvent);
                        Debug.Log($"[HandleGameObjectDestruction] Destroying instanceId: {destroyEvent.instanceId}");

                        // Gather all component instance IDs that need cleanup
                        HashSet<int> componentIdsToCleanup = new();

                        // Cross examine tracked components to determine which component instance IDs are dangling
                        foreach(int componentInstanceId in trackedComponentIds.trackedComponentIds.Keys)
                        {
                            UnityEngine.Object trackedObj = EditorUtility.InstanceIDToObject(componentInstanceId);
                            if(trackedObj == null)
                            {
                                componentIdsToCleanup.Add(componentInstanceId);
                            }
                        }

                        // Clean up all related tracking data
                        var allTrackedIds = new HashSet<uint>();
                        foreach(var componentInstanceId in componentIdsToCleanup)
                        {
                            if(trackedComponentIds.trackedComponentIds.TryGetValue(componentInstanceId, out var trackedIds))
                            {
                                foreach(var id in trackedIds)
                                {
                                    allTrackedIds.Add(id);
                                }
                                trackedComponentIds.trackedComponentIds.Remove(componentInstanceId);
                                Debug.Log($"Removed componentInstanceId {componentInstanceId} from tracking");
                            }
                            processedComponentsThisDomainCycle.Remove(componentInstanceId);
                        }

                        Debug.Log($"Found {allTrackedIds.Count} tracked IDs from {componentIdsToCleanup.Count} components to clean up");

                        // Handle ID unregistration
                        foreach(var id in allTrackedIds)
                        {
                            bool idFoundElsewhere = IsIdCurrentlyInUseInScene(id);
                            if(!idFoundElsewhere && IsIdRegistered(id))
                            {
                                UnregisterId(id);
                                Debug.Log($"Unregistered {nameof(PersistentId)} 0x{id:X8} due to component destruction");
                            }
                            else if(idFoundElsewhere)
                            {
                                Debug.Log($"Preserved {nameof(PersistentId)} 0x{id:X8} - still in use by another component");
                            }
                        }
                    }
                    break;

                    case ObjectChangeKind.ChangeGameObjectOrComponentProperties:
                    {
                        Debug.Log("ObjectChangeKind.ChangeGameObjectOrComponentProperties");
                        stream.GetChangeGameObjectOrComponentPropertiesEvent(eventIndex, out var changeEvent);
                        var obj = EditorUtility.InstanceIDToObject(changeEvent.instanceId);

                        if(obj == null)
                        {
                            // Component was likely removed
                            CleanupComponentInstanceId(changeEvent.instanceId);
                        }
                        else
                        {
                            if(obj is MonoBehaviour comp)
                            {
#region Detect Prefab Asset Editor
                                StageHandle mainStage = StageUtility.GetMainStageHandle();
                                StageHandle currentStage = StageUtility.GetStageHandle(comp.gameObject);
                                if(currentStage != mainStage)
                                {
                                    PrefabStage prefabStage = PrefabStageUtility.GetPrefabStage(comp.gameObject);
                                    if(prefabStage != null)
                                    {
                                        break;
                                    }
                                }
#endregion
                                var componentInstanceId = comp.GetInstanceID();

                                // Check if this component had tracked IDs before this change event
                                bool hadTrackedIds = trackedComponentIds.trackedComponentIds.ContainsKey(componentInstanceId);
                                var previouslyTrackedIds = hadTrackedIds ? new HashSet<uint>(trackedComponentIds.trackedComponentIds[componentInstanceId]) : new HashSet<uint>();

                                // Check current state of the component
                                var currentIds = new HashSet<uint>();
                                CollectIdsFromComponent(comp, currentIds);

                                // Detect if persistent IDs were reverted to 0 (lost tracked IDs but component still exists)
                                bool lostTrackedIds = hadTrackedIds && currentIds.Count == 0 && previouslyTrackedIds.Count > 0;

                                if(lostTrackedIds)
                                {
                                    Debug.Log($"Detected revert operation on {comp.name} - restoring {previouslyTrackedIds.Count} persistent IDs");

                                    // This looks like a revert operation - restore the tracked IDs
                                    SerializedObject so = new SerializedObject(comp);
                                    SerializedProperty iterator = so.GetIterator();
                                    bool hasChanges = false;
                                    int idIndex = 0;
                                    var orderedIds = previouslyTrackedIds.ToList(); // Convert to list for ordered access

                                    while(iterator.NextVisible(true) && idIndex < orderedIds.Count)
                                    {
                                        if(iterator.propertyType == SerializedPropertyType.Generic &&
                                            iterator.type == nameof(PersistentId))
                                        {
                                            var idProp = iterator.FindPropertyRelative(nameof(PersistentId.id));
                                            if(idProp != null && idProp.uintValue == 0)
                                            {
                                                uint restoredId = orderedIds[idIndex];
                                                idProp.uintValue = restoredId;
                                                hasChanges = true;
                                                idIndex++;

                                                if(!IsIdRegistered(restoredId))
                                                {
                                                    RegisterId(restoredId);
                                                }
                                            }
                                        }
                                    }

                                    if(hasChanges)
                                    {
                                        so.ApplyModifiedProperties();

                                        // Critical: Re-establish as prefab override if this is a prefab instance
                                        if(PrefabUtility.IsPartOfPrefabInstance(comp))
                                        {
                                            PrefabUtility.RecordPrefabInstancePropertyModifications(comp);
                                            Debug.Log($"Re-established prefab instance overrides for persistent IDs on {comp.name}");
                                        }

                                        // Restore tracking
                                        trackedComponentIds.trackedComponentIds[componentInstanceId] = previouslyTrackedIds;
                                        processedComponentsThisDomainCycle.Add(componentInstanceId);

                                        EditorUtility.SetDirty(comp);
                                    }
                                }
                                else if(!processedComponentsThisDomainCycle.Contains(componentInstanceId)) // NOTE(Jazz): Are we already guarding against processed components in ProcessComponentForPersistentIds() ...?
                                {
                                    // Normal processing path
                                    ProcessComponentForPersistentIds(comp);
                                }
                            }
                            else if(obj is GameObject go)
                            {
                                foreach(var component in go.GetComponents<MonoBehaviour>())
                                {
                                    if(component != null)
                                    {
                                        var componentInstanceId = component.GetInstanceID();
                                        if(!processedComponentsThisDomainCycle.Contains(componentInstanceId))
                                        {
                                            ProcessComponentForPersistentIds(component);
                                        }
                                    }
                                }
                            }
                        }
                    }
                    break;

                    case ObjectChangeKind.ChangeGameObjectStructure:
                    {
                        Debug.Log("ObjectChangeKind.ChangeGameObjectStructure");
                        stream.GetChangeGameObjectStructureEvent(eventIndex, out var structureEvent);
                        UnityEngine.Object obj = EditorUtility.InstanceIDToObject(structureEvent.instanceId);

                        if(obj is GameObject go)
                        {
                            // Process all components on this GameObject
                            foreach(var comp in go.GetComponents<MonoBehaviour>())
                            {
                                if(comp != null)
                                {
                                    var componentInstanceId = comp.GetInstanceID();
                                    ProcessComponentForPersistentIds(comp);
                                }
                            }

                            // Clean up any orphaned component tracking that might result from structure changes
                            var orphanedComponentIds = new List<int>();
                            foreach(var componentInstanceId in trackedComponentIds.trackedComponentIds.Keys.ToList())
                            {
                                var trackedObj = EditorUtility.InstanceIDToObject(componentInstanceId);
                                if(trackedObj == null)
                                {
                                    orphanedComponentIds.Add(componentInstanceId);
                                }
                            }

                            foreach(var orphanedId in orphanedComponentIds)
                            {
                                CleanupComponentInstanceId(orphanedId);
                            }

                            registry?.ValidateRegistry();
                        }
                    }
                    break;

                    case ObjectChangeKind.ChangeGameObjectStructureHierarchy:
                    {
                        Debug.Log("ObjectChangeKind.ChangeGameObjectStructureHierarchy");
                        stream.GetChangeGameObjectStructureHierarchyEvent(eventIndex, out var structureEvent);
                        var obj = EditorUtility.InstanceIDToObject(structureEvent.instanceId);

                        if(obj is GameObject go)
                        {
                            foreach(var comp in go.GetComponents<MonoBehaviour>())
                            {
                                if(comp != null)
                                {
                                    var componentInstanceId = comp.GetInstanceID();
                                    if(!processedComponentsThisDomainCycle.Contains(componentInstanceId))
                                    {
                                        ProcessComponentForPersistentIds(comp);
                                    }
                                }
                            }
                        }
                    }
                    break;

                    case ObjectChangeKind.CreateAssetObject:
                    {
                        Debug.Log("ObjectChangeKind.CreateAssetObject");
                        stream.GetCreateAssetObjectEvent(eventIndex, out var createEvent);
                        UnityEngine.Object obj = EditorUtility.InstanceIDToObject(createEvent.instanceId);

                        if(obj is GameObject prefab && PrefabUtility.IsPartOfPrefabAsset(prefab))
                        {
                            foreach(var comp in prefab.GetComponentsInChildren<MonoBehaviour>(true))
                            {
                                if(comp == null) continue;
                                var so = new SerializedObject(comp);

                                var prop = so.FindProperty(nameof(PersistentId));
                                if(prop != null && prop.uintValue != 0)
                                {
                                    UnregisterId(prop.uintValue);
                                }

                                ClearIdsFromPrefabAsset(so);
                                EditorUtility.SetDirty(comp);
                            }

                            string path = AssetDatabase.GetAssetPath(prefab);
                            AssetDatabase.SaveAssets();
                            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

                            Debug.Log($"[PersistentIdManager] Cleared and unregistered PersistentIds from new prefab asset: {prefab.name}");
                        }
                    }
                    break;

                    case ObjectChangeKind.UpdatePrefabInstances:
                    {
                        Debug.Log("ObjectChangeKind.UpdatePrefabInstances");
                        bool hasProcessedPrefabAsset = false;
                        stream.GetUpdatePrefabInstancesEvent(eventIndex, out var updatePrefabInstancesEvent);

                        // Preserve the existing tracking data with deterministic order
                        var preservedTracking = new Dictionary<int, List<uint>>();

                        // First pass: collect IDs in serialized property order
                        foreach(int instanceId in updatePrefabInstancesEvent.instanceIds)
                        {
                            GameObject prefabInstance = EditorUtility.InstanceIDToObject(instanceId) as GameObject;
                            if(prefabInstance == null) continue;

                            foreach(var comp in prefabInstance.GetComponentsInChildren<MonoBehaviour>())
                            {
                                if(comp == null) continue;

                                int componentInstanceId = comp.GetInstanceID();
                                if(!trackedComponentIds.trackedComponentIds.ContainsKey(componentInstanceId)) continue;

                                var orderedIds = new List<uint>();
                                SerializedObject so = new SerializedObject(comp);
                                SerializedProperty iterator = so.GetIterator();

                                while(iterator.NextVisible(true))
                                {
                                    if(iterator.propertyType == SerializedPropertyType.Generic &&
                                        iterator.type == nameof(PersistentId))
                                    {
                                        var idProp = iterator.FindPropertyRelative(nameof(PersistentId.id));
                                        if(idProp != null && idProp.uintValue != 0)
                                        {
                                            orderedIds.Add(idProp.uintValue);
                                        }
                                    }
                                }

                                if(orderedIds.Count > 0)
                                {
                                    preservedTracking[componentInstanceId] = orderedIds;
                                }
                            }
                        }

                        foreach(int instanceId in updatePrefabInstancesEvent.instanceIds)
                        {
                            GameObject prefabInstance = EditorUtility.InstanceIDToObject(instanceId) as GameObject;
                            if(prefabInstance == null) continue;

                            // Process prefab asset clearing (first instance only)
                            if(!hasProcessedPrefabAsset)
                            {
                                MonoBehaviour firstComp = prefabInstance.GetComponentInChildren<MonoBehaviour>();
                                if(firstComp != null)
                                {
                                    MonoBehaviour prefabAssetComponent = PrefabUtility.GetCorrespondingObjectFromOriginalSource(firstComp);
                                    if(prefabAssetComponent != null)
                                    {
                                        string assetPath = AssetDatabase.GetAssetPath(prefabAssetComponent);
                                        if(!string.IsNullOrEmpty(assetPath))
                                        {
                                            GameObject prefabRoot = PrefabUtility.LoadPrefabContents(assetPath);
                                            try
                                            {
                                                foreach(var assetComp in prefabRoot.GetComponentsInChildren<MonoBehaviour>(true))
                                                {
                                                    if(assetComp == null) continue;
                                                    ClearIdsFromPrefabAsset(new SerializedObject(assetComp));
                                                }
                                                PrefabUtility.SaveAsPrefabAsset(prefabRoot, assetPath);
                                            }
                                            finally
                                            {
                                                PrefabUtility.UnloadPrefabContents(prefabRoot);
                                            }
                                            hasProcessedPrefabAsset = true;
                                        }
                                    }
                                }
                            }

                            // Now restore IDs to instances using preserved tracking data
                            foreach(var comp in prefabInstance.GetComponentsInChildren<MonoBehaviour>())
                            {
                                if(comp == null) continue;

                                int componentInstanceId = comp.GetInstanceID();

                                // Check if this component had tracked IDs before asset processing
                                if(preservedTracking.TryGetValue(componentInstanceId, out List<uint> preservedIds))
                                {
                                    SerializedObject so = new SerializedObject(comp);
                                    SerializedProperty iterator = so.GetIterator();
                                    bool hasChanges = false;

                                    // Collect current IDs from the component
                                    var currentIds = new HashSet<uint>();
                                    CollectIdsFromComponent(comp, currentIds);

                                    // If the component lost its IDs, restore them from preserved tracking
                                    if(currentIds.Count == 0 && preservedIds.Count > 0)
                                    {
                                        // Restore the preserved IDs back to component properties in order
                                        int idIndex = 0;

                                        while(iterator.NextVisible(true))
                                        {
                                            if(iterator.propertyType == SerializedPropertyType.Generic &&
                                                iterator.type == nameof(PersistentId))
                                            {
                                                var idProp = iterator.FindPropertyRelative(nameof(PersistentId.id));
                                                if(idProp != null && idProp.uintValue == 0 && idIndex < preservedIds.Count)
                                                {
                                                    uint preservedId = preservedIds[idIndex];
                                                    idProp.uintValue = preservedId;
                                                    hasChanges = true;
                                                    idIndex++;

                                                    if(!IsIdRegistered(preservedId))
                                                    {
                                                        RegisterId(preservedId);
                                                    }
                                                }
                                            }
                                        }

                                        if(hasChanges)
                                        {
                                            so.ApplyModifiedProperties();
                                            // Mark as prefab instance override
                                            PrefabUtility.RecordPrefabInstancePropertyModifications(comp);

                                            // Restore tracking (convert back to HashSet for existing system)
                                            trackedComponentIds.trackedComponentIds[componentInstanceId] = new HashSet<uint>(preservedIds);
                                            processedComponentsThisDomainCycle.Add(componentInstanceId);

                                            Debug.Log($"Restored {preservedIds.Count} {nameof(PersistentId)}s in order as overrides on {comp.name}");
                                        }
                                    }
                                }
                            }
                        }
                    }
                    break;
                }
            }

            registry.ValidateRegistry();
        }

        private static void TrackComponentIds(MonoBehaviour component)
        {
            if(component == null) return;

            var componentInstanceId = component.GetInstanceID();
            var idsForComponent = new HashSet<uint>();

            CollectIdsFromComponent(component, idsForComponent);

            if(idsForComponent.Count > 0)
            {
                trackedComponentIds.trackedComponentIds[componentInstanceId] = idsForComponent;
            }
            else
            {
                trackedComponentIds.trackedComponentIds.Remove(componentInstanceId);
            }
        }

        /// <summary>
        /// Populates input idCollection with only non-zero persistent ids found on the input MonoBehaviour component.
        /// </summary>
        private static void CollectIdsFromComponent(MonoBehaviour component, HashSet<uint> idCollection)
        {
            SerializedObject so = new SerializedObject(component);
            SerializedProperty iterator = so.GetIterator();

            while(iterator.NextVisible(true))
            {
                if(iterator.propertyType == SerializedPropertyType.Generic &&
                    iterator.type == nameof(PersistentId))
                {
                    SerializedProperty idProp = iterator.FindPropertyRelative(nameof(PersistentId.id));
                    if(idProp != null && idProp.propertyType == SerializedPropertyType.Integer)
                    {
                        uint id = idProp.uintValue;
                        if(id != 0)
                        {
                            idCollection.Add(id);
                        }
                    }
                }
            }
        }

        private static void CollectAllIdsFromAllComponents(HashSet<uint> idCollection)
        {
            for(int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if(!scene.isLoaded) continue;

                var rootGos = scene.GetRootGameObjects();
                foreach(var root in rootGos)
                {
                    var components = root.GetComponentsInChildren<MonoBehaviour>(true);
                    foreach(var comp in components)
                    {
                        if(comp != null)
                        {
                            CollectIdsFromComponent(comp, idCollection);
                        }
                    }
                }
            }
        }

        private static bool IsIdCurrentlyInUseInScene(uint targetId)
        {
            for(int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if(!scene.isLoaded) continue;

                var rootGos = scene.GetRootGameObjects();
                foreach(var root in rootGos)
                {
                    var components = root.GetComponentsInChildren<MonoBehaviour>(true);
                    foreach(var comp in components)
                    {
                        if(comp != null && CheckComponentForId(comp, targetId))
                        {
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        private static bool CheckComponentForId(MonoBehaviour component, uint targetId)
        {
            var so = new SerializedObject(component);
            var iterator = so.GetIterator();

            while(iterator.NextVisible(true))
            {
                if(iterator.propertyType == SerializedPropertyType.Generic &&
                    iterator.type == nameof(PersistentId))
                {
                    var idProp = iterator.FindPropertyRelative(nameof(PersistentId.id));
                    if(idProp != null && idProp.propertyType == SerializedPropertyType.Integer)
                    {
                        uint id = idProp.uintValue;
                        if(id == targetId)
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private static void CleanupComponentInstanceId(int componentInstanceId)
        {
            if(trackedComponentIds.trackedComponentIds.TryGetValue(componentInstanceId, out var ids))
            {
                foreach(var id in ids)
                {
                    if(!IsIdCurrentlyInUseInScene(id) && IsIdRegistered(id))
                    {
                        UnregisterId(id);
                        Debug.Log($"Unregistered {nameof(PersistentId)} 0x{id:X8} due to component removal (componentInstanceId: {componentInstanceId})");
                    }
                }
                trackedComponentIds.trackedComponentIds.Remove(componentInstanceId);
            }
            processedComponentsThisDomainCycle.Remove(componentInstanceId);
        }

        private static void ProcessComponentForPersistentIds(MonoBehaviour component)
        {
            if(component == null) return;

            var componentInstanceId = component.GetInstanceID();
            if(processedComponentsThisDomainCycle.Contains(componentInstanceId)) return;

            var so = new SerializedObject(component);

            if(PrefabUtility.IsPartOfPrefabAsset(component))
            {
                ClearIdsFromPrefabAsset(so);
            }
            else
            {
                Undo.RecordObject(so.targetObject, $"Assign {nameof(PersistentId)} Values");

                var idsToRegister = new HashSet<uint>();
                var idsToUnregister = new HashSet<uint>();
                bool hasChanges = false;

                var iterator = so.GetIterator();
                while(iterator.NextVisible(true))
                {
                    if(iterator.propertyType == SerializedPropertyType.Generic &&
                        iterator.type == nameof(PersistentId))
                    {
                        var idProp = iterator.FindPropertyRelative(nameof(PersistentId.id));
                        if(idProp != null && idProp.propertyType == SerializedPropertyType.Integer)
                        {
                            processedComponentsThisDomainCycle.Add(componentInstanceId);
                            uint currentPropPersistentId = idProp.uintValue;

                            if(currentPropPersistentId == 0)
                            {
                                uint newId = GenerateUniqueId();
                                idProp.uintValue = newId;
                                idsToRegister.Add(newId);
                                hasChanges = true;

                                UpdateComponentTracking(componentInstanceId, 0, newId);
                                Debug.Log($"Generated {nameof(PersistentId)}: 0x{newId:X8} for {so.targetObject.name}.{iterator.name}");
                            }
                            else
                            {
                                if(IsIdRegistered(currentPropPersistentId))
                                {
                                    bool isLegitimateOwner = false;

                                    if(trackedComponentIds.trackedComponentIds.TryGetValue(componentInstanceId, out var idsForComponent) &&
                                       idsForComponent.Contains(currentPropPersistentId))
                                    {
                                        isLegitimateOwner = true;
                                    }
                                    else
                                    {
                                        bool foundElsewhere = false;
                                        foreach(var kvp in trackedComponentIds.trackedComponentIds)
                                        {
                                            if(kvp.Key != componentInstanceId && kvp.Value.Contains(currentPropPersistentId))
                                            {
                                                foundElsewhere = true;
                                                break;
                                            }
                                        }

                                        if(!foundElsewhere)
                                        {
                                            isLegitimateOwner = true;
                                        }
                                    }

                                    if(isLegitimateOwner)
                                    {
                                        idsToRegister.Add(currentPropPersistentId);
                                        UpdateComponentTracking(componentInstanceId, 0, currentPropPersistentId);
                                    }
                                    else
                                    {
                                        // ID conflict detected: this component is a duplicate
                                        uint newId = GenerateUniqueId();
                                        if(newId != 0)
                                        {
                                            idProp.uintValue = newId;
                                            idsToRegister.Add(newId);
                                            hasChanges = true;

                                            Debug.Log($"Detected duplicate {nameof(PersistentId)} 0x{currentPropPersistentId:X8} on '{so.targetObject.name}'. Generated new {nameof(PersistentId)}: 0x{newId:X8}");

                                            UpdateComponentTracking(componentInstanceId, currentPropPersistentId, newId);
                                        }
                                    }
                                }
                                else
                                {
                                    // Not registered yet — safe to register as-is
                                    idsToRegister.Add(currentPropPersistentId);
                                    UpdateComponentTracking(componentInstanceId, 0, currentPropPersistentId);
                                    Debug.Log($"Registering existing {nameof(PersistentId)}: 0x{currentPropPersistentId:X8} for {so.targetObject.name}.{iterator.name}");
                                }
                            }
                        }
                    }
                }

                if(hasChanges)
                {
                    so.ApplyModifiedProperties();
                    EditorUtility.SetDirty(so.targetObject);
                }

                foreach(var id in idsToUnregister)
                {
                    UnregisterId(id);
                }

                foreach(var id in idsToRegister)
                {
                    RegisterId(id);
                }

                // Update tracking after processing
                TrackComponentIds(component);
            }
        }

        private static void ClearIdsFromPrefabAsset(SerializedObject so)
        {
            Undo.RecordObject(so.targetObject, $"Clear {nameof(PersistentId)} Values");

            var iterator = so.GetIterator();
            bool hasChanges = false;

            while(iterator.NextVisible(true))
            {
                if(iterator.propertyType == SerializedPropertyType.Generic &&
                    iterator.type == nameof(PersistentId))
                {
                    var idProp = iterator.FindPropertyRelative(nameof(PersistentId.id));
                    if(idProp != null && idProp.propertyType == SerializedPropertyType.Integer && idProp.uintValue != 0)
                    {
                        uint oldId = idProp.uintValue;
                        idProp.uintValue = 0;
                        hasChanges = true;
                        UnregisterId(oldId);

                        if(so.targetObject is MonoBehaviour comp)
                        {
                            UpdateComponentTracking(comp.GetInstanceID(), oldId, 0);
                        }
                    }
                }
            }

            if(hasChanges)
            {
                so.ApplyModifiedProperties();
            }
        }

        private static void OnUndoRedoPerformed()
        {
            Debug.Log("OnUndoRedoPerformed");
            var orphanedIds = new HashSet<uint>();
            var keysToRemove = new List<int>();
            var trackingUpdates = new Dictionary<int, HashSet<uint>>();

            foreach(var kvp in trackedComponentIds.trackedComponentIds)
            {
                int componentInstanceId = kvp.Key;
                var trackedIds = kvp.Value;
                var obj = EditorUtility.InstanceIDToObject(componentInstanceId);

                if(obj == null)
                {
                    foreach(var id in trackedIds)
                    {
                        if(!IsIdCurrentlyInUseInScene(id))
                        {
                            orphanedIds.Add(id);
                        }
                    }
                    keysToRemove.Add(componentInstanceId);
                }
                else if(obj is MonoBehaviour comp)
                {
                    var currentIds = new HashSet<uint>();
                    CollectIdsFromComponent(comp, currentIds);

                    foreach(var trackedId in trackedIds)
                    {
                        if(!currentIds.Contains(trackedId) && IsIdRegistered(trackedId))
                        {
                            if(!IsIdCurrentlyInUseInScene(trackedId))
                            {
                                orphanedIds.Add(trackedId);
                            }
                        }
                    }

                    foreach(var currentId in currentIds)
                    {
                        if(!IsIdRegistered(currentId))
                        {
                            RegisterId(currentId);
                            Debug.Log($"Re-registered {nameof(PersistentId)} after undo: 0x{currentId:X8} for {obj.name}");
                        }
                    }

                    trackingUpdates[componentInstanceId] = new HashSet<uint>(currentIds);
                }
            }

            foreach(var update in trackingUpdates)
            {
                trackedComponentIds.trackedComponentIds[update.Key] = update.Value;
            }

            foreach(var key in keysToRemove)
            {
                trackedComponentIds.trackedComponentIds.Remove(key);
                processedComponentsThisDomainCycle.Remove(key);
            }

            foreach(var orphanedId in orphanedIds)
            {
                if(IsIdRegistered(orphanedId))
                {
                    UnregisterId(orphanedId);
                    Debug.Log($"Removed orphaned {nameof(PersistentId)} from registry: 0x{orphanedId:X8}");
                }
            }

            registry.ValidateRegistry();
        }

        public static void RegenerateId(SerializedProperty persistentIdProperty)
        {
            var target = persistentIdProperty.serializedObject.targetObject;
            Undo.RecordObject(target, $"Regenerate {nameof(PersistentId)} Values");

            var idProp = persistentIdProperty.FindPropertyRelative(nameof(PersistentId.id));
            if(idProp != null && idProp.propertyType == SerializedPropertyType.Integer)
            {
                uint oldId = idProp.uintValue;
                uint newId = GenerateUniqueId();

                if(oldId != 0)
                {
                    UnregisterId(oldId);
                }

                if(newId != 0)
                {
                    idProp.uintValue = newId;
                    persistentIdProperty.serializedObject.ApplyModifiedProperties();

                    if(target is MonoBehaviour comp)
                    {
                        UpdateComponentTracking(comp.GetInstanceID(), oldId, newId);
                    }

                    RegisterId(newId);
                    Debug.Log($"Regenerated {nameof(PersistentId)}: 0x{newId:X8} for {target.name}");
                }
            }

            registry?.ValidateRegistry();
        }

        private static void UpdateComponentTracking(int componentInstanceId, uint oldId, uint newId)
        {
            if(!trackedComponentIds.trackedComponentIds.TryGetValue(componentInstanceId, out var ids))
            {
                ids = new HashSet<uint>();
                trackedComponentIds.trackedComponentIds[componentInstanceId] = ids;
            }

            if(oldId != 0) ids.Remove(oldId);
            if(newId != 0) ids.Add(newId);

            if(ids.Count == 0)
            {
                trackedComponentIds.trackedComponentIds.Remove(componentInstanceId);
            }
        }
    }
#endif
}