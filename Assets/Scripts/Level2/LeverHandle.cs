using System.Collections;
using UnityEngine;

/// <summary>
/// The lever's handle: rests leaning away from the vampire and swings toward him when pulled.
/// Hook Pull() to the lever's Interactable.onInteract. The rest pose is the handle's rotation in the scene.
/// </summary>
public class LeverHandle : MonoBehaviour
{
    public Transform handle;
    [Tooltip("Local rotation of the handle once pulled.")]
    public Vector3 pulledEuler = new Vector3(0f, 0f, 30f);
    public float duration = 0.35f;

    public void Pull()
    {
        if (handle != null) StartCoroutine(Swing());
    }

    IEnumerator Swing()
    {
        Quaternion from = handle.localRotation, to = Quaternion.Euler(pulledEuler);
        for (float t = 0f; t < 1f; t += Time.deltaTime / Mathf.Max(0.01f, duration))
        {
            handle.localRotation = Quaternion.Slerp(from, to, t * t * (3f - 2f * t));   // ease in and out
            yield return null;
        }
        handle.localRotation = to;
    }
}
