using System.Collections;
using UnityEngine;

/// <summary>
/// A sliding door or cell gate. Call Open / Close / Toggle (from an Interactable, a lever, or an encounter script).
/// Give it a NavMeshObstacle with Carve on, so police and NPCs path around it while it is shut.
/// </summary>
public class Door : MonoBehaviour
{
    [Tooltip("How far it slides when open, in its own space.")]
    public Vector3 openOffset = new Vector3(0f, 0f, 2.4f);
    public float duration = 0.8f;
    public bool startOpen;
    public AudioClip moveClip;

    public bool IsOpen { get; private set; }

    Vector3 closedPosition;
    Coroutine moving;

    void Awake()
    {
        closedPosition = transform.localPosition;
        if (startOpen) { IsOpen = true; transform.localPosition = OpenPosition; }
    }

    Vector3 OpenPosition => closedPosition + transform.localRotation * openOffset;

    public void Open() => Move(true);
    public void Close() => Move(false);
    public void Toggle() => Move(!IsOpen);

    void Move(bool open)
    {
        if (open == IsOpen) return;
        IsOpen = open;
        if (moveClip != null) AudioSource.PlayClipAtPoint(moveClip, transform.position);
        if (moving != null) StopCoroutine(moving);
        moving = StartCoroutine(Slide(open ? OpenPosition : closedPosition));
    }

    IEnumerator Slide(Vector3 to)
    {
        Vector3 from = transform.localPosition;
        for (float t = 0f; t < 1f; t += Time.deltaTime / Mathf.Max(0.01f, duration))
        {
            transform.localPosition = Vector3.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t));
            yield return null;
        }
        transform.localPosition = to;
        moving = null;
    }
}
