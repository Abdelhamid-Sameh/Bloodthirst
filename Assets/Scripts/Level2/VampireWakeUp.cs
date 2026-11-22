using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Level 2, Room 1 (Sohaila): the vampire wakes up in cell 1.
/// He starts asleep on the cell floor (lying along the cell, feet toward the camera), gets up, stands there exhausted (looping),
/// drinks, and then the player takes over. Order: Sleeping, Getting Up, Exhausted, Drinking, Idle.
/// While it plays his controls are locked (BeginAbility / EndAbility, like any ability), so nothing else has to know about it.
///
/// For Ali (mana and escape): call BeginDrinking() when the cell gives him mana (the rat, the dripping blood),
/// or listen to onDrinkStart to fill the mana bar. By default he drinks by himself after exhaustedSeconds so the
/// room is playable end to end before the mana source exists; set exhaustedSeconds below 0 to wait for BeginDrinking().
/// Replaces the marker: Placeholders/Wake_Exhausted_GainMana_Teleport (the waking-up part only).
/// </summary>
public class VampireWakeUp : MonoBehaviour
{
    [Tooltip("The vampire (found in the scene if empty).")]
    public VampireController vampire;
    [Tooltip("Play the wake-up when the level starts.")]
    public bool playOnStart = true;
    [Tooltip("Which way he lies and then faces when he gets up: the direction his FEET point (0 = +z, 90 = +x, 180 = -z, 270 = -x).\n" +
             "270 = along the cell's long axis (parallel to the bench) with his feet toward the camera, so he wakes facing the camera.")]
    public float lieYaw = 270f;
    [Tooltip("How long he lies asleep before he wakes up. Below 0 = wait until WakeUp() is called.")]
    public float sleepSeconds = 3f;
    [Tooltip("How long he stays slumped before he drinks by himself. Below 0 = wait until BeginDrinking() is called.")]
    public float exhaustedSeconds = 4f;
    [Tooltip("Safety: give up (and give him his controls back) if the animation never finishes.")]
    public float timeout = 45f;

    [Header("Events")]
    [Tooltip("He is lying asleep (the very start of the level).")]
    public UnityEvent onSleeping = new UnityEvent();
    [Tooltip("He wakes and starts getting up.")]
    public UnityEvent onGettingUp = new UnityEvent();
    [Tooltip("He is on his feet, slumped and exhausted.")]
    public UnityEvent onExhausted = new UnityEvent();
    [Tooltip("He starts drinking (fill his mana here).")]
    public UnityEvent onDrinkStart = new UnityEvent();
    [Tooltip("He is on his feet and the player has control again.")]
    public UnityEvent onWoke = new UnityEvent();

    public bool IsWaking { get; private set; }
    public bool HasWoken { get; private set; }

    static readonly int DrinkHash = Animator.StringToHash("Drink");
    static readonly int WakeUpHash = Animator.StringToHash("WakeUp");
    static readonly int[] StrayTriggers = {
        Animator.StringToHash("Jump"), Animator.StringToHash("Bite"), Animator.StringToHash("Hit"),
        Animator.StringToHash("Attack"), Animator.StringToHash("Checkpoint") };
    const string SleepingState = "Sleeping";
    const string ExhaustedState = "Exhausted";
    const string DrinkingState = "Drinking";
    const string GettingUpState = "Getting Up";

    Animator animator;
    bool drinkRequested;
    bool wakeRequested;
    Coroutine routine;

    IEnumerator Start()
    {
        if (vampire == null) vampire = FindFirstObjectByType<VampireController>();
        if (vampire != null) animator = vampire.GetComponent<Animator>();
        // two frames: Level2Director.Start (checkpoint, mana) and the vampire's own Start run first
        yield return null;
        yield return null;
        if (playOnStart) Play();
    }

    /// <summary>Starts the wake-up (does nothing if it is already playing).</summary>
    [ContextMenu("Play wake-up now")]
    public void Play()
    {
        if (IsWaking || vampire == null || animator == null) return;
        routine = StartCoroutine(WakeRoutine());
    }

    /// <summary>Wakes him from sleep now (only needed when sleepSeconds is below 0).</summary>
    public void WakeUp() => wakeRequested = true;

    /// <summary>Ali: call this when the cell gives him mana. He stops being exhausted and drinks.</summary>
    public void BeginDrinking() => drinkRequested = true;

    /// <summary>Gives him his controls back at once (debug, or a checkpoint that skips the intro).</summary>
    public void SkipToAwake()
    {
        if (routine != null) StopCoroutine(routine);
        routine = null;
        if (IsWaking) vampire.EndAbility();
        IsWaking = false;
        HasWoken = true;
        animator.CrossFadeInFixedTime("Idle", 0.1f, 0);
        onWoke.Invoke();
    }

    IEnumerator WakeRoutine()
    {
        IsWaking = true;
        drinkRequested = false;
        wakeRequested = false;
        float started = Time.time;

        vampire.BeginAbility();   // controls locked
        vampire.transform.rotation = Quaternion.Euler(0f, lieYaw, 0f);   // lies along the cell, feet toward the camera (not turned toward the mouse)
        animator.ResetTrigger(DrinkHash);
        animator.ResetTrigger(WakeUpHash);
        ClearStrayTriggers();     // a Jump queued in the first frames would drag him out of the pose

        // asleep, lying down (looping) until he wakes
        animator.CrossFadeInFixedTime(SleepingState, 0.1f, 0);
        onSleeping.Invoke();
        float sleepUntil = sleepSeconds >= 0f ? Time.time + sleepSeconds : float.PositiveInfinity;
        while (!wakeRequested && !drinkRequested && Time.time < sleepUntil)
        {
            if (Aborted(started)) yield break;
            ClearStrayTriggers();
            yield return null;
        }

        // wakes up and gets up (lying -> standing, facing the way his feet pointed)
        animator.SetTrigger(WakeUpHash);
        float woke = 0f;
        while (!InState(GettingUpState) && woke < 1.5f)
        {
            if (Aborted(started)) yield break;
            ClearStrayTriggers();
            woke += Time.deltaTime;
            yield return null;
        }
        onGettingUp.Invoke();

        // on his feet, he stands there exhausted (looping)
        woke = 0f;
        while (!InState(ExhaustedState) && woke < 8f)
        {
            if (Aborted(started)) yield break;
            ClearStrayTriggers();
            woke += Time.deltaTime;
            yield return null;
        }
        onExhausted.Invoke();

        // exhausted (looping) until he drinks
        float exhaustedUntil = exhaustedSeconds >= 0f ? Time.time + exhaustedSeconds : float.PositiveInfinity;
        while (!drinkRequested && Time.time < exhaustedUntil)
        {
            if (Aborted(started)) yield break;
            ClearStrayTriggers();
            yield return null;
        }

        // drink
        animator.SetTrigger(DrinkHash);
        float waited = 0f;
        while (!InState(DrinkingState) && waited < 1f)
        {
            if (Aborted(started)) yield break;
            ClearStrayTriggers();
            waited += Time.deltaTime;
            yield return null;
        }
        onDrinkStart.Invoke();

        // wait until the drink has fully played out (it ends in idle)
        while (InState(DrinkingState) || InState(ExhaustedState))
        {
            if (Aborted(started)) yield break;
            ClearStrayTriggers();
            yield return null;
        }

        vampire.EndAbility();
        IsWaking = false;
        HasWoken = true;
        routine = null;
        onWoke.Invoke();
    }

    void ClearStrayTriggers()
    {
        foreach (int h in StrayTriggers) animator.ResetTrigger(h);
    }

    bool Aborted(float started)
    {
        if (vampire.IsDead)
        {
            // dying hands the vampire over to the checkpoint / respawn flow
            IsWaking = false;
            routine = null;
            return true;
        }
        if (Time.time - started > timeout)
        {
            Debug.LogWarning("[VampireWakeUp] The wake-up did not finish in time; giving him his controls back.");
            vampire.EndAbility();
            animator.CrossFadeInFixedTime("Idle", 0.1f, 0);
            IsWaking = false;
            HasWoken = true;
            routine = null;
            onWoke.Invoke();
            return true;
        }
        return false;
    }

    bool InState(string stateName)
    {
        AnimatorStateInfo now = animator.GetCurrentAnimatorStateInfo(0);
        if (now.IsName(stateName)) return true;
        return animator.IsInTransition(0) && animator.GetNextAnimatorStateInfo(0).IsName(stateName);
    }
}
