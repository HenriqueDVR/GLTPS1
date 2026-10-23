using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ItemSpawnManager : MonoBehaviour
{
    [SerializeField] private GameObject[] item;
    public int[] spawnChance;
    private Save save = new Save();

    // set by WorldGenerator so seeds give the same items
    [System.NonSerialized] public WorldRandom random;
    [System.NonSerialized] public ulong itemId;

    void Start()
    {
        save = save.LoadData("difficultySetting");
        if (save != null)
        {
            for (int i = 0; i < spawnChance.Length; i++)
            {
                spawnChance[i] = save.itemSpawnChance[i];
            }
        }
        RandomWeightedSpawnItem();
    }

    private void RandomWeightedSpawnItem()
    {
        int randomIndex = random != null ? random.NextWeightedIndex(spawnChance) : RandomWeightedGenerator.GenerateRandomIndex(spawnChance);
        if (item[randomIndex] != null && !(itemId != 0 && CoopItem.IsTaken(itemId)))
        {
            GameObject newItem = Instantiate(item[randomIndex], transform.position, transform.rotation);
            if (itemId != 0)
            {
                CoopItem.Track(newItem, itemId);
            }
        }
#if DUMP_WORLDGEN
        Debug.Log("WorldGenItem " + transform.root.name + "/" + name + " -> " + (item[randomIndex] != null ? item[randomIndex].name : "nothing"));
#endif
    }
}
