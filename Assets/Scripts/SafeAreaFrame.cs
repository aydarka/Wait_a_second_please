using UnityEngine;

// Stretches its RectTransform over the screen's safe area, so captions inside it stay clear of the notch,
// the punch-hole camera, the rounded corners and the home indicator. Follows rotations and resolution changes.
[RequireComponent(typeof(RectTransform))]
[DisallowMultipleComponent]
public sealed class SafeAreaFrame : MonoBehaviour
{
    private Rect applied;
    private Vector2Int appliedScreen;

    // Counts every refit, so layouts that depend on the frame know when to redo themselves.
    public int Version { get; private set; }

    private void OnEnable() => Fit();

    private void Update()
    {
        if (Screen.safeArea != applied || Screen.width != appliedScreen.x || Screen.height != appliedScreen.y) Fit();
    }

    private void Fit()
    {
        applied = Screen.safeArea;
        appliedScreen = new Vector2Int(Screen.width, Screen.height);
        var size = new Vector2(Mathf.Max(1, Screen.width), Mathf.Max(1, Screen.height));
        var rect = (RectTransform)transform;
        rect.anchorMin = applied.min / size;
        rect.anchorMax = applied.max / size;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        Version++;
    }
}
