using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// In-game UI: a controls card that appears after the vampire climbs out of his coffin and then fades,
/// a context prompt ("E  Bite", "F  Rest in coffin") when an action is available,
/// and a pause menu (Esc or P) that lists the controls.
/// </summary>
public class GameUI : MonoBehaviour
{
    public VampireController vampire;

    [Header("Controls card (tutorial)")]
    public CanvasGroup controlsCard;
    [Tooltip("Seconds the card stays fully visible.")]
    public float cardHoldTime = 8f;
    public float cardFadeInTime = 0.6f;
    public float cardFadeOutTime = 1.5f;

    [Header("Context prompt")]
    public CanvasGroup prompt;
    public TMP_Text promptKey;
    public TMP_Text promptLabel;
    public float promptFadeSpeed = 6f;

    [Header("Pause menu")]
    public CanvasGroup pauseMenu;
    public Button resumeButton;
    public Button restartButton;
    public Button quitButton;

    bool cardShown;

    void Awake()
    {
        if (vampire == null) vampire = FindFirstObjectByType<VampireController>();
        GameState.Paused = false;
        Time.timeScale = 1f;
        AudioListener.pause = false;
        SetGroup(controlsCard, 0f, false);
        SetGroup(prompt, 0f, false);
        SetGroup(pauseMenu, 0f, false);
        if (resumeButton != null) resumeButton.onClick.AddListener(() => SetPaused(false));
        if (restartButton != null) restartButton.onClick.AddListener(Restart);
        if (quitButton != null) quitButton.onClick.AddListener(Quit);
    }

    void OnEnable()
    {
        if (vampire != null) vampire.onEmerged.AddListener(ShowControlsCard);
    }

    void OnDisable()
    {
        if (vampire != null) vampire.onEmerged.RemoveListener(ShowControlsCard);
    }

    void Start()
    {
        // No coffin intro: show the card straight away.
        if (vampire == null || !vampire.startInCoffin) ShowControlsCard();
    }

    /// <summary>Shows the controls card once (called when he leaves the coffin, or by the intro cutscene).</summary>
    public void ShowControlsCard()
    {
        if (cardShown || controlsCard == null) return;
        cardShown = true;
        StartCoroutine(ControlsCardRoutine());
    }

    IEnumerator ControlsCardRoutine()
    {
        yield return Fade(controlsCard, 1f, cardFadeInTime);
        float t = 0f;
        while (t < cardHoldTime)
        {
            if (!GameState.Paused) t += Time.unscaledDeltaTime;
            yield return null;
        }
        yield return Fade(controlsCard, 0f, cardFadeOutTime);
    }

    IEnumerator Fade(CanvasGroup group, float to, float duration)
    {
        float from = group.alpha;
        float t = 0f;
        while (t < 1f)
        {
            if (!GameState.Paused) t += Time.unscaledDeltaTime / Mathf.Max(0.01f, duration);
            group.alpha = Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t)));
            yield return null;
        }
    }

    void Update()
    {
        Keyboard kb = Keyboard.current;
        if (kb != null && (kb.escapeKey.wasPressedThisFrame || kb.pKey.wasPressedThisFrame)) SetPaused(!GameState.Paused);
        UpdatePrompt();
    }

    void UpdatePrompt()
    {
        if (prompt == null) return;
        string key = null, label = null;
        if (!GameState.Paused && vampire != null)
        {
            if (vampire.IsDead)
            {
                if (vampire.debugKeys) { key = "R"; label = "Rise again"; }
            }
            else if (!vampire.IsBusy)
            {
                if (vampire.BiteTargetInReach != null) { key = "E"; label = "Bite"; }
                else if (vampire.CoffinInReach != null) { key = "F"; label = "Rest in coffin"; }
            }
        }

        if (key != null)
        {
            if (promptKey != null) promptKey.text = key;
            if (promptLabel != null) promptLabel.text = label;
        }
        prompt.alpha = Mathf.MoveTowards(prompt.alpha, key != null ? 1f : 0f, Time.unscaledDeltaTime * promptFadeSpeed);
    }

    public void SetPaused(bool paused)
    {
        GameState.Paused = paused;
        Time.timeScale = paused ? 0f : 1f;
        AudioListener.pause = paused;
        SetGroup(pauseMenu, paused ? 1f : 0f, paused);
        if (paused && resumeButton != null && EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(resumeButton.gameObject);
    }

    public void Restart()
    {
        SetPaused(false);
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    public void Quit()
    {
        SetPaused(false);
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    static void SetGroup(CanvasGroup group, float alpha, bool interactive)
    {
        if (group == null) return;
        group.alpha = alpha;
        group.interactable = interactive;
        group.blocksRaycasts = interactive;
    }
}
