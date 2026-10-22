using UnityEngine;

/// <summary>Global game flags shared by gameplay and UI scripts.</summary>
public static class GameState
{
    /// <summary>True while the pause menu is open. Gameplay scripts ignore input while paused.</summary>
    public static bool Paused { get; set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetOnPlay() => Paused = false;
}
