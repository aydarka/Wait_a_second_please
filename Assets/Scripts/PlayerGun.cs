using System.Collections.Generic;
using UnityEngine;

// The player's pistol, seen at the lower right only around a counter-shot. On a close dodge it is drawn up
// from below the frame on real time, quick but visible even while time stands almost still; it kicks when
// the shot leaves and drops back out of frame once every shot it fired has landed.
// There is no hand, so the gun is placed by the top of its grip, kept just below the bottom edge of the
// screen: the grip stays out of frame and only the slide and barrel come into view, as if held from below.
[DisallowMultipleComponent]
public sealed class PlayerGun : MonoBehaviour
{
    [SerializeField, Tooltip("Barrel tip, a direct child of this object; the counter-shot leaves from here.")]
    private Transform muzzle;
    [SerializeField, Tooltip("Top of the grip under the slide, a direct child of this object; the gun is placed and kicks about this point.")]
    private Transform grip;
    [SerializeField, Tooltip("Where the top of the grip sits on screen once the gun is up (0 to 1 across and up; below 0 is under the edge).")]
    private Vector2 gripOnScreen = new Vector2(0.8f, -0.03f);
    [SerializeField, Min(0.05f), Tooltip("Grip distance in front of the eyes once the gun is up, metres; keep it past the camera's near plane.")]
    private float gripDepth = 0.42f;
    [SerializeField, Tooltip("Barrel direction once up, as yaw to the right and pitch up from straight ahead, degrees. Turned a little, the gun shows its side instead of its back.")]
    private Vector2 presentAngles = new Vector2(10f, 3f);
    [SerializeField, Range(0f, 1f), Tooltip("How far the barrel turns from that direction towards the enemy.")]
    private float aimWeight = 1f;
    [SerializeField, Tooltip("Cant of the gun about its barrel, degrees.")]
    private float cant = -12f;
    [SerializeField, Tooltip("Grip start of the draw relative to the raised gun, along the camera's right, up and forward, metres.")]
    private Vector3 drawFrom = new Vector3(0.05f, -0.3f, 0f);
    [SerializeField, Tooltip("Barrel tilt at the start of the draw, degrees down.")]
    private float drawTilt = 45f;
    [SerializeField, Min(0.01f), Tooltip("Real-time seconds to bring the gun up.")]
    private float drawSeconds = 0.22f;
    [SerializeField, Min(0.01f), Tooltip("Real-time seconds to drop it out of frame.")]
    private float holsterSeconds = 0.3f;
    [SerializeField, Tooltip("Muzzle climb when the shot leaves, degrees.")]
    private float kickAngle = 14f;
    [SerializeField, Min(0.01f), Tooltip("Real-time seconds the kick takes to settle.")]
    private float kickSeconds = 0.15f;

    private static PlayerGun instance;

    private readonly HashSet<object> holders = new HashSet<object>();
    private Renderer[] parts;
    private Vector3 gripOffset;
    private float raised;
    private float kickStart = float.NegativeInfinity;
    private Vector3 aimPoint;
    private bool hasAim;

    public bool IsUp => raised >= 1f;
    // The gun's meshes, so the shoot-back moment can lift them out of the dark.
    public Renderer[] Parts => parts;
    public Vector3 MuzzlePosition => muzzle != null ? muzzle.position : transform.position;

    // Starts drawing the gun at a point to aim at; it stays up while any holder keeps it.
    public static PlayerGun Draw(object holder, Vector3 aimAt)
    {
        if (instance == null)
        {
            var prefab = Resources.Load<PlayerGun>("PlayerGun");
            if (prefab == null)
            {
                Debug.LogError("PlayerGun needs its prefab in a Resources folder.");
                return null;
            }
            instance = Instantiate(prefab);
        }
        instance.holders.Add(holder);
        instance.Aim(aimAt);
        return instance;
    }

    public static void Holster(object holder)
    {
        if (instance != null) instance.holders.Remove(holder);
    }

    public void Aim(Vector3 point)
    {
        aimPoint = point;
        hasAim = true;
    }

    public void Kick() => kickStart = Time.unscaledTime;

    private void Awake()
    {
        parts = GetComponentsInChildren<Renderer>();
        gripOffset = grip != null ? grip.localPosition : Vector3.zero;
        SetVisible(false);
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    private void LateUpdate()
    {
        var view = Camera.main;
        if (view == null) return;
        float target = holders.Count > 0 ? 1f : 0f;
        float seconds = target > raised ? drawSeconds : holsterSeconds;
        raised = Mathf.MoveTowards(raised, target, Time.unscaledDeltaTime / seconds);
        SetVisible(raised > 0f);
        if (raised <= 0f) return;

        Transform eyes = view.transform;
        Vector3 gripUp = view.ViewportToWorldPoint(new Vector3(gripOnScreen.x, gripOnScreen.y, gripDepth));
        Vector3 present = Quaternion.AngleAxis(presentAngles.x, eyes.up) *
            (Quaternion.AngleAxis(-presentAngles.y, eyes.right) * eyes.forward);
        Vector3 toAim = aimPoint - gripUp;
        Vector3 aim = hasAim && toAim.sqrMagnitude > 0.01f ? Vector3.Slerp(present, toAim.normalized, aimWeight) : present;
        Quaternion upRotation = Quaternion.LookRotation(aim, eyes.up) * Quaternion.AngleAxis(cant, Vector3.forward);
        Quaternion downRotation = Quaternion.AngleAxis(drawTilt, eyes.right) * upRotation;

        // Out from below fast, settling on the aim.
        float t = 1f - Mathf.Pow(1f - raised, 3f);
        Vector3 gripPosition = Vector3.Lerp(gripUp + eyes.TransformVector(drawFrom), gripUp, t);
        Quaternion rotation = Quaternion.Slerp(downRotation, upRotation, t);

        // The kick turns the gun about the grip, so the muzzle climbs.
        float kick = Mathf.Clamp01(1f - (Time.unscaledTime - kickStart) / kickSeconds);
        if (kick > 0f) rotation = Quaternion.AngleAxis(-kickAngle * kick * kick, rotation * Vector3.right) * rotation;
        transform.SetPositionAndRotation(gripPosition - rotation * gripOffset, rotation);
    }

    private void SetVisible(bool visible)
    {
        foreach (var part in parts)
            if (part.enabled != visible) part.enabled = visible;
    }
}
