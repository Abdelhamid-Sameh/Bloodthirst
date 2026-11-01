using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// The end of level 1. When the vampire gets close to the police station, the trap closes: every officer stops
/// shooting, a hidden squad pours in, they surround him in a ring and aim, two tranquilizer darts drop him, and
/// the screen fades to an "ARRESTED" card (time, feeds, Restart / Quit). From the moment it triggers he cannot be
/// hurt or starve, so the ending is never stolen by a stray bullet.
/// </summary>
public class ArrestSequence : MonoBehaviour
{
    [Header("Trigger")]
    [Tooltip("The trap springs when the vampire comes within this distance of this object.")]
    public float triggerRadius = 11f;
    public VampireController vampire;

    [Header("Police")]
    [Tooltip("Inactive officers that run in when the trap springs.")]
    public Transform squad;
    [Tooltip("Active officers within this distance also join the ring.")]
    public float joinRadius = 45f;
    public float ringRadius = 4.5f;
    public float runSpeed = 5.5f;
    [Tooltip("Seconds to wait for the ring to close before the darts fly anyway.")]
    public float maxSurroundTime = 7f;

    [Header("Presentation")]
    public IsometricCamera isoCamera;
    public float closeUpDistance = 17f;
    public GameUI gameUI;
    public CanvasGroup black;
    public CanvasGroup endCard;
    public TMP_Text statsText;

    [Header("Audio (Assets/Audio/Arrest, optional)")]
    public AudioClip siren;
    public AudioClip dart;
    public AudioClip stinger;
    [Tooltip("Officers' shouts, played in order as the ring closes (shout_01, shout_02, ...).")]
    public AudioClip[] shouts;

    bool triggered;
    int feeds;
    float startTime;
    AudioSource sfx;
    readonly List<NavMeshAgent> ring = new List<NavMeshAgent>();
    static readonly int SpeedHash = Animator.StringToHash("Speed");
    static readonly int AimingHash = Animator.StringToHash("Aiming");

    void Awake()
    {
        if (vampire == null) vampire = FindFirstObjectByType<VampireController>();
        if (isoCamera == null) isoCamera = FindFirstObjectByType<IsometricCamera>();
        if (gameUI == null) gameUI = FindFirstObjectByType<GameUI>();
        sfx = gameObject.AddComponent<AudioSource>();
        sfx.playOnAwake = false;
        sfx.spatialBlend = 0f;
        if (endCard != null) { endCard.alpha = 0f; endCard.interactable = false; endCard.blocksRaycasts = false; }
        if (black != null) black.alpha = 0f;
    }

    void OnEnable() { if (vampire != null) { vampire.onBite.AddListener(OnBite); vampire.onEmerged.AddListener(OnEmerged); } }
    void OnDisable() { if (vampire != null) { vampire.onBite.RemoveListener(OnBite); vampire.onEmerged.RemoveListener(OnEmerged); } }

    void OnBite(GameObject victim) => feeds++;
    void OnEmerged() { if (startTime <= 0f) startTime = Time.time; }   // the clock starts when he first leaves the coffin

    void Update()
    {
        if (triggered || vampire == null || vampire.IsDead || vampire.IsBusy) return;   // never cut a bite or a coffin short
        Vector3 d = vampire.transform.position - transform.position;
        if (new Vector2(d.x, d.z).magnitude <= triggerRadius) StartCoroutine(Run());
    }

    IEnumerator Run()
    {
        triggered = true;
        vampire.BeginCapture();
        if (siren != null) sfx.PlayOneShot(siren, 0.8f);

        // Everyone stops shooting; the squad appears; all of them run to a ring around him.
        var officers = new List<PoliceAI>();
        if (squad != null) foreach (Transform member in squad) member.gameObject.SetActive(true);
        foreach (var p in FindObjectsByType<PoliceAI>(FindObjectsSortMode.None))
        {
            if (!p.IsAlive || p.currentState == PoliceAI.State.Captured) continue;
            bool inSquad = squad != null && p.transform.IsChildOf(squad);
            if (!inSquad && Vector3.Distance(p.transform.position, vampire.transform.position) > joinRadius) continue;
            officers.Add(p);
        }
        Vector3 center = vampire.transform.position;
        officers.Sort((a, b) => Angle(a.transform.position, center).CompareTo(Angle(b.transform.position, center)));
        for (int i = 0; i < officers.Count; i++)
        {
            var p = officers[i];
            p.enabled = false;
            var agent = p.GetComponent<NavMeshAgent>();
            if (agent == null || !agent.isOnNavMesh) continue;
            float a = Angle(p.transform.position, center) * Mathf.Deg2Rad;     // keep each officer on his own side
            Vector3 slot = center + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * ringRadius;
            if (NavMesh.SamplePosition(slot, out NavMeshHit hit, 2f, NavMesh.AllAreas)) slot = hit.position;
            agent.speed = runSpeed;
            agent.isStopped = false;
            agent.SetDestination(slot);
            ring.Add(agent);
        }
        StartCoroutine(Shouts());
        StartCoroutine(ZoomIn());

        // Wait for the ring to close (or give up after maxSurroundTime).
        float t = 0f;
        while (t < maxSurroundTime)
        {
            bool closed = true;
            foreach (var agent in ring) closed &= UpdateOfficer(agent);
            if (closed && t > 2.5f) break;
            t += Time.deltaTime;
            yield return null;
        }
        float hold = 0.8f;
        while ((hold -= Time.deltaTime) > 0f) { foreach (var agent in ring) UpdateOfficer(agent); yield return null; }

        // Two tranquilizer darts.
        for (int i = 0; i < 2; i++)
        {
            if (dart != null) sfx.PlayOneShot(dart, 0.9f);
            vampire.PlayHitReaction();
            if (black != null) StartCoroutine(Flash());
            float w = i == 0 ? 1.1f : 0.6f;
            while ((w -= Time.deltaTime) > 0f) { foreach (var agent in ring) UpdateOfficer(agent); yield return null; }
        }
        vampire.Collapse();
        if (stinger != null) sfx.PlayOneShot(stinger, 0.9f);
        float fall = 2.2f;
        while ((fall -= Time.deltaTime) > 0f) { foreach (var agent in ring) UpdateOfficer(agent); yield return null; }

        // Fade to the end card.
        yield return Fade(black, 1f, 1.5f);
        ShowEndCard();
    }

    /// <summary>Drives one ringed officer: run animation while moving, then face the vampire and aim. True once in place.</summary>
    bool UpdateOfficer(NavMeshAgent agent)
    {
        if (agent == null) return true;
        var anim = agent.GetComponentInChildren<Animator>();
        bool arrived = !agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.4f;
        if (anim != null)
        {
            anim.SetFloat(SpeedHash, arrived ? 0f : agent.velocity.magnitude / Mathf.Max(0.1f, agent.speed));
            anim.SetBool(AimingHash, arrived);
        }
        if (arrived)
        {
            agent.isStopped = true;
            Vector3 to = Vector3.ProjectOnPlane(vampire.transform.position - agent.transform.position, Vector3.up);
            if (to.sqrMagnitude > 0.01f) agent.transform.rotation = Quaternion.RotateTowards(agent.transform.rotation, Quaternion.LookRotation(to), 360f * Time.deltaTime);
        }
        return arrived;
    }

    IEnumerator Shouts()
    {
        if (shouts == null) yield break;
        foreach (var clip in shouts)
        {
            if (clip == null) continue;
            sfx.PlayOneShot(clip, 1f);
            yield return new WaitForSeconds(clip.length + 0.3f);
        }
    }

    IEnumerator ZoomIn()
    {
        if (isoCamera == null) yield break;
        isoCamera.minDistance = Mathf.Min(isoCamera.minDistance, closeUpDistance);
        isoCamera.distance = closeUpDistance;      // the camera eases there on its own
    }

    IEnumerator Flash()
    {
        black.alpha = 0f;
        var img = black.GetComponent<UnityEngine.UI.Image>();
        Color old = img != null ? img.color : Color.black;
        if (img != null) img.color = new Color(1f, 1f, 1f);
        black.alpha = 0.45f;
        yield return Fade(black, 0f, 0.35f);
        if (img != null) img.color = old;
    }

    void ShowEndCard()
    {
        if (gameUI != null) gameUI.enabled = false;              // no pause menu over the end card
        float seconds = startTime > 0f ? Time.time - startTime : Time.timeSinceLevelLoad;
        if (statsText != null)
            statsText.text = string.Format("Time  {0:00}:{1:00}      Fed  {2}", (int)(seconds / 60f), (int)(seconds % 60f), feeds);
        if (endCard != null)
        {
            endCard.interactable = true;
            endCard.blocksRaycasts = true;
            StartCoroutine(Fade(endCard, 1f, 1.2f));
        }
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
    }

    static float Angle(Vector3 p, Vector3 center) => Mathf.Atan2(p.x - center.x, p.z - center.z) * Mathf.Rad2Deg;

    static IEnumerator Fade(CanvasGroup g, float to, float time)
    {
        if (g == null) yield break;
        float from = g.alpha, t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / Mathf.Max(0.01f, time);
            g.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(t));
            yield return null;
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.2f, 0.5f, 1f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, triggerRadius);
    }
}
