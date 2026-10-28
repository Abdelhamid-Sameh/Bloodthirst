using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Bloodthirst > Assign Sounds: fills every sound slot in the open scene(s) from Assets/Audio by file name.
/// A slot takes every clip in its folder whose name starts with its prefix (footstep_01, footstep_02, ...),
/// so variations are picked at random in game. Slots with no matching files are left as they are.
/// See Assets/Audio/SOUND_LIST.md for the names.
/// </summary>
public static class SoundAssigner
{
    const string Root = "Assets/Audio";

    [MenuItem("Bloodthirst/Assign Sounds From Assets-Audio")]
    public static void Assign()
    {
        EditorUtility.DisplayDialog("Assign Sounds", Run() + "\n\nSave the scene to keep the changes.", "OK");
    }

    /// <summary>Does the assignment and returns the summary (no dialog, so it can be scripted).</summary>
    public static string Run()
    {
        if (!AssetDatabase.IsValidFolder(Root)) return Root + " does not exist yet. Add sound files there first (see SOUND_LIST.md).";
        ApplyImportSettings();
        var report = new StringBuilder();
        var missing = new SortedSet<string>();

        foreach (var v in Find<VampireAudio>())
        {
            Undo.RecordObject(v, "Assign sounds");
            Set(ref v.footsteps, "Vampire", "footstep", missing);
            Set(ref v.jump, "Vampire", "jump", missing);
            Set(ref v.land, "Vampire", "land", missing);
            Set(ref v.bite, "Vampire", "bite", missing);
            Set(ref v.feed, "Vampire", "feed", missing);
            Set(ref v.hurt, "Vampire", "hurt", missing);
            Set(ref v.death, "Vampire", "death", missing);
            Set(ref v.lieDown, "Vampire", "liedown", missing);
            Set(ref v.rise, "Vampire", "rise", missing);
            Set(ref v.vanish, "Vampire", "vanish", missing);
            Set(ref v.appear, "Vampire", "appear", missing);
            Set(ref v.heartbeat, "Vampire", "heartbeat", missing);
            Done(v, report);
        }
        foreach (var h in Find<HumanAudio>())
        {
            Undo.RecordObject(h, "Assign sounds");
            Set(ref h.walkSteps, "Humans", "walk", missing);
            Set(ref h.runSteps, "Humans", "run", missing);
            Set(ref h.alertedGasps, "Humans", "gasp", missing);
            Set(ref h.fleeScreams, "Humans", "scream_flee", missing);
            Set(ref h.capturedScreams, "Humans", "scream_captured", missing);
            Set(ref h.deathSounds, "Humans", "death", missing);
            Done(h, report);
        }
        foreach (var p in Find<PoliceAI>())
        {
            Undo.RecordObject(p, "Assign sounds");
            Set(ref p.shotSound, "Police", "gunshot", missing);
            Done(p, report);
        }
        foreach (var c in Find<CoffinCheckpoint>())
        {
            Undo.RecordObject(c, "Assign sounds");
            Set(ref c.lidOpenClip, "Coffin", "lid_open", missing);
            Set(ref c.lidCloseClip, "Coffin", "lid_close", missing);
            Done(c, report);
        }
        foreach (var m in Find<MusicPlayer>())
        {
            Undo.RecordObject(m, "Assign sounds");
            Set(ref m.music, "Music", "music", missing);
            Set(ref m.ambience, "Music", "ambience", missing);
            Done(m, report);
        }

        for (int i = 0; i < EditorSceneManager.sceneCount; i++) EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetSceneAt(i));
        string summary = "Updated:\n" + (report.Length > 0 ? report.ToString() : "(no sound components in the open scene)\n") +
                         (missing.Count > 0 ? "\nStill silent (no files yet):\n" + string.Join("\n", missing) : "\nEvery slot has a sound.");
        Debug.Log("[Assign Sounds]\n" + summary);
        return summary;
    }

    static IEnumerable<T> Find<T>() where T : Object =>
        Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID);

    static void Done(Component c, StringBuilder report)
    {
        EditorUtility.SetDirty(c);
        PrefabUtility.RecordPrefabInstancePropertyModifications(c);
        report.AppendLine("  " + c.GetType().Name + " on " + c.gameObject.name);
    }

    /// <summary>All clips in Assets/Audio/folder whose file name starts with prefix, sorted by name.</summary>
    public static AudioClip[] Clips(string folder, string prefix)
    {
        string dir = Root + "/" + folder;
        if (!AssetDatabase.IsValidFolder(dir)) return new AudioClip[0];
        return AssetDatabase.FindAssets("t:AudioClip", new[] { dir })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(p => Path.GetFileNameWithoutExtension(p).ToLowerInvariant().StartsWith(prefix))
            .OrderBy(p => p)
            .Select(AssetDatabase.LoadAssetAtPath<AudioClip>)
            .ToArray();
    }

    static void Set(ref AudioClip[] slot, string folder, string prefix, ISet<string> missing)
    {
        var clips = Clips(folder, prefix);
        if (clips.Length > 0) slot = clips;
        else if (slot == null || slot.Length == 0) missing.Add(folder + "/" + prefix + "*");
    }

    static void Set(ref AudioClip slot, string folder, string prefix, ISet<string> missing)
    {
        var clips = Clips(folder, prefix);
        if (clips.Length > 0) slot = clips[0];
        else if (slot == null) missing.Add(folder + "/" + prefix + "*");
    }

    // Music streams from disk; short effects are decompressed in memory and mono (they are positioned in 3D anyway).
    static void ApplyImportSettings()
    {
        foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { Root }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!(AssetImporter.GetAtPath(path) is AudioImporter importer)) continue;
            bool isMusic = path.StartsWith(Root + "/Music/");
            var s = importer.defaultSampleSettings;
            s.loadType = isMusic ? AudioClipLoadType.Streaming : AudioClipLoadType.DecompressOnLoad;
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            s.quality = isMusic ? 0.6f : 0.75f;
            bool changed = importer.forceToMono == isMusic || !SameSettings(importer.defaultSampleSettings, s);
            if (!changed) continue;
            importer.forceToMono = !isMusic;
            importer.defaultSampleSettings = s;
            importer.SaveAndReimport();
        }
    }

    static bool SameSettings(AudioImporterSampleSettings a, AudioImporterSampleSettings b) =>
        a.loadType == b.loadType && a.compressionFormat == b.compressionFormat && Mathf.Approximately(a.quality, b.quality);
}
