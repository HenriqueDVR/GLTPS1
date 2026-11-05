using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;
public class CharacterItemInteraction : MonoBehaviour, IInteractionSource
{
    public PlayerInputInitialize playerInputScript;
    private bool isCampfire;
    public bool isInCampfireArea;


    [SerializeField] private Collider other;
    [SerializeField] private RawImage foodbarPrefab;
    [SerializeField] private GameObject panel;

    public float hungerLevel = 100f;
    [SerializeField] private float eatValue = 20f;
    [SerializeField] private int maxRandomFoodCount = 5;
    private const int maxFoodCount = 18;

    public float thirstLevel = 100f;
    [SerializeField] private float drinkValue = 20f;
    [SerializeField] private float waterRefillRate = 1f;

    public float temperatureLevel = 100f;

    // how long she's been out of something, CharacterStatus counts it
    public float OutFor;
    [SerializeField] private float fuelValue = 20f;
    public bool isUnderCeiling;
    [SerializeField] float isUnderCeilingHeight = 20f;
    [SerializeField] private GameObject campfirePrefab;
    [SerializeField] private Vector3 campfireOffsetPosition;
    [SerializeField] private VehicleFuelManager vehicleFuelManagerScript;


    [SerializeField] private CharacterStatus characterStatusScript;
    public Animator anim;
    [SerializeField] private PlayerGunController playerGunControllerScript;
    [SerializeField] private int maxRandomAmmoCount = 5;
    [SerializeField] private TextMeshProUGUI ammoCountText;


    public AudioSource _audioSource;
    [SerializeField] private AudioClip[] _audioClip;

    void OnEnable()
    {
        playerInputScript.actions.Player.Eat.performed += Eat;
        playerInputScript.actions.Player.Drink.performed += Drink;
        playerInputScript.actions.Player.PlaceCampfire.performed += PlaceCampfire;
    }

    void OnDisable()
    {
        playerInputScript.actions.Player.Eat.performed -= Eat;
        playerInputScript.actions.Player.Drink.performed -= Drink;
        playerInputScript.actions.Player.PlaceCampfire.performed -= PlaceCampfire;
    }

    public void Collect(List<Interaction> into)
    {
        Collider item = other;
        if (item == null)
        {
            return;
        }
        if (isCampfire && item.gameObject.CompareTag("CampfireInteractArea") && item.gameObject.GetComponentInParent<CampfireFuelLevel>().currentFuelLevel > 0)
        {
            into.Add(new Interaction { Verb = Verb.PutOut, Press = Press.Tap, Target = item.transform, Priority = 1, Run = () => PutOut(item), Source = "campfire" });
        }
    }

    private void PutOut(Collider area)
    {
        CampfireFuelLevel campfire = area.gameObject.GetComponentInParent<CampfireFuelLevel>();
        if (campfire.currentFuelLevel > 0)
        {
            campfire.currentFuelLevel = 0;
            CoopCampfire.Extinguished(campfire);
            _audioSource.PlayOneShot(_audioClip[1]);
            isCampfire = false;
        }
    }

    public bool FoodHasRoom => characterStatusScript.index < maxFoodCount;

    public void GiveAmmo()
    {
        //increase ammo count by random amount
        int rounds = Random.Range(1, maxRandomAmmoCount + 1);
        // only yuuri has the gun
        if (!CoopShots.GiveAmmoToGunOwner(playerGunControllerScript, rounds))
        {
            playerGunControllerScript.AddSpareAmmo(rounds);
        }
        PlayPickupSound();
    }

    public void PlayPickupSound()
    {
        _audioSource.PlayOneShot(_audioClip[2]);
    }

    public int RollFood()
    {
        return Random.Range(1, maxRandomFoodCount + 1);
    }

    // host owns the food count in coop
    public void AddFood(int count)
    {
        int foodCount = Mathf.Min(count, maxFoodCount - characterStatusScript.index);
        for (int i = 0; i < foodCount; i++)
        {
            //add food ui object and set rotation and position on canvas
            characterStatusScript.newFoodbar.Add(Instantiate(foodbarPrefab.gameObject, panel.transform));
            characterStatusScript.newFoodbar[characterStatusScript.index].GetComponent<RectTransform>().localRotation = Quaternion.identity;
            characterStatusScript.newFoodbar[characterStatusScript.index].GetComponent<RectTransform>().anchoredPosition3D = new Vector3(foodbarPrefab.rectTransform.anchoredPosition3D.x + characterStatusScript.offsetXPos
                                                                                            , foodbarPrefab.rectTransform.anchoredPosition3D.y
                                                                                            , foodbarPrefab.rectTransform.anchoredPosition3D.z);
            //offset position for adding ui objects
            characterStatusScript.offsetXPos += -30;
            characterStatusScript.index++;
        }
    }

    public void RemoveFood(int count)
    {
        for (int i = 0; i < count && characterStatusScript.index > 0; i++)
        {
            Destroy(characterStatusScript.newFoodbar[characterStatusScript.index - 1]);
            characterStatusScript.newFoodbar.RemoveAt(characterStatusScript.index - 1);
            characterStatusScript.offsetXPos += 30;
            characterStatusScript.index--;
        }
    }

    public void SetFood(int count)
    {
        count = Mathf.Clamp(count, 0, maxFoodCount);
        if (count > characterStatusScript.index)
        {
            AddFood(count - characterStatusScript.index);
        }
        else
        {
            RemoveFood(characterStatusScript.index - count);
        }
    }

    private bool IsCoopPuppet => CoopPlayers.Instance != null && CoopPlayers.Instance.Remote != null && transform.IsChildOf(CoopPlayers.Instance.Remote);

#if DUMP_WORLDGEN
    // -coopsupplies
    public void DebugEatAndDrink()
    {
        if (characterStatusScript.index > 0)
        {
            hungerLevel += eatValue;
            RemoveFood(1);
        }
        // stop at half so both sides can be compared
        if (characterStatusScript.waterLevel > 100f)
        {
            thirstLevel += drinkValue;
            characterStatusScript.waterLevel -= drinkValue;
        }
    }

    public string DebugSupplies()
    {
        return "food " + characterStatusScript.index + " water " + characterStatusScript.waterLevel.ToString("F2");
    }
#endif

    // from CharacterStatus: an AI or remote girl has this script off and still needs it, for the cold and the rain
    public void UpdateCeiling()
    {
        isUnderCeiling = Physics.Raycast(transform.position + new Vector3(0, 2f, 0), Vector3.up, isUnderCeilingHeight, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
    }

    private CampfireFuelLevel warmth;

    public bool AtLitFire => warmth != null && warmth.currentFuelLevel > 0f;

    // a lit fire she's in the warmth of, whoever she is: no trigger brings it to an AI or a remote girl
    public CampfireFuelLevel FireHere()
    {
        int count = Physics.OverlapSphereNonAlloc(transform.position + Vector3.up * 0.5f, 0.3f, feltNear, Physics.AllLayers, QueryTriggerInteraction.Collide);
        for (int i = 0; i < count; i++)
        {
            if (feltNear[i].CompareTag("CampfireWarmthArea"))
            {
                CampfireFuelLevel fire = feltNear[i].GetComponent<CampfireFuelLevel>();
                if (fire != null && fire.currentFuelLevel > 0f)
                {
                    return fire;
                }
            }
        }
        return null;
    }

    // the AI girl seeing to the other one
    public void LightFireNow()
    {
        TryPlaceCampfire();
    }

    private readonly Collider[] feltNear = new Collider[16];

    // the AI girl has no controller to bring her a fire's trigger: she looks for it. per physics step's worth, as the trigger
    public void FeelForFire()
    {
        int count = Physics.OverlapSphereNonAlloc(transform.position + Vector3.up * 0.5f, 0.3f, feltNear, Physics.AllLayers, QueryTriggerInteraction.Collide);
        warmth = null;
        for (int i = 0; i < count; i++)
        {
            if (feltNear[i].CompareTag("CampfireWarmthArea"))
            {
                warmth = feltNear[i].GetComponent<CampfireFuelLevel>();
                break;
            }
        }
        isInCampfireArea = warmth != null;
        if (AtLitFire)
        {
            temperatureLevel += CampFire.WarmPerSecond * Time.deltaTime;
        }
    }

    // the AI girl, nobody presses anything for her
    public void LookAfterSelf(float below)
    {
        if (hungerLevel < below && characterStatusScript.index > 0)
        {
            hungerLevel += eatValue;
            RemoveFood(1);
            anim.SetTrigger("eat");
        }
        else if (thirstLevel < below && characterStatusScript.waterLevel >= drinkValue)
        {
            thirstLevel += drinkValue;
            characterStatusScript.waterLevel -= drinkValue;
            anim.SetTrigger("drink");
        }
    }

    private void Eat(InputAction.CallbackContext context)
    {
        if (context.performed && characterStatusScript.index > 0 && !anim.GetCurrentAnimatorStateInfo(3).IsTag("ADS") && Time.timeScale == 1)
        {
            hungerLevel += eatValue;
            RemoveFood(1);

            //trigger eating animation
            anim.SetTrigger("eat");

            _audioSource.PlayOneShot(_audioClip[Random.Range(4, _audioClip.Length)]);
        }
    }

    private void Drink(InputAction.CallbackContext context)
    {
        if (context.performed && characterStatusScript.waterLevel > 0 && !anim.GetCurrentAnimatorStateInfo(3).IsTag("ADS") && Time.timeScale == 1)
        {
            thirstLevel += drinkValue;
            characterStatusScript.waterLevel -= drinkValue;

            anim.SetTrigger("drink");

            _audioSource.PlayOneShot(_audioClip[3]);
        }
    }

    private void PlaceCampfire(InputAction.CallbackContext context)
    {
        if (context.performed)
        {
            TryPlaceCampfire();
        }
    }

    // its fuel comes out of the ket, so the ket's by the fire: not from miles off, not off its moving back seat
    private const float campfireKetReach = 25f;

    private bool KetAtHand()
    {
        return !anim.GetCurrentAnimatorStateInfo(1).IsTag("Ket") && Vector3.Distance(transform.position, vehicleFuelManagerScript.transform.position) < campfireKetReach;
    }

    private void TryPlaceCampfire()
    {
        if (vehicleFuelManagerScript.currentFuel > 0 && KetAtHand())
        {
            // check if previous campfire is extinguised before allowing for placing new one
            if (characterStatusScript.newCampfire != null)
            {
                if (characterStatusScript.newCampfire.GetComponent<CampfireFuelLevel>().currentFuelLevel > 0)
                {
                    return;
                }
            }

            //spawn at raycast hit point
            RaycastHit hit;
            if (Physics.Raycast(transform.position + new Vector3(0, 0.4f, 0), transform.forward, out hit, campfireOffsetPosition.magnitude))
            {
                SpawnCampfire(hit.point, transform.rotation);
            }
            //spawn at default offset position
            else
            {
                SpawnCampfire(transform.position + transform.forward, transform.rotation);
            }
            CoopCampfire.Placed(characterStatusScript.newCampfire);
            vehicleFuelManagerScript.currentFuel -= fuelValue;
            _audioSource.PlayOneShot(_audioClip[0]);
        }
    }

    public GameObject SpawnCampfire(Vector3 position, Quaternion rotation)
    {
        characterStatusScript.newCampfire = Instantiate(campfirePrefab, position, rotation);
        characterStatusScript.newCampfire.AddComponent<CampFire>();
        return characterStatusScript.newCampfire;
    }

#if DUMP_WORLDGEN
    // -coopcampfire
    public void DebugPlaceCampfire()
    {
        TryPlaceCampfire();
    }

    public void DebugPutOutCampfire()
    {
        if (characterStatusScript.newCampfire != null && characterStatusScript.newCampfire.GetComponent<CampfireFuelLevel>().currentFuelLevel > 0f)
        {
            characterStatusScript.newCampfire.GetComponent<CampfireFuelLevel>().currentFuelLevel = 0f;
            CoopCampfire.Extinguished(characterStatusScript.newCampfire.GetComponent<CampfireFuelLevel>());
        }
    }

    public string DebugCampfire()
    {
        GameObject newCampfire = characterStatusScript.newCampfire;
        if (newCampfire == null)
        {
            return "campfire none";
        }
        CoopCampfire coopCampfire = newCampfire.GetComponent<CoopCampfire>();
        return "campfire " + (coopCampfire != null ? coopCampfire.Id.ToString() : "?") + " fuel " + newCampfire.GetComponent<CampfireFuelLevel>().currentFuelLevel.ToString("F0") + " tank " + vehicleFuelManagerScript.currentFuel.ToString("F1");
    }
#endif

    void OnTriggerStay(Collider other)
    {
        if (other.gameObject.CompareTag("Water"))
        {
            if (!IsCoopPuppet)
            {
                characterStatusScript.waterLevel += waterRefillRate * Time.fixedDeltaTime;
            }
        }
        else if (other.gameObject.CompareTag("CampfireInteractArea"))
        {
            isCampfire = true;
            this.other = other;
        }
        else if (other.gameObject.CompareTag("CampfireWarmthArea"))
        {
            isInCampfireArea = true;
            warmth = other.gameObject.GetComponent<CampfireFuelLevel>();
            if (other.gameObject.GetComponent<CampfireFuelLevel>().currentFuelLevel > 0)
            {
                temperatureLevel += CampFire.WarmPerSecond * Time.fixedDeltaTime;
            }
        }
    }

    void OnTriggerExit(Collider other)
    {
        if (other.gameObject.CompareTag("CampfireWarmthArea"))
        {
            isInCampfireArea = false;
            warmth = null;
        }
        if (other.gameObject.CompareTag("CampfireInteractArea"))
        {
            isCampfire = false;
        }
    }

    void OnParticleCollision(GameObject other)
    {
        if (other.gameObject.CompareTag("Water") && !IsCoopPuppet)
        {
            // one call a frame: was a fixed amount a call, so a fast machine filled faster. 0.02 s a call at 60 fps
            characterStatusScript.waterLevel += waterRefillRate * 1.2f * Time.deltaTime;
        }
    }

    void OnDrawGizmos()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawRay(transform.position + new Vector3(0, .1f, 0), transform.forward);
        Gizmos.DrawRay(transform.position + new Vector3(0, 2, 0), Vector2.up * isUnderCeilingHeight);
    }
}
