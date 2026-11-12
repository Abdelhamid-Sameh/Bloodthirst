using UnityEngine;

/// <summary>An invisible box: when the vampire walks in, Level 2 moves on to this stage.</summary>
[RequireComponent(typeof(BoxCollider))]
public class StageTrigger : MonoBehaviour
{
    public Level2Director.Stage stage;

    void Reset() => GetComponent<BoxCollider>().isTrigger = true;

    void OnTriggerEnter(Collider other)
    {
        if (other.GetComponent<VampireController>() != null && Level2Director.Instance != null)
            Level2Director.Instance.Advance(stage);
    }

    void OnDrawGizmos()
    {
        var box = GetComponent<BoxCollider>();
        Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.25f);
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawCube(box.center, box.size);
    }
}
