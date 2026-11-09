using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

// Builds the game's AudioMixer and fills the SoundBank with the placeholder clips, so both can be rebuilt from code.
// Unity has no public API for creating mixer groups, effects or exposed parameters, so the mixer is built through
// the editor's internal AudioMixerController (the same calls the Audio Mixer window makes). Rebuilding replaces the
// mixer asset in place, keeping its GUID, so the SoundBank's reference survives.
public static class SoundSetup
{
    public const string MixerPath = "Assets/Resources/Audio/GameMixer.mixer";
    public const string BankPath = "Assets/Resources/Audio/SoundBank.asset";
    public const string PlaceholderFolder = "Assets/Audio/Placeholder";

    // Exposed parameter names read by SoundDirector.
    public static readonly string[] GroupNames = { "World", "Bullets", "Player", "Music" };
    private static readonly HashSet<string> GroupsWithLowPass = new HashSet<string> { "World", "Bullets", "Music" };
    private const string LowPassEffect = "Lowpass Simple";
    private const string CutoffParameter = "Cutoff freq";

    private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private static readonly Assembly EditorAssembly = typeof(AudioImporter).Assembly;
    private static Type T(string name) => EditorAssembly.GetType("UnityEditor.Audio." + name, true);

    [MenuItem("Tools/Sound/Rebuild Game Mixer")]
    public static void RebuildMixer()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(MixerPath));
        string guid = AssetDatabase.AssetPathToGUID(MixerPath);
        string metaBackup = null;
        if (!string.IsNullOrEmpty(guid) && File.Exists(MixerPath + ".meta"))
            metaBackup = File.ReadAllText(MixerPath + ".meta");

        var controllerType = T("AudioMixerController");
        string temporary = "Assets/Resources/Audio/GameMixer.building.mixer";
        AssetDatabase.DeleteAsset(temporary);
        var mixer = controllerType.GetMethod("CreateMixerControllerAtPath", Any).Invoke(null, new object[] { temporary });
        var master = controllerType.GetProperty("masterGroup", Any).GetValue(mixer);
        var snapshot = controllerType.GetProperty("TargetSnapshot", Any).GetValue(mixer);

        foreach (var name in GroupNames)
        {
            var group = controllerType.GetMethod("CreateNewGroup", Any).Invoke(mixer, new object[] { name, false });
            controllerType.GetMethod("AddChildToParent", Any).Invoke(mixer, new[] { group, master });
            var groupType = group.GetType();

            Expose(mixer, NewGroupPath(group, (GUID)groupType.GetMethod("GetGUIDForVolume", Any).Invoke(group, null)), name + "Volume");
            if (name != "Player")
                Expose(mixer, NewGroupPath(group, (GUID)groupType.GetMethod("GetGUIDForPitch", Any).Invoke(group, null)), name + "Pitch");

            if (GroupsWithLowPass.Contains(name))
            {
                var effectType = T("AudioMixerEffectController");
                var effect = Activator.CreateInstance(effectType, Any, null, new object[] { LowPassEffect }, null);
                controllerType.GetMethod("AddNewSubAsset", Any).Invoke(mixer, new[] { effect, (object)false });
                effectType.GetMethod("PreallocateGUIDs", Any).Invoke(effect, null);
                groupType.GetMethod("InsertEffect", Any, null, new[] { effectType, typeof(int) }, null).Invoke(group, new[] { effect, (object)0 });
                effectType.GetMethod("SetValueForParameter", Any).Invoke(effect, new[] { mixer, snapshot, CutoffParameter, (object)22000f });
                var cutoff = (GUID)effectType.GetMethod("GetGUIDForParameter", Any).Invoke(effect, new object[] { CutoffParameter });
                var path = Activator.CreateInstance(T("AudioEffectParameterPath"), Any, null, new[] { group, effect, (object)cutoff }, null);
                Expose(mixer, path, name + "Cutoff");
            }
        }
        ShowAllGroupsInOneView(mixer, master);
        controllerType.GetMethod("OnSubAssetChanged", Any)?.Invoke(mixer, null);
        EditorUtility.SetDirty((UnityEngine.Object)mixer);
        AssetDatabase.SaveAssets();

        // Swap the new mixer into place under the old GUID.
        if (metaBackup != null)
        {
            AssetDatabase.DeleteAsset(MixerPath);
            File.Copy(temporary, MixerPath, true);
            File.WriteAllText(MixerPath + ".meta", metaBackup);
            AssetDatabase.DeleteAsset(temporary);
        }
        else
        {
            AssetDatabase.MoveAsset(temporary, MixerPath);
        }
        AssetDatabase.ImportAsset(MixerPath, ImportAssetOptions.ForceUpdate);
        var built = AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath);
        Debug.Log("Game mixer built at " + MixerPath + ": " + Describe(built));
    }

    // A mixer made from code starts with no group view, and the Audio Mixer window lists only groups in a view.
    private static void ShowAllGroupsInOneView(object mixer, object master)
    {
        var controllerType = mixer.GetType();
        var groups = new List<GUID>();
        var stack = new Stack<object>();
        stack.Push(master);
        while (stack.Count > 0)
        {
            var group = stack.Pop();
            groups.Add((GUID)group.GetType().GetProperty("groupID", Any).GetValue(group));
            foreach (var child in (Array)group.GetType().GetProperty("children", Any).GetValue(group)) stack.Push(child);
        }
        var viewsProperty = controllerType.GetProperty("views", Any);
        var viewType = viewsProperty.PropertyType.GetElementType();
        object view = Activator.CreateInstance(viewType);
        viewType.GetField("guids", Any).SetValue(view, groups.ToArray());
        viewType.GetField("name", Any).SetValue(view, "View");
        var views = Array.CreateInstance(viewType, 1);
        views.SetValue(view, 0);
        viewsProperty.SetValue(mixer, views);
        controllerType.GetProperty("currentViewIndex", Any).SetValue(mixer, 0);
    }

    private static object NewGroupPath(object group, GUID parameter) =>
        Activator.CreateInstance(T("AudioGroupParameterPath"), Any, null, new[] { group, (object)parameter }, null);

    private static void Expose(object mixer, object path, string name)
    {
        var controllerType = mixer.GetType();
        var property = controllerType.GetProperty("exposedParameters", Any);
        var elementType = property.PropertyType.GetElementType();
        var guidField = elementType.GetField("guid", Any);
        var nameField = elementType.GetField("name", Any);

        // The new parameter is the one whose GUID was not exposed before.
        var before = new HashSet<object>();
        foreach (var entry in (Array)property.GetValue(mixer)) before.Add(guidField.GetValue(entry));
        controllerType.GetMethod("AddExposedParameter", Any).Invoke(mixer, new[] { path });

        var parameters = (Array)property.GetValue(mixer);
        bool renamed = false;
        for (int i = 0; i < parameters.Length; i++)
        {
            object entry = parameters.GetValue(i);
            if (before.Contains(guidField.GetValue(entry))) continue;
            nameField.SetValue(entry, name);
            parameters.SetValue(entry, i);
            renamed = true;
        }
        property.SetValue(mixer, parameters);
        if (!renamed) Debug.LogError("Could not expose mixer parameter " + name);
    }

    public static string Describe(AudioMixer mixer)
    {
        if (mixer == null) return "missing";
        var parts = new List<string>();
        foreach (var group in mixer.FindMatchingGroups(string.Empty)) parts.Add("group " + group.name);
        foreach (var name in GroupNames)
            foreach (var suffix in new[] { "Volume", "Pitch", "Cutoff" })
                if (mixer.GetFloat(name + suffix, out float value)) parts.Add(name + suffix + "=" + value);
        return string.Join(", ", parts);
    }

    [MenuItem("Tools/Sound/Fill Sound Bank With Placeholders")]
    public static void FillBankWithPlaceholders()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(BankPath));
        var bank = AssetDatabase.LoadAssetAtPath<SoundBank>(BankPath);
        if (bank == null)
        {
            bank = ScriptableObject.CreateInstance<SoundBank>();
            AssetDatabase.CreateAsset(bank, BankPath);
        }
        bank.mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath);

        Set(bank.enemyShotNear, "enemy_shot_near", 0.9f, 0.06f);
        Set(bank.enemyShotFar, "enemy_shot_far", 0.9f, 0.06f);
        Set(bank.enemyHit, "enemy_hit", 1f, 0.05f);
        Set(bank.ricochetMetal, "ricochet_metal", 0.8f, 0.12f);
        Set(bank.ricochetConcrete, "ricochet_concrete", 0.8f, 0.12f);
        Set(bank.bulletAir, "bullet_air_loop", 0.6f, 0.04f);
        Set(bank.bulletPulse, "bullet_pulse", 0.8f, 0.05f);
        Set(bank.nearAir, "near_air_loop", 0.9f, 0f);
        Set(bank.nearWhump, "near_whump", 1f, 0.05f);
        Set(bank.gunDraw, "gun_draw", 0.5f, 0.04f);
        Set(bank.gunHolster, "gun_holster", 0.35f, 0.04f);
        Set(bank.pistolShot, "pistol_shot", 0.85f, 0.04f);
        Set(bank.earRing, "ear_ring", 0.18f, 0.02f);
        Set(bank.slowMotionEnter, "slowmo_enter", 0.35f, 0.03f);
        Set(bank.slowMotionExit, "slowmo_exit", 0.3f, 0.03f);
        Set(bank.tapFocus, "tap_focus", 0.6f, 0f);
        Set(bank.clockTick, "clock_tick", 0.45f, 0.01f);
        Set(bank.clockTock, "clock_tock", 0.45f, 0.01f);
        Set(bank.tapMiss, "tap_miss", 0.5f, 0.03f);
        Set(bank.hitNote, "hit_note", 0.5f, 0f);
        Set(bank.deathImpact, "death_impact", 1f, 0f);
        Set(bank.deathSwell, "death_swell", 0.7f, 0f);
        Set(bank.deathChord, "death_chord", 0.6f, 0f);
        Set(bank.slowMotionBed, "slowmo_bed_loop", 0.35f, 0f);
        Set(bank.heartbeat, "heartbeat_loop", 0.5f, 0f);
        Set(bank.music, "music_loop", 0.45f, 0f);

        EditorUtility.SetDirty(bank);
        AssetDatabase.SaveAssets();
        Debug.Log("Sound bank filled with placeholders at " + BankPath + "; mixer " + (bank.mixer != null ? "set" : "MISSING"));
    }

    private static void Set(SoundBank.Sound sound, string file, float volume, float pitchSpread)
    {
        sound.clip = AssetDatabase.LoadAssetAtPath<AudioClip>(PlaceholderFolder + "/" + file + ".wav");
        sound.volume = volume;
        sound.pitchSpread = pitchSpread;
        if (sound.clip == null) Debug.LogWarning("Placeholder sound missing: " + file);
    }
}
