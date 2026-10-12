using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public sealed class TimeScaleController : MonoBehaviour
{
    [SerializeField, Min(0f)] private float slowTimeScale = 0.1f;
    [SerializeField, Min(0f)] private float normalTimeScale = 1f;
    [SerializeField, Min(0f), Tooltip("Real-time duration of a full transition between slow and normal speed.")]
    private float transitionDuration = 0.2f;

    private float previousTimeScale;

    private void OnEnable()
    {
        previousTimeScale = Time.timeScale;
        Time.timeScale = Mathf.Max(0f, slowTimeScale);
    }

    private void Update()
    {
        bool isHeld = Application.isFocused && Mouse.current != null && Mouse.current.leftButton.isPressed;
        float slowScale = Mathf.Max(0f, slowTimeScale);
        float normalScale = Mathf.Max(slowScale, normalTimeScale);
        float targetScale = isHeld ? normalScale : slowScale;

        if (transitionDuration <= 0f || slowScale == normalScale)
        {
            Time.timeScale = targetScale;
            return;
        }

        float changePerSecond = (normalScale - slowScale) / transitionDuration;
        Time.timeScale = Mathf.MoveTowards(Time.timeScale, targetScale, changePerSecond * Time.unscaledDeltaTime);
    }

    private void OnDisable()
    {
        Time.timeScale = previousTimeScale;
    }

    private void OnValidate()
    {
        slowTimeScale = Mathf.Max(0f, slowTimeScale);
        normalTimeScale = Mathf.Max(slowTimeScale, normalTimeScale);
        transitionDuration = Mathf.Max(0f, transitionDuration);
    }
}
