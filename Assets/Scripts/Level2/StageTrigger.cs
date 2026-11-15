using UnityEngine;
using UnityEngine.Events;

/// <summary>An invisible box: when the vampire walks in, Level 2 moves on to this stage and onEnter runs (once).</summary>
[RequireComponent(typeof(BoxCollider))]
public class StageTrigger : MonoBehaviour
{
    public Level2Director.Stage stage;
    public UnityEvent onEnter = new UnityEvent();

    bool done;

    void Reset() => GetComponent<BoxCollider>().isTrigger = true;

    void OnTriggerEnter(Collider other)
    {
        if (done || other.GetComponent<VampireController>() == null) return;
        done = true;
        if (Level2Director.Instance != null) Level2Director.Instance.Advance(stage);
        onEnter.Invoke();
    }

    void OnDrawGizmos()
    {
        var box = GetComponent<BoxCollider>();
        Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.25f);
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawCube(box.center, box.size);
    }
}
