using UnityEngine;

/// <summary>
/// Keeps an officer's gun in his right hand while the animations play.
/// (The gun is a separate object, so without this it would float where the officer used to stand.)
/// </summary>
public class GunInHand : MonoBehaviour
{
    [Tooltip("The officer's right hand bone (mixamorig:RightHand).")]
    public Transform hand;
    [Tooltip("Where the gun sits, relative to the hand bone.")]
    public Vector3 localPosition = new Vector3(-0.047f, 0.096f, 0.023f);
    public Vector3 localEulerAngles = new Vector3(283.1f, 358.9f, 105f);

    void LateUpdate()
    {
        if (hand == null) return;
        transform.SetPositionAndRotation(hand.TransformPoint(localPosition), hand.rotation * Quaternion.Euler(localEulerAngles));
    }
}
