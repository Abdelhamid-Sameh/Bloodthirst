using UnityEngine;

/// <summary>
/// The sound of the vampire's followers: shuffling feet and low moans that follow the crowd of thralls (ThrallAI.All)
/// and swell with its size, plus now and then a single moan from one of them. Silent while he has no followers.
/// </summary>
public class CrowdAudio : MonoBehaviour
{
    public AudioClip steps;
    public AudioClip moans;
    public AudioClip[] singleMoans = new AudioClip[0];
    [Range(0f, 1f)] public float stepsVolume = 0.7f;
    [Range(0f, 1f)] public float moansVolume = 0.45f;
    [Range(0f, 1f)] public float singleMoanVolume = 0.8f;
    [Tooltip("Followers needed for the full sound; fewer are quieter.")]
    public int fullCrowd = 8;
    [Tooltip("Seconds between two single moans (random in this range).")]
    public Vector2 moanGap = new Vector2(3f, 7f);

    AudioSource stepSource, moanSource;
    float nextMoan;

    void Awake()
    {
        stepSource = Loop(steps);
        moanSource = Loop(moans);
    }

    AudioSource Loop(AudioClip clip)
    {
        var s = gameObject.AddComponent<AudioSource>();
        s.clip = clip;
        s.loop = true;
        s.playOnAwake = false;
        s.spatialBlend = 0.6f;   // follows the crowd on screen but stays audible
        s.minDistance = 8f;
        s.maxDistance = 60f;
        s.volume = 0f;
        if (clip != null) { s.time = Random.Range(0f, clip.length); s.Play(); }
        return s;
    }

    void Update()
    {
        int count = 0;
        Vector3 centre = Vector3.zero;
        ThrallAI one = null;
        foreach (var t in ThrallAI.All)
        {
            if (t == null || !t.IsAlive) continue;
            count++;
            centre += t.transform.position;
            if (Random.Range(0, count) == 0) one = t;   // a random one of them
        }
        float size = Mathf.Clamp01(count / (float)Mathf.Max(1, fullCrowd));
        if (count > 0) transform.position = centre / count;
        stepSource.volume = Mathf.MoveTowards(stepSource.volume, stepsVolume * size, Time.deltaTime * 0.5f);
        moanSource.volume = Mathf.MoveTowards(moanSource.volume, moansVolume * size, Time.deltaTime * 0.5f);

        if (one != null && singleMoans.Length > 0 && Time.time >= nextMoan)
        {
            nextMoan = Time.time + Random.Range(moanGap.x, moanGap.y);
            var clip = singleMoans[Random.Range(0, singleMoans.Length)];
            if (clip != null) AudioSource.PlayClipAtPoint(clip, one.transform.position + Vector3.up * 1.6f, singleMoanVolume);
        }
    }
}
