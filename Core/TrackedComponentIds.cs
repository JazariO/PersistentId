using System.Collections.Generic;

namespace Proselyte.Persistence
{
    [System.Serializable]
    internal class TrackedComponentIds
    {
        public Dictionary<int, HashSet<uint>> trackedComponentIds = new();
    }

    [System.Serializable]
    internal class TrackedComponentEntry
    {
        public int instanceId;
        public List<uint> persistentIds;
    }

    [System.Serializable]
    internal class TrackedComponentIdsWrapper
    {
        public List<TrackedComponentEntry> trackedComponentEntries = new List<TrackedComponentEntry>();
    }
}
