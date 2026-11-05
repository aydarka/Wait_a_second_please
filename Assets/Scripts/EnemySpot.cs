using System.Collections.Generic;
using UnityEngine;

// A place in the level where an enemy can stand: pavement, light pool, balcony, lit window, bridge.
// Street segments carry these; EnemySpawner puts enemies on free ones ahead of the player.
// The transform position is where the feet go; enemies turn to face the player on their own.
[DisallowMultipleComponent]
public sealed class EnemySpot : MonoBehaviour
{
    private static readonly List<EnemySpot> active = new List<EnemySpot>();

    public static IReadOnlyList<EnemySpot> Active => active;

    public GameObject Occupant { get; set; }

    public bool IsFree => Occupant == null || !Occupant.activeInHierarchy;

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
        Gizmos.DrawWireCube(transform.position + Vector3.up, new Vector3(0.6f, 2f, 0.6f));
        Gizmos.DrawLine(transform.position, transform.position + Vector3.up * 2.2f);
    }
}
