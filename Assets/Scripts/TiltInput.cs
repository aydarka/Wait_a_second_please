using UnityEngine;
using UnityEngine.InputSystem;

// Screen-relative device tilt shared by time control and dodging; sampled once per frame.
// Without a gravity sensor (Editor, desktop) the mouse position stands in for the tilt.
public static class TiltInput
{
    private const float MouseTiltRange = 30f;
    private const float AccelerometerSmoothing = 12f;
    private const float RateSmoothing = 20f;
    // Real-time seconds to wait after the screen turns before taking a new neutral pose.
    private const float OrientationSettleTime = 0.3f;

    private static int sampledFrame = -1;
    private static bool sensorsRequested;
    private static bool hasNeutral;
    private static bool hasPreviousDirection;
    private static Vector3 smoothedGravity;
    private static Vector3 previousDirection;
    private static float previousRoll;
    private static Vector2 neutralAngles;
    private static Vector2 angles;
    private static float angularSpeed;
    private static float rollSpeed;
    private static int screenShape;
    private static float settleUntil;

    // x: roll, positive when the right edge goes down; y: pitch, positive when the top edge comes towards the player.
    // Degrees from the pose captured by the first sample or by Recalibrate.
    public static Vector2 Angles
    {
        get { Sample(); return angles; }
    }

    // How fast the device is being tilted, in degrees per real-time second.
    public static float AngularSpeed
    {
        get { Sample(); return angularSpeed; }
    }

    // Like AngularSpeed, but counts only the sideways (roll) part of the tilt.
    public static float RollSpeed
    {
        get { Sample(); return rollSpeed; }
    }

    public static void Recalibrate()
    {
        hasNeutral = false;
        hasPreviousDirection = false;
        sampledFrame = -1;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        sensorsRequested = false;
        smoothedGravity = Vector3.zero;
        angularSpeed = 0f;
        rollSpeed = 0f;
        angles = Vector2.zero;
        screenShape = 0;
        settleUntil = 0f;
        Recalibrate();
    }

    private static void Sample()
    {
        if (sampledFrame == Time.frameCount) return;
        sampledFrame = Time.frameCount;
        float deltaTime = Time.unscaledDeltaTime;
        float rateBlend = 1f - Mathf.Exp(-RateSmoothing * deltaTime);

        // The sensor axes follow the screen, so after a turn between portrait and landscape the old neutral is meaningless.
        int shape = Screen.height > Screen.width ? 2 : 1;
        if (shape != screenShape)
        {
            if (screenShape != 0) settleUntil = Time.unscaledTime + OrientationSettleTime;
            screenShape = shape;
            hasNeutral = false;
            hasPreviousDirection = false;
        }

        // The sensor keeps being read while the screen settles, so its smoothing has caught up by the time a neutral is taken.
        bool fromSensor = TryReadGravity(deltaTime, out var gravity);
        Vector3 direction = gravity.normalized;
        bool hasInput = Time.unscaledTime >= settleUntil && (fromSensor || TryReadMouse(out direction));
        if (!hasInput)
        {
            // No usable input: hold the current pose, which reads as standing still.
            angularSpeed = Mathf.Lerp(angularSpeed, 0f, rateBlend);
            rollSpeed = Mathf.Lerp(rollSpeed, 0f, rateBlend);
            return;
        }

        Vector2 currentAngles = ToAngles(direction);
        // The cursor already has an absolute neutral (the screen centre); only a held device needs calibrating.
        if (!fromSensor)
        {
            neutralAngles = Vector2.zero;
            hasNeutral = true;
        }
        else if (!hasNeutral)
        {
            neutralAngles = currentAngles;
            hasNeutral = true;
        }
        angles = new Vector2(currentAngles.x - neutralAngles.x, Mathf.DeltaAngle(neutralAngles.y, currentAngles.y));

        bool hasRate = hasPreviousDirection && deltaTime > 0f;
        float rate = hasRate ? Vector3.Angle(previousDirection, direction) / deltaTime : 0f;
        float rollRate = hasRate ? Mathf.Abs(currentAngles.x - previousRoll) / deltaTime : 0f;
        angularSpeed = Mathf.Lerp(angularSpeed, rate, rateBlend);
        rollSpeed = Mathf.Lerp(rollSpeed, rollRate, rateBlend);
        previousDirection = direction;
        previousRoll = currentAngles.x;
        hasPreviousDirection = true;
    }

    private static bool TryReadGravity(float deltaTime, out Vector3 gravity)
    {
        if (!sensorsRequested)
        {
            sensorsRequested = true;
            if (GravitySensor.current != null) InputSystem.EnableDevice(GravitySensor.current);
            else if (Accelerometer.current != null) InputSystem.EnableDevice(Accelerometer.current);
        }

        if (GravitySensor.current != null && GravitySensor.current.enabled)
        {
            gravity = GravitySensor.current.gravity.ReadValue();
            if (gravity.sqrMagnitude > 0.01f) return true;
        }
        if (Accelerometer.current != null && Accelerometer.current.enabled)
        {
            // Raw acceleration includes hand shake; a low-pass filter leaves mostly gravity.
            Vector3 acceleration = Accelerometer.current.acceleration.ReadValue();
            smoothedGravity = smoothedGravity.sqrMagnitude > 0f
                ? Vector3.Lerp(smoothedGravity, acceleration, 1f - Mathf.Exp(-AccelerometerSmoothing * deltaTime))
                : acceleration;
            gravity = smoothedGravity;
            if (gravity.sqrMagnitude > 0.01f) return true;
        }
        gravity = Vector3.zero;
        return false;
    }

    private static bool TryReadMouse(out Vector3 direction)
    {
        direction = Vector3.down;
        if (Mouse.current == null || !Application.isFocused) return false;
        Vector2 position = Mouse.current.position.ReadValue();
        // Outside the game view the cursor belongs to other windows and must not jerk the player around;
        // (0, 0) is also what the mouse reports before the cursor has ever entered the view.
        if (position.x <= 0f || position.y <= 0f || position.x >= Screen.width || position.y >= Screen.height) return false;
        // Reaching for the controls menu must not tilt the player.
        if (ControlsPanel.Covers(position)) return false;

        // Map the cursor to a virtual gravity vector so both the angles and the rate behave like a sensor.
        float roll = (position.x / Mathf.Max(1f, Screen.width) * 2f - 1f) * MouseTiltRange;
        float pitch = (position.y / Mathf.Max(1f, Screen.height) * 2f - 1f) * MouseTiltRange;
        direction = Quaternion.Euler(pitch, 0f, roll) * Vector3.down;
        return true;
    }

    private static Vector2 ToAngles(Vector3 direction)
    {
        // Roll stays stable at any pitch because it only measures how far the screen's X axis leaves the horizon.
        float roll = Mathf.Asin(Mathf.Clamp(direction.x, -1f, 1f)) * Mathf.Rad2Deg;
        float pitch = Mathf.Atan2(-direction.z, -direction.y) * Mathf.Rad2Deg;
        return new Vector2(roll, pitch);
    }
}
