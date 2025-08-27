using UnityEngine;

/// <summary>
/// Serializable struct that stores a persistent uint ID for game objects.
/// Does not inherit from MonoBehaviour - designed to be composed into a MonoBehaviour as a property.
/// </summary>
[System.Serializable]
public struct PersistentId
{
    [SerializeField]
    private uint id;

    public uint Id => id;

    public bool IsValid => id != 0;

    public PersistentId(uint id)
    {
        this.id = id;
    }

    public void SetId(uint newId)
    {
        id = newId;
    }

    public void Clear()
    {
        id = 0;
    }

    public override string ToString()
    {
        return id == 0 ? "Invalid" : $"0x{id:X8}";
    }

    public override bool Equals(object obj)
    {
        return obj is PersistentId other && id == other.id;
    }

    public override int GetHashCode()
    {
        return id.GetHashCode();
    }

    public static bool operator ==(PersistentId a, PersistentId b)
    {
        return a.id == b.id;
    }

    public static bool operator !=(PersistentId a, PersistentId b)
    {
        return a.id != b.id;
    }

    public static implicit operator uint(PersistentId persistentId)
    {
        return persistentId.id;
    }
}

