using System;
using UnityEngine;

public static class NpcCampaign
{
    public static event Action<string, string> Changed;
    public static string Current(string worldId)
    {
        string key = "NpcCampaign." + worldId;
        string value = PlayerPrefs.GetString(key, "");
        if (Guid.TryParse(value, out _)) return value;
        return Store(key);
    }
    public static string WriterId
    {
        get
        {
            string value = PlayerPrefs.GetString("NpcCampaign.Writer", "");
            return Guid.TryParse(value, out _) ? value : Store("NpcCampaign.Writer");
        }
    }
    public static string Reset(string worldId)
    {
        string value = Store("NpcCampaign." + worldId);
        Changed?.Invoke(worldId, value);
        return value;
    }
    static string Store(string key)
    {
        string value = Guid.NewGuid().ToString();
        PlayerPrefs.SetString(key, value); PlayerPrefs.Save();
        return value;
    }
}
