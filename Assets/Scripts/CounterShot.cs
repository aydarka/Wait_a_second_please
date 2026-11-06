using UnityEngine;

// The player's answer to a close dodge: a bullet from the player's hip at the enemy who fired, flying
// in game time with the same air rings as enemy shots. It always lands: the enemy stays out of cover
// until it does, and dies the moment it arrives.
[DisallowMultipleComponent]
public sealed class CounterShot : MonoBehaviour
{
    [SerializeField, Min(0.01f), Tooltip("Flight speed, metres per game second.")]
    private float speed = 20f;
    [SerializeField, Tooltip("Launch point on screen (0 to 1 across and up); low right reads as a gun at the hip. On screen terms it stays in frame in landscape and portrait.")]
    private Vector2 hipOnScreen = new Vector2(0.78f, 0.12f);
    [SerializeField, Min(0.05f), Tooltip("Launch point distance in front of the eyes, metres.")]
    private float hipDepth = 0.6f;
    [SerializeField, Min(0f), Tooltip("Muzzle flash radius at the hip, metres.")]
    private float flashRadius = 0.03f;
    [SerializeField, Min(0.01f), Tooltip("Bullet size at the hip relative to the model; it shrinks back to the model size at the enemy.")]
    private float launchScale = 2.5f;
    [SerializeField] private AirRingTrail rings = new AirRingTrail();

    private EnemyGunman target;
    private Vector3 launchPosition;
    private Vector3 initialScale;
    private float flown;

    // Fires at the enemy; an enemy already dead or gone gets no answer.
    public static void Fire(EnemyGunman enemy)
    {
        if (enemy == null || enemy.IsDead || !enemy.isActiveAndEnabled) return;
        var view = Camera.main;
        var prefab = Resources.Load<CounterShot>("PlayerBullet");
        if (prefab == null)
        {
            Debug.LogError("CounterShot needs the PlayerBullet prefab in a Resources folder.");
            return;
        }
        if (view == null) return;
        Instantiate(prefab).Launch(enemy, view);
    }

    private void Launch(EnemyGunman enemy, Camera view)
    {
        target = enemy;
        initialScale = transform.localScale;
        launchPosition = view.ViewportToWorldPoint(new Vector3(hipOnScreen.x, hipOnScreen.y, hipDepth));
        flown = 0f;
        rings.Restart();
        enemy.HoldExposed();
        ShotEffects.MuzzleFlash(launchPosition, flashRadius);
        Place(launchPosition, enemy.TargetPoint - launchPosition, 0f);
    }

    private void Update()
    {
        if (target == null || target.IsDead || !target.isActiveAndEnabled)
        {
            Destroy(gameObject);
            return;
        }
        // The enemy may still be stepping out of cover, so the line is re-aimed at his chest every frame.
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

    private void Place(Vector3 position, Vector3 direction, float progress)
    {
        if (direction.sqrMagnitude > 0f) transform.SetPositionAndRotation(position, Quaternion.LookRotation(direction));
        else transform.position = position;
        transform.localScale = initialScale * Mathf.Lerp(launchScale, 1f, progress);
    }
}
