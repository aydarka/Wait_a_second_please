using System;
using UnityEngine;

// Gangster behaviour in game time, posed like comic panels. The enemy hides at its EnemySpot,
// shows itself (stands up from a crouch behind low cover or side-steps out from an edge), levels the gun
// at the player as the warning, fires, holds for a beat and hides again.
// Poses are bone rotations derived from the prefab's aiming pose; the legs are handled by EnemyBody.
// There are no animation clips.
[DisallowMultipleComponent]
public sealed class EnemyGunman : MonoBehaviour
{
    // An enemy starts levelling its gun; the float is the warning time before the first bullet, in game seconds.
    public static event Action<EnemyGunman, float> AimStarted;
    // A bullet leaves the muzzle.
    public static event Action<EnemyGunman> ShotFired;

    // Held: out in the open for good, waiting for the player's answer. Dead: falling, then gone.
    private enum State { Hidden, Exposing, Aiming, Firing, Holding, Hiding, Held, Dead }

    private const string DefaultBlockerLayer = "StreetCover";
    private static readonly int FloorHeightId = Shader.PropertyToID("_FloorHeight");
    private const float NoFloor = -100000f;

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

    [Header("Legs, hips, crouch and side steps")]
    [SerializeField] private EnemyBody body = new EnemyBody();

    [Header("Death: knocked back, blood, hat flying off")]
    [SerializeField] private EnemyDeath death = new EnemyDeath();

    [Header("Cover")]
    [SerializeField, Min(0f), Tooltip("Muzzle height above the feet when standing to shoot, for checks made before showing.")]
    private float exposedMuzzleHeight = 1.3f;
    [SerializeField, Tooltip("Colliders that stop bullets. Empty uses the StreetCover layer when it exists.")]
    private LayerMask lineOfFireBlockers;

    [Header("Engagement")]
    [SerializeField, Min(0f), Tooltip("Starts showing itself once the player is this close, metres.")]
    private float engageDistance = 38f;
    [SerializeField, Min(0f), Tooltip("Never starts showing itself closer than this, metres.")]
    private float minFireDistance = 15f;
    [SerializeField, Range(0f, 0.5f), Tooltip("Margin inside the screen edges the whole figure must keep while it is out.")]
    private float viewMargin = 0.03f;
    [SerializeField, Min(0f), Tooltip("Half the figure's width with the gun, metres, for the on-screen check.")]
    private float figureHalfWidth = 0.5f;
    [SerializeField, Min(0f), Tooltip("Height of the hat top above the feet, metres, for the on-screen check.")]
    private float figureHeight = 2.1f;
    [SerializeField, Min(1)] private int maxSimultaneousShooters = 2;

    [Header("Rhythm, game seconds")]
    [SerializeField, Tooltip("Time hidden between shots (min, max); random so enemies do not show in unison.")]
    private Vector2 hiddenTime = new Vector2(1f, 1.5f);
    [SerializeField, Min(0.01f), Tooltip("Rising from the crouch behind low cover, and sinking back into it.")]
    private float standUpDuration = 0.35f;
    [SerializeField, Min(0.01f), Tooltip("One foot's move in a side step out from an edge, and back.")]
    private float footMoveDuration = 0.12f;
    [SerializeField, Min(0.01f)] private float raiseDuration = 0.25f;
    [SerializeField, Min(0f), Tooltip("Warning from the moment the enemy is out to the first bullet.")]
    private float aimDuration = 0.5f;
    [SerializeField, Min(1), Tooltip("Bullets after each warning. Single shots for now; bursts may come later.")]
    private int burstSize = 1;
    [SerializeField, Min(0f)] private float burstInterval = 0.12f;
    [SerializeField, Min(0f), Tooltip("Random aim offset of the follow-up bullets in a burst, metres.")]
    private float burstSpread = 0.12f;
    [SerializeField, Min(0), Tooltip("Times the enemy shows itself to shoot; each ends in burstSize bullets.")]
    private int maxBursts = 3;
    [SerializeField, Min(0f), Tooltip("Stays out after the shot so the recoil reads.")]
    private float holdAfterShot = 0.25f;
    [SerializeField, Min(0.01f)] private float lowerDuration = 0.4f;
    [SerializeField, Min(0f), Tooltip("Degrees per game second.")] private float turnSpeed = 240f;

    [Header("Recoil")]
    [SerializeField] private float recoilAngle = 9f;
    [SerializeField, Min(0.01f)] private float recoilRecovery = 0.1f;

    private static int engagedShooters;

    private Transform target;
    private CameraMovement runner;
    private Transform muzzle;
    private Transform gunBody;
    private Renderer[] bodyRenderers;
    private MaterialPropertyBlock properties;
    private Quaternion aimHandRotation;
    private Quaternion aimUpperArmRotation;
    private Quaternion aimForearmRotation;
    private Quaternion idleHandRotation;
    private Quaternion idleUpperArmRotation;
    private Quaternion idleForearmRotation;
    private Quaternion spineRotation;
    private bool configurationValid;

    private bool hasPlacement;
    private EnemyCover cover;
    private Vector3 hiddenPosition;
    private Vector3 exposedPosition;
    private int sideSteps;

    private State state;
    private float stateTimer;
    private float exposure;
    private float aimWeight;
    private float recoil;
    private int shotsLeft;
    private int burstsFired;
    private bool holdsShooterSlot;
    private Vector3 deathPivot;
    private Quaternion deathStartRotation;

    public Transform Muzzle => muzzle;
    public bool IsAiming => state == State.Aiming;
    // 0 when the warning starts, 1 when the first bullet leaves.
    public float AimProgress => state == State.Aiming && aimDuration > 0f ? 1f - stateTimer / aimDuration
        : state == State.Firing ? 1f : 0f;
    public bool IsDead => state == State.Dead;
    // Where the player's answering bullet should go: the chest in the current pose.
    public Vector3 TargetPoint => configurationValid ? spine.position : transform.position + Vector3.up * exposedMuzzleHeight;

    // The player has answered this enemy's shot: it stops hiding and shooting, comes back out with its own
    // step or stand-up if it already ducked, and stays in the open, gun over the shoulder, until Die.
    public void HoldExposed()
    {
        if (!configurationValid || state == State.Dead || state == State.Held) return;
        state = State.Held;
        shotsLeft = 0;
        burstsFired = Mathf.Max(burstsFired, maxBursts);
        ReleaseShooterSlot();
    }

    // The agreed death, in game time: knocked back along the shot, a red splash behind the silhouette,
    // the hat flying off, then the body sinks into the dark and a red stain stays behind.
    public void Die(Vector3 shotDirection)
    {
        if (!configurationValid || state == State.Dead) return;
        ReleaseShooterSlot();
        state = State.Dead;
        shotsLeft = 0;
        recoil = 0f;
        // Mid-step legs would be planted at the old spot: the fall starts from where the body is now.
        if (cover == EnemyCover.Edge && exposure > 0f) exposure = 1f;
        deathPivot = transform.position;
        deathStartRotation = transform.rotation;
        death.Begin(transform, shotDirection, spine.position, exposedPosition.y, lineOfFireBlockers);
    }

    // Call before activating a pooled enemy; null stands it in the open where it was placed.
    public void AssignSpot(EnemySpot spot)
    {
        hasPlacement = spot != null;
        if (spot == null) return;
        cover = spot.Cover;
        hiddenPosition = spot.HiddenPosition;
        exposedPosition = spot.ExposedPosition;
    }

    public void ShowTemporaryLedge(bool show)
    {
        if (temporaryLedge != null) temporaryLedge.SetActive(show);
    }

    // Where the feet stand when this enemy shoots: out of its cover.
    public Vector3 ShootingPosition => exposedPosition;

    // For the death screen: stands the enemy where it fired from, facing the player, gun raised over
    // the shoulder as just before levelling it, so the weapon reads in silhouette (levelled at the camera
    // it hides behind the body). Disable this component first, or the next frame poses it again.
    public void PoseReadyToShoot()
    {
        if (!configurationValid || state == State.Dead) return;
        transform.position = exposedPosition;
        if (target != null) transform.rotation = FacingTarget();
        exposure = 1f;
        aimWeight = 0f;
        recoil = 0f;
        ApplyPose();
    }

    // For the death screen, where the game clock stands still: walks the enemy into the PoseReadyToShoot pose
    // with its real side step or stand-up and lowers the gun back over the shoulder, on the caller's clock.
    // Call every frame with this component disabled; returns true once the enemy stands ready.
    // From deep in cover it takes at most about half a second.
    public bool StepToReadyPose(float deltaSeconds)
    {
        if (!configurationValid || state == State.Dead) return true;
        float dt = Mathf.Max(0f, deltaSeconds);
        if (target != null)
            transform.rotation = Quaternion.RotateTowards(transform.rotation, FacingTarget(), turnSpeed * dt);
        exposure = cover == EnemyCover.None ? 1f : Mathf.MoveTowards(exposure, 1f, dt / ExposeDuration);
        aimWeight = Mathf.MoveTowards(aimWeight, 0f, dt / lowerDuration);
        recoil = Mathf.MoveTowards(recoil, 0f, dt / recoilRecovery);
        PlaceRoot();
        ApplyPose();
        return exposure >= 1f && aimWeight <= 0f && recoil <= 0f;
    }

    // In a side step the body follows the feet; elsewhere the root stays on the spot.
    private void PlaceRoot()
    {
        float travel = cover == EnemyCover.Edge ? body.BodyProgress(exposure, sideSteps)
            : exposure * exposure * (3f - 2f * exposure);
        transform.position = Vector3.Lerp(hiddenPosition, exposedPosition, travel);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetShooters() => engagedShooters = 0;

    private void Awake()
    {
        muzzle = gun != null ? gun.ShootPoint : null;
        gunBody = muzzle != null ? muzzle.parent : null;
        configurationValid = gun != null && spine != null && gunHand != null && supportUpperArm != null &&
            supportForearm != null && muzzle != null && gunBody != null && supportForearm.childCount > 0 &&
            body != null && body.IsValid && death != null && death.IsValid;
        if (!configurationValid)
        {
            Debug.LogError("EnemyGunman requires a gun with a muzzle, the spine, arm, hip and leg bones, the hat and a blood material.", this);
            return;
        }
        if (lineOfFireBlockers.value == 0) lineOfFireBlockers = LayerMask.GetMask(DefaultBlockerLayer);
        CollectBodyRenderers();
        CapturePoses();
        body.Capture(transform);
        death.Capture();
    }

    private void CollectBodyRenderers()
    {
        var all = GetComponentsInChildren<Renderer>(true);
        int count = 0;
        var renderers = new Renderer[all.Length];
        foreach (var candidate in all)
        {
            if (temporaryLedge != null && candidate.transform.IsChildOf(temporaryLedge.transform)) continue;
            renderers[count++] = candidate;
        }
        Array.Resize(ref renderers, count);
        bodyRenderers = renderers;
        properties = new MaterialPropertyBlock();
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
        if (!hasPlacement)
        {
            cover = EnemyCover.None;
            hiddenPosition = exposedPosition = transform.position;
        }
        Vector3 stepVector = exposedPosition - hiddenPosition;
        stepVector.y = 0f;
        sideSteps = body != null && body.IsValid ? body.StepCount(stepVector.magnitude) : 1;
        state = State.Hidden;
        stateTimer = 0f;
        exposure = 0f;
        aimWeight = 0f;
        recoil = 0f;
        shotsLeft = 0;
        burstsFired = 0;
        var mainCamera = Camera.main;
        target = mainCamera != null ? mainCamera.transform : null;
        runner = target != null ? target.GetComponent<CameraMovement>() : null;
        if (!configurationValid) return;
        death.ResetState();
        ApplyFloorClip();
        transform.position = hiddenPosition;
        if (target != null) transform.rotation = FacingTarget();
        ApplyPose();
    }

    private void OnDisable()
    {
        ReleaseShooterSlot();
        hasPlacement = false;
        // The death screen disables a living shooter to pose it; only a dead body's leftovers go away.
        if (configurationValid && state == State.Dead && !gameObject.activeInHierarchy) death.ResetState();
    }

    // Time to show or hide: standing up from the crouch, or the feet's moves of the side step.
    private float ExposeDuration => cover == EnemyCover.Low ? standUpDuration
        : cover == EnemyCover.Edge ? sideSteps * 2 * footMoveDuration : 0f;

    // The coat tails of a crouching enemy may still touch the floor; the shader cuts away what goes under it.
    private void ApplyFloorClip()
    {
        SetFloorCut(cover == EnemyCover.Low ? hiddenPosition.y - 0.005f : NoFloor);
    }

    private void SetFloorCut(float height)
    {
        foreach (var bodyRenderer in bodyRenderers)
        {
            bodyRenderer.GetPropertyBlock(properties);
            properties.SetFloat(FloorHeightId, height);
            bodyRenderer.SetPropertyBlock(properties);
        }
    }

    private void LateUpdate()
    {
        if (!configurationValid || target == null) return;
        float deltaTime = Time.deltaTime;
        if (state == State.Dead)
        {
            UpdateDeath(deltaTime);
            return;
        }

        transform.rotation = Quaternion.RotateTowards(transform.rotation, FacingTarget(), turnSpeed * deltaTime);
        UpdateState(deltaTime);

        bool raised = state == State.Aiming || state == State.Firing || state == State.Holding;
        aimWeight = Mathf.MoveTowards(aimWeight, raised ? 1f : 0f,
            deltaTime / (raised ? raiseDuration : lowerDuration));
        recoil = Mathf.MoveTowards(recoil, 0f, deltaTime / recoilRecovery);
        PlaceRoot();
        ApplyPose();

        if (state == State.Firing) FireDueShots();
    }

    private void UpdateDeath(float deltaTime)
    {
        death.Advance(deltaTime);
        aimWeight = Mathf.MoveTowards(aimWeight, 0f, deltaTime / lowerDuration);
        transform.SetPositionAndRotation(deathPivot, death.Tilt() * deathStartRotation);
        ApplyPose();
        death.AfterPose(transform, spine, Camera.main);
        if (death.Cutting(out float cut)) SetFloorCut(cut);
    }

    private void UpdateState(float deltaTime)
    {
        bool inOpen = cover == EnemyCover.None;
        switch (state)
        {
            case State.Held:
                exposure = inOpen ? 1f : Mathf.MoveTowards(exposure, 1f, deltaTime / ExposeDuration);
                break;
            case State.Hidden:
                stateTimer -= deltaTime;
                if (stateTimer <= 0f && burstsFired < maxBursts && CanEngage() && TakeShooterSlot())
                    state = State.Exposing;
                break;
            case State.Exposing:
                exposure = inOpen ? 1f : Mathf.MoveTowards(exposure, 1f, deltaTime / ExposeDuration);
                if (exposure >= 1f)
                {
                    state = State.Aiming;
                    stateTimer = aimDuration;
                    AimStarted?.Invoke(this, aimDuration);
                }
                break;
            case State.Aiming:
                stateTimer -= deltaTime;
                // The overshoot stays in the timer, so a burst keeps its rhythm on long frames.
                if (stateTimer <= 0f)
                {
                    state = State.Firing;
                    shotsLeft = Mathf.Max(1, burstSize);
                }
                break;
            case State.Firing:
                stateTimer -= deltaTime;
                break;
            case State.Holding:
                stateTimer -= deltaTime;
                if (stateTimer <= 0f)
                {
                    state = State.Hiding;
                    ReleaseShooterSlot();
                }
                break;
            case State.Hiding:
                exposure = inOpen ? 0f : Mathf.MoveTowards(exposure, 0f, deltaTime / ExposeDuration);
                if (exposure <= 0f)
                {
                    state = State.Hidden;
                    stateTimer = UnityEngine.Random.Range(hiddenTime.x, Mathf.Max(hiddenTime.x, hiddenTime.y));
                }
                break;
        }
    }

    // Runs after the pose so bullets leave from where the muzzle is drawn this frame.
    private void FireDueShots()
    {
        while (shotsLeft > 0 && stateTimer <= 0f)
        {
            // Something moved into the way (or the spot shoots into its own cover): no bullet through walls,
            // and none from beyond the screen edge.
            if (!LineOfFireClear(muzzle.position, target.position) || !MuzzleOnScreen())
            {
                shotsLeft = 0;
                break;
            }
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
            state = State.Holding;
            stateTimer = holdAfterShot;
        }
    }

    // Checked from where the muzzle will be once the enemy is out, before it shows itself.
    private bool CanEngage()
    {
        Vector3 shootFrom = exposedPosition + Vector3.up * exposedMuzzleHeight;
        Vector3 toTarget = target.position - shootFrom;
        float distance = toTarget.magnitude;
        if (toTarget.z >= 0f || distance > engageDistance || distance < minFireDistance) return false;
        var mainCamera = Camera.main;
        if (mainCamera == null) return false;
        return StaysInFrame(mainCamera) && LineOfFireClear(shootFrom, target.position);
    }

    // The whole figure must be on screen when it steps out and still be there when it is back in cover,
    // although the player keeps running towards it meanwhile; a narrow portrait view makes this matter.
    private bool StaysInFrame(Camera view)
    {
        float shownFor = ExposeDuration + aimDuration + holdAfterShot + ExposeDuration;
        Vector3 cameraTravel = Vector3.forward * ((runner != null ? runner.Speed : 0f) * shownFor);
        Vector3 across = view.transform.right * figureHalfWidth;
        Vector3 chest = exposedPosition + Vector3.up * exposedMuzzleHeight;
        Vector3 top = exposedPosition + Vector3.up * figureHeight;
        // Moving the figure back by the camera's travel is the same as moving the camera forward.
        for (int moment = 0; moment < 2; moment++)
        {
            Vector3 shift = moment == 0 ? Vector3.zero : -cameraTravel;
            if (!InFrame(view, chest - across + shift, viewMargin) || !InFrame(view, chest + across + shift, viewMargin) ||
                !InFrame(view, top + shift, viewMargin))
                return false;
        }
        return true;
    }

    private bool MuzzleOnScreen()
    {
        var view = Camera.main;
        return view == null || InFrame(view, muzzle.position, 0f);
    }

    private static bool InFrame(Camera view, Vector3 point, float margin)
    {
        Vector3 viewport = view.WorldToViewportPoint(point);
        return viewport.z > 0f &&
            viewport.x >= margin && viewport.x <= 1f - margin &&
            viewport.y >= margin && viewport.y <= 1f - margin;
    }

    private bool LineOfFireClear(Vector3 from, Vector3 to)
    {
        return lineOfFireBlockers.value == 0 ||
            !Physics.Linecast(from, to, lineOfFireBlockers, QueryTriggerInteraction.Ignore);
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
        float shown = exposure * exposure * (3f - 2f * exposure);
        float crouch = cover == EnemyCover.Low ? 1f - shown : 0f;
        bool stepping = cover == EnemyCover.Edge && exposure > 0f && exposure < 1f;
        Vector3 stepVector = exposedPosition - hiddenPosition;
        body.ResetPose();
        body.ApplyCrouch(transform, crouch);
        if (stepping) body.ApplyStepLean(transform, stepVector, exposure);

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

        // Legs last, once the hips are where the crouch put them; at rest they need no solving.
        if (crouch > 0f) body.PlantFeet(transform, transform.position, Vector3.zero, 0f, 1);
        else if (stepping) body.PlantFeet(transform, hiddenPosition, stepVector, exposure, sideSteps);
    }

    private void OnValidate()
    {
        minFireDistance = Mathf.Min(minFireDistance, engageDistance);
        hiddenTime.x = Mathf.Max(0f, hiddenTime.x);
        hiddenTime.y = Mathf.Max(hiddenTime.x, hiddenTime.y);
    }
}
