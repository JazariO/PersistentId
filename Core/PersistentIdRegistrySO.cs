using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using static UnityEngine.GraphicsBuffer;

/// <summary>
/// Scriptable Object that maintains a registry of all registered persistent IDs.
/// Serves as the single source of truth for ID uniqueness validation.
/// </summary>
[CreateAssetMenu(fileName = "PersistentIdRegistry", menuName = "System/PersistentId Registry")]
public class PersistentIdRegistrySO : ScriptableObject
{
    [SerializeField]
    private List<uint> registeredIds = new List<uint>();

    private HashSet<uint> idHashSet;

    private void OnEnable()
    {
        InitializeHashSet();
    }

    private void InitializeHashSet()
    {
        if(idHashSet == null)
        {
            idHashSet = new HashSet<uint>(registeredIds);
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
            newId = (uint)Random.Range(1, int.MaxValue);
        }
        while(idHashSet.Contains(newId) || newId == 0);

        return newId;
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

