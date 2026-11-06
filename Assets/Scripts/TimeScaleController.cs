using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// Time barely moves while the device is still, follows how fast it is tilted, and runs normally while the screen is held
// or while game code holds normal time (the player's counter-shot in flight, as in Superhot).
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

    private static readonly HashSet<object> normalTimeHolders = new HashSet<object>();
    private static readonly Predicate<object> IsDestroyed = holder => holder is UnityEngine.Object unityObject && unityObject == null;
    private static float flowSlowScale;
    private static float flowNormalScale = 1f;

    private float previousTimeScale;
    private float flow;
    private bool pressOnPanel;

    // 0 at the slow time scale, 1 at the normal one; drives slow-motion visuals.
    // Derived from the actual time scale so scripted slow motion (the death shot) reads as slow too.
    public static float Flow => Mathf.InverseLerp(flowSlowScale, flowNormalScale, Time.timeScale);

    // Runs time at normal speed, like holding the screen, until the same requester releases it.
    // Holding twice is harmless; a destroyed Unity object releases itself; a scene reload clears every hold.
    public static void HoldNormalTime(object requester)
    {
        if (requester != null) normalTimeHolders.Add(requester);
    }

    // Lets time settle back to the tilt-driven speed (over slowDownDuration) once no one else holds it. Safe without a hold.
    public static void ReleaseNormalTime(object requester)
    {
        if (requester != null) normalTimeHolders.Remove(requester);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        normalTimeHolders.Clear();
    }

    private void OnEnable()
    {
        previousTimeScale = Time.timeScale;
        Time.timeScale = Mathf.Max(0f, slowTimeScale);
        flow = 0f;
        // A new controller means a new run: holds left over from the previous scene no longer apply.
        normalTimeHolders.Clear();
        TiltInput.Recalibrate();
    }

    private void Update()
    {
        float slowScale = Mathf.Max(0f, slowTimeScale);
        float normalScale = Mathf.Max(slowScale, normalTimeScale);
        var pointer = Pointer.current;
        bool isHeld = false;
        if (Application.isFocused && pointer != null)
        {
            // A press on the controls menu is a menu tap, not a request for normal time.
            if (pointer.press.wasPressedThisFrame) pressOnPanel = ControlsPanel.Covers(pointer.position.ReadValue());
            isHeld = pointer.press.isPressed && !pressOnPanel;
        }
        // With the vertical dodge off, tilting the top edge does nothing, so it must not run time either.
        float tiltSpeed = ControlOptions.VerticalDodge ? TiltInput.AngularSpeed : TiltInput.RollSpeed;

        if (normalTimeHolders.Count > 0) normalTimeHolders.RemoveWhere(IsDestroyed);
        isHeld |= normalTimeHolders.Count > 0;

        float targetFlow = isHeld ? 1f : Mathf.Pow(
            Mathf.InverseLerp(tiltDeadZone, Mathf.Max(tiltDeadZone + 0.01f, fullSpeedTiltRate), tiltSpeed),
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
