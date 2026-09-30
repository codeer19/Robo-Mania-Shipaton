using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>One save boundary. CrazyGames commits a single document through its Data module.</summary>
public static class ProfileStore
{
    [Serializable] private sealed class Document { public int version = 1; public List<Item> items = new List<Item>(); }
    [Serializable] private sealed class Item { public string key; public string value; }
    private static readonly Dictionary<string, string> values = new Dictionary<string, string>();
    private static bool loaded, writable;
    private static int batchDepth;
    private static bool dirty;
    public static int Revision { get; private set; }
    public static bool PersistenceAvailable => !UsesPlatform || (loaded && writable);
    public static bool UsesPlatform
    {
        get
        {
#if CRAZYGAMES_BUILD
            return true;
#else
            return false;
#endif
        }
    }
    internal static bool LoadPlatform()
    {
        writable = false;
        try
        {
            if (!CrazyGamesPlatformService.TryReadProfile(out string json)) return false;
            var next = string.IsNullOrEmpty(json) ? new Document() : JsonUtility.FromJson<Document>(json);
            if (next == null || next.version != 1 || next.items == null) throw new FormatException("Unsupported profile format");
            var restored = new Dictionary<string, string>();
            foreach (var item in next.items)
            {
                if (item == null || string.IsNullOrEmpty(item.key) || item.value == null || restored.ContainsKey(item.key))
                    throw new FormatException("Invalid profile entry");
                restored.Add(item.key, item.value);
            }
            values.Clear();
            foreach (var item in restored) values.Add(item.Key, item.Value);
            loaded = writable = true;
            dirty = false;
            Revision++;
            return true;
        }
        catch (Exception e) { Debug.LogWarning("Profile could not be loaded; existing cloud data will not be overwritten. " + e.Message); return false; }
    }
    internal static void SuspendWrites() { writable = false; Revision++; }
    public static string GetString(string key, string fallback = "") => UsesPlatform ? (values.TryGetValue(key, out var value) ? value : fallback) : PlayerPrefs.GetString(key, fallback);
    public static int GetInt(string key, int fallback = 0) => UsesPlatform ? (int.TryParse(GetString(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : fallback) : PlayerPrefs.GetInt(key, fallback);
    public static float GetFloat(string key, float fallback = 0) => UsesPlatform ? (float.TryParse(GetString(key), NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ? value : fallback) : PlayerPrefs.GetFloat(key, fallback);
    public static bool HasKey(string key) => UsesPlatform ? values.ContainsKey(key) : PlayerPrefs.HasKey(key);
    public static void SetString(string key, string value) { if (UsesPlatform) { values[key] = value; dirty = true; } else PlayerPrefs.SetString(key, value); }
    public static void SetInt(string key, int value) { if (UsesPlatform) SetString(key, value.ToString(CultureInfo.InvariantCulture)); else PlayerPrefs.SetInt(key, value); }
    public static void SetFloat(string key, float value) { if (UsesPlatform) SetString(key, value.ToString(CultureInfo.InvariantCulture)); else PlayerPrefs.SetFloat(key, value); }
    public static void DeleteKey(string key) { if (UsesPlatform) { values.Remove(key); dirty = true; } else PlayerPrefs.DeleteKey(key); }
    public static IDisposable Batch() { batchDepth++; return new BatchScope(); }
    private sealed class BatchScope : IDisposable
    {
        private bool disposed;
        public void Dispose() { if (disposed) return; disposed = true; if (--batchDepth == 0) Save(); }
    }
    public static void Save()
    {
        if (batchDepth > 0) return;
        if (!UsesPlatform) { PlayerPrefs.Save(); return; }
        if (!dirty || !loaded || !writable) return;
        var document = new Document();
        foreach (var item in values) document.items.Add(new Item { key = item.Key, value = item.Value });
        string json = JsonUtility.ToJson(document);
        if (System.Text.Encoding.UTF8.GetByteCount(json) > 900000) { Debug.LogWarning("Profile save exceeds the supported size."); return; }
        if (CrazyGamesPlatformService.TryWriteProfile(json)) dirty = false;
        else writable = false;
    }
}
