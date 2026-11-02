using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// A trail of glowing chevrons on the ground, along the walkable path from the vampire to where he should go:
/// the police station normally, the nearest living human (brighter, pulsing) when he is starving.
/// The chevrons flow forward continuously and the trail starts at his feet every frame, so it never pops when
/// the path is recalculated; colour changes and appearing/disappearing are cross-faded.
public class BloodTrail : MonoBehaviour
{
    public VampireController vampire;
    public VampireBlood blood;
    [Tooltip("The police station (the ArrestZone).")]
    public Transform objective;
    [Tooltip("The trail ends this close to the objective, where the arrest begins.")]
    public float objectiveReachedDistance = 12f;

    [Header("Look")]
    public Material material;
    public float length = 18f;
    public float spacing = 2.2f;
    public float startAhead = 1.6f;
    public float chevronSize = 1.2f;
    [Tooltip("Metres per second the chevrons drift toward the target.")]
    public float flowSpeed = 1.2f;
    public Color guideColor = new Color(0.85f, 0.06f, 0.05f, 0.7f);
    public Color hungerColor = new Color(1f, 0.15f, 0.08f, 0.9f);
    [Range(0f, 1f)] public float hungerBelow = 0.45f;

    NavMeshPath path;
    readonly List<Vector3> corners = new List<Vector3>();
    readonly List<Vector3> line = new List<Vector3>();
    Mesh mesh;
    MaterialPropertyBlock block;
    float nextRepath, visibility, hungerBlend, flow;
    bool hasTarget;
    static readonly int ColorId = Shader.PropertyToID("_Color");

    void Awake()
    {
        if (vampire == null) vampire = FindFirstObjectByType<VampireController>();
        if (blood == null && vampire != null) blood = vampire.GetComponent<VampireBlood>();
        path = new NavMeshPath();
        block = new MaterialPropertyBlock();
        if (material == null) material = new Material(Shader.Find("Sprites/Default"));
        mesh = BuildChevron();
    }

    void Update()
    {
        if (vampire == null || blood == null) return;
        bool active = blood.IsActive;
        Vector3 me = vampire.transform.position;
        bool hungry = active && blood.Fraction < hungerBelow;

        if (active && Time.time >= nextRepath)
        {
            nextRepath = Time.time + 0.3f;
            Vector3? target = hungry ? NearestPrey(me) : (Vector3?)null;
            if (target == null && objective != null && Flat(objective.position - me) > objectiveReachedDistance) target = objective.position;
            hasTarget = target != null;
            // A failed query (mid-jump, say) keeps the last good path instead of blinking the trail off.
            if (hasTarget && NavMesh.SamplePosition(me, out var a, 2f, NavMesh.AllAreas) && NavMesh.SamplePosition(target.Value, out var b, 4f, NavMesh.AllAreas)
                && NavMesh.CalculatePath(a.position, b.position, NavMesh.AllAreas, path) && path.corners.Length >= 2)
            {
                corners.Clear();
                corners.AddRange(path.corners);
            }
        }

        bool show = active && hasTarget && corners.Count >= 2;
        visibility = Mathf.MoveTowards(visibility, show ? 1f : 0f, Time.deltaTime * 2.5f);
        hungerBlend = Mathf.MoveTowards(hungerBlend, hungry ? 1f : 0f, Time.deltaTime * 2f);
        flow = Mathf.Repeat(flow + Time.deltaTime * Mathf.Lerp(flowSpeed, flowSpeed * 2.5f, hungerBlend), spacing);
        if (visibility > 0f && corners.Count >= 2) Draw(me);
    }

    void Draw(Vector3 me)
    {
        // The trail starts where he stands now: his projection onto the path, then the rest of the corners.
        int seg = 0; Vector3 start = corners[0]; float best = float.MaxValue;
        for (int i = 1; i < corners.Count; i++)
        {
            Vector3 p = ClosestOnSegment(corners[i - 1], corners[i], me);
            float d = Flat(p - me);
            if (d < best) { best = d; seg = i; start = p; }
        }
        line.Clear();
        line.Add(start);
        for (int i = seg; i < corners.Count; i++) line.Add(corners[i]);
        if (line.Count < 2) return;

        float pulse = 1f - hungerBlend * 0.35f * (0.5f + 0.5f * Mathf.Sin(Time.time * 7f));
        Color baseColor = Color.Lerp(guideColor, hungerColor, hungerBlend);
        float next = startAhead + flow, walked = 0f;
        for (int i = 1; i < line.Count && next < length; i++)
        {
            Vector3 a = line[i - 1], b = line[i];
            float segLen = Flat(b - a);
            if (segLen < 0.001f) continue;
            Vector3 dir = b - a; dir.y = 0f;
            var rot = Quaternion.LookRotation(dir.normalized, Vector3.up);
            while (next <= walked + segLen && next < length)
            {
                Vector3 p = Vector3.Lerp(a, b, (next - walked) / segLen) + Vector3.up * 0.06f;
                // Fade in near his feet and out at the far end, so chevrons slide in and out instead of popping.
                float ends = Mathf.Clamp01((next - startAhead) / 1.5f) * Mathf.Clamp01((length - next) / 4f);
                var c = baseColor; c.a *= ends * visibility * pulse;
                block.SetColor(ColorId, c);
                Graphics.DrawMesh(mesh, Matrix4x4.TRS(p, rot, Vector3.one * chevronSize), material, 0, null, 0, block, false, false);
                next += spacing;
            }
            walked += segLen;
        }
    }

    static Vector3 ClosestOnSegment(Vector3 a, Vector3 b, Vector3 p)
    {
        Vector3 ab = b - a; ab.y = 0f;
        float len2 = ab.sqrMagnitude;
        if (len2 < 0.0001f) return a;
        Vector3 ap = p - a; ap.y = 0f;
        return Vector3.Lerp(a, b, Mathf.Clamp01(Vector3.Dot(ap, ab) / len2));
    }

    Vector3? NearestPrey(Vector3 me)
    {
        HumanAI prey = null; float best = float.MaxValue;
        foreach (var h in FindObjectsByType<HumanAI>(FindObjectsSortMode.None))
        {
            if (!h.CanBeBitten) continue;
            float d = (h.transform.position - me).sqrMagnitude;
            if (d < best) { best = d; prey = h; }
        }
        return prey != null ? prey.transform.position : (Vector3?)null;
    }

    static float Flat(Vector3 v) { v.y = 0f; return v.magnitude; }

    /// A flat V pointing along +z, lying on the ground.
    static Mesh BuildChevron()
    {
        const float w = 0.5f, t = 0.22f, back = 0.4f;
        var m = new Mesh { name = "Chevron" };
        m.vertices = new[]
        {
            new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, -t * 1.4f),
            new Vector3(-w, 0f, -back), new Vector3(-w, 0f, -back - t),
            new Vector3(w, 0f, -back), new Vector3(w, 0f, -back - t),
        };
        m.triangles = new[] { 0, 2, 3, 0, 3, 1, 0, 1, 5, 0, 5, 4 };
        m.colors = new[] { Color.white, Color.white, Color.white, Color.white, Color.white, Color.white };
        m.RecalculateBounds();
        return m;
    }
}
