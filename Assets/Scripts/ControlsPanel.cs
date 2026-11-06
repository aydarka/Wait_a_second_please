using UnityEngine;
using UnityEngine.InputSystem;

// Small in-game menu in the top-right corner that switches ControlOptions, plus the camera fit for portrait screens.
// Creates itself on launch; nothing needs adding to a scene.
[DisallowMultipleComponent]
public sealed class ControlsPanel : MonoBehaviour
{
    // Sizes are in points of a 540-pixel short screen side and scale with the screen.
    private const float ReferenceShortSide = 540f;
    private const float Margin = 12f;
    private const float ButtonWidth = 200f;
    private const float ButtonHeight = 38f;
    private const float Gap = 6f;
    private const float Outline = 1.5f;
    private const int FontSize = 15;

    private static ControlsPanel instance;

    private bool open;
    private Rect menuRect;
    private Rect verticalRect;
    private Rect orientationRect;
    private GUIStyle style;
    private Texture2D black;
    private Camera fittedCamera;
    private float baseFieldOfView;

    // Whether a pointer at this screen position (Input System coordinates) is on the menu,
    // so the press must not run time and the cursor must not tilt the player.
    public static bool Covers(Vector2 pointerPosition)
    {
        if (instance == null || !instance.isActiveAndEnabled) return false;
        return instance.HitTest(pointerPosition) != Hit.None;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Create()
    {
        if (instance != null) return;
        var host = new GameObject("Controls Panel");
        DontDestroyOnLoad(host);
        instance = host.AddComponent<ControlsPanel>();
    }

    private enum Hit { None, Menu, VerticalDodge, Orientation }

    private void Update()
    {
        var pointer = Pointer.current;
        if (!Application.isFocused || pointer == null || !pointer.press.wasPressedThisFrame) return;
        switch (HitTest(pointer.position.ReadValue()))
        {
            case Hit.Menu: open = !open; break;
            case Hit.VerticalDodge: ControlOptions.VerticalDodge = !ControlOptions.VerticalDodge; break;
            case Hit.Orientation: ControlOptions.Portrait = !ControlOptions.Portrait; break;
        }
    }

    private void LateUpdate()
    {
        // Cameras keep their vertical field of view, so a portrait screen would show only a narrow slice of the street.
        Camera view = Camera.main;
        if (view == null) return;
        if (view != fittedCamera)
        {
            fittedCamera = view;
            baseFieldOfView = view.fieldOfView;
        }
        float fieldOfView = baseFieldOfView;
        if (view.aspect < 1f)
            fieldOfView = Mathf.Max(fieldOfView,
                Camera.HorizontalToVerticalFieldOfView(ControlOptions.PortraitHorizontalFieldOfView, view.aspect));
        if (!Mathf.Approximately(view.fieldOfView, fieldOfView)) view.fieldOfView = fieldOfView;
    }

    private void OnGUI()
    {
        if (Event.current.type != EventType.Repaint) return;
        Layout();
        DrawButton(menuRect, "CONTROLS");
        if (!open) return;
        DrawButton(verticalRect, "DUCK / RISE: " + (ControlOptions.VerticalDodge ? "ON" : "OFF"));
        DrawButton(orientationRect, "SCREEN: " + (ControlOptions.Portrait ? "PORTRAIT" : "LANDSCAPE"));
    }

    private void OnDestroy()
    {
        if (black != null) Destroy(black);
    }

    private Hit HitTest(Vector2 pointerPosition)
    {
        Layout();
        // Input System positions start at the bottom-left corner, IMGUI rects at the top-left.
        var point = new Vector2(pointerPosition.x, Screen.height - pointerPosition.y);
        if (menuRect.Contains(point)) return Hit.Menu;
        if (!open) return Hit.None;
        if (verticalRect.Contains(point)) return Hit.VerticalDodge;
        if (orientationRect.Contains(point)) return Hit.Orientation;
        return Hit.None;
    }

    private void Layout()
    {
        float scale = Mathf.Min(Screen.width, Screen.height) / ReferenceShortSide;
        Rect safe = Screen.safeArea;
        float right = safe.xMax - Margin * scale;
        float top = Screen.height - safe.yMax + Margin * scale;
        float width = ButtonWidth * scale;
        float height = ButtonHeight * scale;
        float step = height + Gap * scale;
        menuRect = new Rect(right - width, top, width, height);
        verticalRect = new Rect(right - width, top + step, width, height);
        orientationRect = new Rect(right - width, top + step * 2f, width, height);
    }

    private void DrawButton(Rect rect, string label)
    {
        float scale = Mathf.Min(Screen.width, Screen.height) / ReferenceShortSide;
        if (style == null)
        {
            black = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            black.SetPixel(0, 0, Color.black);
            black.Apply();
            style = new GUIStyle
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white },
            };
        }
        style.fontSize = Mathf.RoundToInt(FontSize * scale);

        float outline = Mathf.Max(1f, Outline * scale);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(rect.x + outline, rect.y + outline, rect.width - outline * 2f, rect.height - outline * 2f), black);
        GUI.Label(rect, label, style);
    }
}
