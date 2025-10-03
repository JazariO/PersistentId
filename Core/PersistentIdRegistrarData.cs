using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using static Proselyte.Persistence.PersistentIdLogger;

namespace Proselyte.Persistence
{
    /// <summary> 
    /// Stores transient editor-time data only - for keeping data across editor domain reloads,
    /// but not across editor sessions (editor restarts).
    /// Uses ISerializationCallbackReceiver to automatically convert between
    /// serializable lists and runtime dictionaries.
    /// </summary>
    internal class PersistentIdRegistrarData : ScriptableSingleton<PersistentIdRegistrarData>, ISerializationCallbackReceiver
    {
        // Serializable backing fields
        [SerializeField] private List<TrackedComponentEntry> _trackedComponentEntries = new();
        [SerializeField] private List<UnsavedIdsEntry> _unsavedIdsEntries = new();
        [SerializeField] private List<SceneSnapshotEntry> _sceneSnapshotEntries = new();

        // Transient edit-time collections (not serialized)
        [System.NonSerialized] private Dictionary<int, HashSet<uint>> _trackedComponentIds = new();
        [System.NonSerialized] private HashSet<int> _processedComponentsThisDomainCycle = new();
        [System.NonSerialized] private Dictionary<string, HashSet<uint>> _unsavedIds = new();
        [System.NonSerialized] private Dictionary<string, HashSet<uint>> _savedSceneSnapshots = new();

        #region Public Accessors
        // NOTE(Jazz): Tracks components by instanceId that contain PersistentIds, keeps a hashset of all unique PersistentId values.
        // Requires clearing on scene open/close in editor.
        internal Dictionary<int, HashSet<uint>> trackedComponentIds => _trackedComponentIds;

        // NOTE(Jazz): This acts purely as a speed boost to component processing as an early-out guard to avoid reprocessing components.
        // Will be cleared with each domain reload.
        internal HashSet<int> processedComponentsThisDomainCycle => _processedComponentsThisDomainCycle;

        // NOTE(Jazz): Keeps track of unsaved IDs so that they may be removed from the registry when an unsaved scene is unloaded
        internal Dictionary<string, HashSet<uint>> unsavedIds => _unsavedIds;

        // NOTE(Jazz): tracks snapshots during scene saving/opening so that undo can correctly clear the scene's dirty state - like vanilla Unity behaves.
        internal Dictionary<string, HashSet<uint>> savedSceneSnapshots => _savedSceneSnapshots;
        #endregion

        // Called BEFORE Unity serializes (before domain reload, before save)
        public void OnBeforeSerialize()
        {
            LogDebug("OnBeforeSerialize - converting dictionaries to lists");

            _trackedComponentEntries.Clear();
            foreach(var kvp in _trackedComponentIds)
            {
                _trackedComponentEntries.Add(new TrackedComponentEntry
                {
                    instanceId = kvp.Key,
                    persistentIds = new List<uint>(kvp.Value)
                });
            }

            _sceneSnapshotEntries.Clear();
            foreach(var kvp in _savedSceneSnapshots)
            {
                _sceneSnapshotEntries.Add(new SceneSnapshotEntry
                {
                    sceneGuid = kvp.Key,
                    persistentIds = new List<uint>(kvp.Value)
                });
            }

            _unsavedIdsEntries.Clear();
            foreach(var kvp in _unsavedIds)
            {
                _unsavedIdsEntries.Add(new UnsavedIdsEntry
                {
                    sceneGuid = kvp.Key,
                    unsavedIds = new List<uint>(kvp.Value)
                });
            }

            LogDebug($"Serialized {_trackedComponentEntries.Count} tracked components, " +
                     $"{_sceneSnapshotEntries.Count} snapshots, " +
                     $"{_unsavedIdsEntries.Count} unsaved ID entries");
        }

        // Called AFTER Unity deserializes (after domain reload, after load)
        public void OnAfterDeserialize()
        {
            LogDebug("OnAfterDeserialize - reconstructing dictionaries from lists");

            _trackedComponentIds.Clear();
            foreach(var entry in _trackedComponentEntries)
            {
                _trackedComponentIds[entry.instanceId] = new HashSet<uint>(entry.persistentIds);
            }

            _savedSceneSnapshots.Clear();
            foreach(var entry in _sceneSnapshotEntries)
            {
                _savedSceneSnapshots[entry.sceneGuid] = new HashSet<uint>(entry.persistentIds);
            }

            _unsavedIds.Clear();
            foreach(var entry in _unsavedIdsEntries)
            {
                _unsavedIds[entry.sceneGuid] = new HashSet<uint>(entry.unsavedIds);
            }

            // Note: processedComponentsThisDomainCycle intentionally NOT restored
            // It should start empty after each domain reload
            _processedComponentsThisDomainCycle.Clear();

            LogDebug($"Deserialized {_trackedComponentIds.Count} tracked components, " +
                     $"{_savedSceneSnapshots.Count} snapshots, " +
                     $"{_unsavedIds.Count} unsaved ID entries");
        }

        internal void ClearAll()
        {
            _trackedComponentIds.Clear();
            _savedSceneSnapshots.Clear();
            _unsavedIds.Clear();
            _processedComponentsThisDomainCycle.Clear();

            _trackedComponentEntries.Clear();
            _sceneSnapshotEntries.Clear();
            _unsavedIdsEntries.Clear();

            LogDebug("Cleared all transient registrar data");
        }
    }

    // NOTE(Jazz): These intermediate wrappers are required because Unity does not support serialization of Dictionaries or Hashsets.
    #region Serialization Wrappers
    [System.Serializable] internal class TrackedComponentEntry
    {
        public int instanceId;
        public List<uint> persistentIds;
    }

    [System.Serializable] internal class SceneSnapshotEntry
    {
        public string sceneGuid;
        public List<uint> persistentIds;
    }

    [System.Serializable] internal class UnsavedIdsEntry
    {
        public string sceneGuid;
        public List<uint> unsavedIds;
    }
    #endregion
}
