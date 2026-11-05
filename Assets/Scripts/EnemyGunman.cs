using System;
using UnityEngine;

// Gangster behaviour in game time, posed like comic panels: waits with the barrel up over the shoulder,
// turns and levels the gun at the player as the warning, fires a short burst with recoil, lowers it again.
// Poses are bone rotations derived from the prefab's aiming pose; there are no animation clips.
[DisallowMultipleComponent]
public sealed class EnemyGunman : MonoBehaviour
{
    // An enemy starts levelling its gun; the float is the warning time before the first bullet, in game seconds.
    public static event Action<EnemyGunman, float> AimStarted;
    // A bullet leaves the muzzle.
    public static event Action<EnemyGunman> ShotFired;

    private enum State { Waiting, Aiming, Firing, Cooldown }

    [Header("Rig")]
    [SerializeField] private ProjectileSpawner gun;
    [SerializeField, Tooltip("Upper body bone that turns to aim and kicks with recoil.")] private Transform spine;
    [SerializeField, Tooltip("Hand bone holding the gun.")] private Transform gunHand;
    [SerializeField] private Transform supportUpperArm;
    [SerializeField] private Transform supportForearm;
    [SerializeField, Tooltip("Shown under enemies the temporary layout puts on a ledge.")]
    private GameObject temporaryLedge;

    [Header("Waiting pose")]
    [SerializeField, Tooltip("Barrel direction while waiting, in the enemy's space: x to the gun side, y up, z towards the player.")]
    private Vector3 idleBarrelDirection = new Vector3(0.35f, 1f, 0.1f);
    [SerializeField, Tooltip("Free arm direction while waiting, in the enemy's space.")]
    private Vector3 idleSupportArmDirection = new Vector3(-0.2f, -1f, 0.1f);

    [Header("Engagement")]
    [SerializeField, Min(0f), Tooltip("Starts aiming once the player is this close, metres.")]
    private float engageDistance = 38f;
    [SerializeField, Min(0f), Tooltip("Never starts aiming closer than this, metres.")]
    private float minFireDistance = 15f;
    [SerializeField, Range(0f, 0.5f), Tooltip("Margin inside the screen edges the muzzle must be in to start aiming.")]
    private float viewMargin = 0.03f;
    [SerializeField, Min(1)] private int maxSimultaneousShooters = 2;

    [Header("Rhythm, game seconds")]
    [SerializeField, Min(0.01f)] private float raiseDuration = 0.25f;
    [SerializeField, Min(0f), Tooltip("Warning from the start of the raise to the first bullet.")]
    private float aimDuration = 0.5f;
    [SerializeField, Min(1), Tooltip("Bullets after each warning. Single shots for now; bursts may come later.")]
    private int burstSize = 1;
    [SerializeField, Min(0f)] private float burstInterval = 0.12f;
    [SerializeField, Min(0f), Tooltip("Random aim offset of the follow-up bullets in a burst, metres.")]
    private float burstSpread = 0.12f;
    [SerializeField, Min(0), Tooltip("Warnings per enemy; each ends in burstSize bullets.")]
    private int maxBursts = 3;
    [SerializeField, Min(0f)] private float cooldown = 1f;
    [SerializeField, Min(0.01f)] private float lowerDuration = 0.4f;
    [SerializeField, Min(0f), Tooltip("Degrees per game second.")] private float turnSpeed = 240f;

    [Header("Recoil")]
    [SerializeField] private float recoilAngle = 9f;
    [SerializeField, Min(0.01f)] private float recoilRecovery = 0.1f;

    private static int engagedShooters;

    private Transform target;
    private Transform muzzle;
    private Transform gunBody;
    private Quaternion aimHandRotation;
    private Quaternion aimUpperArmRotation;
    private Quaternion aimForearmRotation;
    private Quaternion idleHandRotation;
    private Quaternion idleUpperArmRotation;
    private Quaternion idleForearmRotation;
    private Quaternion spineRotation;
    private bool configurationValid;

    private State state;
    private float stateTimer;
    private float aimWeight;
    private float recoil;
    private int shotsLeft;
    private int burstsFired;
    private bool holdsShooterSlot;

    public Transform Muzzle => muzzle;
    public bool IsAiming => state == State.Aiming;
    // 0 when the warning starts, 1 when the first bullet leaves.
    public float AimProgress => state == State.Aiming && aimDuration > 0f ? 1f - stateTimer / aimDuration
        : state == State.Firing ? 1f : 0f;

    public void ShowTemporaryLedge(bool show)
    {
        if (temporaryLedge != null) temporaryLedge.SetActive(show);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetShooters() => engagedShooters = 0;

    private void Awake()
    {
        muzzle = gun != null ? gun.ShootPoint : null;
        gunBody = muzzle != null ? muzzle.parent : null;
        configurationValid = gun != null && spine != null && gunHand != null && supportUpperArm != null &&
            supportForearm != null && muzzle != null && gunBody != null && supportForearm.childCount > 0;
        if (!configurationValid)
        {
            Debug.LogError("EnemyGunman requires a gun with a muzzle, the spine, gun hand and free arm bones.", this);
            return;
        }
        CapturePoses();
    }

    // The prefab is posed aiming; the waiting pose turns the gun and the free arm away from it.
    private void CapturePoses()
    {
        spineRotation = spine.localRotation;
        aimHandRotation = gunHand.localRotation;
        aimUpperArmRotation = supportUpperArm.localRotation;
        aimForearmRotation = supportForearm.localRotation;

        Vector3 barrel = muzzle.position - gunBody.position;
        Vector3 idleBarrel = transform.TransformDirection(idleBarrelDirection.normalized);
        Quaternion handWorld = Quaternion.FromToRotation(barrel, idleBarrel) * gunHand.rotation;
        idleHandRotation = Quaternion.Inverse(gunHand.parent.rotation) * handWorld;

        Vector3 hangDirection = transform.TransformDirection(idleSupportArmDirection.normalized);
        Vector3 shoulder = supportUpperArm.position;
        Vector3 elbow = supportForearm.position;
        Vector3 wrist = supportForearm.GetChild(0).position;
        Quaternion upperTurn = Quaternion.FromToRotation(elbow - shoulder, hangDirection);
        Quaternion upperWorld = upperTurn * supportUpperArm.rotation;
        Quaternion forearmTurn = Quaternion.FromToRotation(upperTurn * (wrist - elbow), hangDirection);
        Quaternion forearmWorld = forearmTurn * upperTurn * supportForearm.rotation;
        idleUpperArmRotation = Quaternion.Inverse(supportUpperArm.parent.rotation) * upperWorld;
        idleForearmRotation = Quaternion.Inverse(upperWorld) * forearmWorld;
    }

    private void OnEnable()
    {
        state = State.Waiting;
        stateTimer = 0f;
        aimWeight = 0f;
        recoil = 0f;
        shotsLeft = 0;
        burstsFired = 0;
        var mainCamera = Camera.main;
        target = mainCamera != null ? mainCamera.transform : null;
        if (target != null) transform.rotation = FacingTarget();
        ApplyPose();
    }

    private void OnDisable()
    {
        ReleaseShooterSlot();
    }

    private void LateUpdate()
    {
        if (!configurationValid || target == null) return;
        float deltaTime = Time.deltaTime;

        transform.rotation = Quaternion.RotateTowards(transform.rotation, FacingTarget(), turnSpeed * deltaTime);
        UpdateState(deltaTime);

        bool raised = state == State.Aiming || state == State.Firing;
        aimWeight = Mathf.MoveTowards(aimWeight, raised ? 1f : 0f,
            deltaTime / (raised ? raiseDuration : lowerDuration));
        recoil = Mathf.MoveTowards(recoil, 0f, deltaTime / recoilRecovery);
        ApplyPose();

        if (state == State.Firing) FireDueShots();
    }

    private void UpdateState(float deltaTime)
    {
        switch (state)
        {
            case State.Waiting:
                if (burstsFired < maxBursts && CanEngage() && TakeShooterSlot())
                {
                    state = State.Aiming;
                    stateTimer = aimDuration;
                    AimStarted?.Invoke(this, aimDuration);
                }
                break;
            case State.Aiming:
                stateTimer -= deltaTime;
                // The overshoot stays in the timer, so the burst keeps its rhythm on long frames.
                if (stateTimer <= 0f)
                {
                    state = State.Firing;
                    shotsLeft = Mathf.Max(1, burstSize);
                }
                break;
            case State.Firing:
                stateTimer -= deltaTime;
                break;
            case State.Cooldown:
                stateTimer -= deltaTime;
                if (stateTimer <= 0f) state = State.Waiting;
                break;
        }
    }

    // Runs after the pose so bullets leave from where the muzzle is drawn this frame.
    private void FireDueShots()
    {
        while (shotsLeft > 0 && stateTimer <= 0f)
        {
            // The first bullet goes where the warning pointed; only follow-ups scatter.
            bool firstOfBurst = shotsLeft == Mathf.Max(1, burstSize);
            Vector2 spread = firstOfBurst ? Vector2.zero : UnityEngine.Random.insideUnitCircle * burstSpread;
            if (gun.Fire(spread))
            {
                recoil = 1f;
                ShotFired?.Invoke(this);
            }
            shotsLeft--;
            stateTimer += burstInterval;
        }
        if (shotsLeft == 0)
        {
            burstsFired++;
            state = State.Cooldown;
            stateTimer = cooldown;
            ReleaseShooterSlot();
        }
    }

    private bool CanEngage()
    {
        Vector3 toTarget = target.position - muzzle.position;
        float distance = toTarget.magnitude;
        if (toTarget.z >= 0f || distance > engageDistance || distance < minFireDistance) return false;
        var mainCamera = Camera.main;
        if (mainCamera == null) return false;
        Vector3 viewport = mainCamera.WorldToViewportPoint(muzzle.position);
        return viewport.z > 0f &&
            viewport.x >= viewMargin && viewport.x <= 1f - viewMargin &&
            viewport.y >= viewMargin && viewport.y <= 1f - viewMargin;
    }

    private bool TakeShooterSlot()
    {
        if (holdsShooterSlot) return true;
        if (engagedShooters >= maxSimultaneousShooters) return false;
        engagedShooters++;
        holdsShooterSlot = true;
        return true;
    }

    private void ReleaseShooterSlot()
    {
        if (!holdsShooterSlot) return;
        holdsShooterSlot = false;
        engagedShooters = Mathf.Max(0, engagedShooters - 1);
    }

    private Quaternion FacingTarget()
    {
        Vector3 toTarget = target.position - transform.position;
        toTarget.y = 0f;
        return toTarget.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(toTarget) : transform.rotation;
    }

    private void ApplyPose()
    {
        if (!configurationValid) return;
        float weight = aimWeight * aimWeight * (3f - 2f * aimWeight);
        spine.localRotation = spineRotation;
        Quaternion correction = Quaternion.identity;
        if (weight > 0f && target != null)
        {
            // Measured in the full aiming pose: the half-raised gun must not tip the body over.
            gunHand.localRotation = aimHandRotation;
            Vector3 barrel = muzzle.position - gunBody.position;
            Vector3 toTarget = target.position - muzzle.position;
            correction = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(barrel, toTarget), weight);
        }
        gunHand.localRotation = Quaternion.Slerp(idleHandRotation, aimHandRotation, weight);
        supportUpperArm.localRotation = Quaternion.Slerp(idleUpperArmRotation, aimUpperArmRotation, weight);
        supportForearm.localRotation = Quaternion.Slerp(idleForearmRotation, aimForearmRotation, weight);
        // Lean the whole upper body so the barrel lines up with the player's eyes.
        spine.rotation = correction * spine.rotation;
        if (recoil > 0f)
        {
            // The muzzle climbs: a negative turn about the enemy's right axis lifts what points forward.
            float kick = recoil * recoil * recoilAngle;
            spine.rotation = Quaternion.AngleAxis(-kick, transform.right) * spine.rotation;
            gunHand.rotation = Quaternion.AngleAxis(-kick, transform.right) * gunHand.rotation;
        }
    }

    private void OnValidate()
    {
        minFireDistance = Mathf.Min(minFireDistance, engageDistance);
    }
}
