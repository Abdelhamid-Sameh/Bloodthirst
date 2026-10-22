using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

/// <summary>
/// Hatred-style isometric camera: fixed high diagonal angle, smooth follow, a slight lean toward
/// the mouse cursor, scroll-wheel zoom, and buildings that block the view of the player are cut
/// away (they stop drawing but keep casting their shadows).
/// </summary>
[RequireComponent(typeof(Camera))]
public class IsometricCamera : MonoBehaviour
{
    [Header("Target")]
    public Transform target;
    public Vector3 targetOffset = new Vector3(0f, 1f, 0f);

    [Header("Angle")]
    [Range(0f, 360f)] public float yaw = 45f;
    [Range(20f, 89f)] public float pitch = 52f;
    [Tooltip("A narrow field of view flattens perspective toward a true isometric look.")]
    public float fieldOfView = 30f;

    [Header("Distance and zoom")]
    public float distance = 38f;
    public float minDistance = 24f;
    public float maxDistance = 56f;
    public float zoomStep = 4f;
    public float zoomSmoothTime = 0.15f;

    [Header("Audio")]
    [Tooltip("Sound listener kept next to the target, so 3D sounds are heard from the player's spot rather than from the high camera.")]
    public Transform audioListener;
    public float listenerHeight = 1.6f;

    [Header("Follow")]
    public float followSmoothTime = 0.12f;
    [Tooltip("How far (metres) the view leans toward the mouse cursor.")]
    public float mouseLookAhead = 3.5f;

    [Header("Cutaway")]
    public bool cutaway = true;
    [Tooltip("Objects whose parent has one of these names are hidden while they block the player.")]
    public string[] cutawayGroups = { "Buildings", "Backdrop" };
    [Tooltip("Anything else that blocks the player (kiosks, trees, lamp posts) is hidden too if it is at least this tall.")]
    public float minPropHeight = 1.8f;
    public float cutawayRadius = 1.5f;
    public LayerMask cutawayMask = 1;
    [Tooltip("Seconds a building stays hidden after it stops blocking, to avoid flicker.")]
    public float restoreDelay = 0.25f;

    Camera cam;
    Vector3 focus, focusVelocity;
    float currentDistance, zoomVelocity;
    bool initialised;

    readonly Dictionary<Transform, float> hiddenUntil = new Dictionary<Transform, float>();
    readonly Dictionary<Renderer, ShadowCastingMode> originalModes = new Dictionary<Renderer, ShadowCastingMode>();
    readonly HashSet<Transform> blocking = new HashSet<Transform>();
    readonly List<Transform> toRestore = new List<Transform>();

    void Awake()
    {
        cam = GetComponent<Camera>();
        if (target == null)
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null) target = player.transform;
        }
    }

    void OnValidate()
    {
        minDistance = Mathf.Max(1f, minDistance);
        maxDistance = Mathf.Max(minDistance, maxDistance);
        distance = Mathf.Clamp(distance, minDistance, maxDistance);
    }

    void LateUpdate()
    {
        if (target == null) return;

        if (!initialised) SnapToTarget();

        if (Mouse.current != null)
        {
            float scroll = Mouse.current.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f) distance = Mathf.Clamp(distance - Mathf.Sign(scroll) * zoomStep, minDistance, maxDistance);
        }
        currentDistance = Mathf.SmoothDamp(currentDistance, distance, ref zoomVelocity, zoomSmoothTime);

        Vector3 desired = target.position + targetOffset + LookAhead();
        focus = Vector3.SmoothDamp(focus, desired, ref focusVelocity, followSmoothTime);
        Place();
        if (audioListener != null) audioListener.SetPositionAndRotation(target.position + Vector3.up * listenerHeight, Quaternion.Euler(0f, yaw, 0f));

        if (cutaway) UpdateCutaway();
    }

    /// <summary>Jump straight to the target with no smoothing (use after teleports and respawns).</summary>
    public void SnapToTarget()
    {
        if (target == null) return;
        cam = cam != null ? cam : GetComponent<Camera>();
        focus = target.position + targetOffset;
        focusVelocity = Vector3.zero;
        currentDistance = distance;
        zoomVelocity = 0f;
        initialised = true;
        Place();
    }

    void Place()
    {
        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
        transform.SetPositionAndRotation(focus - rotation * Vector3.forward * currentDistance, rotation);
        cam.fieldOfView = fieldOfView;
    }

    Vector3 LookAhead()
    {
        if (mouseLookAhead <= 0f || Mouse.current == null || !Application.isFocused) return Vector3.zero;
        Vector2 mouse = Mouse.current.position.ReadValue();
        Vector2 n = new Vector2(mouse.x / Mathf.Max(1, Screen.width) - 0.5f, mouse.y / Mathf.Max(1, Screen.height) - 0.5f) * 2f;
        n = Vector2.ClampMagnitude(n, 1f);
        return Quaternion.Euler(0f, yaw, 0f) * new Vector3(n.x, 0f, n.y) * mouseLookAhead;
    }

    // ---------------------------------------------------------------- cutaway

    void UpdateCutaway()
    {
        blocking.Clear();
        Vector3 from = transform.position;
        // Check lines to the head and to the feet so both are kept visible.
        CollectBlockers(from, target.position + Vector3.up * 1.8f);
        CollectBlockers(from, target.position + Vector3.up * 0.3f);

        float now = Time.time;
        foreach (Transform root in blocking)
        {
            if (!hiddenUntil.ContainsKey(root)) SetHidden(root, true);
            hiddenUntil[root] = now + restoreDelay;
        }

        toRestore.Clear();
        foreach (KeyValuePair<Transform, float> entry in hiddenUntil)
            if (entry.Key == null || entry.Value < now) toRestore.Add(entry.Key);
        foreach (Transform root in toRestore)
        {
            if (root != null) SetHidden(root, false);
            hiddenUntil.Remove(root);
        }
    }

    void CollectBlockers(Vector3 from, Vector3 to)
    {
        Vector3 dir = to - from;
        float length = dir.magnitude - cutawayRadius;
        if (length <= 0f) return;
        foreach (RaycastHit hit in Physics.SphereCastAll(from, cutawayRadius, dir.normalized, length, cutawayMask, QueryTriggerInteraction.Ignore))
        {
            Transform root = FindGroupRoot(hit.collider.transform);
            if (root == null && IsTallProp(hit.collider)) root = hit.collider.transform;
            if (root != null && !target.IsChildOf(root) && !root.IsChildOf(target)) blocking.Add(root);
        }
    }

    bool IsTallProp(Collider c)
    {
        if (c.GetComponentInParent<CharacterController>() != null) return false; // never hide characters
        Renderer r = c.GetComponentInChildren<Renderer>();
        if (r == null) r = c.GetComponentInParent<Renderer>();
        return r != null && r.bounds.size.y >= minPropHeight;
    }

    Transform FindGroupRoot(Transform t)
    {
        while (t.parent != null)
        {
            string parentName = t.parent.name;
            for (int i = 0; i < cutawayGroups.Length; i++)
                if (parentName == cutawayGroups[i]) return t;
            t = t.parent;
        }
        return null;
    }

    void SetHidden(Transform root, bool hide)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0 && root.parent != null) renderers = root.parent.GetComponentsInChildren<Renderer>();
        foreach (Renderer r in renderers)
        {
            if (hide)
            {
                if (!originalModes.ContainsKey(r)) originalModes[r] = r.shadowCastingMode;
                r.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
            }
            else if (originalModes.TryGetValue(r, out ShadowCastingMode mode))
            {
                r.shadowCastingMode = mode;
                originalModes.Remove(r);
            }
        }
    }

    void OnDisable()
    {
        foreach (Transform root in hiddenUntil.Keys)
            if (root != null) SetHidden(root, false);
        hiddenUntil.Clear();
    }
}
