using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;
public class CharacterItemInteraction : MonoBehaviour
{
    public PlayerInputInitialize playerInputScript;
    private bool isFood;
    private bool isAmmo;
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
    [SerializeField] private float temperatureRefillValue = 1f;
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
        playerInputScript.actions.Player.Interact.performed += Interact;
        playerInputScript.actions.Player.Eat.performed += Eat;
        playerInputScript.actions.Player.Drink.performed += Drink;
        playerInputScript.actions.Player.PlaceCampfire.performed += PlaceCampfire;
    }

    void OnDisable()
    {
        playerInputScript.actions.Player.Interact.performed -= Interact;
        playerInputScript.actions.Player.Eat.performed -= Eat;
        playerInputScript.actions.Player.Drink.performed -= Drink;
        playerInputScript.actions.Player.PlaceCampfire.performed -= PlaceCampfire;
    }

    private void Interact(InputAction.CallbackContext context)
    {
        if (context.performed && Time.timeScale == 1)
        {
            if (isFood && characterStatusScript.index < maxFoodCount && other.gameObject.CompareTag("Food"))
            {
                if (TakeItem())
                {
                    GiveFood();
                }
                isFood = false;
            }
            else if (isAmmo && other.gameObject.CompareTag("Ammo"))
            {
                if (TakeItem())
                {
                    GiveAmmo();
                }
                isAmmo = false;
            }
            else if (isCampfire && other.gameObject.CompareTag("CampfireInteractArea"))
            {
                if (other.gameObject.GetComponentInParent<CampfireFuelLevel>().currentFuelLevel > 0)
                {
                    other.gameObject.GetComponentInParent<CampfireFuelLevel>().currentFuelLevel = 0;
                    CoopCampfire.Extinguished(other.gameObject.GetComponentInParent<CampfireFuelLevel>());
                    _audioSource.PlayOneShot(_audioClip[1]);
                    isCampfire = false;
                }
            }
        }
    }

    // false if the loot comes later (coop friend asks the host first)
    private bool TakeItem()
    {
        CoopItem coopItem = CoopItem.Find(other);
        if (coopItem != null && !CoopSession.Instance.IsHost)
        {
            coopItem.RequestAsFriend();
            return false;
        }
        coopItem?.TakeAsHost();
        Destroy(other.gameObject);
        return true;
    }

    public void GiveFood()
    {
        //increase food count by random amount
        AddFood(RollFood());
        PlayPickupSound();
    }

    public void GiveAmmo()
    {
        //increase ammo count by random amount
        playerGunControllerScript.ammoCount += Random.Range(1, maxRandomAmmoCount + 1);

        //change ammo ui
        ammoCountText.text = playerGunControllerScript.ammoCount.ToString();
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

    // -cooptake
    public void DebugTake(Collider item)
    {
        other = item;
        if (TakeItem())
        {
            if (item.CompareTag("Ammo"))
            {
                GiveAmmo();
            }
            else
            {
                GiveFood();
            }
        }
    }
#endif

    void Update()
    {
        //check if character is under ceiling
        if (Physics.Raycast(transform.position + new Vector3(0, 2f, 0), Vector2.up, isUnderCeilingHeight))
        {
            isUnderCeiling = true;
        }
        else
        {
            isUnderCeiling = false;
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

    private void TryPlaceCampfire()
    {
        if (vehicleFuelManagerScript.currentFuel > 0)
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
        if (other.gameObject.CompareTag("Food"))
        {
            isFood = true;
            this.other = other;
        }
        else if (other.gameObject.CompareTag("Ammo"))
        {
            isAmmo = true;
            this.other = other;
        }
        else if (other.gameObject.CompareTag("Water"))
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
            if (other.gameObject.GetComponent<CampfireFuelLevel>().currentFuelLevel > 0)
            {
                temperatureLevel += temperatureRefillValue;
            }
        }
    }

    void OnTriggerExit(Collider other)
    {
        if (other.gameObject.CompareTag("Food"))
        {
            isFood = false;
        }
        if (other.gameObject.CompareTag("Ammo"))
        {
            isAmmo = false;
        }
        if (other.gameObject.CompareTag("CampfireWarmthArea"))
        {
            isInCampfireArea = false;
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
            characterStatusScript.waterLevel += waterRefillRate * Time.fixedDeltaTime;
        }
    }

    void OnDrawGizmos()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawRay(transform.position + new Vector3(0, .1f, 0), transform.forward);
        Gizmos.DrawRay(transform.position + new Vector3(0, 2, 0), Vector2.up * isUnderCeilingHeight);
    }
}
