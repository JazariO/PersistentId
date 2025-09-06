#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Proselyte.PersistentIdSystem
{
    /// <summary>
    /// Scriptable Object that maintains a registry of all registered persistent IDs.
    /// Serves as the single source of truth for ID uniqueness validation.
    /// </summary>
    [CreateAssetMenu(fileName = "PersistentIdRegistry", menuName = "System/PersistentId Registry")]
    public class PersistentIdRegistrySO : ScriptableObject
    {
        // NOTE(Jazz): Unity cannot serialize a Dictionary directly so use this 
        // wrapper class to let unity serialize the data with the scriptable 
        // object.

        [System.Serializable]
        internal class SceneIdData
        {
            public string sceneGuid;
            public List<uint> registeredIds = new();
        }

        [UnityEngine.SerializeField]
        private List<SceneIdData> sceneDataList = new();

        private Dictionary<string, HashSet<uint>> sceneIdRegistry;

        private string previousGameObjectPath = string.Empty;
        private string previousGameObjectSceneGuid = string.Empty;

        private void OnEnable()
        {
            InitializeRegistry();
        }

        private void InitializeRegistry()
        {
            if(sceneIdRegistry == null)
            {
                sceneIdRegistry = new Dictionary<string, HashSet<uint>>();

                // Rebuild dictionary from serialized data
                foreach(var sceneData in sceneDataList)
                {
                    if(!string.IsNullOrEmpty(sceneData.sceneGuid))
                        sceneIdRegistry[sceneData.sceneGuid] = new HashSet<uint>(sceneData.registeredIds);
                }
            }
        }

        private void UpdateSerializedData()
        {
            sceneDataList.Clear();
            foreach(var kvp in sceneIdRegistry)
            {
                var sceneData = new SceneIdData()
                {
                    sceneGuid = kvp.Key,
                    registeredIds = new List<uint>(kvp.Value)
                };
                sceneDataList.Add(sceneData);
            }
            UnityEditor.EditorUtility.SetDirty(this);
        }

        // TODO(Jazz): call from [InitializeOnLoadMethod] EditorApplication.delayCall in PersistentIdManager class.
        static void CleanupMissingScenes(PersistentIdRegistrySO registryGuids)
        {

        }

        internal bool IsIdRegisteredInScene(string sceneGuid, uint id)
        {
            InitializeRegistry();
            return sceneIdRegistry.ContainsKey(sceneGuid) && sceneIdRegistry[sceneGuid].Contains(id);
        }

        internal bool IsIdRegisteredGlobally(uint id)
        {
            InitializeRegistry();
            bool globalContainsId = sceneIdRegistry.Values.Any(hashSet => hashSet.Contains(id));
            return globalContainsId;
        }

        public string GetSceneGuid(GameObject gameObject)
        {
            string curr_path = gameObject.scene.path;
            if(curr_path != null && !string.IsNullOrEmpty(curr_path))
            {
                if(curr_path == previousGameObjectPath)
                    return previousGameObjectSceneGuid;

                var curr_scene_guid = AssetDatabase.AssetPathToGUID(gameObject.scene.path);
                previousGameObjectSceneGuid = curr_scene_guid;
                previousGameObjectPath = curr_path;

                return curr_scene_guid;
            }

            return string.Empty;
        }

        public bool RegisterId(string sceneGuid, uint id)
        {
            InitializeRegistry();

            if(id == 0 || string.IsNullOrEmpty(sceneGuid))
                return false;

            if(IsIdRegisteredGlobally(id))
                return false;

            if(!sceneIdRegistry.ContainsKey(sceneGuid))
            {
                sceneIdRegistry[sceneGuid] = new HashSet<uint>();
            }

            sceneIdRegistry[sceneGuid].Add(id);
            UpdateSerializedData();
            return true;
        }

        /// <summary>
        /// Registers an ID for a specific GameObject's scene
        /// </summary>
        public bool RegisterId(GameObject gameObject, uint id)
        {
            if(gameObject == null) return false;

            string sceneGuid = GetSceneGuid(gameObject);
            if(string.IsNullOrEmpty(sceneGuid)) 
                return false;
            
            return RegisterId(sceneGuid, id);
        }

        public bool UnregisterId(uint id)
        {
            InitializeRegistry();

            bool removed = false;
            var scenesToRemove = new List<string>();

            // Search all scenes for this ID
            foreach(var kvp in sceneIdRegistry)
            {
                if(kvp.Value.Remove(id))
                {
                    removed = true;

                    // Mark empty scenes for removal
                    if(kvp.Value.Count == 0)
                    {
                        scenesToRemove.Add(kvp.Key);
                    }
                }
            }

            // Remove empty scenes
            foreach(var sceneGuid in scenesToRemove)
            {
                sceneIdRegistry.Remove(sceneGuid);
            }

            if(removed)
            {
                UpdateSerializedData();
            }

            return removed;
        }

        public bool RemoveScene(string sceneGuid)
        {
            InitializeRegistry();

            if(sceneIdRegistry.Remove(sceneGuid))
            {
                UpdateSerializedData();
                return true;
            }

            return false;
        }

        public int RegisteredCount
        {
            get
            {
                InitializeRegistry();
                return sceneIdRegistry.Values.Sum(hashSet => hashSet.Count);
            }
        }

        public IEnumerable<uint> RegisteredIds
        {
            get
            {
                InitializeRegistry();
                return sceneIdRegistry.Values.SelectMany(hashSet => hashSet);
            }
        }

        public int RegisteredSceneCount
        {
            get
            {
                InitializeRegistry();
                return sceneIdRegistry.Count;
            }
        }

        private IReadOnlyCollection<uint> GetSceneIds(string sceneGuid)
        {
            InitializeRegistry();
            return sceneIdRegistry.ContainsKey(sceneGuid)
                ? sceneIdRegistry[sceneGuid]
                : new HashSet<uint>();
        }

        private IReadOnlyCollection<string> GetRegisteredScenes()
        {
            InitializeRegistry();
            return sceneIdRegistry.Keys;
        }

        /// <summary>
        /// Generates a new unique ID that is not already registered
        /// </summary>
        public uint GenerateUniqueId()
        {
            InitializeRegistry();

            uint newId;
            do
            {
                int high = Random.Range(0, 1 << 16);
                int low = Random.Range(0, 1 << 16);
                newId = ((uint)high << 16) | (uint)low;
            }
            while(IsIdRegisteredGlobally(newId) || newId == 0);

            return newId;
        }

        /// <summary>
        /// Generates a random hex value and logs it
        /// </summary>
        [ContextMenu("Generate Random Hex", false, 0)]
        public void GenerateHexCode()
        {
            InitializeRegistry();

            uint newId;
            do
            {
                int high = Random.Range(1, int.MaxValue);
                int low = Random.Range(1, int.MaxValue);
                newId = ((uint)high << 28) | (uint)low;
            }
            while(IsIdRegisteredGlobally(newId) || newId == 0);
            Debug.Log($"newId: {newId} Hex: 0x{newId:X8}");
        }

        // TODO(Jazz): Remove debugging functions here
        [ContextMenu("Print All Ids", false, 1)]
        public void PrintAllIds()
        {
            InitializeRegistry();

            int registeredCount = RegisteredCount;
            if(registeredCount < 1)
            {
                Debug.Log("No IDs registered.");
                return;
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"===== All Registered IDs ({registeredCount} total across {RegisteredSceneCount} scenes) =====");

            foreach(var sceneGuid in sceneIdRegistry.Keys.OrderBy(guid => guid))
            {
                var ids = sceneIdRegistry[sceneGuid];
                sb.AppendLine($"\n--- Scene: {sceneGuid} ({ids.Count} IDs) ---");

                foreach(var id in ids.OrderBy(i => i))
                {
                    sb.AppendLine($"  Dec: {id}\tHex: 0x{id:X8}");
                }
            }

            Debug.Log(sb.ToString());
        }

        [ContextMenu("Remove All Ids", false, 2)]
        public void RemoveAllIds()
        {
            InitializeRegistry();

            var totalRemoved = RegisteredCount;
            sceneIdRegistry.Clear();
            UpdateSerializedData();

            Debug.Log($"Registry cleanup: Removed all {totalRemoved} IDs from {sceneDataList.Count} scenes");
        }

        /// <summary>
        /// Validates the internal consistency of the registry
        /// </summary>
        public void ValidateRegistry()
        {
            InitializeRegistry();

            // Get all scene GUIDs from the project
            var allSceneGuids = AssetDatabase.FindAssets("t:Scene").ToHashSet();

            // Find registry entries for scenes that no longer exist
            var scenesToRemove = sceneIdRegistry.Keys
                .Where(sceneGuid => !allSceneGuids.Contains(sceneGuid))
                .ToList();

            // Clean up missing scenes
            foreach(var missingSceneGuid in scenesToRemove)
            {
                var removedCount = sceneIdRegistry[missingSceneGuid].Count;
                RemoveScene(missingSceneGuid);
                Debug.Log($"Registry cleanup: Removed {removedCount} persistent IDs from missing scene: {missingSceneGuid}");
            }

            if(scenesToRemove.Count > 0)
            {
                Debug.Log($"Registry cleanup: Removed {scenesToRemove.Count} missing scenes");
            }

            // Remove duplicate IDs within scenes and invalid IDs
            foreach(var sceneGuid in sceneIdRegistry.Keys.ToList())
            {
                var idSet = sceneIdRegistry[sceneGuid];
                var validIds = idSet.Where(id => id != 0).ToHashSet();

                if(validIds.Count != idSet.Count)
                {
                    sceneIdRegistry[sceneGuid] = validIds;
                    Debug.Log($"Registry cleanup: Removed invalid IDs from scene {sceneGuid}");
                }
            }

            UpdateSerializedData();
        }
    }
}
#endif
