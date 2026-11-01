using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// A human or police officer the vampire has turned into his thrall with the Shadow Bite ability (ThrallAbility).
///
/// - Follows the vampire around.
/// - Fights the police for him: a police thrall shoots (or punches up close), a human thrall rushes in and hits.
///   His blows wear enemies down like the vampire's fight does (they never kill - only a bite does).
/// - Has real health: enemy officers shoot at him (see PoliceAI.TryFightThrall) and he keeps fighting until he is dead.
/// </summary>
[DisallowMultipleComponent]
public class ThrallAI : MonoBehaviour
{
    static readonly List<ThrallAI> all = new List<ThrallAI>();
    /// <summary>Every thrall that is still alive.</summary>
    public static IReadOnlyList<ThrallAI> All => all;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => all.Clear();

    [Header("Health")]
    [Tooltip("A thrall's health is the character's normal max health times this.")]
    public float healthMultiplier = 2f;
    [Tooltip("Minimum seconds between two hit-reaction animations.")]
    public float hitRetriggerDelay = 0.8f;

    [Header("Following the vampire")]
    [Tooltip("He stops walking once he is this close to the vampire.")]
    public float followDistance = 3f;
    [Tooltip("Further away than this he runs to catch up.")]
    public float runDistance = 7f;
    public float walkSpeed = 3.5f;
    public float runSpeed = 5.5f;
    [Tooltip("Enemies further than this from the vampire are ignored, so thralls do not wander off on their own.")]
    public float leashRadius = 18f;

    [Header("Fighting")]
    [Tooltip("He picks fights with enemy officers this close to him.")]
    public float aggroRadius = 12f;
    public float meleeRange = 1.5f;
    [Tooltip("Damage of one blow (it wears enemies down but never kills).")]
    public float meleeDamage = 12f;
    public float meleeInterval = 1.1f;
    [Tooltip("Seconds into the swing when the blow lands.")]
    public float meleeHitDelay = 0.32f;
    [Tooltip("Police thralls shoot at enemies up to this far away.")]
    public float shootRange = 9f;

    [Header("Look")]
    [Tooltip("Colour multiplied onto his clothes so thralls are easy to tell apart.")]
    public Color tint = new Color(0.72f, 0.55f, 1f);

    HumanAI human;
    PoliceAI police;
    NavMeshAgent agent;
    Animator animator;
    Transform vampire;
    VampireController vampireController;
    PoliceAI target;

    float health, maxHealth;
    bool alive;
    float nextScan, repathTimer, nextAttackTime, nextShotTime, nextHitAnimTime, staggerUntil, tracerHideTime;
    float shootInterval = 1.2f, shootDamage = 10f, hitChance = 0.6f;
    bool aiming;
    bool hasHit, hasShoot, hasAimingParam;
    int attackTrigger;                       // hash of the "Punch" or "Attack" trigger, 0 = none (he lunges instead)
    LineRenderer tracer;
    AudioSource audioSource;
    AudioClip shotSound;
    Transform gunTransform;

    static readonly int SpeedHash = Animator.StringToHash("Speed");
    static readonly int AimingHash = Animator.StringToHash("Aiming");
    static readonly int ShootHash = Animator.StringToHash("Shoot");
    static readonly int HitHash = Animator.StringToHash("Hit");

    public bool IsAlive => alive;
    public float HealthFraction => maxHealth > 0f ? Mathf.Clamp01(health / maxHealth) : 0f;
    /// <summary>Roughly his chest: where enemy bullets are aimed.</summary>
    public Vector3 AimPoint => transform.position + Vector3.up * 1.0f;

    /// <summary>The nearest living thrall within maxDistance (on the ground plane), or null.</summary>
    public static ThrallAI NearestTo(Vector3 position, float maxDistance)
    {
        ThrallAI best = null;
        float bestDistance = maxDistance;
        foreach (ThrallAI t in all)
        {
            if (t == null || !t.alive) continue;
            float d = FlatDistance(position, t.transform.position);
            if (d < bestDistance) { bestDistance = d; best = t; }
        }
        return best;
    }

    /// <summary>Turns a human or officer into the vampire's thrall.</summary>
    public static ThrallAI Convert(GameObject victim, Transform vampire)
    {
        if (victim == null || victim.GetComponent<ThrallAI>() != null) return null;
        ThrallAI thrall = victim.AddComponent<ThrallAI>();
        thrall.Init(vampire);
        return thrall;
    }

    // ------------------------------------------------------------------ setup

    void Init(Transform vampireTransform)
    {
        vampire = vampireTransform;
        if (vampire != null) vampireController = vampire.GetComponent<VampireController>();
        human = GetComponent<HumanAI>();
        police = GetComponent<PoliceAI>();
        agent = GetComponent<NavMeshAgent>();
        animator = GetComponentInChildren<Animator>();

        float baseHealth = 40f;
        if (human != null)
        {
            baseHealth = human.maxHealth;
            human.StopAllCoroutines();        // no more running away
            human.IsThrall = true;
            human.currentState = HumanAI.State.Idle;
            human.enabled = false;            // from now on this script drives him
        }
        if (police != null)
        {
            baseHealth = police.maxHealth;
            police.StopAllCoroutines();
            police.IsThrall = true;
            police.currentState = PoliceAI.State.Idle;
            police.enabled = false;
            shootInterval = police.fireInterval;
            shootDamage = police.damage;
            hitChance = police.hitChance;
            shotSound = police.shotSound;
            gunTransform = police.gun;
            HideOfficerEffects();
        }

        maxHealth = baseHealth * healthMultiplier;
        health = maxHealth;
        alive = true;
        all.Add(this);

        if (agent != null)
        {
            agent.enabled = true;
            if (agent.isOnNavMesh) { agent.isStopped = false; agent.ResetPath(); }
            agent.speed = walkSpeed;
        }

        if (animator != null)
        {
            foreach (AnimatorControllerParameter p in animator.parameters)
            {
                if (p.type == AnimatorControllerParameterType.Trigger)
                {
                    if (p.name == "Hit") hasHit = true;
                    else if (p.name == "Shoot") hasShoot = true;
                    else if (attackTrigger == 0 && (p.name == "Punch" || p.name == "Attack")) attackTrigger = Animator.StringToHash(p.name);
                }
                else if (p.type == AnimatorControllerParameterType.Bool && p.name == "Aiming") hasAimingParam = true;
            }
        }

        if (police != null) BuildShootingEffects();
        ApplyTint();
    }

    // The officer's own muzzle flash and tracer must not stay lit after his AI is switched off.
    void HideOfficerEffects()
    {
        foreach (LineRenderer lr in GetComponentsInChildren<LineRenderer>(true))
            if (lr.gameObject.name == "Tracer") lr.enabled = false;
        foreach (Light l in GetComponentsInChildren<Light>(true))
            if (l.gameObject.name == "MuzzleFlash") l.enabled = false;
    }

    void BuildShootingEffects()
    {
        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
        GameObject go = new GameObject("ThrallTracer");
        go.transform.SetParent(transform, false);
        tracer = go.AddComponent<LineRenderer>();
        tracer.useWorldSpace = true;
        tracer.positionCount = 2;
        tracer.startWidth = 0.04f;
        tracer.endWidth = 0.02f;
        tracer.material = new Material(shader);
        tracer.startColor = new Color(0.75f, 0.55f, 1f, 1f);   // a purple tracer, so it is clearly a thrall's shot
        tracer.endColor = new Color(0.55f, 0.3f, 0.9f, 0.4f);
        tracer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        tracer.receiveShadows = false;
        tracer.enabled = false;

        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 1f;
        audioSource.rolloffMode = AudioRolloffMode.Linear;
        audioSource.minDistance = 10f;
        audioSource.maxDistance = 120f;
    }

    void ApplyTint()
    {
        MaterialPropertyBlock block = new MaterialPropertyBlock();
        foreach (Renderer r in GetComponentsInChildren<Renderer>())
        {
            if (!(r is SkinnedMeshRenderer) && !(r is MeshRenderer)) continue;
            Material[] mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                Material m = mats[i];
                if (m == null) continue;
                string prop = m.HasProperty("_BaseColor") ? "_BaseColor" : (m.HasProperty("_Color") ? "_Color" : null);
                if (prop == null) continue;
                r.GetPropertyBlock(block, i);
                block.SetColor(prop, m.GetColor(prop) * tint);
                r.SetPropertyBlock(block, i);
            }
        }
    }

    void OnDestroy() { all.Remove(this); }

    // ------------------------------------------------------------------ behaviour

    void Update()
    {
        if (!alive || GameState.Paused) return;
        if (vampire == null)
        {
            GameObject p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) { vampire = p.transform; vampireController = p.GetComponent<VampireController>(); }
        }

        if (tracer != null && tracer.enabled && Time.time >= tracerHideTime) tracer.enabled = false;
        aiming = false;

        if (Time.time >= nextScan)
        {
            nextScan = Time.time + 0.25f;
            target = FindTarget();
        }
        if (target != null && !target.CanBeHit) target = null;   // he went down, or the vampire grabbed him

        if (Time.time < staggerUntil)
        {
            StopAgent();
        }
        else if (target != null)
        {
            Fight();
        }
        else
        {
            Follow();
        }
        UpdateAnimator();
    }

    PoliceAI FindTarget()
    {
        PoliceAI best = null;
        float bestDistance = aggroRadius;
        foreach (PoliceAI officer in FindObjectsByType<PoliceAI>(FindObjectsSortMode.None))
        {
            if (!officer.CanBeHit) continue;   // dead, grabbed, or another thrall
            float d = FlatDistance(transform.position, officer.transform.position);
            if (d >= bestDistance) continue;
            if (vampire != null && FlatDistance(vampire.position, officer.transform.position) > leashRadius) continue;
            best = officer;
            bestDistance = d;
        }
        return best;
    }

    void Follow()
    {
        if (vampire == null) { StopAgent(); return; }
        float d = FlatDistance(transform.position, vampire.position);
        if (d <= followDistance) { StopAgent(); return; }

        // Each thrall waits at his own spot behind the vampire so they do not pile up.
        int index = Mathf.Max(0, all.IndexOf(this));
        float angle = vampire.eulerAngles.y + 180f + (index - (all.Count - 1) * 0.5f) * 55f;
        Vector3 spot = vampire.position + Quaternion.Euler(0f, angle, 0f) * Vector3.forward * 2.2f;
        MoveTo(spot, d > runDistance ? runSpeed : walkSpeed, 0.3f);
    }

    void Fight()
    {
        Vector3 enemy = target.transform.position;
        float dist = FlatDistance(transform.position, enemy);

        if (dist <= meleeRange) { Melee(); return; }
        if (police != null && dist <= shootRange && ClearShot()) { Shoot(); return; }
        MoveTo(enemy, runSpeed, meleeRange * 0.6f);
    }

    // ---- melee

    void Melee()
    {
        StopAgent();
        FaceToward(target.transform.position, 720f);
        if (Time.time < nextAttackTime) return;
        nextAttackTime = Time.time + meleeInterval;
        StartCoroutine(MeleeRoutine(target));
    }

    IEnumerator MeleeRoutine(PoliceAI victim)
    {
        if (attackTrigger != 0 && animator != null)
        {
            animator.ResetTrigger(attackTrigger);
            animator.SetTrigger(attackTrigger);
        }
        else
        {
            StartCoroutine(Lunge());   // no attack animation: he throws his weight forward instead
        }

        yield return new WaitForSeconds(meleeHitDelay);
        if (!alive || victim == null || !victim.CanBeHit) yield break;
        if (FlatDistance(transform.position, victim.transform.position) > meleeRange + 0.8f) yield break;
        victim.TakeHit(meleeDamage, transform);
    }

    IEnumerator Lunge()
    {
        const float outTime = 0.1f, backTime = 0.18f, distance = 0.35f;
        float t = 0f;
        while (t < outTime && alive && agent != null && agent.enabled && agent.isOnNavMesh)
        {
            agent.Move(transform.forward * (distance / outTime) * Time.deltaTime);
            t += Time.deltaTime;
            yield return null;
        }
        t = 0f;
        while (t < backTime && alive && agent != null && agent.enabled && agent.isOnNavMesh)
        {
            agent.Move(-transform.forward * (distance / backTime) * Time.deltaTime);
            t += Time.deltaTime;
            yield return null;
        }
    }

    // ---- shooting (police thralls)

    Vector3 MuzzlePosition() => gunTransform != null ? gunTransform.position : transform.position + Vector3.up * 1.3f + transform.forward * 0.4f;

    bool ClearShot()
    {
        Vector3 from = MuzzlePosition();
        Vector3 to = target.transform.position + Vector3.up * 1.0f;
        Vector3 dir = to - from;
        float distance = dir.magnitude;
        if (distance < 0.01f) return true;
        foreach (RaycastHit h in Physics.RaycastAll(from, dir / distance, distance, ~0, QueryTriggerInteraction.Ignore))
        {
            Transform t = h.collider.transform;
            if (t.IsChildOf(transform) || t.IsChildOf(target.transform) || (vampire != null && t.IsChildOf(vampire))) continue;
            return false;
        }
        return true;
    }

    void Shoot()
    {
        StopAgent();
        aiming = true;
        FaceToward(target.transform.position, 540f);
        if (Time.time < nextShotTime) return;

        Vector3 look = target.transform.position - transform.position;
        look.y = 0f;
        if (Vector3.Angle(transform.forward, look) > 25f) return;   // he fires once he faces the enemy

        nextShotTime = Time.time + shootInterval;
        Vector3 origin = MuzzlePosition();
        Vector3 aim = target.transform.position + Vector3.up * 1.0f;
        bool hit = Random.value <= hitChance;
        Vector3 end = hit ? aim : aim + Random.onUnitSphere * 0.8f;
        if (hit) target.TakeHit(shootDamage, transform);

        if (tracer != null)
        {
            tracer.SetPosition(0, origin);
            tracer.SetPosition(1, end);
            tracer.enabled = true;
            tracerHideTime = Time.time + 0.07f;
        }
        if (animator != null && hasShoot) { animator.ResetTrigger(ShootHash); animator.SetTrigger(ShootHash); }
        if (audioSource != null && shotSound != null)
        {
            audioSource.pitch = Random.Range(0.94f, 1.06f);
            audioSource.PlayOneShot(shotSound, 0.8f);
        }
    }

    // ---- movement helpers

    void MoveTo(Vector3 destination, float speed, float stopDistance)
    {
        if (agent == null || !agent.enabled || !agent.isOnNavMesh) return;
        agent.isStopped = false;
        agent.speed = speed;
        agent.stoppingDistance = stopDistance;
        repathTimer -= Time.deltaTime;
        if (repathTimer > 0f) return;   // calling SetDestination every frame keeps cancelling the path that is being computed
        repathTimer = 0.35f;
        agent.SetDestination(destination);
    }

    void StopAgent()
    {
        if (agent != null && agent.enabled && agent.isOnNavMesh)
        {
            agent.isStopped = true;
            agent.velocity = Vector3.zero;
        }
        repathTimer = 0f;
    }

    void FaceToward(Vector3 worldPosition, float degreesPerSecond)
    {
        Vector3 look = worldPosition - transform.position;
        look.y = 0f;
        if (look.sqrMagnitude < 0.0001f) return;
        transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(look), degreesPerSecond * Time.deltaTime);
    }

    void UpdateAnimator()
    {
        if (animator == null) return;
        float speed = agent != null && agent.enabled && agent.isOnNavMesh ? agent.velocity.magnitude : 0f;
        animator.SetFloat(SpeedHash, speed, 0.1f, Time.deltaTime);
        if (hasAimingParam) animator.SetBool(AimingHash, aiming);
    }

    static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }

    // ------------------------------------------------------------------ taking damage

    /// <summary>An enemy officer's bullet (see PoliceAI.TryFightThrall). Unlike other characters, a thrall can really die.</summary>
    public void TakeDamage(float amount, Transform attacker)
    {
        if (!alive) return;
        health -= amount;
        staggerUntil = Mathf.Max(staggerUntil, Time.time + 0.2f);
        if (animator != null && hasHit && Time.time >= nextHitAnimTime)
        {
            nextHitAnimTime = Time.time + hitRetriggerDelay;
            animator.ResetTrigger(HitHash);
            animator.SetTrigger(HitHash);
        }
        if (health <= 0f) Die();
    }

    void Die()
    {
        if (!alive) return;
        alive = false;
        health = 0f;
        all.Remove(this);
        StopAllCoroutines();
        if (tracer != null) tracer.enabled = false;
        if (human != null) human.OnKilled();
        else if (police != null) police.Die();
        enabled = false;
    }
}
