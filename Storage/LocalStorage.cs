using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

public class StoredItem
{
    public string? Value { get; set; }
    public DateTime? Expiry { get; set; }  // Null = never expires
}

public static class LocalStorage
{
    // Store localStorage file in AppData\Local instead of Program Files to avoid permission issues
    private static readonly string AppDataFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), 
        "Railax");
    private static readonly string FilePath = Path.Combine(AppDataFolder, "localstorage.json");

    private static Dictionary<string, StoredItem> data = new();

    static LocalStorage()
    {
        // Ensure the directory exists
        Directory.CreateDirectory(AppDataFolder);
        
        if (File.Exists(FilePath))
        {
            try
            {
                string json = File.ReadAllText(FilePath);
                data = JsonConvert.DeserializeObject<Dictionary<string, StoredItem>>(json)
                       ?? new Dictionary<string, StoredItem>();
                CleanupExpired();
            }
            catch
            {
                data = new Dictionary<string, StoredItem>();
            }
        }
    }

    public static void SetItem(string key, string value, TimeSpan? expiryTime = null)
    {
        data[key] = new StoredItem
        {
            Value = value,
            Expiry = expiryTime.HasValue ? DateTime.Now.Add(expiryTime.Value) : null
        };
        SaveToFile();
    }

    public static string? GetItem(string key)
    {
        if (data.TryGetValue(key, out var item))
        {
            if (item.Expiry == null || item.Expiry > DateTime.Now)
                return item.Value;

            // Expired → remove automatically
            data.Remove(key);
            SaveToFile();
        }
        return null;
    }

    public static void RemoveItem(string key)
    {
        if (data.ContainsKey(key))
        {
            data.Remove(key);
            SaveToFile();
        }
    }

    public static void Clear()
    {
        data.Clear();
        SaveToFile();
    }

    private static void CleanupExpired()
    {
        bool changed = false;
        foreach (var key in new List<string>(data.Keys))
        {
            var item = data[key];
            if (item.Expiry != null && item.Expiry <= DateTime.Now)
            {
                data.Remove(key);
                changed = true;
            }
        }
        if (changed) SaveToFile();
    }

    private static void SaveToFile()
    {
        string json = JsonConvert.SerializeObject(data, Formatting.Indented);
        File.WriteAllText(FilePath, json);
    }
}
