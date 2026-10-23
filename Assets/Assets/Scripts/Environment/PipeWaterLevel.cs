using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PipeWaterLevel : MonoBehaviour
{
    public float waterlevel = 100f;

    public float StartLevel { get; private set; }

    void Awake()
    {
        StartLevel = waterlevel;
    }

    void Start()
    {
        CoopPipes.Restore(this);
    }
}
