using UnityEngine;

// Leans the player sideways and up/down with the device tilt so incoming shots can be dodged.
// Works in real time: the faster the lean, the faster TimeScaleController lets time run.
[DisallowMultipleComponent]
public sealed class PlayerTiltDodge : MonoBehaviour
{
    [SerializeField, Min(0.01f), Tooltip("Roll in degrees that reaches the full sideways offset.")]
    private float maxRollAngle = 25f;
    [SerializeField, Min(0.01f), Tooltip("Pitch in degrees that reaches the full vertical offset.")]
    private float maxPitchAngle = 20f;
    [SerializeField, Min(0f)] private float maxSideOffset = 1.4f;
    [SerializeField, Min(0f)] private float maxDuckOffset = 0.9f;
    [SerializeField, Min(0f)] private float maxRiseOffset = 0.3f;
    [SerializeField, Tooltip("Tilting the top edge away raises the player instead of ducking.")]
    private bool invertVertical;
    [SerializeField, Range(0f, 1f), Tooltip("Share of the device roll copied to the camera.")]
    private float cameraRollFactor = 0.4f;
    [SerializeField, Min(0f), Tooltip("Real-time smoothing rate; higher follows the tilt more tightly.")]
    private float responsiveness = 18f;

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
        float side = Mathf.Clamp(tilt.x / Mathf.Max(0.01f, maxRollAngle), -1f, 1f);
        float vertical = Mathf.Clamp(tilt.y / Mathf.Max(0.01f, maxPitchAngle), -1f, 1f);
        if (invertVertical) vertical = -vertical;
        Vector2 target = new Vector2(
            side * maxSideOffset,
            vertical >= 0f ? vertical * maxRiseOffset : vertical * maxDuckOffset);

        float blend = responsiveness > 0f ? 1f - Mathf.Exp(-responsiveness * Time.unscaledDeltaTime) : 1f;
        offset = Vector2.Lerp(offset, target, blend);
        roll = Mathf.Lerp(roll, -Mathf.Clamp(tilt.x, -maxRollAngle, maxRollAngle) * cameraRollFactor, blend);

        // Only X and Y belong to this component; forward motion along Z is left to CameraMovement.
        Vector3 position = transform.position;
        position.x = basePosition.x + offset.x;
        position.y = basePosition.y + offset.y;
        transform.SetPositionAndRotation(position, baseRotation * Quaternion.Euler(0f, 0f, roll));
    }
}
