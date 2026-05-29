using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Video;

/// <summary>
/// Configurable list of remote Addressable videos (no hardcoded keys in gameplay code).
/// Create via Assets > Create > AR > Remote Video Catalog.
/// </summary>
[CreateAssetMenu(fileName = "RemoteVideoCatalog", menuName = "AR/Remote Video Catalog")]
public class RemoteVideoCatalog : ScriptableObject
{
    [Serializable]
    public class Entry
    {
        [Tooltip("Shown on UI buttons / debug logs.")]
        public string displayName;
        public AssetReferenceT<VideoClip> video;
    }

    public List<Entry> entries = new List<Entry>();

    public int Count => entries?.Count ?? 0;

    public int ValidEntryCount
    {
        get
        {
            if (entries == null) return 0;
            int n = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i] != null && entries[i].video != null && entries[i].video.RuntimeKeyIsValid())
                    n++;
            }
            return n;
        }
    }

    public int FindIndexByGuid(string guid)
    {
        if (entries == null || string.IsNullOrEmpty(guid))
            return -1;

        for (int i = 0; i < entries.Count; i++)
        {
            if (entries[i]?.video != null && entries[i].video.AssetGUID == guid)
                return i;
        }
        return -1;
    }

    public bool TryGetEntry(int index, out Entry entry)
    {
        entry = null;
        if (entries == null || index < 0 || index >= entries.Count)
            return false;

        entry = entries[index];
        return entry != null && entry.video != null && entry.video.RuntimeKeyIsValid();
    }

    public string GetDisplayName(int index)
    {
        if (entries == null || index < 0 || index >= entries.Count)
            return $"Video {index + 1}";

        return string.IsNullOrWhiteSpace(entries[index].displayName)
            ? $"Video {index + 1}"
            : entries[index].displayName;
    }
}
