using System.Collections;
using UnityEngine;

/// <summary>One-shot smoke burst with a short light flash. Destroys itself when done.</summary>
public class MagicPoof : MonoBehaviour
{
    public Light flash;
    public float flashIntensity = 30f;
    public float flashTime = 0.35f;
    public float lifetime = 2.5f;

    IEnumerator Start()
    {
        foreach (ParticleSystem ps in GetComponentsInChildren<ParticleSystem>()) ps.Play();
        float t = 0f;
        while (t < flashTime)
        {
            t += Time.deltaTime;
            if (flash != null) flash.intensity = flashIntensity * (1f - t / flashTime);
            yield return null;
        }
        if (flash != null) flash.intensity = 0f;
        yield return new WaitForSeconds(Mathf.Max(0f, lifetime - flashTime));
        Destroy(gameObject);
    }
}
