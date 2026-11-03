using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AngleUtils : MonoBehaviour
{
    // how far on the level: a spot on a leaning ket is up off the ground
    public static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = b.y;
        return Vector3.Distance(a, b);
    }

    public static bool RotationsApproximatelyEqual(Transform t1, Transform t2, float threshold = 1f)
    {
        float angle = Quaternion.Angle(t1.rotation, t2.rotation);
        return angle < threshold;
    }
}
