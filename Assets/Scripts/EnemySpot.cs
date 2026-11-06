using System.Collections.Generic;
using UnityEngine;

// How an enemy at a spot hides and shows itself to shoot.
public enum EnemyCover
{
    // In the open, like a fire escape landing: always exposed.
    None,
    // Behind something waist-high (concrete block, car seen side-on, parapet): crouches, stands up to shoot.
    Low,
    // Behind an edge (car seen end-on, door pier, corner): steps out sideways to shoot.
    Edge,
}

// A place in the level where an enemy can stand: pavement, light pool, balcony, lit window, bridge.
// Street segments carry these; EnemySpawner puts enemies on free ones ahead of the player.
// The transform position is where the feet go while hiding; enemies turn to face the player on their own.
[DisallowMultipleComponent]
public sealed class EnemySpot : MonoBehaviour
{
    private static readonly List<EnemySpot> active = new List<EnemySpot>();

    [SerializeField] private EnemyCover cover = EnemyCover.None;
    [SerializeField, Tooltip("Where the feet go to shoot, relative to this spot in world axes. " +
        "Zero for an Edge means one metre towards the middle of the road.")]
    private Vector3 exposedOffset;
    [SerializeField, Min(0f), Tooltip("Top of a Low cover above the feet, metres; the crouching enemy hides below it.")]
    private float coverHeight = 1f;

    public static IReadOnlyList<EnemySpot> Active => active;

    public GameObject Occupant { get; set; }

    public bool IsFree => Occupant == null || !Occupant.activeInHierarchy;

    public EnemyCover Cover => cover;

    public float CoverHeight => coverHeight;

    public Vector3 HiddenPosition => transform.position;

    public Vector3 ExposedPosition
    {
        get
        {
            if (cover != EnemyCover.Edge) return transform.position + exposedOffset;
            if (exposedOffset.sqrMagnitude > 0.0001f) return transform.position + exposedOffset;
            float towardsRoad = transform.position.x > 0f ? -1f : 1f;
            return transform.position + new Vector3(towardsRoad, 0f, 0f);
        }
    }

    // For level generators that recycle spots: set before the spot is enabled for a new layout.
    public void SetCover(EnemyCover coverType, Vector3 offsetToExposed, float lowCoverHeight = 1f)
    {
        cover = coverType;
        exposedOffset = offsetToExposed;
        coverHeight = Mathf.Max(0f, lowCoverHeight);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRegistry() => active.Clear();

    private void OnEnable() => active.Add(this);

    private void OnDisable()
    {
        active.Remove(this);
        Occupant = null;
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.9f, 0.1f, 0.1f, 0.8f);
        float height = cover == EnemyCover.Low ? coverHeight : 2f;
        Gizmos.DrawWireCube(transform.position + Vector3.up * height * 0.5f, new Vector3(0.6f, height, 0.6f));
        if (cover == EnemyCover.None) return;
        Vector3 exposed = ExposedPosition;
        Gizmos.color = new Color(1f, 0.85f, 0.1f, 0.8f);
        Gizmos.DrawLine(transform.position, exposed);
        Gizmos.DrawWireCube(exposed + Vector3.up, new Vector3(0.6f, 2f, 0.6f));
    }
}
