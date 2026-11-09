using UnityEngine;

// Leans the player sideways and up/down with the device tilt so incoming shots can be dodged.
// The tilt sets where the player goes; how fast they get there thickens with slow motion (Dodge Time Share), so a dodge
// in bullet time is a heavy, deliberate move. At the default the body is as slow as the bullets: slow motion gives time
// to see and decide, not a faster body.
[DisallowMultipleComponent]
public sealed class PlayerTiltDodge : MonoBehaviour
{
    [SerializeField, Min(0.01f), Tooltip("Roll from the horizon in degrees that reaches the full sideways offset.")]
    private float maxRollAngle = 25f;
    [SerializeField, Min(0f), Tooltip("Roll from the horizon in degrees that is still straight, so a slightly crooked grip does not move the player.")]
    private float rollDeadZone = 2f;
    [SerializeField, Min(0.01f), Tooltip("Pitch in degrees that reaches the full vertical offset.")]
    private float maxPitchAngle = 20f;
    [SerializeField, Min(0f)] private float maxSideOffset = 1.4f;
    [SerializeField, Min(0f)] private float maxDuckOffset = 0.9f;
    [SerializeField, Min(0f)] private float maxRiseOffset = 0.3f;
    [SerializeField, Tooltip("Tilting the top edge away raises the player instead of ducking.")]
    private bool invertVertical;
    [SerializeField, Range(0f, 1f), Tooltip("Share of the device roll copied to the camera.")]
    private float cameraRollFactor = 0.4f;
    [SerializeField, Min(0f), Tooltip("Smoothing rate at normal time, per real-time second; higher follows the tilt more tightly.")]
    private float responsiveness = 18f;
    [SerializeField, Range(0f, 1f), Tooltip("How much the dodge slows down with time. 0: the player moves in real time whatever the slow-down. 1: as slowed as the bullets, so slow motion no longer helps the dodge itself. In between: slower than in real time, yet quicker than the bullets.")]
    private float dodgeTimeShare = 1f;

    private Vector2 basePosition;
    private Quaternion baseRotation;
    private Vector2 offset;
    private float roll;

    // Current sideways/vertical offset from the run line, in metres.
    public Vector2 Offset => offset;

    private void Awake()
    {
        basePosition = new Vector2(transform.position.x, transform.position.y);
        baseRotation = transform.rotation;
    }

    private void LateUpdate()
    {
        Vector2 tilt = TiltInput.Angles;
        float rollRange = Mathf.Max(0.01f, maxRollAngle - rollDeadZone);
        float rollTilt = Mathf.Clamp(Mathf.Sign(tilt.x) * Mathf.Max(0f, Mathf.Abs(tilt.x) - rollDeadZone), -rollRange, rollRange);
        float side = rollTilt / rollRange;
        // ControlOptions.VerticalDodge switches ducking and rising off entirely.
        float vertical = ControlOptions.VerticalDodge ? Mathf.Clamp(tilt.y / Mathf.Max(0.01f, maxPitchAngle), -1f, 1f) : 0f;
        if (invertVertical) vertical = -vertical;
        Vector2 target = new Vector2(
            side * maxSideOffset,
            vertical >= 0f ? vertical * maxRiseOffset : vertical * maxDuckOffset);

        // In slow motion the same tilt still leads to the same pose, only the way there is thicker.
        float timeFactor = Mathf.Pow(Mathf.Clamp(Time.timeScale, 0.0001f, 1f), dodgeTimeShare);
        float blend = responsiveness > 0f ? 1f - Mathf.Exp(-responsiveness * timeFactor * Time.unscaledDeltaTime) : 1f;
        offset = Vector2.Lerp(offset, target, blend);
        roll = Mathf.Lerp(roll, -rollTilt * cameraRollFactor, blend);

        // Only X and Y belong to this component; forward motion along Z is left to CameraMovement.
        Vector3 position = transform.position;
        position.x = basePosition.x + offset.x;
        position.y = basePosition.y + offset.y;
        transform.SetPositionAndRotation(position, baseRotation * Quaternion.Euler(0f, 0f, roll));
    }
}
