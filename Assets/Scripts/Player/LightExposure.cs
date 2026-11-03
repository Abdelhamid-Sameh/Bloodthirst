using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Lamplight burns the vampire. While he stands in the light of a street lamp, post lamp or wall lamp
/// (any light driven by LightFlicker, except police light bars and window glow) his blood drains faster
/// (drainMultiplier, x2.5 by default) and he visibly smokes, the blood wing glows hot (no extra HUD: the smoke and the wing say it).
/// A lamp that has flickered off does not burn, and walls block the light, so shadows are safe ground.
/// Added to the vampire automatically (ThrallAbilityInstaller).
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(VampireController))]
public class LightExposure : MonoBehaviour
{
    [Tooltip("How much faster the blood drains while he is in lamplight.")]
    public float drainMultiplier = 2.5f;
    [Tooltip("How strongly a light must hit him to count (intensity / distance squared, roughly what the eye sees on the ground).")]
    public float burnThreshold = 2.5f;
    [Tooltip("Seconds of light before the burn starts and of shade before it stops, so passing a lamp's edge does not flicker the effect.")]
    public float settleTime = 0.25f;

    public bool Exposed { get; private set; }

    VampireController controller;
    VampireBlood blood;
    ParticleSystem smoke, embers;
    Image hotWing;
    float exposure;   // 0..1, smoothed
    float nextCheck;
    bool litNow;

    void Awake()
    {
        controller = GetComponent<VampireController>();
        blood = GetComponent<VampireBlood>();
        smoke = MakeParticles("ExposureSmoke", 1.4f, 2.2f, 0.3f, 0.7f, new Color(0.55f, 0.52f, 0.5f, 0.55f), new Color(0.25f, 0.22f, 0.22f, 0.35f), 0.6f, 1.2f, 1.9f);
        embers = MakeParticles("ExposureEmbers", 0.5f, 0.9f, 0.04f, 0.09f, new Color(1f, 0.55f, 0.15f, 1f), new Color(1f, 0.25f, 0.05f, 1f), 1.0f, 2.2f, 0.6f);
    }

    void OnDisable() { if (blood != null) blood.drainMultiplier = 1f; }

    void Update()
    {
        bool canBurn = blood != null && blood.IsActive && !controller.IsDead && !controller.IsBusy && !GameState.Paused && !ShieldAbility.Active;
        if (canBurn && Time.time >= nextCheck) { nextCheck = Time.time + 0.1f; litNow = InLamplight(); }
        if (!canBurn) litNow = false;

        exposure = Mathf.MoveTowards(exposure, litNow ? 1f : 0f, Time.deltaTime / Mathf.Max(0.01f, settleTime));
        Exposed = exposure >= 1f || (Exposed && exposure > 0f);
        if (blood != null) blood.drainMultiplier = Exposed ? drainMultiplier : 1f;

        var e1 = smoke.emission; e1.rateOverTime = Exposed ? 34f : 0f;
        var e2 = embers.emission; e2.rateOverTime = Exposed ? 10f : 0f;

        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 7f);
        if (hotWing == null) hotWing = FindHealthWing();
        if (hotWing != null) hotWing.color = Exposed ? Color.Lerp(Color.white, new Color(1f, 0.65f, 0.25f), 0.35f + 0.45f * pulse) : Color.white;
    }

    /// <summary>Is any lamp strong enough at his position, with nothing solid in between?</summary>
    bool InLamplight()
    {
        Vector3 chest = transform.position + Vector3.up * 1.2f;
        foreach (LightFlicker lamp in FindObjectsByType<LightFlicker>(FindObjectsSortMode.None))
        {
            if (lamp.kind == LightFlicker.Kind.Strobe || lamp.kind == LightFlicker.Kind.Window) continue;
            if (Burns(lamp, lamp.target, chest)) return true;
            foreach (Light f in lamp.followers) if (Burns(lamp, f, chest)) return true;
        }
        return false;
    }

    bool Burns(LightFlicker lamp, Light l, Vector3 chest)
    {
        if (l == null || !l.enabled || !l.gameObject.activeInHierarchy || l.intensity <= 0f) return false;
        Vector3 to = chest - l.transform.position;
        float d = to.magnitude;
        if (d > l.range || d < 0.01f) return false;
        if (l.type == LightType.Spot && Vector3.Angle(l.transform.forward, to) > l.spotAngle * 0.5f) return false;
        float strength = l.intensity / Mathf.Max(1f, d * d) * Mathf.Clamp01(1f - d / l.range * 0.5f);
        if (strength < burnThreshold) return false;
        // Walls and roofs shade him; people and the lamp itself do not.
        foreach (RaycastHit hit in Physics.RaycastAll(l.transform.position, to / d, d - 0.3f, ~0, QueryTriggerInteraction.Ignore))
        {
            Transform t = hit.collider.transform;
            if (t.IsChildOf(transform) || t.IsChildOf(lamp.transform)) continue;
            if (hit.collider.GetComponentInParent<HumanAI>() != null || hit.collider.GetComponentInParent<PoliceAI>() != null) continue;
            return false;
        }
        return true;
    }

    static Image FindHealthWing()
    {
        var v = FindFirstObjectByType<VerticalBloodBarUI>();
        if (v != null) return v.frame;
        var h = FindFirstObjectByType<BloodBarUI>();
        return h != null ? h.frame : null;
    }

    // ------------------------------------------------------------------ smoke and embers rising off him

    ParticleSystem MakeParticles(string objectName, float lifeMin, float lifeMax, float sizeMin, float sizeMax, Color a, Color b, float riseMin, float riseMax, float grow)
    {
        var go = new GameObject(objectName);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.up * 0.5f;   // his root is scaled x2, so this is about chest height
        var ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifeMin, lifeMax);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
        main.startColor = new ParticleSystem.MinMaxGradient(a, b);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Shape;   // sizes in world units despite his x2 scale
        main.maxParticles = 300;
        var emission = ps.emission;
        emission.rateOverTime = 0f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.2f;
        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f);
        vel.y = new ParticleSystem.MinMaxCurve(riseMin, riseMax);
        vel.z = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f);
        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.3f;
        noise.frequency = 0.6f;
        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient fade = new Gradient();
        fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                     new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
        col.color = fade;
        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, grow));
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Billboard;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.material = new Material(Shader.Find("Sprites/Default")) { mainTexture = ThrallAbility.MakeBlobTexture(64) };
        ps.Play();
        return ps;
    }
}
