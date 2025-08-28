using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Scriptable Object that maintains a registry of all registered persistent IDs.
/// Serves as the single source of truth for ID uniqueness validation.
/// </summary>
[CreateAssetMenu(fileName = "PersistentIdRegistry", menuName = "System/PersistentId Registry")]
public class PersistentIdRegistrySO : ScriptableObject
{
    [SerializeField]
    private List<uint> registeredIds = new List<uint>();

    private HashSet<uint> idHashSet; // NOTE(Jazz): May need to change to a better structure for storing values.

    private void OnEnable()
    {
        InitializeHashSet();
    }

    private void InitializeHashSet()
    {
        if(idHashSet == null)
        {
            idHashSet = new HashSet<uint>(registeredIds);
        } else if(idHashSet.Count >= int.MaxValue)
        {
            Debug.LogError($"Persistent ID Registry has reached the practical upper limit of {int.MaxValue} entries due to HashSet limitations." +
                $"Do you really need this many ID values? " +
                $"You may need to shard the idHashSet or consider alternative storage strategies.");
        }
    }

    public bool IsIdRegistered(uint id)
    {
        InitializeHashSet();
        return idHashSet.Contains(id);
    }

    public bool RegisterId(uint id)
    {
        InitializeHashSet();

        if(id == 0 || idHashSet.Contains(id))
            return false;

        registeredIds.Add(id);
        idHashSet.Add(id);

#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
#endif

        return true;
    }

    public bool UnregisterId(uint id)
    {
        InitializeHashSet();

        if(idHashSet.Remove(id))
        {
            registeredIds.Remove(id);

#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif

            return true;
        }

        return false;
    }

    public int RegisteredCount => registeredIds.Count;

    public IReadOnlyList<uint> RegisteredIds => registeredIds;

    /// <summary>
    /// Generates a new unique ID that is not already registered
    /// </summary>
    public uint GenerateUniqueId()
    {
        InitializeHashSet();

        uint newId;
        do
        {
            int high = Random.Range(0, 1 << 16);
            int low = Random.Range(0, 1 << 16);
            newId = ((uint)high << 16) | (uint)low;
        }
        while(idHashSet.Contains(newId) || newId == 0);

        return newId;
    }

    /// <summary>
    /// Generates a random hex value and logs it
    /// </summary>
    [ContextMenu("Generate Random Hex", false, 0)]
    public void GenerateHexCode()
    {
        InitializeHashSet();

        uint newId;
        do
        {
            int high = Random.Range(1, int.MaxValue);
            int low =  Random.Range(1, int.MaxValue);
            newId = ((uint)high << 28) | (uint)low;
        }
        while(idHashSet.Contains(newId) || newId == 0);
        Debug.Log($"newId: {newId} Hex: 0x{newId:X8}");
    }

    // TODO(Jazz): Remove debugging functions here
    [ContextMenu("Print Ids", false, 0)]
    public void PrintAllIds()
    {
        if(registeredIds.Count < 1)
        {
            Debug.Log("No Ids Registered.");
            return;
        }

        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.AppendLine("===== All Registered Ids =====");
        foreach(uint id in RegisteredIds)
        {
            sb.AppendLine($"Dec: {id}\tHex: 0x{id:X8}");
        }
        Debug.Log(sb.ToString());
    }

    [ContextMenu("Remove All Ids", false, 0)]
    public void RemoveAllIds()
    {
        for(int i = RegisteredIds.Count - 1; i >= 0; i--)
        {
            UnregisterId(RegisteredIds[i]);
        }
    }

    /// <summary>
    /// Validates the internal consistency of the registry
    /// </summary>
    public void ValidateRegistry()
    {
        InitializeHashSet();

        // Remove duplicates and zeros
        var uniqueIds = new HashSet<uint>();
        for(int i = registeredIds.Count - 1; i >= 0; i--)
        {
            var id = registeredIds[i];
            if(id == 0 || !uniqueIds.Add(id))
            {
                registeredIds.RemoveAt(i);
            }
        }

        // Rebuild hash set
        idHashSet = new HashSet<uint>(registeredIds);

#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
#endif
    }
}

