using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class VehicleLeftTrigger : MonoBehaviour
{
    private static readonly Collider[] hits = new Collider[16];

    // checked on exit, the old enter/exit bools got stuck on
    public static bool Blocked(Component side)
    {
        if (side == null)
        {
            return false;
        }
        BoxCollider box = side.GetComponent<BoxCollider>();
        int count = Physics.OverlapBoxNonAlloc(box.transform.TransformPoint(box.center), Vector3.Scale(box.size, box.transform.lossyScale) * 0.5f, hits, box.transform.rotation, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            // the ket itself
            if (hits[i].transform.root == side.transform.root)
            {
                continue;
            }
            GameObject other = hits[i].gameObject;
            if (other.CompareTag("Untagged") || other.CompareTag("Metal") || other.CompareTag("Pipe") || other.CompareTag("Elevator"))
            {
                return true;
            }
        }
        return false;
    }
}
