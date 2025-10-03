#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using System.Linq;
using static Proselyte.Persistence.PersistentIdLogger;
using static Proselyte.Persistence.PersistentIdRegistrySO;

namespace Proselyte.Persistence
{
    /// <summary>
    /// Registrar of persistent IDs.
    /// Ensures ids are correctly registered with the registry and validated to avoid collisions during editor-time.
    /// </summary>
    public static class PersistentIdRegistrar
    {
        private static PersistentIdRegistrySO registry;
        private static PersistentIdRegistrarData registrar_data;

        internal const string PROJECT_SETTINGS_ASSET_FILE_NAME = "PersistentIdSettings.asset";
        internal const string PERSISTENCE_PROJECT_SETTINGS_PATH = "ProjectSettings/Packages/com.proselyte.persistence";

        internal const string INITIALIZED_KEY = "com.proselyte.persistence.initialized";
        internal const string DEFAULT_REGISTRY_GUID = "com.proselyte.persistence.default_registry_guid";
        internal const string DEFAULT_REGISTRY_ASSET_PATH = "Assets/Settings/Persistent ID Registry SO.asset";

        public static void ClearTrackingData()
        {
            // Clear the transient RegistrarData
            registrar_data.ClearAll();

            // Clear session state
            SessionState.SetBool("sessionActive", false);

            LogDebug($"{nameof(PersistentIdRegistrar)} tracking data cleared.");
        }

        [UnityEditor.InitializeOnLoadMethod]
        public static void Initialize()
        {
            LogDebug($"[{nameof(PersistentIdRegistrar)}] Initialize()");
            
            if(Application.isPlaying)
            {
                LogDebug("Editor is in Playmode!");
                UnsubscribeToCallbacks();
                return;
            } else
            {
                LogDebug("Editor is not in play mode!");
            }

            // Set static registrarinstance variable
            registrar_data = PersistentIdRegistrarData.instance;

            // TODO(Jazz): Add better sentinel initialization tracking using package version as key
            if(!EditorPrefs.GetBool(INITIALIZED_KEY, false))
            {
                InitializeOnFirstInstall();
            }
            else
            {
                LogDebug("Package already initialized, skipping first time setup.");
            }

            if(EditorPrefs.GetBool(INITIALIZED_KEY,false))
                PersistentIdProjectSettings.instance.ApplyLoggingSettings();
            
            if(!PersistentIdProjectSettings.RehydrateRegistryReference())
                LogError($"Could not find registry asset at expected path. You may need to " +
                    $"set the registry asset from: Project Settings > {nameof(Persistence)}");

            if(registry == null)
                registry = PersistentIdProjectSettings.instance.registry;

            if(registry == null)
            { 
                LogWarning($"Registry not set in project settings. Skipping further initialization.");
                return;
            }

            System.Text.StringBuilder sb = new();
            sb.AppendLine($"---- Initializing Registry Scenes: {registry.RegisteredSceneCount}----");
            foreach(var scene in registry.sceneDataList)
            {
                sb.AppendLine("Scene found: " + AssetDatabase.GUIDToAssetPath(scene.sceneGuid));
            }
            LogDebug(sb.ToString());

            LogDebug($"[{nameof(PersistentIdRegistrar)}] Initializing()");

            EditorApplication.playModeStateChanged -= OnPlaymodeChanged;
            EditorApplication.playModeStateChanged += OnPlaymodeChanged;

            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
            AssemblyReloadEvents.afterAssemblyReload -= OnAfterAssemblyReload;
            AssemblyReloadEvents.afterAssemblyReload += OnAfterAssemblyReload;

            EditorApplication.delayCall -= OnDelayCallInit;
            EditorApplication.delayCall += OnDelayCallInit;
        }

        private static void OnPlaymodeChanged(PlayModeStateChange state)
        {
            switch(state)
            {
                case PlayModeStateChange.ExitingEditMode:
                    LogDebug("Exiting Edit Mode - Unsubscribing callbacks");
                    UnsubscribeToCallbacks();
                    // Also remove any pending delay calls
                    EditorApplication.delayCall -= OnDelayCallInit;
                    break;

                case PlayModeStateChange.ExitingPlayMode:
                    if(registry != null)
                    {
                        EditorApplication.delayCall -= OnDelayCallInit;
                        EditorApplication.delayCall += OnDelayCallInit;
                    }
                    break;

                case PlayModeStateChange.EnteredEditMode:
                    LogDebug("Entered Edit Mode - Resubscribing callbacks");
                    if(registry != null)
                    {
                        SubscribeToCallbacks();
                    }
                    break;
            }
        }

        private static void InitializeOnFirstInstall()
        {
            // Sentinel indicates this is a fresh install
            LogDebug("Detected fresh package installation. Setting up PersistentIdRegistrySO.");

            // Check for an existing registry
            var existingRegistryGUIDs = AssetDatabase.FindAssets("t:" + nameof(PersistentIdRegistrySO));
            if(existingRegistryGUIDs.Length > 0)
            {
                var existingRegistryPath = AssetDatabase.GUIDToAssetPath(existingRegistryGUIDs[0]);
                var existingRegistry = AssetDatabase.LoadAssetAtPath<PersistentIdRegistrySO>(existingRegistryPath);
                if(existingRegistry != null)
                {
                    EditorPrefs.SetString(DEFAULT_REGISTRY_GUID, existingRegistryGUIDs[0]);
                    return;
                }
            }

            // Create the PersistentIdRegistrySO asset
            PersistentIdRegistrySO registry = AssetDatabase.LoadAssetAtPath<PersistentIdRegistrySO>(DEFAULT_REGISTRY_ASSET_PATH);
            if(registry != null)
            {
                LogDebug("[PersistentIdInitializer] Found existing PersistentIdRegistrySO at " + DEFAULT_REGISTRY_ASSET_PATH);
            }
            else
            {
                // Create a new PersistentIdRegistrySO
                registry = ScriptableObject.CreateInstance<PersistentIdRegistrySO>();
                if(registry == null)
                {
                    LogError("[PersistentIdInitializer] Failed to create PersistentIdRegistrySO. Aborting first time initialization");
                    return;
                }
            }

            // Save the registry asset to the Assets folder
            AssetDatabase.CreateAsset(registry, DEFAULT_REGISTRY_ASSET_PATH);
            AssetDatabase.SaveAssets();
            LogDebug("[PersistentIdInitializer] Created new PersistentIdRegistrySO at " + DEFAULT_REGISTRY_ASSET_PATH);

            // Assign the registry to the settings provider
            if(registry != null && PersistentIdProjectSettings.instance != null)
            {
                var registryGuid = AssetDatabase.GUIDFromAssetPath(DEFAULT_REGISTRY_ASSET_PATH);
                EditorPrefs.SetString(DEFAULT_REGISTRY_GUID, registryGuid.ToString());

                PersistentIdProjectSettings.instance.registry = registry;
                // Save the settings to ensure the assignment persists
                EditorUtility.SetDirty(PersistentIdProjectSettings.instance);
                AssetDatabase.SaveAssets();
                LogDebug("Assigned Persistent Id Registry SO to project settings.");
            }
            else
            {
                LogDebug("Failed to assign PersistentIdRegistrySO. " +
                    "Ensure PersistentIdProjectSettings is properly set up.");
            }

            // Set the sentinel to indicate initialization is complete
            EditorPrefs.SetBool(INITIALIZED_KEY, true);
            LogDebug("Package initialization completed.");
        }

        private static void OnDelayCallInit()
        {
            if(Application.isPlaying)
            {
                LogDebug("DelayCall executed in playmode - skipping initialization");
                return;
            }
            if(registry == null)
            {
                LogWarning($"[{nameof(PersistentIdRegistrar)}] Registry not set. Skipping delay call initialization.");
                return;
            }

            LogDebug("Editor Application Delay Call");
            SubscribeToCallbacks();

            string sessionStateKey = "sessionActive";
            bool sessionWasActive = SessionState.GetBool(sessionStateKey, false);
            bool newSession = !sessionWasActive;
            LogDebug("New Session: " + newSession);
            if(newSession)
            {
                LogDebug("New Session Detected. Setting up first time session variables.");
                SessionState.SetBool(sessionStateKey, true);

                // Clean up orphaned scenes from registry (scenes that no longer exist in project)
                if(registry != null)
                {
                    registry.InitializeRegistry();

                    var scenesToRemove = new List<string>();

                    foreach(var sceneData in registry.sceneDataList)
                    {
                        string scenePath = AssetDatabase.GUIDToAssetPath(sceneData.sceneGuid);

                        if(string.IsNullOrEmpty(scenePath))
                        {
                            scenesToRemove.Add(sceneData.sceneGuid);
                            LogDebug($"Marking deleted scene with GUID {sceneData.sceneGuid} for removal from registry");
                        }
                        else
                        {
                            LogDebug($"Preserving registry data for scene: {scenePath}");
                        }
                    }

                    foreach(string sceneGuid in scenesToRemove)
                    {
                        registry.RemoveScene(sceneGuid);
                        LogDebug($"Removed orphaned scene with GUID {sceneGuid} from registry");
                    }
                }

                // Initialize tracker with all currently open scenes
                for(int sceneIndex = 0; sceneIndex < EditorSceneManager.sceneCount; sceneIndex++)
                {
                    var scene = EditorSceneManager.GetSceneAt(sceneIndex);
                    OnSceneOpened(scene, OpenSceneMode.Additive);
                }
            }

            if(registrar_data.trackedComponentIds == null || registrar_data.trackedComponentIds.Count == 0)
            {
                LogDebug("No tracked components found after loading from RegistrarData.");
                return;
            }

            // NOTE(Jazz): Cannot lay trust in previously processed components being updated correctly,
            // since the user could make a change to a script, such as adding another PersistentId field.
            // In that case, re-process each component to generate a unique id for any new fields,
            // or unregister any missing persistent ids which might be missing from the tracked component.
            //
            // Detect if the tracked component id key has been:
            //          1. Removed via editing scripts
            //          2. Added via editing scripts
            //          3. Duplicated via duplicating scene asset

            // Keep track of found ids, then after processing each component
            // compare these to the tracked component ids to discover removed properties.
            HashSet<uint> foundIds = new();
            HashSet<uint> addedIds = new();
            List<int> componentsToRemove = new();

            // Loop through all tracked objects to reprocess all persistent id values
            foreach(var tracked_comp_kvp in registrar_data.trackedComponentIds)
            {
                var target_component = EditorUtility.InstanceIDToObject(tracked_comp_kvp.Key) as MonoBehaviour;
                if(target_component == null)
                {
                    // Component no longer exists - mark for removal
                    componentsToRemove.Add(tracked_comp_kvp.Key);
                    continue;
                }

                    var so = new SerializedObject(target_component);
                var iterator = so.GetIterator();

                // Clear addedIds for this component
                addedIds.Clear();
                HashSet<uint> currentComponentIds = new(); // Track current component's IDs

                while(iterator.NextVisible(true))
                {
                    if(!(iterator.propertyType == SerializedPropertyType.Generic &&
                         iterator.type == nameof(PersistentId))) continue; // NAND

                    var idProp = iterator.FindPropertyRelative(nameof(PersistentId.id));
                    if(idProp == null) continue;

                    var target_id = idProp.uintValue;

                    // Add current ID to found set
                    if(target_id != 0)
                    {
                        foundIds.Add(target_id);
                        currentComponentIds.Add(target_id);
                    }

                    if(target_id == 0)
                    {
                        // This field has no ID assigned - generate one
                        var uniquePersistentId = GenerateUniqueId();
                        idProp.uintValue = uniquePersistentId;
                        so.ApplyModifiedPropertiesWithoutUndo();
                        addedIds.Add(uniquePersistentId);
                        currentComponentIds.Add(uniquePersistentId);
                        foundIds.Add(uniquePersistentId);
                        registry.RegisterId(target_component.gameObject, uniquePersistentId);
                        LogDebug($"Generated new {nameof(PersistentId)} for field: 0x{uniquePersistentId:X8}");
                    }
                    else if(!tracked_comp_kvp.Value.Contains(target_id))
                    {
                        // This field has an ID but it's not tracked - could be new field or collision
                        if(IsIdRegistered(target_id))
                        {
                            // ID is already registered elsewhere - generate new one to avoid collision
                            var uniquePersistentId = GenerateUniqueId();
                            idProp.uintValue = uniquePersistentId;
                            so.ApplyModifiedPropertiesWithoutUndo();
                            addedIds.Add(uniquePersistentId);
                            currentComponentIds.Remove(target_id); // Remove old
                            currentComponentIds.Add(uniquePersistentId); // Add new
                            foundIds.Remove(target_id); // Remove old from found
                            foundIds.Add(uniquePersistentId); // Add new to found
                            registry.RegisterId(target_component.gameObject, uniquePersistentId);
                            LogDebug($"Resolved conflict for field: 0x{target_id:X8} -> 0x{uniquePersistentId:X8}");
                        }
                        else
                        {
                            // Valid existing ID that needs to be registered and tracked
                            // This is likely a field that existed but wasn't properly tracked after domain reload
                            addedIds.Add(target_id);
                            RegisterId(target_component.gameObject, target_id);
                            LogDebug($"Registered existing {nameof(PersistentId)}: 0x{target_id:X8}");
                        }
                    }
                    else
                    {
                        // ID is already tracked - ensure it's registered
                        if(!IsIdRegistered(target_id))
                        {
                            // Tracked ID exists but isn't registered - register it
                            RegisterId(target_component.gameObject, target_id);
                            LogDebug($"Re-registered tracked {nameof(PersistentId)}: 0x{target_id:X8}");
                        }
                        // If target_id > 0 and is both tracked and registered, do nothing - it's fine as-is
                    }
                }

                // Check for Removed persistent id properties in the current component
                List<uint> idsToRemove = new List<uint>();
                foreach(var trackedPersistentId in tracked_comp_kvp.Value)
                {
                    if(!currentComponentIds.Contains(trackedPersistentId))
                    {
                        // Detected mismatch between tracked id and found id. Possibly a removed property.
                        idsToRemove.Add(trackedPersistentId);
                    }
                }

                // Handle removed properties
                for(int removalIndex = idsToRemove.Count - 1; removalIndex >= 0; removalIndex--)
                {
                    uint curr_id = idsToRemove[removalIndex];
                    registrar_data.trackedComponentIds[tracked_comp_kvp.Key].Remove(curr_id);
                    UnregisterId(curr_id);
                    LogDebug($"Removed deleted {nameof(PersistentId)}: 0x{curr_id:X8}");
                }

                // Track added properties
                foreach(var idToAdd in addedIds)
                {
                    if(!registrar_data.trackedComponentIds.ContainsKey(tracked_comp_kvp.Key))
                        registrar_data.trackedComponentIds[tracked_comp_kvp.Key] = new HashSet<uint>();

                    registrar_data.trackedComponentIds[tracked_comp_kvp.Key].Add(idToAdd);
                }
            }

            // Clean up tracked components that no longer exist
            foreach(var componentId in componentsToRemove)
            {
                if(registrar_data.trackedComponentIds.TryGetValue(componentId, out var ids))
                {
                    foreach(var id in ids)
                    {
                        // Only unregister if not used elsewhere
                        if(!IsIdCurrentlyInUseInScene(id))
                        {
                            UnregisterId(id);
                            LogDebug($"Unregistered orphaned {nameof(PersistentId)}: 0x{id:X8}");
                        }
                    }
                }
                registrar_data.trackedComponentIds.Remove(componentId);
                registrar_data.processedComponentsThisDomainCycle.Remove(componentId);
                LogDebug($"Removed orphaned component tracking for instanceId: {componentId}");
            }

            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine("---- Tracked Component Ids After Delayed Editor Call ----");
            foreach(var trackedComponent in registrar_data.trackedComponentIds)
            {
                sb.Append($"Instance Id: {trackedComponent.Key.ToString()}, {nameof(PersistentId)}: ");
                foreach(var persistentId in trackedComponent.Value)
                {
                    sb.Append($"0x{persistentId:X8}, ");
                }
                sb.Append("\n");
            }
            LogDebug(sb.ToString());

            registry.ValidateRegistry();
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
            EditorSceneManager.sceneSaved -= OnSceneSaved;
            EditorSceneManager.sceneSaved += OnSceneSaved;
        }

        private static void UnsubscribeToCallbacks()
        {
            ObjectChangeEvents.changesPublished -= OnObjectChangesPublished;

            Undo.undoRedoPerformed -= OnUndoRedoPerformed;
            Undo.postprocessModifications -= OnPostProcessModifications;

            EditorSceneManager.sceneOpened -= OnSceneOpened;
            EditorSceneManager.sceneClosing -= OnSceneClosing;
            EditorSceneManager.sceneSaved-= OnSceneSaved;
        }

        private static void OnBeforeAssemblyReload()
        {
            LogDebug("Before Domain Reload.");
            PrintTrackedComponentIds();

            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine("---- Tracked Component Ids Before Domain Reload ----");
            foreach(var trackedComponent in registrar_data.trackedComponentIds)
            {
                sb.Append($"Instance Id: {trackedComponent.Key}, {nameof(PersistentId)}: ");
                foreach(var persistentId in trackedComponent.Value)
                {
                    sb.Append($"0x{persistentId:X8}, ");
                }
                sb.Append("\n");
            }
            LogDebug(sb.ToString());
        }

        private static void OnAfterAssemblyReload()
        {
            LogDebug("After Domain Reload: performing ");
            //var gos = GameObject.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
            //var sb = new System.Text.StringBuilder();
            //foreach(var g in gos)
            //{
            //    sb.AppendLine($"InstanceID Found: {g.GetInstanceID()}");
            //}
            //LogDebug(sb.ToString());

            //PrintTrackedComponentJson("OnAfterAssemblyReload");
        }

        private static void OnObjectChangesPublished(ref ObjectChangeEventStream stream)
        {
            if(registry == null)
            {
                LogWarning($"[{nameof(PersistentIdRegistrar)}] Registry not set. Skipping object change processing.");
                return;
            }

            for(int eventIndex = 0; eventIndex < stream.length; eventIndex++)
            {
                var eventType = stream.GetEventType(eventIndex);

                switch(eventType)
                {
                    case ObjectChangeKind.CreateGameObjectHierarchy:
                    {
                        LogDebug("ObjectChangeKind.CreateGameObjectHierarchy");
                        stream.GetCreateGameObjectHierarchyEvent(eventIndex, out var createEvent);
                        var obj = EditorUtility.InstanceIDToObject(createEvent.instanceId);
                        if(obj is GameObject go)
                        {
                            foreach(var comp in go.GetComponents<MonoBehaviour>())
                            {
                                if(comp != null)
                                {
                                    var componentInstanceId = comp.GetInstanceID();
                                    if(!registrar_data.processedComponentsThisDomainCycle.Contains(componentInstanceId))
                                    {
                                        ProcessComponentForPersistentIds(comp);
                                    }
                                }
                            }
                        }
                        else if(obj is Component comp && comp is MonoBehaviour monoBehaviour)
                        {
                            var componentInstanceId = monoBehaviour.GetInstanceID();
                            if(!registrar_data.processedComponentsThisDomainCycle.Contains(componentInstanceId))
                            {
                                ProcessComponentForPersistentIds(monoBehaviour);
                            }
                        }
                    }
                    break;

                    case ObjectChangeKind.DestroyGameObjectHierarchy:
                    {
                        LogDebug("ObjectChangeKind.DestroyGameObjectHierarchy");
                        stream.GetDestroyGameObjectHierarchyEvent(eventIndex, out var destroyEvent);
                        LogDebug($"[HandleGameObjectDestruction] Destroying instanceId: {destroyEvent.instanceId}");

                        // Gather all component instance IDs that need cleanup
                        HashSet<int> componentIdsToCleanup = new();

                        // Cross examine tracked components to determine which component instance IDs are dangling
                        foreach(int componentInstanceId in registrar_data.trackedComponentIds.Keys)
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
                            if(registrar_data.trackedComponentIds.TryGetValue(componentInstanceId, out var trackedIds))
                            {
                                foreach(var id in trackedIds)
                                {
                                    allTrackedIds.Add(id);
                                }
                                registrar_data.trackedComponentIds.Remove(componentInstanceId);
                                LogDebug($"Removed componentInstanceId {componentInstanceId} from tracking");
                            }
                            registrar_data.processedComponentsThisDomainCycle.Remove(componentInstanceId);
                        }

                        LogDebug($"Found {allTrackedIds.Count} tracked IDs from {componentIdsToCleanup.Count} components to clean up");

                        // Handle ID unregistration
                        foreach(var id in allTrackedIds)
                        {
                            bool idFoundElsewhere = IsIdCurrentlyInUseInScene(id);
                            if(!idFoundElsewhere && IsIdRegistered(id))
                            {
                                UnregisterId(id);
                                LogDebug($"Unregistered {nameof(PersistentId)} 0x{id:X8} due to component destruction");
                            }
                            else if(idFoundElsewhere)
                            {
                                LogDebug($"Preserved {nameof(PersistentId)} 0x{id:X8} - still in use by another component");
                            }
                        }
                    }
                    break;

                    case ObjectChangeKind.ChangeGameObjectOrComponentProperties:
                    {
                        LogDebug("ObjectChangeKind.ChangeGameObjectOrComponentProperties");
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
                                bool hadTrackedIds = registrar_data.trackedComponentIds.ContainsKey(componentInstanceId);
                                var previouslyTrackedIds = hadTrackedIds ? new HashSet<uint>(registrar_data.trackedComponentIds[componentInstanceId]) : new HashSet<uint>();

                                // Check current state of the component
                                var currentIds = new HashSet<uint>();
                                CollectIdsFromComponent(comp, currentIds);

                                // Detect if persistent IDs were reverted to 0 (lost tracked IDs but component still exists)
                                bool lostTrackedIds = hadTrackedIds && currentIds.Count == 0 && previouslyTrackedIds.Count > 0;

                                if(lostTrackedIds)
                                {
                                    LogDebug($"Detected revert operation on {comp.name} - restoring {previouslyTrackedIds.Count} persistent IDs");

                                    // This looks like a revert operation - restore the tracked IDs
                                    SerializedObject so = new SerializedObject(comp);
                                    SerializedProperty iterator = so.GetIterator();
                                    bool hasChanges = false;
                                    int idIndex = 0;
                                    var orderedIds = previouslyTrackedIds.ToList(); // Convert to list for ordered access

                                    while(iterator.NextVisible(true) && idIndex < orderedIds.Count)
                                    {
                                        if(!(iterator.propertyType == SerializedPropertyType.Generic &&
                                            iterator.type == nameof(PersistentId))) continue; // NAND

                                        var idProp = iterator.FindPropertyRelative(nameof(PersistentId.id));
                                        if(!(idProp != null && idProp.uintValue == 0)) continue; // NAND

                                        uint restoredId = orderedIds[idIndex];
                                        idProp.uintValue = restoredId;
                                        hasChanges = true;
                                        idIndex++;

                                        if(!IsIdRegistered(restoredId))
                                        {
                                            RegisterId(comp.gameObject, restoredId);
                                        }
                                    }

                                    if(hasChanges)
                                    {
                                        so.ApplyModifiedPropertiesWithoutUndo();

                                        // Raestore prefab override if this is a prefab instance
                                        if(PrefabUtility.IsPartOfPrefabInstance(comp))
                                        {
                                            PrefabUtility.RecordPrefabInstancePropertyModifications(comp);
                                            LogDebug($"Re-established prefab instance overrides for persistent IDs on {comp.name}");
                                        }

                                        // Restore tracking
                                        registrar_data.trackedComponentIds[componentInstanceId] = previouslyTrackedIds;
                                        registrar_data.processedComponentsThisDomainCycle.Add(componentInstanceId);

                                        EditorUtility.SetDirty(comp);
                                    }
                                }
                                // NOTE(Jazz): Are we already guarding against processed components in ProcessComponentForPersistentIds() ...?
                                else if(!registrar_data.processedComponentsThisDomainCycle.Contains(componentInstanceId)) 
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
                                        if(!registrar_data.processedComponentsThisDomainCycle.Contains(componentInstanceId))
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
                        LogDebug("ObjectChangeKind.ChangeGameObjectStructure");
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
                            foreach(var componentInstanceId in registrar_data.trackedComponentIds.Keys.ToList())
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

                            registry.ValidateRegistry();
                        }
                    }
                    break;

                    case ObjectChangeKind.ChangeGameObjectStructureHierarchy:
                    {
                        LogDebug("ObjectChangeKind.ChangeGameObjectStructureHierarchy");
                        stream.GetChangeGameObjectStructureHierarchyEvent(eventIndex, out var structureEvent);
                        var obj = EditorUtility.InstanceIDToObject(structureEvent.instanceId);

                        if(obj is GameObject go)
                        {
                            foreach(var comp in go.GetComponents<MonoBehaviour>())
                            {
                                if(comp != null)
                                {
                                    var componentInstanceId = comp.GetInstanceID();
                                    if(!registrar_data.processedComponentsThisDomainCycle.Contains(componentInstanceId))
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
                        LogDebug("ObjectChangeKind.CreateAssetObject");
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

                            LogDebug($"{nameof(PersistentIdRegistrar)} Cleared and unregistered PersistentIds from new prefab asset: {prefab.name}");
                        }
                    }
                    break;

                    case ObjectChangeKind.UpdatePrefabInstances:
                    {
                        LogDebug("ObjectChangeKind.UpdatePrefabInstances");
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
                                if(!registrar_data.trackedComponentIds.ContainsKey(componentInstanceId)) continue;

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
                            
                            // Process prefab asset clearing (first instance only)
                            if(prefabInstance == null || hasProcessedPrefabAsset) continue;

                            MonoBehaviour firstComp = prefabInstance.GetComponentInChildren<MonoBehaviour>();
                            if(firstComp == null) continue;
                                
                            MonoBehaviour prefabAssetComponent = PrefabUtility.GetCorrespondingObjectFromOriginalSource(firstComp);
                            if(prefabAssetComponent == null) continue;
                                    
                            string assetPath = AssetDatabase.GetAssetPath(prefabAssetComponent);
                            if(string.IsNullOrEmpty(assetPath)) continue;
                                        
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

                            // Now restore IDs to instances using preserved tracking data
                            foreach(var comp in prefabInstance.GetComponentsInChildren<MonoBehaviour>())
                            {
                                if(comp == null) continue;

                                int componentInstanceId = comp.GetInstanceID();

                                // Check if this component had tracked IDs before asset processing
                                if(!preservedTracking.TryGetValue(componentInstanceId, out List<uint> preservedIds)) 
                                    continue;
                                
                                SerializedObject so = new SerializedObject(comp);
                                SerializedProperty iterator = so.GetIterator();
                                bool hasChanges = false;

                                // Collect current IDs from the component
                                var currentIds = new HashSet<uint>();
                                CollectIdsFromComponent(comp, currentIds);

                                // If the component lost its IDs, restore them from preserved tracking
                                if(!(currentIds.Count == 0 && preservedIds.Count > 0)) continue; // NAND
                                    
                                // Restore the preserved IDs back to component properties in order
                                int idIndex = 0;

                                while(iterator.NextVisible(true))
                                {
                                    if(!(iterator.propertyType == SerializedPropertyType.Generic &&
                                        iterator.type == nameof(PersistentId))) continue; // NAND
                                            
                                    var idProp = iterator.FindPropertyRelative(nameof(PersistentId.id));
                                    if(idProp != null && idProp.uintValue == 0 && idIndex < preservedIds.Count)
                                    {
                                        uint preservedId = preservedIds[idIndex];
                                        idProp.uintValue = preservedId;
                                        hasChanges = true;
                                        idIndex++;

                                        if(!IsIdRegistered(preservedId))
                                        {
                                            RegisterId(comp.gameObject, preservedId);
                                        }
                                    }
                                }

                                if(!hasChanges) continue;

                                so.ApplyModifiedPropertiesWithoutUndo();
                                // Mark as prefab instance override
                                PrefabUtility.RecordPrefabInstancePropertyModifications(comp);

                                // Restore tracking (convert back to HashSet for existing system)
                                registrar_data.trackedComponentIds[componentInstanceId] = new HashSet<uint>(preservedIds);
                                registrar_data.processedComponentsThisDomainCycle.Add(componentInstanceId);

                                LogDebug($"Restored {preservedIds.Count} {nameof(PersistentId)}s in " +
                                    $"order as overrides on {comp.name}");
                            }
                        }
                    }
                    break;
                }
            }

            registry.ValidateRegistry();
        }

        private static UndoPropertyModification[]
        OnPostProcessModifications(UndoPropertyModification[] modifications)
        {
            LogDebug("Post processing undo modifications.");

            // Avoid processing prefab assets
            if(PrefabStageUtility.GetCurrentPrefabStage() != null)
                return modifications;

            for(int i = 0; i < modifications.Length; i++)
            {
                var mod = modifications[i];

                if(mod.currentValue.target is not MonoBehaviour component) continue;

                var scene = component.gameObject.scene;

                var so = new SerializedObject(component);
                var prop = so.FindProperty(mod.previousValue.propertyPath);

                if(!(prop != null &&
                     prop.propertyType == SerializedPropertyType.Generic &&
                     prop.type == nameof(PersistentId))) continue; // NAND

                var idProp = prop.FindPropertyRelative(nameof(PersistentId.id));
                if(idProp == null) continue;

                // Check if undo is trying to zero out an ID
                if(uint.TryParse(mod.currentValue.value, out var newValue) &&
                    newValue == 0)
                {
                    var componentInstanceId = component.GetInstanceID();

                    // Check tracking to see if this component has IDs
                    if(registrar_data.trackedComponentIds.TryGetValue(componentInstanceId, out var trackedIds))
                    {
                        // Try to get the previous value from undo stack
                        uint previousId = 0;
                        uint.TryParse(mod.previousValue.value, out previousId);

                        // If previousId is 0 but we have tracked IDs, find the right one
                        if(previousId == 0 && trackedIds.Count > 0)
                        {
                            // Try to match by property path or use the current property value
                            previousId = idProp.uintValue;

                            if(previousId == 0)
                            {
                                // Find which tracked ID corresponds to this property
                                // Iterate through properties to find index
                                SerializedProperty iterator = so.GetIterator();
                                int persistentIdIndex = 0;
                                int currentIndex = 0;

                                while(iterator.NextVisible(true))
                                {
                                    if(iterator.propertyType == SerializedPropertyType.Generic &&
                                       iterator.type == nameof(PersistentId))
                                    {
                                        if(iterator.propertyPath == prop.propertyPath)
                                        {
                                            persistentIdIndex = currentIndex;
                                            break;
                                        }
                                        currentIndex++;
                                    }
                                }

                                // Use the tracked ID at this index
                                var trackedIdsList = trackedIds.ToList();
                                if(persistentIdIndex < trackedIdsList.Count)
                                {
                                    previousId = trackedIdsList[persistentIdIndex];
                                }
                            }
                        }

                        // Block the zeroing if we found a valid previous ID
                        if(previousId != 0 && trackedIds.Contains(previousId))
                        {
                            mod.currentValue.value = previousId.ToString();
                            modifications[i] = mod;

                            if(PrefabUtility.IsPartOfPrefabInstance(mod.currentValue.target))
                            {
                                modifications[i].keepPrefabOverride = true;
                            }

                            if(!IsIdRegistered(previousId))
                            {
                                RegisterId(component.gameObject, previousId);
                            }

                            LogDebug($"Blocked zeroing of {nameof(PersistentId)}. " +
                                $"Restored tracked ID: 0x{previousId:X8} on '{component.name}'");
                        }
                    }
                }
            }

            return modifications;
        }

        private static void OnUndoRedoPerformed()
        {
            #region Validating Registry & Tracked Components
            LogDebug("OnUndoRedoPerformed");
            var orphanedIds = new HashSet<uint>();
            var keysToRemove = new List<int>();
            var trackingUpdates = new Dictionary<int, HashSet<uint>>();

            foreach(var kvp in registrar_data.trackedComponentIds)
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
                            RegisterId(comp.gameObject, currentId);
                            LogDebug($"Re-registered {nameof(PersistentId)} after undo/redo: 0x{currentId:X8} for {obj.name}");
                        }
                    }

                    trackingUpdates[componentInstanceId] = new HashSet<uint>(currentIds);
                }
            }

            foreach(var update in trackingUpdates)
            {
                registrar_data.trackedComponentIds[update.Key] = update.Value;
            }

            foreach(var key in keysToRemove)
            {
                registrar_data.trackedComponentIds.Remove(key);
                registrar_data.processedComponentsThisDomainCycle.Remove(key);
            }

            foreach(var orphanedId in orphanedIds)
            {
                if(IsIdRegistered(orphanedId))
                {
                    UnregisterId(orphanedId);
                    LogDebug($"Removed orphaned {nameof(PersistentId)} from registry: 0x{orphanedId:X8}");
                }
            }

            registry.ValidateRegistry();
            #endregion

            // TODO(Jazz): Check for clean state restoration in delaycall
            EditorApplication.delayCall -= CheckForCleanStateRestoration;
            EditorApplication.delayCall += CheckForCleanStateRestoration;
        }

        private static void CheckForCleanStateRestoration()
        {
            for(int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if(!scene.isLoaded || !scene.isDirty) continue;

                string sceneGuid = AssetDatabase.AssetPathToGUID(scene.path);

                // Check if we have a saved snapshot for this scene
                if(!registrar_data.savedSceneSnapshots.TryGetValue(sceneGuid, out var savedSnapshot)) continue;

                // Collect current IDs in the scene
                var currentIds = new HashSet<uint>();
                foreach(var root in scene.GetRootGameObjects())
                {
                    foreach(var comp in root.GetComponentsInChildren<MonoBehaviour>(true))
                    {
                        if(comp != null)
                        {
                            CollectIdsFromComponent(comp, currentIds);
                        }
                    }
                }

                // Compare: if current state matches saved snapshot, clear dirty flag
                if(currentIds.SetEquals(savedSnapshot))
                {
                    EditorSceneManager.SaveScene(scene);
                    LogDebug($"Undo/Redo restored scene '{scene.name}' to saved state. Cleared dirty flag.");
                }
            }
        }

        #region Scene Operations
        private static void OnSceneOpened(Scene scene, OpenSceneMode mode)
        {
            LogDebug("Editor Scene Opened Beginning. Tracked Component Count: " + registrar_data.trackedComponentIds.Count);

            string sceneGuid = AssetDatabase.GUIDFromAssetPath(scene.path).ToString();

            // Get all MonoBehaviour components in the scene when opening
            // NOTE(Jazz): HEAVY SCAN - we need to do this on scene open in case any PersistentId-holding components have been modified.
            var rootGameObjects = scene.GetRootGameObjects();
            var allComponents = new List<MonoBehaviour>();

            foreach(var rootGO in rootGameObjects)
            {
                allComponents.AddRange(rootGO.GetComponentsInChildren<MonoBehaviour>(true));
            }

            bool sceneModified = false;
            List<uint> openedScenePersistentIDs = new();

            foreach(var component in allComponents)
            {
                if(component == null) continue;

                var so = new SerializedObject(component);
                var iterator = so.GetIterator();
                bool componentModified = false;

                while(iterator.NextVisible(true))
                {
                    if(!(iterator.propertyType == SerializedPropertyType.Generic &&
                         iterator.type == nameof(PersistentId))) continue;

                    var idProp = iterator.FindPropertyRelative(nameof(PersistentId.id));
                    if(idProp == null) continue;

                    uint currentId = idProp.uintValue;

                    // Handle unassigned fields (value 0)
                    if(currentId == 0)
                    {
                        uint newId = GenerateUniqueId();
                        idProp.uintValue = newId;
                        RegisterId(component.gameObject, newId);
                        componentModified = true;
                        sceneModified = true;
                        LogDebug($"Generated {nameof(PersistentId)} for scene field: 0x{newId:X8} on {component.name}");

                        // Update tracking
                        var instanceId = component.GetInstanceID();
                        if(!registrar_data.trackedComponentIds.ContainsKey(instanceId))
                            registrar_data.trackedComponentIds[instanceId] = new HashSet<uint>();
                        
                        registrar_data.trackedComponentIds[instanceId].Add(newId);
                        openedScenePersistentIDs.Add(newId);

                        // Track as unsaved since we just generated it
                        if(!registrar_data.unsavedIds.ContainsKey(sceneGuid))
                            registrar_data.unsavedIds[sceneGuid] = new HashSet<uint>();
                        registrar_data.unsavedIds[sceneGuid].Add(newId);
                    }
                    // Handle duplicate detection for non-zero IDs
                    else if(registry.IsIdRegisteredGlobally(currentId))
                    {
                        string currentSceneGuid = registry.GetSceneGuid(component.gameObject);

                        if(registry.IsIdRegisteredInScene(currentSceneGuid, currentId))
                        {
                            // This ID already belongs to this scene - just ensure tracking is updated
                            var instanceId = component.GetInstanceID();
                            if(!registrar_data.trackedComponentIds.ContainsKey(instanceId))
                                registrar_data.trackedComponentIds[instanceId] = new HashSet<uint>();

                            registrar_data.trackedComponentIds[instanceId].Add(currentId);
                            openedScenePersistentIDs.Add(currentId);
                        }
                        else
                        {
                            // This is a true duplicate from another scene - generate new ID
                            uint newId = GenerateUniqueId();
                            idProp.uintValue = newId;
                            RegisterId(component.gameObject, newId);
                            componentModified = true;
                            sceneModified = true;

                            LogWarning($"Duplicate {nameof(PersistentId)} 0x{currentId:X8} detected " +
                                $"on {component.name} in scene '{scene.name}'. Generated new ID {newId} (Hex: 0x{newId:X8}).");

                            // Update tracking
                            var instanceId = component.GetInstanceID();
                            if(!registrar_data.trackedComponentIds.ContainsKey(instanceId))
                                registrar_data.trackedComponentIds[instanceId] = new HashSet<uint>();
                            registrar_data.trackedComponentIds[instanceId].Add(newId);

                            openedScenePersistentIDs.Add(newId);

                            // Track as unsaved
                            if(!registrar_data.unsavedIds.ContainsKey(sceneGuid))
                                registrar_data.unsavedIds[sceneGuid] = new HashSet<uint>();
                            registrar_data.unsavedIds[sceneGuid].Add(newId);
                        }
                    }
                    else
                    {
                        // Component not registered yet - register it and track it
                        RegisterId(component.gameObject, currentId);
                        var instanceId = component.GetInstanceID();
                        if(!registrar_data.trackedComponentIds.ContainsKey(instanceId))
                            registrar_data.trackedComponentIds[instanceId] = new HashSet<uint>();
                        registrar_data.trackedComponentIds[instanceId].Add(currentId);

                        openedScenePersistentIDs.Add(currentId);
                    }
                }

                if(componentModified)
                {
                    so.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(component);
                }
            }
            
            // Capture a snapshot after processing the freshly opened scene
            var snapshot = new HashSet<uint>(openedScenePersistentIDs);
            registrar_data.savedSceneSnapshots[sceneGuid] = snapshot;
            LogDebug($"Captured clean state snapshot for '{scene.name}' with {snapshot.Count} IDs");
            
            // If the scene was modified (duplicated fixed, generated / removed ids, etc.) update on the next save
            if(sceneModified)
            {
                EditorSceneManager.MarkSceneDirty(scene);
            }

            // Orphaned Id cleanup
            string curr_scene_guid = AssetDatabase.AssetPathToGUID(scene.path);
            if(!string.IsNullOrEmpty(curr_scene_guid))
            {
                SceneIdData curr_scene_ID_data = null;
                foreach(var sceneIdData in registry.sceneDataList)
                {
                    if(curr_scene_guid == sceneIdData.sceneGuid)
                    {
                        curr_scene_ID_data = sceneIdData;
                        break;
                    }
                }

                if(curr_scene_ID_data == null) return;

                int orphanedIdsRemoved = 0;
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                sb.AppendLine($"Removed Registered Orphaned IDs from Scene: {scene.name}");
                for(int registeredIdIndex = curr_scene_ID_data.registeredIds.Count - 1;
                    registeredIdIndex  >= 0; 
                    registeredIdIndex --)
                {
                    var targetId = curr_scene_ID_data.registeredIds[registeredIdIndex];
                    if(!openedScenePersistentIDs.Contains(targetId))
                    {
                        // Found mismatch between registry and opened scene persistent IDs,
                        // remove orphaned ID from registry
                        registry.UnregisterId(targetId);

                        orphanedIdsRemoved++;
                        sb.AppendLine($"Removed Registered ID: {targetId}");
                    }
                }

                if(orphanedIdsRemoved > 0)
                {
                    sb.AppendLine($"Total Registered Ids removed due to ID orphaning: {orphanedIdsRemoved}");
                    LogWarning(sb.ToString());
                }
            }

            LogDebug($"Rehydrated {nameof(PersistentId)} tracking for scene '{scene.name}'");
            LogDebug("Editor Scene Opened Completed. Tracked Component Count: " + registrar_data.trackedComponentIds.Count);
        }

        public static void OnSceneClosing(Scene scene, bool removingScene)
        {
            // Safe to remove scene from clean-state tracking
            var sceneGuid = AssetDatabase.AssetPathToGUID(scene.path);

            // Remove snapshot tracking for scene on close
            if(registrar_data.savedSceneSnapshots.ContainsKey(sceneGuid))
            {
                registrar_data.savedSceneSnapshots.Remove(sceneGuid);
                LogDebug($"Closing Scene: {scene.name}. Removed snapshot.");
            }

            var component_ids_to_remove = new List<int>();

            // NOTE(Jazz): Another big, HEAVY SCAN during edit-time,
            // but only on scene closing in editor
            foreach(var root in scene.GetRootGameObjects())
            {
                var components = root.GetComponentsInChildren<MonoBehaviour>(true);
                foreach(var comp in components)
                {
                    if(comp == null) continue;

                    var componentInstanceId = comp.GetInstanceID();
                    component_ids_to_remove.Add(componentInstanceId);
                }
            }

            // Remove all components from scene tracking
            // and remove any unsaved persistent ids from the registry -
            // that were generated while the scene was dirty
            for(int componentIndex = component_ids_to_remove.Count - 1;
                componentIndex >= 0;
                componentIndex--)
            {
                var curr_comp_instanceID = component_ids_to_remove[componentIndex];
                if(registrar_data.trackedComponentIds.ContainsKey(curr_comp_instanceID))
                {
                    registrar_data.trackedComponentIds.Remove(curr_comp_instanceID);

                    // NOTE(Jazz): Remove only unserialized registered IDs in dirtied scene, the scene has unserialized
                    // Persistent Id values which need to be wiped from the registry.
                    if(scene.isDirty)
                    {
                        var componentObj = EditorUtility.InstanceIDToObject(curr_comp_instanceID);
                        if(componentObj == null) continue;
                    
                        var so = new SerializedObject(componentObj);
                        SerializedProperty iterator = so.GetIterator();

                        while(iterator.NextVisible(true))
                        {
                            if(!(iterator.propertyType == SerializedPropertyType.Generic &&
                               iterator.type == nameof(PersistentId))) continue; // NAND
                        
                            var idProp = iterator.FindPropertyRelative(nameof(PersistentId.id));
                            if(!(idProp != null && idProp.uintValue != 0)) continue; // NAND
                            
                            // Valid persistentId property found
                            // Compare against unsaved ids hashset for the scene
                            if(registrar_data.unsavedIds.TryGetValue(sceneGuid, out var unsavedSet) &&
                                    unsavedSet.Contains(idProp.uintValue))
                            {
                                registry.UnregisterId(idProp.uintValue);
                                unsavedSet.Remove(idProp.uintValue);
                            }
                        }
                    }
                }
            }
        }

        public static void OnSceneSaved(Scene savedScene)
        {
            // Safe to remove unsaved ids from scene and reset clean-state scene tracking
            string sceneGuid = AssetDatabase.AssetPathToGUID(savedScene.path);

            // Capture snapshot of all PersistentIds in this scene
            // NOTE(Jazz): BIG HEAVY SLOW full hierarchy scan to collect gameobjects - aren't we already storing this scene's registered ids?
            var snapshot = new HashSet<uint>();
            foreach(var root in savedScene.GetRootGameObjects())
            {
                foreach(var comp in root.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if(comp != null)
                    {
                        CollectIdsFromComponent(comp, snapshot);
                    }
                }
            }
            registrar_data.savedSceneSnapshots[sceneGuid] = snapshot;

            registrar_data.unsavedIds.Remove(sceneGuid);
            LogDebug($"Saved Scene: {savedScene.name}. Captured snapshot with {snapshot.Count} IDs.");
        }
        #endregion Scene Operations

        #region ID Management Operations
        public static bool RegisterId(GameObject _gameObject, uint id)
        {
            if(registry == null) return false;

            // NOTE(Jazz): Track scene cleanliness state on registration?


            return registry.RegisterId(_gameObject, id);
        }

        public static bool UnregisterId(uint id)
        {
            return registry != null && registry.UnregisterId(id);
        }

        public static bool IsIdRegistered(uint id)
        {
            return registry != null && registry.IsIdRegisteredGlobally(id);
        }

        public static uint GenerateUniqueId()
        {
            if(registry == null)
            {
                LogError("Registry not found, generating invalid id.");
                return 0;
            }
            return registry.GenerateUniqueId();
        }

        public static void RegenerateId(SerializedProperty persistentIdProperty)
        {
            var target = persistentIdProperty.serializedObject.targetObject;

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
                    persistentIdProperty.serializedObject.ApplyModifiedPropertiesWithoutUndo();
                    if(target is MonoBehaviour comp)
                    {
                        UpdateComponentTracking(comp.GetInstanceID(), oldId, newId);
                        RegisterId(comp.gameObject, newId);
                        LogDebug($"Regenerated {nameof(PersistentId)}: 0x{newId:X8} for {target.name}");
                    }
                }
            }

            registry.ValidateRegistry();
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

        private static void ClearIdsFromPrefabAsset(SerializedObject so)
        {
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
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        #endregion ID Management Operations

        #region Component Operations
        private static void TrackComponentIds(MonoBehaviour component)
        {
            if(component == null) return;

            var componentInstanceId = component.GetInstanceID();
            var idsForComponent = new HashSet<uint>();

            CollectIdsFromComponent(component, idsForComponent);

            if(idsForComponent.Count > 0)
            {
                registrar_data.trackedComponentIds[componentInstanceId] = idsForComponent;
            }
            else
            {
                registrar_data.trackedComponentIds.Remove(componentInstanceId);
            }
        }

        /// <summary>
        /// Populates input idCollection with only non-zero persistent ids found 
        /// on the input MonoBehaviour component.
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
            if(registrar_data.trackedComponentIds.TryGetValue(componentInstanceId, out var ids))
            {
                foreach(var id in ids)
                {
                    if(!IsIdCurrentlyInUseInScene(id) && IsIdRegistered(id))
                    {
                        UnregisterId(id);
                        LogDebug($"Unregistered {nameof(PersistentId)} 0x{id:X8} due to " +
                            $"component removal (componentInstanceId: {componentInstanceId})");
                    }
                }
                registrar_data.trackedComponentIds.Remove(componentInstanceId);
            }
            registrar_data.processedComponentsThisDomainCycle.Remove(componentInstanceId);
        }

        private static void ProcessComponentForPersistentIds(MonoBehaviour component)
        {
            LogDebug("ProcessComponentForPersistentIds");
            if(component == null) return;

            var componentInstanceId = component.GetInstanceID();
            if(registrar_data.processedComponentsThisDomainCycle.Contains(componentInstanceId)) return;

            var so = new SerializedObject(component);

            if(PrefabUtility.IsPartOfPrefabAsset(component))
            {
                ClearIdsFromPrefabAsset(so);
            }
            else
            {
                var idsToRegister = new Dictionary<GameObject, HashSet<uint>>();
                var idsToUnregister = new HashSet<uint>();
                bool hasChanges = false;

                var iterator = so.GetIterator();
                while(iterator.NextVisible(true))
                {
                    if(!(iterator.propertyType == SerializedPropertyType.Generic &&
                        iterator.type == nameof(PersistentId))) continue; // NAND

                    var idProp = iterator.FindPropertyRelative(nameof(PersistentId.id));
                    if(!(idProp != null && 
                        idProp.propertyType == SerializedPropertyType.Integer)) continue; // NAND

                    registrar_data.processedComponentsThisDomainCycle.Add(componentInstanceId);
                    uint currentPropPersistentId = idProp.uintValue;

                    if(currentPropPersistentId == 0)
                    {
                        uint newId = GenerateUniqueId();
                        idProp.uintValue = newId;
                        if(!idsToRegister.ContainsKey(component.gameObject))
                            idsToRegister[component.gameObject] = new HashSet<uint>();
                        idsToRegister[component.gameObject].Add(newId);
                        hasChanges = true;

                        UpdateComponentTracking(componentInstanceId, 0, newId);
                        LogDebug($"Generated {nameof(PersistentId)}: 0x{newId:X8} " +
                            $"for {so.targetObject.name}.{iterator.name}");
                    }
                    else
                    {
                        if(IsIdRegistered(currentPropPersistentId))
                        {
                            bool isLegitimateOwner = false;

                            if(registrar_data.trackedComponentIds.TryGetValue(componentInstanceId, out var idsForComponent) &&
                                idsForComponent.Contains(currentPropPersistentId))
                            {
                                isLegitimateOwner = true;
                            }
                            else
                            {
                                bool foundElsewhere = false;
                                foreach(var kvp in registrar_data.trackedComponentIds)
                                {
                                    if(kvp.Key != componentInstanceId && 
                                        kvp.Value.Contains(currentPropPersistentId))
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
                                if(!idsToRegister.ContainsKey(component.gameObject))
                                    idsToRegister[component.gameObject] = new HashSet<uint>();

                                idsToRegister[component.gameObject].Add(currentPropPersistentId);
                                UpdateComponentTracking(componentInstanceId, 0, currentPropPersistentId);
                            }
                            else
                            {
                                // ID conflict detected: this component is a duplicate
                                uint newId = GenerateUniqueId();
                                idProp.uintValue = newId;

                                if(!idsToRegister.ContainsKey(component.gameObject))
                                    idsToRegister[component.gameObject] = new HashSet<uint>();

                                idsToRegister[component.gameObject].Add(newId);
                                hasChanges = true;

                                LogDebug($"Detected duplicate {nameof(PersistentId)} 0x{currentPropPersistentId:X8} on '{so.targetObject.name}'. Generated new {nameof(PersistentId)}: 0x{newId:X8}");

                                UpdateComponentTracking(componentInstanceId, currentPropPersistentId, newId);
                            }
                        }
                        else
                        {
                            // Not registered yet — safe to register as-is
                            if(!idsToRegister.ContainsKey(component.gameObject))
                                idsToRegister[component.gameObject] = new HashSet<uint>();

                            idsToRegister[component.gameObject].Add(currentPropPersistentId);
                            UpdateComponentTracking(componentInstanceId, 0, currentPropPersistentId);
                            LogDebug($"Registering existing {nameof(PersistentId)}: 0x{currentPropPersistentId:X8} for {so.targetObject.name}.{iterator.name}");
                        }
                    }
                }

                if(hasChanges)
                {
                    so.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(so.targetObject);
                }

                foreach(var id in idsToUnregister)
                {
                    UnregisterId(id);
                }

                foreach(var kvp in idsToRegister)
                {
                    foreach(var value in kvp.Value)
                    RegisterId(kvp.Key.gameObject, value);
                }

                // Update tracking after processing
                TrackComponentIds(component);
            }
        }

        private static void UpdateComponentTracking(int componentInstanceId, uint oldId, uint newId)
        {
            if(!registrar_data.trackedComponentIds.TryGetValue(componentInstanceId, out var ids))
            {
                ids = new HashSet<uint>();
                registrar_data.trackedComponentIds[componentInstanceId] = ids;
            }

            if(oldId != 0) ids.Remove(oldId);
            if(newId != 0) ids.Add(newId);

            if(ids.Count == 0)
            {
                registrar_data.trackedComponentIds.Remove(componentInstanceId);
            }
        }
        #endregion Component Operations

        // TODO(Jazz): Remove for release
        #region Debugging Operations
        [MenuItem("Tools/Persistent Id/RESET Registry Setup Key", false, 2)]
        private static void ResetRegistrySetupKey()
        {
            EditorPrefs.SetBool(INITIALIZED_KEY, false);
        }

        [MenuItem("Tools/Persistent Id/PRINT Registry Setup Key", false, 2)]
        private static void PrintRegistryKey()
        {
            LogDebug("initialized registry installation key: " + EditorPrefs.GetBool(INITIALIZED_KEY, false));
        }

        [MenuItem("Tools/Persistent Id/Print Tracked Component Ids")]
        private static void PrintTrackedComponentIds()
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine("---- Tracked Component Ids ----");
            foreach(var trackedComponent in registrar_data.trackedComponentIds)
            {
                sb.Append($"Instance Id: {trackedComponent.ToString()}, ");
                foreach(var persistentId in trackedComponent.Value)
                {
                    sb.Append($"0x{persistentId:X8}, ");
                }
                sb.Append("\n");
            }
            LogDebug(sb.ToString());
        }

        [MenuItem("Tools/Persistent Id/Remove Tracked Component Ids")]
        private static void RemoveTrackedComponentIds()
        {
            // Clear in-memory data
            registrar_data.trackedComponentIds.Clear();
            registrar_data.processedComponentsThisDomainCycle.Clear();

            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine("---- Cleared Tracked Component Ids ----");
            sb.AppendLine("No tracked components remaining.");
            LogDebug(sb.ToString());
        }

        [MenuItem("Tools/Persistent Id/Print Processed Component Ids", false, 0)]
        public static void PrintProcessedComponentIds()
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine($"==== Processed Component Instance Ids [{registrar_data.processedComponentsThisDomainCycle.Count}] =====");
            foreach(var componentInstanceID in registrar_data.processedComponentsThisDomainCycle)
            {
                if(!registrar_data.trackedComponentIds.ContainsKey(componentInstanceID)) continue;
                sb.Append("\nInstanceID: " + componentInstanceID.ToString() + " ");

                int count = 0;
                foreach(var persistentId in registrar_data.trackedComponentIds[componentInstanceID])
                {
                    sb.Append($"ID {count:D2}: 0x{persistentId:X8}, ");
                    count++;
                }
            }
            LogDebug(sb.ToString());
        }
        #endregion Debugging Operations
    }
}
#endif
