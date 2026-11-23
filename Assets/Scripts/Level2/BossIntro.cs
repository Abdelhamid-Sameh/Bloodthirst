using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// The Armoured Officer's entrance, once, when the level reaches the Boss stage (the waves are beaten):
/// the camera pans to the garage, the shutter rolls up, he walks out into the yard, then his name and
/// health bar come up at the bottom of the screen, the boss music starts and the camera returns to the vampire.
/// The vampire cannot act during it. When the boss falls (Level2Director.onBossDefeated) the bar fades and the music ends.
/// </summary>
public class BossIntro : MonoBehaviour
{
    [Tooltip("The boss himself (hidden until he steps out).")]
    public Transform boss;
    public Door garage;
    public AudioClip garageSound;
    [Range(0f, 1f)] public float garageVolume = 0.9f;
    [Tooltip("Where he waits behind the shutter.")]
    public Vector3 insideGarage = new Vector3(82.5f, 0f, 23.5f);
    [Tooltip("Where he stops in the yard, facing the vampire.")]
    public Vector3 stepOutTo = new Vector3(82.5f, 0f, 15f);
    public float walkSpeed = 1.4f;

    [Header("Fight")]
    public string bossName = "Armoured Officer";
    [Tooltip("Until his combat system sets the bar itself (BossBar.SetHealth).")]
    public float bossMaxHealth = 100f;
    [Tooltip("Played once when the bar comes up, then the loop follows without a gap.")]
    public AudioClip bossMusicIntro;
    [Tooltip("The fight music; loops until he falls.")]
    public AudioClip bossMusic;
    [Range(0f, 1f)] public float musicVolume = 0.6f;
    [Tooltip("Music volume multiplier while the pause menu is open (as the level music does).")]
    [Range(0f, 1f)] public float pausedVolumeScale = 0.4f;

    [Header("Camera")]
    public float panTime = 1.4f;
    public float holdOnBoss = 1.2f;

    [Tooltip("The intro is over: the fight is on (start the boss's AI here).")]
    public UnityEvent onFightStart = new UnityEvent();

    public bool Played { get; private set; }

    AudioSource intro, music;
    MusicPlayer levelMusic;
    float fightVolume;   // 0..1, eased: the boss music in, the level music out (and back after the fight)

    void Start()
    {
        var director = Level2Director.Instance;
        if (boss != null) boss.gameObject.SetActive(false);
        if (director == null) return;
        director.onStageChanged.AddListener(s => { if (s == Level2Director.Stage.Boss && !Played) StartCoroutine(Play()); });
        director.onBossDefeated.AddListener(EndFight);
    }

    IEnumerator Play()
    {
        Played = true;
        var vampire = Level2Director.Instance.vampire;
        vampire.BeginAbility();   // no moving or fighting during the entrance
        var cam = FindFirstObjectByType<IsometricCamera>();
        Transform follow = cam != null ? cam.target : vampire.transform;
        float lookAhead = cam != null ? cam.mouseLookAhead : 0f;
        var view = new GameObject("BossIntroView").transform;
        view.position = follow.position;
        if (cam != null) { cam.target = view; cam.mouseLookAhead = 0f; }

        Vector3 door = garage != null ? new Vector3(garage.transform.position.x, 0f, garage.transform.position.z) : insideGarage;
        yield return Glide(view, door, panTime);

        if (garage != null) garage.Open();
        if (garageSound != null) AudioSource.PlayClipAtPoint(garageSound, door, garageVolume);
        yield return new WaitForSeconds(garage != null ? garage.duration + 0.3f : 0.8f);

        if (boss != null)
        {
            boss.position = insideGarage;
            boss.rotation = Quaternion.LookRotation(Flat(stepOutTo - insideGarage));
            boss.gameObject.SetActive(true);
            var anim = boss.GetComponentInChildren<Animator>();
            if (anim != null) anim.SetFloat("Speed", walkSpeed);
            while (Flat(stepOutTo - boss.position).sqrMagnitude > 0.0025f)
            {
                boss.position = Vector3.MoveTowards(boss.position, stepOutTo, walkSpeed * Time.deltaTime);
                view.position = Vector3.Lerp(view.position, boss.position, Time.deltaTime * 3f);
                yield return null;
            }
            if (anim != null) anim.SetFloat("Speed", 0f);
            boss.rotation = Quaternion.LookRotation(Flat(vampire.transform.position - boss.position));
        }

        BossBar.Show(bossName, bossMaxHealth);
        StartMusic();
        yield return new WaitForSeconds(holdOnBoss);

        yield return Glide(view, follow.position, panTime * 0.6f);
        if (cam != null) { cam.target = follow; cam.mouseLookAhead = lookAhead; }
        Destroy(view.gameObject);
        vampire.EndAbility();
        onFightStart.Invoke();
    }

    IEnumerator Glide(Transform t, Vector3 to, float time)
    {
        Vector3 from = t.position;
        for (float k = 0f; k < 1f; k += Time.deltaTime / Mathf.Max(0.01f, time))
        {
            t.position = Vector3.Lerp(from, to, k * k * (3f - 2f * k));   // ease in and out
            yield return null;
        }
        t.position = to;
    }

    void StartMusic()
    {
        if (bossMusic == null) return;
        levelMusic = FindFirstObjectByType<MusicPlayer>();
        if (levelMusic != null) levelMusic.duck = 0f;   // the level's own music fades out under the boss theme
        intro = MakeSource(bossMusicIntro, false);
        music = MakeSource(bossMusic, true);
        double start = AudioSettings.dspTime + 0.1;
        if (bossMusicIntro != null)
        {
            intro.PlayScheduled(start);
            start += (double)bossMusicIntro.samples / bossMusicIntro.frequency;   // the loop starts on the intro's last sample
        }
        music.PlayScheduled(start);
        fightVolume = 1f;
        music.volume = intro.volume = musicVolume * GameSettings.MusicVolume;   // the theme hits at full volume; only its end fades
    }

    AudioSource MakeSource(AudioClip clip, bool loop)
    {
        var s = gameObject.AddComponent<AudioSource>();
        s.clip = clip;
        s.loop = loop;
        s.playOnAwake = false;
        s.spatialBlend = 0f;
        s.ignoreListenerPause = true;
        s.volume = 0f;
        return s;
    }

    void Update()
    {
        if (music == null) return;
        float target = fightVolume * musicVolume * GameSettings.MusicVolume * (GameState.Paused ? pausedVolumeScale : 1f);
        music.volume = intro.volume = Mathf.MoveTowards(music.volume, target, Time.unscaledDeltaTime / 3f);   // 3 s fades
        if (fightVolume <= 0f && music.volume <= 0f) { intro.Stop(); music.Stop(); }
    }

    void EndFight()
    {
        BossBar.Hide();
        fightVolume = 0f;
        if (levelMusic != null) levelMusic.duck = 1f;
    }

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
}
