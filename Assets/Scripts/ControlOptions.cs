using UnityEngine;

// Control choices that can be switched while testing; remembered between launches.
// ControlsPanel flips them in game, so a phone build does not need rebuilding to compare.
public static class ControlOptions
{
    public enum TimeControlMode
    {
        // Time follows how fast the device is tilted and runs normally while the screen is held.
        Tilt,
        // Time follows how dangerous the incoming shots are; a sharp tilt wins back part of the slow-down, the tap does nothing.
        Danger,
    }

    // Smallest horizontal field of view on a portrait screen, in degrees, so enemies at the street sides stay in frame.
    public const float PortraitHorizontalFieldOfView = 50f;

    private const string TimeControlKey = "Controls.TimeControl";
    private const string VerticalDodgeKey = "Controls.VerticalDodge";
    private const string PortraitKey = "Controls.Portrait";

    private static bool loaded;
    private static TimeControlMode timeControl;
    private static bool verticalDodge;
    private static bool portrait;

    public static TimeControlMode TimeControl
    {
        get { Load(); return timeControl; }
        set
        {
            Load();
            if (timeControl == value) return;
            timeControl = value;
            PlayerPrefs.SetInt(TimeControlKey, (int)value);
            PlayerPrefs.Save();
        }
    }

    // On: tilting the top edge towards or away ducks or raises the player and, in Tilt time control, also drives time.
    // Off: that tilt does nothing at all, so adjusting the grip neither moves the player nor lets time run.
    public static bool VerticalDodge
    {
        get { Load(); return verticalDodge; }
        set
        {
            Load();
            if (verticalDodge == value) return;
            verticalDodge = value;
            PlayerPrefs.SetInt(VerticalDodgeKey, value ? 1 : 0);
            PlayerPrefs.Save();
        }
    }

    // Portrait instead of landscape. A device turns its screen; the Editor flips the Game View resolution.
    public static bool Portrait
    {
        get { Load(); return portrait; }
        set
        {
            Load();
            if (portrait == value) return;
            portrait = value;
            PlayerPrefs.SetInt(PortraitKey, value ? 1 : 0);
            PlayerPrefs.Save();
            ApplyOrientation();
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        loaded = false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ApplyOnLaunch()
    {
        ApplyOrientation();
    }

    private static void Load()
    {
        if (loaded) return;
        loaded = true;
        timeControl = PlayerPrefs.GetInt(TimeControlKey, (int)TimeControlMode.Danger) == (int)TimeControlMode.Tilt
            ? TimeControlMode.Tilt
            : TimeControlMode.Danger;
        verticalDodge = PlayerPrefs.GetInt(VerticalDodgeKey, 1) != 0;
        portrait = PlayerPrefs.GetInt(PortraitKey, 0) != 0;
    }

    private static void ApplyOrientation()
    {
        bool wantPortrait = Portrait;
        // Tilt controls need a fixed orientation: auto-rotation would turn the screen mid-dodge.
        if (Application.isMobilePlatform)
            Screen.orientation = wantPortrait ? ScreenOrientation.Portrait : ScreenOrientation.LandscapeLeft;
#if UNITY_EDITOR
        UnityEditor.PlayModeWindow.GetRenderingResolution(out uint width, out uint height);
        if (width > 0 && height > 0 && height > width != wantPortrait)
            UnityEditor.PlayModeWindow.SetCustomRenderingResolution(height, width, wantPortrait ? "Portrait" : "Landscape");
#endif
    }
}
