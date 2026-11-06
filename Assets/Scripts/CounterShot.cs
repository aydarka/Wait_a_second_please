using UnityEngine;

// The player's answer to a close dodge. The moment the dodge counts, the enemy who fired is held out of cover
// and the player's pistol is drawn up from below the frame; once it is up the shot leaves its muzzle and flies
// in game time with the same air rings as enemy shots. It always lands, and the enemy dies the moment it
// arrives. As in Superhot the shot is an action, so time runs at normal speed while it flies, but only while
// no enemy bullet is still coming at the player: another shot in the air keeps time in the player's hands,
// so it can still be read and dodged.
[DisallowMultipleComponent]
public sealed class CounterShot : MonoBehaviour
{
    [SerializeField, Min(0.01f), Tooltip("Flight speed, metres per game second.")]
    private float speed = 50f;
    [SerializeField, Tooltip("Launch point on screen (0 to 1 across and up) when there is no pistol to fire from.")]
    private Vector2 hipOnScreen = new Vector2(0.78f, 0.12f);
    [SerializeField, Min(0.05f), Tooltip("Launch point distance in front of the eyes without a pistol, metres.")]
    private float hipDepth = 0.6f;
    [SerializeField, Min(0f), Tooltip("Muzzle flash radius, metres.")]
    private float flashRadius = 0.16f;
    [SerializeField, Min(0f), Tooltip("Real-time seconds the muzzle flash stays on screen.")]
    private float flashSeconds = 0.18f;
    [SerializeField, Min(0.01f), Tooltip("Bullet size at the muzzle relative to the model; it shrinks back to the model size at the enemy.")]
    private float launchScale = 2.5f;
    [SerializeField] private AirRingTrail rings = new AirRingTrail();

    private EnemyGunman target;
    private PlayerGun gun;
    private Renderer[] parts;
    private bool launched;
    private Vector3 launchPosition;
    private Vector3 initialScale;
    private float flown;

    // Answers the enemy; one already dead or gone gets no answer.
    public static void Fire(EnemyGunman enemy)
    {
        if (enemy == null || enemy.IsDead || !enemy.isActiveAndEnabled) return;
        var prefab = Resources.Load<CounterShot>("PlayerBullet");
        if (prefab == null)
        {
            Debug.LogError("CounterShot needs the PlayerBullet prefab in a Resources folder.");
            return;
        }
        // He stays out of cover from now on, while the pistol comes up.
        enemy.HoldExposed();
        Instantiate(prefab).Prepare(enemy);
    }

    // The bullet waits unseen until the pistol is up.
    private void Prepare(EnemyGunman enemy)
    {
        target = enemy;
        initialScale = transform.localScale;
        parts = GetComponentsInChildren<Renderer>();
        SetVisible(false);
        rings.Restart();
        gun = PlayerGun.Draw(this, enemy.TargetPoint);
    }

    private void Launch(Camera view)
    {
        launched = true;
        launchPosition = gun != null
            ? gun.MuzzlePosition
            : view.ViewportToWorldPoint(new Vector3(hipOnScreen.x, hipOnScreen.y, hipDepth));
        flown = 0f;
        HoldTimeWhileSafe();
        ShotEffects.PlayerFlash(view, launchPosition, flashRadius, flashSeconds);
        if (gun != null) gun.Kick();
        SetVisible(true);
        Place(launchPosition, target.TargetPoint - launchPosition, 0f);
    }

    private void Update()
    {
        if (target == null || target.IsDead || !target.isActiveAndEnabled)
        {
            Destroy(gameObject);
            return;
        }
        // The enemy may still be stepping out of cover, so the aim follows his chest every frame.
        if (gun != null) gun.Aim(target.TargetPoint);
        if (!launched)
        {
            var view = Camera.main;
            if (view != null && (gun == null || gun.IsUp)) Launch(view);
            return;
        }
        HoldTimeWhileSafe();

        Vector3 path = target.TargetPoint - launchPosition;
        float length = path.magnitude;
        flown += speed * Time.deltaTime;
        if (flown >= length)
        {
            target.Die(path);
            Destroy(gameObject);
            return;
        }
        Place(launchPosition + path / length * flown, path, flown / length);
        rings.Emit(launchPosition, path / length * speed, flown, launchScale, 1f, length);
    }

    // Landing, losing its target or a scene reload all end the shot here.
    private void OnDestroy()
    {
        TimeScaleController.ReleaseNormalTime(this);
        PlayerGun.Holster(this);
    }

    // Normal time while the shot flies, unless an enemy bullet is still on its way to the player. Once that
    // bullet has passed, normal time comes back for the rest of the flight; a bullet fired meanwhile lets
    // time settle back to slow.
    private void HoldTimeWhileSafe()
    {
        if (Projectile.AnyApproaching) TimeScaleController.ReleaseNormalTime(this);
        else TimeScaleController.HoldNormalTime(this);
    }

    private void Place(Vector3 position, Vector3 direction, float progress)
    {
        if (direction.sqrMagnitude > 0f) transform.SetPositionAndRotation(position, Quaternion.LookRotation(direction));
        else transform.position = position;
        transform.localScale = initialScale * Mathf.Lerp(launchScale, 1f, progress);
    }

    private void SetVisible(bool visible)
    {
        foreach (var part in parts) part.enabled = visible;
    }
}
