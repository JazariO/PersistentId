using System.Collections.Generic;

namespace Proselyte.PersistentIdSystem
{
    [System.Serializable]
    public class TrackedComponentIds
    {
        public Dictionary<int, HashSet<uint>> trackedComponentIds = new();
    }

    [System.Serializable]
    public class TrackedComponentEntry
    {
        public int instanceId;
        public List<uint> persistentIds;
    }

    [System.Serializable]
    public class TrackedComponentIdsWrapper
    {
        public List<TrackedComponentEntry> trackedComponentEntries = new List<TrackedComponentEntry>();
    }
}
