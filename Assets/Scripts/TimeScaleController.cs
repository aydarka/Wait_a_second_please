using UnityEngine;
using UnityEngine.InputSystem;

// Time barely moves while the device is still, follows how fast it is tilted, and runs normally while the screen is held.
[DisallowMultipleComponent]
public sealed class TimeScaleController : MonoBehaviour
{
    [SerializeField, Min(0f)] private float slowTimeScale = 0.03f;
    [SerializeField, Min(0f)] private float normalTimeScale = 1f;
    [SerializeField, Min(0f), Tooltip("Tilt speed in degrees per second that is ignored as hand tremor.")]
    private float tiltDeadZone = 6f;
    [SerializeField, Min(0.01f), Tooltip("Tilt speed in degrees per second that reaches normal time.")]
    private float fullSpeedTiltRate = 120f;
    [SerializeField, Min(0.01f), Tooltip("Shapes the tilt response: above 1 keeps slow tilts slower.")]
    private float tiltResponseExponent = 1.5f;
    [SerializeField, Min(0f), Tooltip("Real-time duration of a full transition from slow to normal speed.")]
    private float transitionDuration = 0.08f;
    [SerializeField, Min(0f), Tooltip("Real-time duration of a full transition from normal back to slow speed.")]
    private float slowDownDuration = 0.35f;

    private static float flowSlowScale;
    private static float flowNormalScale = 1f;

    private float previousTimeScale;
    private float flow;

    // 0 at the slow time scale, 1 at the normal one; drives slow-motion visuals.
    // Derived from the actual time scale so scripted slow motion (the death shot) reads as slow too.
    public static float Flow => Mathf.InverseLerp(flowSlowScale, flowNormalScale, Time.timeScale);

    private void OnEnable()
    {
        previousTimeScale = Time.timeScale;
        Time.timeScale = Mathf.Max(0f, slowTimeScale);
        flow = 0f;
        TiltInput.Recalibrate();
    }

    private void Update()
    {
        float slowScale = Mathf.Max(0f, slowTimeScale);
        float normalScale = Mathf.Max(slowScale, normalTimeScale);
        bool isHeld = Application.isFocused && Pointer.current != null && Pointer.current.press.isPressed;

        float targetFlow = isHeld ? 1f : Mathf.Pow(
            Mathf.InverseLerp(tiltDeadZone, Mathf.Max(tiltDeadZone + 0.01f, fullSpeedTiltRate), TiltInput.AngularSpeed),
            Mathf.Max(0.01f, tiltResponseExponent));

        // Speed up almost instantly so a sharp tilt is felt, but let time settle back gradually.
        float duration = targetFlow > flow ? transitionDuration : slowDownDuration;
        flow = duration > 0f
            ? Mathf.MoveTowards(flow, targetFlow, Time.unscaledDeltaTime / duration)
            : targetFlow;
        Time.timeScale = Mathf.Lerp(slowScale, normalScale, flow);
        flowSlowScale = slowScale;
        flowNormalScale = normalScale;
    }

    private void OnDisable()
    {
        Time.timeScale = previousTimeScale;
    }

    private void OnValidate()
    {
        slowTimeScale = Mathf.Max(0f, slowTimeScale);
        normalTimeScale = Mathf.Max(slowTimeScale, normalTimeScale);
        tiltDeadZone = Mathf.Max(0f, tiltDeadZone);
        fullSpeedTiltRate = Mathf.Max(tiltDeadZone + 0.01f, fullSpeedTiltRate);
        transitionDuration = Mathf.Max(0f, transitionDuration);
        slowDownDuration = Mathf.Max(0f, slowDownDuration);
    }
}
