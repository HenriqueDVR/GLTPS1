using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class Save
{
    public float hungerRate;
    public float thirstRate;
    public List<int> temperatureLossRate;
    public List<int> weatherChance;
    public List<int> itemSpawnChance;

    private static Save difficulty;
    private static bool difficultyKnown;

    // read once, every item spawner of a tile asks
    public static Save Difficulty()
    {
        if (!difficultyKnown)
        {
            difficulty = new Save().LoadData("difficultySetting");
            difficultyKnown = true;
        }
        return difficulty;
    }

    public static void DifficultyChanged()
    {
        difficultyKnown = false;
    }

    public void SaveData(Save save, string saveKey)
    {
        DifficultyChanged();
        // serialize json
        string saveJson = JsonUtility.ToJson(save);
        PlayerPrefs.SetString(saveKey, saveJson);
        PlayerPrefs.Save();
    }

    public Save LoadData(string saveKey)
    {
        if (PlayerPrefs.HasKey(saveKey))
        {
            // deserialize json
            string saveJson = PlayerPrefs.GetString(saveKey);
            Save save = JsonUtility.FromJson<Save>(saveJson);

            return save;
        }
        else
        {
            return null;
        }
    }
}
