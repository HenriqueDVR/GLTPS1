using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
public class PlayerInputInitialize : MonoBehaviour
{
    public InputActions actions;

    // the only thing that turns actions on and off
    public InputContextStack Contexts { get; private set; }

    void Awake()
    {
        actions = new InputActions();
        // the scene starts with chito played, Girl.SetControl takes over from here
        Contexts = new InputContextStack(actions, gameObject.name == "ChitoParent" ? InputContext.OnFoot : InputContext.None);
    }
}
