using UnityEngine;

/// <summary>
/// A marked spot in the Level 2 base: who builds what here. Visible in the Scene view (name and owner);
/// select it to read the task. Delete it once the real thing is in place.
/// </summary>
public class Placeholder : MonoBehaviour
{
    public string owner;
    [TextArea(3, 10)] public string task;
    public Color color = new Color(1f, 0.2f, 0.8f);

    void OnDrawGizmos()
    {
        Gizmos.color = color;
        Gizmos.DrawWireSphere(transform.position + Vector3.up, 0.6f);
        Gizmos.DrawLine(transform.position, transform.position + Vector3.up * 2f);
#if UNITY_EDITOR
        UnityEditor.Handles.color = color;
        UnityEditor.Handles.Label(transform.position + Vector3.up * 2.3f, "[" + owner + "] " + name);
#endif
    }
}
