using UnityEngine;

// Puts enemies on free EnemySpots about spawnDistance ahead of the player.
// Until the street provides spots, a temporary layout stands them on the ground beside the road
// and on ledges at the facade line.
[DisallowMultipleComponent]
public sealed class EnemySpawner : MonoBehaviour
{
    [SerializeField] private Transform player;
    [SerializeField] private PrefabPool pool;
    [SerializeField, Min(0.01f)] private float spawnDistance = 50f;
    [SerializeField, Min(0f), Tooltip("Spots up to this many metres closer than the spawn distance still qualify.")]
    private float spotWindow = 10f;
    [SerializeField, Min(0f)] private float initialDelay = 10f;
    [SerializeField, Min(0.01f)] private float minSpawnInterval = 3f;
    [SerializeField, Min(0.01f)] private float maxSpawnInterval = 5f;

    [Header("Temporary layout, used while the level has no EnemySpots")]
    [SerializeField, Tooltip("Ground height under the player's line.")] private float groundHeight;
    [SerializeField, Tooltip("Sideways distance from the player's line for ground spots (min, max), metres.")]
    private Vector2 groundOffset = new Vector2(2.5f, 5f);
    [SerializeField, Min(0f), Tooltip("Sideways distance of the ledges, roughly the facade line.")]
    private float ledgeOffset = 6f;
    [SerializeField] private float[] ledgeHeights = { 4f, 8f };
    [SerializeField, Range(0f, 1f)] private float ledgeChance = 0.4f;

    private float timeUntilSpawn;
    private bool configurationValid;

    private void Awake()
    {
        configurationValid = player != null && pool != null && pool.Prefab != null &&
            pool.Prefab.TryGetComponent<ReturnToPoolBehindPlayer>(out _);
        if (!configurationValid)
            Debug.LogError("EnemySpawner requires a player and a pool with a ReturnToPoolBehindPlayer prefab.", this);
    }

    private void OnEnable()
    {
        timeUntilSpawn = Mathf.Max(0f, initialDelay);
    }

    private void Update()
    {
        if (!configurationValid || player == null || pool == null) return;
        timeUntilSpawn -= Time.deltaTime;
        if (timeUntilSpawn > 0f) return;

        float minimumInterval = Mathf.Max(0.01f, minSpawnInterval);
        timeUntilSpawn = Random.Range(minimumInterval, Mathf.Max(minimumInterval, maxSpawnInterval));

        Vector3 position;
        EnemySpot spot = null;
        bool onLedge = false;
        if (EnemySpot.Active.Count > 0)
        {
            spot = PickSpot();
            // The street has its own spots; when none is free ahead, this beat stays empty.
            if (spot == null) return;
            position = spot.transform.position;
        }
        else
        {
            position = PickTemporaryPosition(out onLedge);
        }

        Vector3 toPlayer = player.position - position;
        toPlayer.y = 0f;
        Quaternion facing = toPlayer.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(toPlayer) : Quaternion.identity;
        var enemy = pool.Rent(position, facing);
        if (enemy == null) return;
        enemy.GetComponent<ReturnToPoolBehindPlayer>().Initialize(player, pool);
        if (enemy.TryGetComponent<EnemyGunman>(out var gunman))
        {
            gunman.AssignSpot(spot);
            gunman.ShowTemporaryLedge(onLedge);
        }
        if (spot != null) spot.Occupant = enemy;
        enemy.SetActive(true);
    }

    private EnemySpot PickSpot()
    {
        float farthest = player.position.z + Mathf.Max(0.01f, spawnDistance);
        float nearest = farthest - Mathf.Max(0f, spotWindow);
        EnemySpot chosen = null;
        int candidates = 0;
        var spots = EnemySpot.Active;
        for (int i = 0; i < spots.Count; i++)
        {
            var candidate = spots[i];
            float z = candidate.transform.position.z;
            if (z < nearest || z > farthest || !candidate.IsFree) continue;
            // Reservoir sampling: a uniform pick without building a list.
            candidates++;
            if (Random.Range(0, candidates) == 0) chosen = candidate;
        }
        return chosen;
    }

    private Vector3 PickTemporaryPosition(out bool onLedge)
    {
        float side = Random.value < 0.5f ? -1f : 1f;
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
        minSpawnInterval = Mathf.Max(0.01f, minSpawnInterval);
        maxSpawnInterval = Mathf.Max(minSpawnInterval, maxSpawnInterval);
        groundOffset.x = Mathf.Max(0f, groundOffset.x);
        groundOffset.y = Mathf.Max(groundOffset.x, groundOffset.y);
        ledgeOffset = Mathf.Max(0f, ledgeOffset);
    }
}
