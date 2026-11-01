using UnityEngine;
using UnityEngine.AI;

/// A trail of glowing chevrons on the ground, along the walkable path from the vampire to where he should go:
/// the police station normally, the nearest living human (brighter and pulsing faster) when he is starving.
public class BloodTrail : MonoBehaviour
{
    public VampireController vampire;
    public VampireBlood blood;
    [Tooltip("The police station (the ArrestZone).")]
    public Transform objective;
    [Tooltip("The trail ends this close to the objective, where the arrest begins.")]
    public float objectiveReachedDistance = 12f;

    [Header("Look")]
    public float length = 18f;
    public float spacing = 2.2f;
    public float startAhead = 1.6f;
    public float chevronSize = 0.9f;
    public Color guideColor = new Color(0.75f, 0.05f, 0.05f, 0.45f);
    public Color hungerColor = new Color(1f, 0.15f, 0.08f, 0.9f);
    [Range(0f, 1f)] public float hungerBelow = 0.45f;

    NavMeshPath path;
    Mesh mesh;
    public Material material;
    MaterialPropertyBlock block;
    float nextRepath;
    bool hungry;
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
        if (vampire == null || blood == null || !blood.IsActive) return;
        Vector3 me = vampire.transform.position;

        if (Time.time >= nextRepath)
        {
            nextRepath = Time.time + 0.4f;
            hungry = blood.Fraction < hungerBelow;
            Vector3? target = hungry ? NearestPrey(me) : (Vector3?)null;
            if (target == null && objective != null && Flat(objective.position - me) > objectiveReachedDistance) target = objective.position;
            path.ClearCorners();
            if (target != null && NavMesh.SamplePosition(me, out var a, 2f, NavMesh.AllAreas) && NavMesh.SamplePosition(target.Value, out var b, 4f, NavMesh.AllAreas))
                NavMesh.CalculatePath(a.position, b.position, NavMesh.AllAreas, path);
        }
        Draw();
    }

    void Draw()
    {
        var corners = path.corners;
        if (corners.Length < 2) return;
        Color baseColor = hungry ? hungerColor : guideColor;
        float speed = hungry ? 9f : 4f;
        float walked = 0f, next = startAhead;
        for (int i = 1; i < corners.Length && next < length; i++)
        {
            Vector3 a = corners[i - 1], b = corners[i];
            float seg = Vector3.Distance(a, b);
            Vector3 dir = seg > 0.001f ? (b - a) / seg : Vector3.forward;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) { walked += seg; continue; }
            var rot = Quaternion.LookRotation(dir.normalized, Vector3.up);
            while (next <= walked + seg && next < length)
            {
                Vector3 p = Vector3.Lerp(a, b, (next - walked) / seg) + Vector3.up * 0.06f;
                // A pulse runs along the trail toward the target; chevrons fade in near him and out at the far end.
                float wave = 0.45f + 0.55f * Mathf.Max(0f, Mathf.Sin((next - Time.time * speed) * 0.6f));
                float ends = Mathf.Clamp01((next - startAhead) / 2f + 0.3f) * Mathf.Clamp01((length - next) / 5f);
                var c = baseColor; c.a *= wave * ends;
                block.SetColor(ColorId, c);
                Graphics.DrawMesh(mesh, Matrix4x4.TRS(p, rot, Vector3.one * chevronSize), material, 0, null, 0, block, false, false);
                next += spacing;
            }
            walked += seg;
        }
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
