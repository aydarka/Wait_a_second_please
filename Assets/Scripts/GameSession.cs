using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Run state: counts distance and dodged shots, ends the run on a hit and restarts the scene.
[DisallowMultipleComponent]
public sealed class GameSession : MonoBehaviour
{
    [SerializeField] private Transform player;
    [SerializeField, Tooltip("Tilt controls need a fixed orientation: auto-rotation would turn the screen mid-dodge.")]
    private ScreenOrientation orientation = ScreenOrientation.LandscapeLeft;
    [SerializeField] private string title = "WAIT A SECOND, PLEASE";
    [SerializeField] private string deathMessage = "TOO LATE";
    [SerializeField, Min(0f), Tooltip("Real-time seconds the title stays on screen.")]
    private float titleDuration = 3f;
    [SerializeField, Min(0f), Tooltip("Misses closer than this, in metres, flash a near-miss message.")]
    private float nearMissDistance = 0.9f;
    [SerializeField, Min(0f), Tooltip("Time scale while the death shot plays out.")]
    private float deathTimeScale = 0.05f;
    [SerializeField, Min(0f), Tooltip("Real-time seconds before a tap restarts the run.")]
    private float restartDelay = 1.2f;
    [SerializeField, Min(0f), Tooltip("Real-time seconds before the run restarts on its own.")]
    private float autoRestartDelay = 4f;
    [SerializeField, Tooltip("Tip radius of the hole onto the street as the hit screen floods in and once it has closed, in screen heights.")]
    private Vector2 hitHoleRadius = new Vector2(0.75f, 0.17f);
    [SerializeField, Min(0.01f), Tooltip("Real-time seconds the hole in the hit screen takes to close in.")]
    private float hitHoleCloseDuration = 0.45f;
    [SerializeField] private Color red = new Color(0.85f, 0.04f, 0.04f);
    [SerializeField] private Color yellow = new Color(1f, 0.86f, 0.1f);

    private static GameSession instance;

    private float startZ;
    private float startTime;
    private float endTime;
    private float nearMissTime = float.NegativeInfinity;
    private int dodged;
    private bool isOver;
    private Text titleText;
    private Text scoreText;
    private Text messageText;
    private JaggedHole hitScreen;

    public static void ReportPlayerHit(Vector3 point)
    {
        if (instance != null && !instance.isOver) instance.EndRun();
    }

    public static void ReportDodge(float missDistance)
    {
        if (instance == null || instance.isOver) return;
        instance.dodged++;
        if (missDistance <= instance.nearMissDistance)
            instance.nearMissTime = Time.unscaledTime;
    }

    private void Awake()
    {
        if (player == null && Camera.main != null) player = Camera.main.transform;
        if (Application.isMobilePlatform) Screen.orientation = orientation;
        BuildHud();
    }

    private void OnEnable()
    {
        if (instance != null && instance != this)
            Debug.LogWarning("Only one GameSession should be active.", this);
        instance = this;
        startZ = player != null ? player.position.z : 0f;
        startTime = Time.unscaledTime;
    }

    private void OnDisable()
    {
        if (instance == this) instance = null;
    }

    private void Update()
    {
        float now = Time.unscaledTime;
        float distance = player != null ? Mathf.Max(0f, player.position.z - startZ) : 0f;
        scoreText.text = $"{distance:0} m   ·   {dodged} dodged";

        float titleAge = now - startTime;
        titleText.color = WithAlpha(Color.white, isOver ? 0f : 1f - Mathf.InverseLerp(titleDuration * 0.6f, titleDuration, titleAge));

        if (!isOver)
        {
            float nearMissAge = now - nearMissTime;
            messageText.text = "CLOSE ONE";
            messageText.color = WithAlpha(yellow, 1f - Mathf.Clamp01(nearMissAge / 0.8f));
            return;
        }

        float age = now - endTime;
        // Pistol Whip-style hit: flat red floods the screen and a jagged hole onto the street closes in.
        float closing = 1f - Mathf.Pow(1f - Mathf.Clamp01(age / hitHoleCloseDuration), 3f);
        hitScreen.HoleRadius = Mathf.Lerp(hitHoleRadius.x, hitHoleRadius.y, closing);
        messageText.text = age >= restartDelay ? $"{deathMessage}\n<size=40>tap to try again</size>" : deathMessage;
        messageText.color = Color.white;

        bool tapped = Pointer.current != null && Pointer.current.press.wasPressedThisFrame;
        if ((age >= restartDelay && tapped) || age >= autoRestartDelay)
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    private void EndRun()
    {
        isOver = true;
        endTime = Time.unscaledTime;
        hitScreen.HoleRadius = hitHoleRadius.x;
        hitScreen.enabled = true;
        // The killing bullet hangs in the middle of the hole, so the message moves below it.
        Stretch(messageText.rectTransform, new Vector2(0.05f, 0.04f), new Vector2(0.95f, 0.3f));
        // The time controller would answer the player's flinch with normal speed; the death shot stays in slow motion.
        foreach (var controller in FindObjectsByType<TimeScaleController>(FindObjectsSortMode.None))
            controller.enabled = false;
        foreach (var dodge in FindObjectsByType<PlayerTiltDodge>(FindObjectsSortMode.None))
            dodge.enabled = false;
        Time.timeScale = deathTimeScale;
    }

    private void OnDestroy()
    {
        // Scene reloads must not inherit the death slow motion if no controller takes over.
        if (isOver) Time.timeScale = 1f;
    }

    private void BuildHud()
    {
        var canvasObject = new GameObject("HUD", typeof(Canvas), typeof(CanvasScaler));
        canvasObject.transform.SetParent(transform, false);
        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        hitScreen = new GameObject("Hit Screen", typeof(RectTransform), typeof(JaggedHole)).GetComponent<JaggedHole>();
        hitScreen.transform.SetParent(canvasObject.transform, false);
        Stretch(hitScreen.rectTransform, Vector2.zero, Vector2.one);
        hitScreen.color = red;
        hitScreen.raycastTarget = false;
        hitScreen.enabled = false;

        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        titleText = CreateText(canvasObject.transform, font, "Title", title, 96, FontStyle.Bold,
            new Vector2(0.05f, 0.55f), new Vector2(0.95f, 0.8f), TextAnchor.MiddleCenter);
        scoreText = CreateText(canvasObject.transform, font, "Score", string.Empty, 44, FontStyle.Bold,
            new Vector2(0.03f, 0.88f), new Vector2(0.97f, 0.98f), TextAnchor.UpperLeft);
        messageText = CreateText(canvasObject.transform, font, "Message", string.Empty, 120, FontStyle.Bold,
            new Vector2(0.05f, 0.3f), new Vector2(0.95f, 0.7f), TextAnchor.MiddleCenter);
        messageText.color = WithAlpha(yellow, 0f);
    }

    private static Text CreateText(Transform parent, Font font, string objectName, string content, int size,
        FontStyle style, Vector2 anchorMin, Vector2 anchorMax, TextAnchor alignment)
    {
        var text = new GameObject(objectName, typeof(RectTransform), typeof(Text)).GetComponent<Text>();
        text.transform.SetParent(parent, false);
        Stretch(text.rectTransform, anchorMin, anchorMax);
        text.font = font;
        text.text = content;
        text.fontSize = size;
        text.fontStyle = style;
        text.alignment = alignment;
        text.supportRichText = true;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        var outline = text.gameObject.AddComponent<Outline>();
        outline.effectColor = Color.black;
        outline.effectDistance = new Vector2(3f, -3f);
        return text;
    }

    private static void Stretch(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static Color WithAlpha(Color color, float alpha)
    {
        color.a = Mathf.Clamp01(alpha);
        return color;
    }
}
