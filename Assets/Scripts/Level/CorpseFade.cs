using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

/// Makes a dead human fade out and sink a few seconds after dying, then switches the body off.
[RequireComponent(typeof(HumanAI))]
public class CorpseFade : MonoBehaviour
{
    public float delay = 3f;
    public float fadeTime = 1.5f;
    public float sinkDepth = 0.4f;

    HumanAI ai;
    bool started;

    void Awake() { ai = GetComponent<HumanAI>(); }

    void Update()
    {
        if (!started && ai.currentState == HumanAI.State.Dead) { started = true; StartCoroutine(Fade()); }
    }

    IEnumerator Fade()
    {
        yield return new WaitForSeconds(delay);
        var renderers = GetComponentsInChildren<Renderer>();
        var mats = new System.Collections.Generic.List<Material>();
        foreach (var r in renderers)
        {
            r.shadowCastingMode = ShadowCastingMode.Off;
            foreach (var m in r.materials) { MakeTransparent(m); mats.Add(m); }
        }
        foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
        var agent = GetComponent<UnityEngine.AI.NavMeshAgent>(); if (agent != null) agent.enabled = false;

        Vector3 from = transform.position;
        for (float t = 0f; t < 1f; t += Time.deltaTime / fadeTime)
        {
            foreach (var m in mats) { var c = m.GetColor("_BaseColor"); c.a = 1f - t; m.SetColor("_BaseColor", c); }
            transform.position = from + Vector3.down * (sinkDepth * t * t);
            yield return null;
        }
        gameObject.SetActive(false);
    }

    // shortcut: switches URP Lit to transparent at runtime; if a build strips that variant the body still sinks out of view.
    static void MakeTransparent(Material m)
    {
        if (!m.HasProperty("_BaseColor")) return;
        m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f);
        m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One); m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_ZWrite", 0f);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.SetOverrideTag("RenderType", "Transparent");
        m.renderQueue = (int)RenderQueue.Transparent;
    }
}
