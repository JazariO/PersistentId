#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.SceneManagement;
using System.Linq;

public static class PersistentIdManager
{
    private static PersistentIdRegistrySO registry;
    private const string REGISTRY_SEARCH_FILTER = "t:PersistentIdRegistrySO";
    private const string REGISTRY_PATH = "Assets/PersistentIdRegistry.asset";

    private static Dictionary<int, HashSet<uint>> trackedObjectIds = new Dictionary<int, HashSet<uint>>();
    private static HashSet<int> processedComponentsThisDomainCycle = new HashSet<int>();

    static PersistentIdManager()
    {
        InitializeRegistry();
        SubscribeToCallbacks();
    }

    [InitializeOnLoadMethod]
    private static void Initialize()
    {
        processedComponentsThisDomainCycle.Clear();
        EditorApplication.delayCall += () => {
            InitializeRegistry();
            SubscribeToCallbacks();
            ScanAllObjectsForTracking();
        };
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
                Debug.LogWarning($"Multiple PersistentIdRegistry assets found. Using: {path}");
            }
        }

        if(registry == null)
        {
            CreateRegistry();
        }
    }
    private static void CreateRegistry()
    {
        Debug.LogError("PersistentIdRegistry not found in project. Creating new registry at: " + REGISTRY_PATH);

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
    private static void SubscribeToCallbacks()
    {
        ObjectChangeEvents.changesPublished -= OnObjectChangesPublished;
        ObjectChangeEvents.changesPublished += OnObjectChangesPublished;

        Undo.undoRedoPerformed -= OnUndoRedoPerformed;
        Undo.undoRedoPerformed += OnUndoRedoPerformed;
    }
    private static void ScanAllObjectsForTracking()
    {
        for(int i = 0; i < SceneManager.sceneCount; i++)
        {
            var scene = SceneManager.GetSceneAt(i);
            if(!scene.isLoaded) continue;

            var rootGos = scene.GetRootGameObjects();
            foreach(var root in rootGos)
            {
                ProcessGameObjectForTracking(root);
            }
        }
    }
    private static void ProcessGameObjectForTracking(GameObject go)
    {
        TrackObjectIds(go.GetInstanceID());

        foreach(Transform child in go.transform)
        {
            ProcessGameObjectForTracking(child.gameObject);
        }
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
        if(registry == null) return 0;
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
                            if(comp != null && !processedComponentsThisDomainCycle.Contains(comp.GetInstanceID()))
                            {
                                ProcessComponentForPersistentIds(comp);
                            }
                        }
                        
                        // Track ids after processing
                        TrackObjectIds(createEvent.instanceId);
                    }
                    else if(obj is Component comp && comp is MonoBehaviour monoBehaviour)
                    {
                        if(!processedComponentsThisDomainCycle.Contains(monoBehaviour.GetInstanceID()))
                        {
                            ProcessComponentForPersistentIds(monoBehaviour);
                        }

                        // Track after processing
                        if(monoBehaviour.gameObject != null)
                        {
                            TrackObjectIds(monoBehaviour.gameObject.GetInstanceID());
                        }
                    }
                } break;

                case ObjectChangeKind.DestroyGameObjectHierarchy:
                {
                    Debug.Log("ObjectChangeKind.DestroyGameObjectHierarchy");
                    stream.GetDestroyGameObjectHierarchyEvent(eventIndex, out var destroyEvent);
                    Debug.Log($"[HandleGameObjectDestruction] Destroying instanceId: {destroyEvent.instanceId}");

                    // Since the object is being destroyed, we can't access it directly anymore.
                    // We need to rely on our tracking data to know what IDs were associated with this object.
                    if(trackedObjectIds.TryGetValue(destroyEvent.instanceId, out var trackedIds))
                    {
                        Debug.Log($"[HandleGameObjectDestruction] Found {trackedIds.Count} tracked IDs for destroyed object");

                        // Create a copy to iterate over since we'll be modifying collections
                        var idsToProcess = new HashSet<uint>(trackedIds);

                        foreach(var id in idsToProcess)
                        {
                            // Check if this ID is used by any OTHER existing objects in the scene
                            bool idFoundElsewhere = IsIdCurrentlyInUseInScene(id, destroyEvent.instanceId);

                            if(!idFoundElsewhere && IsIdRegistered(id))
                            {
                                UnregisterId(id);
                                Debug.Log($"Unregistered PersistentId 0x{id:X8} due to GameObject destruction (instanceId: {destroyEvent.instanceId})");
                            }
                            else if(idFoundElsewhere)
                            {
                                Debug.Log($"Preserved PersistentId 0x{id:X8} - still in use by another object");
                            }
                            else if(!IsIdRegistered(id))
                            {
                                Debug.Log($"PersistentId 0x{id:X8} was not registered (instanceId: {destroyEvent.instanceId})");
                            }
                        }

                        // Remove the tracking entry for this destroyed object
                        trackedObjectIds.Remove(destroyEvent.instanceId);
                    }
                    else
                    {
                        Debug.Log($"[HandleGameObjectDestruction] No tracked IDs found for destroyed instanceId: {destroyEvent.instanceId}");

                        // FALLBACK: The destroyed object might not be tracked under this instanceId
                        // This can happen when objects change instance IDs during their lifecycle
                        // Let's search for any IDs that are no longer in use anywhere in the scene
                        Debug.Log($"[HandleGameObjectDestruction] Performing fallback cleanup for orphaned IDs");

                        var allCurrentIds = new HashSet<uint>();
                        for(int sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
                        {
                            var scene = SceneManager.GetSceneAt(sceneIndex);
                            if(!scene.isLoaded) continue;

                            var rootGos = scene.GetRootGameObjects();
                            foreach(var root in rootGos)
                            {
                                CollectAllIdsFromGameObject(root, allCurrentIds);
                            }
                        }

                        // Find registered IDs that are no longer in the scene
                        var orphanedIds = new List<uint>();
                        if(registry != null)
                        {
                            // We'd need to add a method to get all registered IDs from the registry
                            // For now, we'll check our tracking data for any IDs that aren't in the current scene
                            foreach(var kvp in trackedObjectIds.ToList())
                            {
                                var obj = EditorUtility.InstanceIDToObject(kvp.Key);
                                if(obj == null)
                                {
                                    foreach(var id in kvp.Value)
                                    {
                                        if(!allCurrentIds.Contains(id))
                                        {
                                            orphanedIds.Add(id);
                                        }
                                    }
                                    trackedObjectIds.Remove(kvp.Key);
                                }
                            }
                        }

                        foreach(var orphanedId in orphanedIds)
                        {
                            if(IsIdRegistered(orphanedId))
                            {
                                UnregisterId(orphanedId);
                                Debug.Log($"Unregistered orphaned PersistentId 0x{orphanedId:X8} during fallback cleanup");
                            }
                        }
                    }
                } break;

                case ObjectChangeKind.ChangeGameObjectOrComponentProperties:
                {
                    Debug.Log("ObjectChangeKind.ChangeGameObjectOrComponentProperties");
                    stream.GetChangeGameObjectOrComponentPropertiesEvent(eventIndex, out var changeEvent);
                    var obj = EditorUtility.InstanceIDToObject(changeEvent.instanceId);

                    // Handle component removal explicitly
                    if(obj == null) // Component was likely removed
                    {
                        if(trackedObjectIds.TryGetValue(changeEvent.instanceId, out var ids))
                        {
                            foreach(var id in ids)
                            {
                                if(IsIdRegistered(id))
                                {
                                    UnregisterId(id);
                                    Debug.Log($"Unregistered PersistentId 0x{id:X8} due to component removal (Properties Change)");
                                }
                            }
                            trackedObjectIds.Remove(changeEvent.instanceId);
                        }
                    }
                    else
                    {
                        // Handle component property change
                        if(obj is MonoBehaviour comp)
                        {
                            if(!processedComponentsThisDomainCycle.Contains(comp.GetInstanceID()))
                            {
                                ProcessComponentForPersistentIds(comp);
                            }
                            
                            // Track at the GameObject & component level
                            if(comp.gameObject != null)
                            {
                                TrackObjectIds(comp.gameObject.GetInstanceID());
                            }
                        }
                        else if(obj is GameObject go)
                        {
                            foreach(var component in go.GetComponents<MonoBehaviour>())
                            {
                                if(component != null && !processedComponentsThisDomainCycle.Contains(component.GetInstanceID()))
                                {
                                    ProcessComponentForPersistentIds(component);
                                }
                            }

                            // Track after processing
                            TrackObjectIds(go.GetInstanceID());
                        }
                    }
                } break;

                case ObjectChangeKind.ChangeGameObjectStructure:
                {
                    Debug.Log("ObjectChangeKind.ChangeGameObjectStructure");
                    stream.GetChangeGameObjectStructureEvent(eventIndex, out var structureEvent);
                    UnityEngine.Object obj = EditorUtility.InstanceIDToObject(structureEvent.instanceId);

                    if(obj is GameObject go)
                    {
                        int instanceId = structureEvent.instanceId;

                        // Gather current PersistentIds after structure change
                        var currentIds = new HashSet<uint>();
                        foreach(var comp in go.GetComponents<MonoBehaviour>())
                        {
                            if(comp != null)
                            {
                                ProcessComponentForPersistentIds(comp);
                                CollectIdsFromComponent(comp, currentIds);
                            }
                        }

                        // Compare with previously tracked IDs for this GameObject
                        if(trackedObjectIds.TryGetValue(instanceId, out var oldIds))
                        {
                            foreach(var oldId in oldIds)
                            {
                                if(!currentIds.Contains(oldId) && IsIdRegistered(oldId))
                                {
                                    // Check if the ID is used elsewhere before unregistering
                                    if(!IsIdCurrentlyInUseInScene(oldId))
                                    {
                                        UnregisterId(oldId);
                                        Debug.Log($"[PersistentIdManager] Unregistered orphaned PersistentId 0x{oldId:X8} " +
                                            $"due to component removal on GameObject: {go.name}");
                                    }
                                    else
                                    {
                                        Debug.Log($"[PersistentIdManager] Preserved PersistentId 0x{oldId:X8} - still in use elsewhere");
                                    }
                                }
                            }
                        }

                        // Update tracking state
                        if(currentIds.Count > 0)
                        {
                            trackedObjectIds[instanceId] = currentIds;
                        }
                        else
                        {
                            trackedObjectIds.Remove(instanceId);
                        }

                        registry?.ValidateRegistry();
                    }
                } break;

                case ObjectChangeKind.ChangeGameObjectStructureHierarchy:
                {
                    Debug.Log("ObjectChangeKind.ChangeGameObjectStructureHierarchy");
                    stream.GetChangeGameObjectStructureHierarchyEvent(eventIndex, out var structureEvent);
                    var obj = EditorUtility.InstanceIDToObject(structureEvent.instanceId);

                    if(obj is GameObject go)
                    {
                        foreach(var comp in go.GetComponents<MonoBehaviour>())
                        {
                            if(comp != null && !processedComponentsThisDomainCycle.Contains(comp.GetInstanceID()))
                            {
                                ProcessComponentForPersistentIds(comp);
                            }
                        }

                        // Track after processing
                        TrackObjectIds(structureEvent.instanceId);
                    }
                } break;

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

                            var prop = so.FindProperty("PersistentId");
                            if(prop != null && prop.intValue != 0)
                            {
                                UnregisterId((uint)prop.intValue);
                            }

                            // Clear prefab asset IDs                                                                   
                            ClearIdsFromPrefabAsset(so);

                            // Mark the component dirty so Unity knows it must be written back to disk
                            EditorUtility.SetDirty(comp);
                        }

                        // Force the prefab asset itself to resave with cleared IDs
                        string path = AssetDatabase.GetAssetPath(prefab);
                        AssetDatabase.SaveAssets();
                        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

                        Debug.Log($"[PersistentIdManager] Cleared and unregistered PersistentIds from new prefab asset: {prefab.name}");
                    }
                } break;

                case ObjectChangeKind.UpdatePrefabInstances:
                {
                    stream.GetUpdatePrefabInstancesEvent(eventIndex, out var updatePrefabInstancesEvent);
                    foreach(int instanceId in updatePrefabInstancesEvent.instanceIds)
                    {
                        UnityEngine.Object obj = EditorUtility.InstanceIDToObject(instanceId);
                        if(obj is GameObject prefabInstance)
                        {
                            foreach(var comp in prefabInstance.GetComponentsInChildren<MonoBehaviour>())
                            {
                                if(comp == null) continue;
                                SerializedObject so = new SerializedObject(comp);

                                SerializedProperty iterator = so.GetIterator();
                                while(iterator.NextVisible(true))
                                {
                                    if(iterator.propertyType == SerializedPropertyType.Generic &&
                                        iterator.type == "PersistentId")
                                    {
                                        var idProp = iterator.FindPropertyRelative("id");
                                        if(idProp != null && idProp.propertyType == SerializedPropertyType.Integer)
                                        {
                                            uint currentId = (uint)idProp.intValue;
                                            if(currentId != 0)
                                            {
                                                // Valid persistent id detected on prefab instance.
                                                // Mark as prefab override.

                                                // Force Unity to register a change
                                                idProp.intValue = 0;
                                                so.ApplyModifiedProperties();

                                                // Now restore the original value
                                                idProp.intValue = unchecked((int)currentId);
                                                so.ApplyModifiedProperties();

                                                // Record the modification explicitly
                                                PrefabUtility.RecordPrefabInstancePropertyModifications(comp);

                                                // Clear the persistent id on the prefab asset
                                                MonoBehaviour prefabAsset = PrefabUtility.GetCorrespondingObjectFromOriginalSource(comp);
                                                string assetPath = AssetDatabase.GetAssetPath(prefabAsset);
                                                if(!string.IsNullOrEmpty(assetPath))
                                                {
                                                    GameObject prefabRoot = PrefabUtility.LoadPrefabContents(assetPath);
                                                    try
                                                    {
                                                        foreach(var assetComp in prefabRoot.GetComponentsInChildren<MonoBehaviour>(true))
                                                        {
                                                            if(assetComp == null) continue;

                                                            SerializedObject assetSO = new SerializedObject(assetComp);
                                                            SerializedProperty assetIterator = assetSO.GetIterator();

                                                            while(assetIterator.NextVisible(true))
                                                            {
                                                                if(assetIterator.propertyType == SerializedPropertyType.Generic &&
                                                                    assetIterator.type == "PersistentId")
                                                                {
                                                                    var assetIdProp = assetIterator.FindPropertyRelative("id");
                                                                    if(assetIdProp != null && assetIdProp.propertyType == SerializedPropertyType.Integer)
                                                                    {
                                                                        assetIdProp.intValue = 0;
                                                                        assetSO.ApplyModifiedProperties();
                                                                    }
                                                                }
                                                            }
                                                        }

                                                        PrefabUtility.SaveAsPrefabAsset(prefabRoot, assetPath);
                                                    }
                                                    finally
                                                    {
                                                        PrefabUtility.UnloadPrefabContents(prefabRoot);
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                } break;
            }
        }

        registry.ValidateRegistry();
    }
    private static void TrackObjectIds(int instanceId)
    {
        UnityEngine.Object obj = EditorUtility.InstanceIDToObject(instanceId);
        if(obj == null)
        {
            // Remove tracking for non-existent objects
            if(trackedObjectIds.ContainsKey(instanceId))
            {
                foreach(var id in trackedObjectIds[instanceId])
                {
                    if(IsIdRegistered(id))
                    {
                        UnregisterId(id);
                        Debug.Log($"Unregistered PersistentId 0x{id:X8} due to missing object (instanceId: {instanceId})");
                    }
                }
                trackedObjectIds.Remove(instanceId);
            }
            return;
        }

        var idsForObject = new HashSet<uint>();

        if(obj is GameObject go)
        {
            foreach(var comp in go.GetComponents<MonoBehaviour>())
            {
                if(comp != null)
                {
                    CollectIdsFromComponent(comp, idsForObject);
                }
            }
        }
        else if(obj is MonoBehaviour comp)
        {
            CollectIdsFromComponent(comp, idsForObject);
        }

        if(idsForObject.Count > 0)
        {
            trackedObjectIds[instanceId] = idsForObject;
            //Debug.Log($"[TrackObjectIds] Tracked {idsForObject.Count} ID(s) for instance {instanceId} with objectName: {EditorUtility.InstanceIDToObject(instanceId).name}: {string.Join(", ", idsForObject.Select(id => $"0x{id:X8}"))}");
        }
        else
        {
            trackedObjectIds.Remove(instanceId);
            //Debug.Log($"[TrackObjectIds] Removed tracking for instance {instanceId} with objectName: {EditorUtility.InstanceIDToObject(instanceId).name}.");
        }
    }
    private static void CollectIdsFromComponent(MonoBehaviour component, HashSet<uint> idCollection)
    {
        SerializedObject so = new SerializedObject(component);
        SerializedProperty iterator = so.GetIterator();

        while(iterator.NextVisible(true))
        {
            if(iterator.propertyType == SerializedPropertyType.Generic &&
                iterator.type == "PersistentId")
            {
                SerializedProperty idProp = iterator.FindPropertyRelative("id");
                if(idProp != null && idProp.propertyType == SerializedPropertyType.Integer)
                {
                    uint id = (uint)idProp.intValue;
                    if(id != 0)
                    {
                        idCollection.Add(id);
                    }
                }
            }
        }
    }
    // Helper method to collect all IDs from a GameObject hierarchy
    private static void CollectAllIdsFromGameObject(GameObject go, HashSet<uint> idCollection)
    {
        foreach(var comp in go.GetComponents<MonoBehaviour>())
        {
            if(comp != null)
            {
                CollectIdsFromComponent(comp, idCollection);
            }
        }

        foreach(Transform child in go.transform)
        {
            CollectAllIdsFromGameObject(child.gameObject, idCollection);
        }
    }
    // Helper method to check if an ID is currently in use in the scene, excluding a specific instanceId
    private static bool IsIdCurrentlyInUseInScene(uint targetId, int excludeInstanceId = -1)
    {
        for(int i = 0; i < SceneManager.sceneCount; i++)
        {
            var scene = SceneManager.GetSceneAt(i);
            if(!scene.isLoaded) continue;

            var rootGos = scene.GetRootGameObjects();
            foreach(var root in rootGos)
            {
                if(CheckGameObjectForId(root, targetId, excludeInstanceId))
                {
                    return true;
                }
            }
        }
        return false;
    }
    // Helper method to recursively check a GameObject and its children for a specific ID, excluding a specific instanceId
    private static bool CheckGameObjectForId(GameObject go, uint targetId, int excludeInstanceId = -1)
    {
        // Skip checking this GameObject if it's the one being excluded (i.e., the one being destroyed)
        if(excludeInstanceId != -1 && go.GetInstanceID() == excludeInstanceId)
        {
            return false;
        }

        foreach(var comp in go.GetComponents<MonoBehaviour>())
        {
            if(comp == null) return false;

            // Check component for id
            bool componentHasId = false;
            var so = new SerializedObject(comp);
            var iterator = so.GetIterator();

            while(iterator.NextVisible(true))
            {
                if(iterator.propertyType == SerializedPropertyType.Generic &&
                    iterator.type == "PersistentId")
                {
                    var idProp = iterator.FindPropertyRelative("id");
                    if(idProp != null && idProp.propertyType == SerializedPropertyType.Integer)
                    {
                        uint id = (uint)idProp.intValue;
                        if(id == targetId)
                        {
                            componentHasId = true;
                        }
                    }
                }
            }

            if(comp != null && componentHasId)
            {
                return true;
            }
        }

        foreach(Transform child in go.transform)
        {
            if(CheckGameObjectForId(child.gameObject, targetId, excludeInstanceId))
            {
                return true;
            }
        }

        return false;
    }
    private static void ProcessComponentForPersistentIds(MonoBehaviour component)
    {
        if(component == null) return;

        var componentId = component.GetInstanceID();
        if(processedComponentsThisDomainCycle.Contains(componentId)) return;

        processedComponentsThisDomainCycle.Add(componentId);
        var so = new SerializedObject(component);

        if(PrefabUtility.IsPartOfPrefabAsset(component))
        {
            // This is the actual prefab .asset file - must clear IDs
            ClearIdsFromPrefabAsset(so);
        }
        else
        {
            // Keep IDs for prefab instances in the scene
            // Assign or Replace ids
            if(component == null) return;

            Undo.RecordObject(so.targetObject, "Assign Persistent ID");

            var idsToRegister = new HashSet<uint>();
            var idsToUnregister = new HashSet<uint>();
            bool hasChanges = false;

            var iterator = so.GetIterator();
            while(iterator.NextVisible(true))
            {
                if(iterator.propertyType == SerializedPropertyType.Generic &&
                    iterator.type == "PersistentId")
                {
                    var idProp = iterator.FindPropertyRelative("id");
                    if(idProp != null && idProp.propertyType == SerializedPropertyType.Integer)
                    {
                        uint currentId = (uint)idProp.intValue;

                        // Only generate a new ID if the current ID is 0
                        if(currentId == 0)
                        {
                            uint newId = GenerateUniqueId();
                            if(newId != 0)
                            {
                                idProp.intValue = unchecked((int)newId);
                                idsToRegister.Add(newId);
                                hasChanges = true;

                                UpdateTracking(so.targetObject, 0, newId);
                                Debug.Log($"Generated PersistentId: 0x{newId:X8} for {so.targetObject.name}.{iterator.name}");
                            }
                        }
                        else
                        {
                            var instanceId = so.targetObject.GetInstanceID();

                            // Check if this ID is registered
                            if(IsIdRegistered(currentId))
                            {
                                // Check if this object legitimately owns this ID
                                bool isLegitimateOwner = false;

                                if(trackedObjectIds.TryGetValue(instanceId, out var idsForObject) && idsForObject.Contains(currentId))
                                {
                                    isLegitimateOwner = true;
                                }
                                else
                                {
                                    // Check if this is the only object in the scene with this ID
                                    // This handles cases where tracking might be temporarily out of sync
                                    bool foundElsewhere = false;
                                    foreach(var kvp in trackedObjectIds)
                                    {
                                        if(kvp.Key != instanceId && kvp.Value.Contains(currentId))
                                        {
                                            foundElsewhere = true;
                                            break;
                                        }
                                    }

                                    if(!foundElsewhere)
                                    {
                                        // This appears to be the legitimate owner despite not being tracked
                                        isLegitimateOwner = true;
                                    }
                                }

                                if(isLegitimateOwner)
                                {
                                    // This object legitimately owns the ID - keep it
                                    idsToRegister.Add(currentId);
                                    UpdateTracking(so.targetObject, 0, currentId);
                                }
                                else
                                {
                                    // ID conflict detected: this component is a duplicate
                                    uint newId = GenerateUniqueId();
                                    if(newId != 0)
                                    {
                                        idProp.intValue = unchecked((int)newId);
                                        idsToRegister.Add(newId);
                                        hasChanges = true;

                                        Debug.LogWarning($"[PersistentIdManager] Detected duplicate PersistentId 0x{currentId:X8} on '{so.targetObject.name}'. Generated new PersistentId: 0x{newId:X8}");

                                        UpdateTracking(so.targetObject, currentId, newId);
                                    }
                                }
                            }
                            else
                            {
                                // Not registered yet — safe to register as-is
                                idsToRegister.Add(currentId);
                                UpdateTracking(so.targetObject, 0, currentId);
                                Debug.Log($"Registering existing PersistentId: 0x{currentId:X8} for {so.targetObject.name}.{iterator.name}");
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
        }
    }
    private static void ClearIdsFromPrefabAsset(SerializedObject so)
    {
        Undo.RecordObject(so.targetObject, "Clear Persistent ID");

        var iterator = so.GetIterator();
        bool hasChanges = false;

        while(iterator.NextVisible(true))
        {
            if(iterator.propertyType == SerializedPropertyType.Generic &&
                iterator.type == "PersistentId")
            {
                var idProp = iterator.FindPropertyRelative("id");
                if(idProp != null && idProp.propertyType == SerializedPropertyType.Integer && idProp.intValue != 0)
                {
                    uint oldId = (uint)idProp.intValue;
                    idProp.intValue = 0;
                    hasChanges = true;
                    UnregisterId(oldId);
                    UpdateTracking(so.targetObject, oldId, 0);
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
        var orphanedIds = new HashSet<uint>();
        var keysToRemove = new List<int>();
        var trackingUpdates = new Dictionary<int, HashSet<uint>>();

        foreach(var kvp in trackedObjectIds)
        {
            int instanceId = kvp.Key;
            var trackedIds = kvp.Value;
            var obj = EditorUtility.InstanceIDToObject(instanceId);

            if(obj == null)
            {
                foreach(var id in trackedIds)
                {
                    // Check if ID is used elsewhere before marking as orphaned
                    if(!IsIdCurrentlyInUseInScene(id))
                    {
                        orphanedIds.Add(id);
                    }
                }
                keysToRemove.Add(instanceId);
            }
            else
            {
                var currentIds = new HashSet<uint>();

                if(obj is GameObject go)
                {
                    foreach(var comp in go.GetComponents<MonoBehaviour>())
                    {
                        if(comp != null)
                        {
                            CollectIdsFromComponent(comp, currentIds);
                        }
                    }
                }
                else if(obj is MonoBehaviour comp)
                {
                    CollectIdsFromComponent(comp, currentIds);
                }

                foreach(var trackedId in trackedIds)
                {
                    if(!currentIds.Contains(trackedId) && IsIdRegistered(trackedId))
                    {
                        // Check if ID is used elsewhere before marking as orphaned
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
                        Debug.Log($"Re-registered PersistentId after undo: 0x{currentId:X8} for {obj.name}");
                    }
                }

                trackingUpdates[instanceId] = new HashSet<uint>(currentIds);
            }
        }

        foreach(var update in trackingUpdates)
        {
            trackedObjectIds[update.Key] = update.Value;
        }

        foreach(var key in keysToRemove)
        {
            trackedObjectIds.Remove(key);
        }

        foreach(var orphanedId in orphanedIds)
        {
            if(IsIdRegistered(orphanedId))
            {
                UnregisterId(orphanedId);
                Debug.Log($"Removed orphaned PersistentId from registry: 0x{orphanedId:X8}");
            }
        }

        registry?.ValidateRegistry();
    }
    public static void RegenerateId(SerializedProperty persistentIdProperty)
    {
        var target = persistentIdProperty.serializedObject.targetObject;
        Undo.RecordObject(target, "Regenerate Persistent ID");

        var idProp = persistentIdProperty.FindPropertyRelative("id");
        if(idProp != null && idProp.propertyType == SerializedPropertyType.Integer)
        {
            uint oldId = (uint)idProp.intValue;
            uint newId = GenerateUniqueId();

            if(oldId != 0)
            {
                UnregisterId(oldId);
            }

            if(newId != 0)
            {
                idProp.intValue = unchecked((int)newId);
                persistentIdProperty.serializedObject.ApplyModifiedProperties();

                UpdateTracking(target, oldId, newId);
                RegisterId(newId);

                Debug.Log($"Regenerated PersistentId: 0x{newId:X8} for {target.name}");

                // Update GameObject's tracked IDs if target is a MonoBehaviour
                if(target is MonoBehaviour mb && mb.gameObject != null)
                {
                    TrackObjectIds(mb.gameObject.GetInstanceID());
                }
            }
        }

        registry?.ValidateRegistry();
    }
    private static void UpdateTracking(UnityEngine.Object obj, uint oldId, uint newId)
    {
        if(obj == null) return;

        int instanceId = obj.GetInstanceID();

        // Clean up any old tracking entries for this object that might be under different instance IDs
        var keysToRemove = new List<int>();
        foreach(var kvp in trackedObjectIds.ToList())
        {
            var trackedObj = EditorUtility.InstanceIDToObject(kvp.Key);
            if(trackedObj == obj && kvp.Key != instanceId)
            {
                // This is the same object but tracked under a different instance ID
                // Merge the IDs and remove the old entry
                if(!trackedObjectIds.TryGetValue(instanceId, out var currentIds))
                {
                    currentIds = new HashSet<uint>();
                    trackedObjectIds[instanceId] = currentIds;
                }

                foreach(var id in kvp.Value)
                {
                    currentIds.Add(id);
                }

                keysToRemove.Add(kvp.Key);
                Debug.Log($"[UpdateTracking] Consolidated tracking from old instanceId {kvp.Key} to new instanceId {instanceId}");
            }
        }

        foreach(var key in keysToRemove)
        {
            trackedObjectIds.Remove(key);
        }

        // Now update the tracking for the current instance ID
        if(!trackedObjectIds.TryGetValue(instanceId, out var ids))
        {
            ids = new HashSet<uint>();
            trackedObjectIds[instanceId] = ids;
        }

        if(oldId != 0) ids.Remove(oldId);
        if(newId != 0) ids.Add(newId);

        if(ids.Count == 0)
        {
            trackedObjectIds.Remove(instanceId);
        }

        //Debug.Log($"[UpdateTracking] Updated tracking for instanceId {instanceId}: {string.Join(", ", ids.Select(id => $"0x{id:X8}"))}");
    }
}
#endif