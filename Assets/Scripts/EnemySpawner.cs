using System;
using UnityEngine;
using Random = UnityEngine.Random;

// Puts enemies on free EnemySpots about spawnDistance ahead of the player. The pace follows the distance run:
// enemies come more often, later in pairs from both sides, more of them may shoot at once and they spend less
// time in cover. Short breathers without new enemies turn the rise into waves.
// Until the street provides spots, a temporary layout stands them on the ground and on ledges.
[DisallowMultipleComponent]
public sealed class EnemySpawner : MonoBehaviour
{
    [Serializable]
    private struct Stage
    {
        [Tooltip("Distance run from the start, metres.")] public float Distance;
        [Tooltip("Time between new enemies (min, max), game seconds.")] public Vector2 Interval;
        [Range(0f, 1f), Tooltip("Chance that a second enemy appears at the same time, preferably across the street.")]
        public float PairChance;
        [Min(1), Tooltip("Enemies allowed to aim and shoot at once.")] public int Shooters;
        [Tooltip("Time an enemy stays in cover between shots (min, max), game seconds.")] public Vector2 HiddenTime;
        [Tooltip("An enemy starts showing itself once the player is this close, metres.")] public float EngageDistance;

        public static Stage Blend(Stage from, Stage to, float t)
        {
            return new Stage
            {
                Distance = Mathf.Lerp(from.Distance, to.Distance, t),
                Interval = Vector2.Lerp(from.Interval, to.Interval, t),
                PairChance = Mathf.Lerp(from.PairChance, to.PairChance, t),
                Shooters = Mathf.RoundToInt(Mathf.Lerp(from.Shooters, to.Shooters, t)),
                HiddenTime = Vector2.Lerp(from.HiddenTime, to.HiddenTime, t),
                EngageDistance = Mathf.Lerp(from.EngageDistance, to.EngageDistance, t),
            };
        }
    }

    [SerializeField] private Transform player;
    [SerializeField] private PrefabPool pool;
    [SerializeField, Min(0.01f)] private float spawnDistance = 50f;
    [SerializeField, Min(0f), Tooltip("Spots up to this many metres closer than the spawn distance still qualify.")]
    private float spotWindow = 10f;
    [SerializeField, Min(0f)] private float initialDelay = 10f;

    [Header("Pace: values blend between stages by the distance run (stage distances × pace stretch)")]
    [SerializeField] private Stage[] stages =
    {
        new Stage { Distance = 0f, Interval = new Vector2(3.5f, 4.5f), PairChance = 0f, Shooters = 1,
            HiddenTime = new Vector2(1f, 1.5f), EngageDistance = 38f },
        new Stage { Distance = 40f, Interval = new Vector2(2.6f, 3.4f), PairChance = 0f, Shooters = 2,
            HiddenTime = new Vector2(1f, 1.5f), EngageDistance = 38f },
        new Stage { Distance = 120f, Interval = new Vector2(1.9f, 2.6f), PairChance = 0.25f, Shooters = 2,
            HiddenTime = new Vector2(0.7f, 1.1f), EngageDistance = 40f },
        new Stage { Distance = 250f, Interval = new Vector2(1.4f, 2f), PairChance = 0.45f, Shooters = 3,
            HiddenTime = new Vector2(0.3f, 0.6f), EngageDistance = 44f },
    };
    [SerializeField, Min(0.01f), Tooltip("Multiplies every stage distance: one knob to stretch or compress the whole curve. " +
        "4 puts the peak at 1000 m, on the same scale as the slow-down in the time controller's danger mode.")]
    private float paceStretch = 4f;
    [SerializeField, Min(0f), Tooltip("A breather comes every this many metres; 0 turns breathers off.")]
    private float breatherEvery = 80f;
    [SerializeField, Min(0f), Tooltip("No new enemies for this long, game seconds.")]
    private float breatherLength = 2.5f;

    [Header("Temporary layout, used while the level has no EnemySpots")]
    [SerializeField, Tooltip("Ground height under the player's line.")] private float groundHeight;
    [SerializeField, Tooltip("Sideways distance from the player's line for ground spots (min, max), metres.")]
    private Vector2 groundOffset = new Vector2(2.5f, 5f);
    [SerializeField, Min(0f), Tooltip("Sideways distance of the ledges, roughly the facade line.")]
    private float ledgeOffset = 6f;
    [SerializeField] private float[] ledgeHeights = { 4f, 8f };
    [SerializeField, Range(0f, 1f)] private float ledgeChance = 0.4f;

    private float timeUntilSpawn;
    private float startZ;
    private float nextBreather;
    private bool configurationValid;

    // Distance run since this spawner started, metres.
    public float DistanceRun => player != null ? Mathf.Max(0f, player.position.z - startZ) : 0f;

    private void Awake()
    {
        configurationValid = player != null && pool != null && pool.Prefab != null &&
            pool.Prefab.TryGetComponent<ReturnToPoolBehindPlayer>(out _) && stages != null && stages.Length > 0;
        if (!configurationValid)
            Debug.LogError("EnemySpawner requires a player, a pool with a ReturnToPoolBehindPlayer prefab and at least one stage.", this);
    }

    private void OnEnable()
    {
        timeUntilSpawn = Mathf.Max(0f, initialDelay);
        startZ = player != null ? player.position.z : 0f;
        nextBreather = breatherEvery;
    }

    private void OnDisable()
    {
        EnemyGunman.ShooterLimit = 0;
    }

    private void Update()
    {
        if (!configurationValid || player == null || pool == null) return;
        float run = DistanceRun;
        Stage pace = StageAt(run);
        EnemyGunman.ShooterLimit = Mathf.Max(1, pace.Shooters);

        if (breatherEvery > 0f && run >= nextBreather)
        {
            nextBreather += breatherEvery;
            timeUntilSpawn = Mathf.Max(timeUntilSpawn, breatherLength);
        }

        timeUntilSpawn -= Time.deltaTime;
        if (timeUntilSpawn > 0f) return;
        float minimum = Mathf.Max(0.01f, pace.Interval.x);
        timeUntilSpawn = Random.Range(minimum, Mathf.Max(minimum, pace.Interval.y));

        float side = Spawn(pace, 0f);
        // The partner comes from the other side of the street when it can, so the player is caught between them.
        if (side != 0f && Random.value < pace.PairChance) Spawn(pace, -side);
    }

    private Stage StageAt(float run)
    {
        run /= Mathf.Max(0.01f, paceStretch);
        if (run <= stages[0].Distance || stages.Length == 1) return stages[0];
        for (int i = 1; i < stages.Length; i++)
        {
            if (run > stages[i].Distance) continue;
            float span = stages[i].Distance - stages[i - 1].Distance;
            float t = span > 0f ? (run - stages[i - 1].Distance) / span : 1f;
            return Stage.Blend(stages[i - 1], stages[i], t);
        }
        return stages[stages.Length - 1];
    }

    // Returns the side of the street (-1 or 1) the enemy went to, or 0 when no place was free.
    private float Spawn(Stage pace, float preferredSide)
    {
        Vector3 position;
        EnemySpot spot = null;
        bool onLedge = false;
        if (EnemySpot.Active.Count > 0)
        {
            spot = PickSpot(preferredSide);
            // The street has its own spots; when none is free ahead, this beat stays empty.
            if (spot == null) return 0f;
            position = spot.transform.position;
        }
        else
        {
            position = PickTemporaryPosition(preferredSide, out onLedge);
        }

        Vector3 toPlayer = player.position - position;
        toPlayer.y = 0f;
        Quaternion facing = toPlayer.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(toPlayer) : Quaternion.identity;
        var enemy = pool.Rent(position, facing);
        if (enemy == null) return 0f;
        enemy.GetComponent<ReturnToPoolBehindPlayer>().Initialize(player, pool);
        if (enemy.TryGetComponent<EnemyGunman>(out var gunman))
        {
            gunman.AssignSpot(spot);
            gunman.SetPace(pace.HiddenTime, pace.EngageDistance);
            gunman.ShowTemporaryLedge(onLedge);
        }
        if (spot != null) spot.Occupant = enemy;
        enemy.SetActive(true);
        return position.x >= player.position.x ? 1f : -1f;
    }

    private EnemySpot PickSpot(float preferredSide)
    {
        float farthest = player.position.z + Mathf.Max(0.01f, spawnDistance);
        float nearest = farthest - Mathf.Max(0f, spotWindow);
        EnemySpot preferred = null, any = null;
        int preferredCount = 0, anyCount = 0;
        var spots = EnemySpot.Active;
        for (int i = 0; i < spots.Count; i++)
        {
            var candidate = spots[i];
            Vector3 position = candidate.transform.position;
            if (position.z < nearest || position.z > farthest || !candidate.IsFree) continue;
            // Reservoir sampling: uniform picks without building lists.
            anyCount++;
            if (Random.Range(0, anyCount) == 0) any = candidate;
            if (preferredSide != 0f && Mathf.Sign(position.x - player.position.x) == preferredSide)
            {
                preferredCount++;
                if (Random.Range(0, preferredCount) == 0) preferred = candidate;
            }
        }
        return preferred != null ? preferred : any;
    }

    private Vector3 PickTemporaryPosition(float preferredSide, out bool onLedge)
    {
        float side = preferredSide != 0f ? preferredSide : (Random.value < 0.5f ? -1f : 1f);
        Vector3 position = player.position;
        position.z += Mathf.Max(0.01f, spawnDistance);
        onLedge = ledgeHeights != null && ledgeHeights.Length > 0 && Random.value < ledgeChance;
        if (onLedge)
        {
            position.x += side * ledgeOffset;
            position.y = groundHeight + ledgeHeights[Random.Range(0, ledgeHeights.Length)];
        }
        else
        {
            position.x += side * Random.Range(groundOffset.x, Mathf.Max(groundOffset.x, groundOffset.y));
            position.y = groundHeight;
        }
        return position;
    }

    private void OnValidate()
    {
        spawnDistance = Mathf.Max(0.01f, spawnDistance);
        spotWindow = Mathf.Max(0f, spotWindow);
        initialDelay = Mathf.Max(0f, initialDelay);
        paceStretch = Mathf.Max(0.01f, paceStretch);
        groundOffset.x = Mathf.Max(0f, groundOffset.x);
        groundOffset.y = Mathf.Max(groundOffset.x, groundOffset.y);
        ledgeOffset = Mathf.Max(0f, ledgeOffset);
    }
}
