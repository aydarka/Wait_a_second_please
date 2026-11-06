using System;
using UnityEngine;

// The Matrix-style rings a flying bullet leaves in the air, for enemy shots and the player's answer alike.
// Rings stay where they were born and are only born while time is slowed: the trail records the player's time.
[Serializable]
public sealed class AirRingTrail
{
    [SerializeField, Min(0.05f), Tooltip("Flight distance between rings, metres.")]
    private float spacing = 0.35f;
    [SerializeField, Tooltip("Smallest and largest ring radius at launch size, metres; rings grow with the bullet.")]
    private Vector2 radius = new Vector2(0.03f, 0.065f);
    [SerializeField, Min(1f), Tooltip("Rings grow with the bullet up to this many times their launch size.")]
    private float maxGrowth = 2.5f;
    [SerializeField, Min(0.1f), Tooltip("Flight distance of one small-to-large wave of ring sizes, metres.")]
    private float waveLength = 1.7f;
    [SerializeField, Min(0f), Tooltip("Ring line width at launch size, metres; the shader keeps a minimum in pixels.")]
    private float lineWidth = 0.004f;
    [SerializeField, Min(0.01f), Tooltip("Game seconds a ring hangs before it has crumbled away.")]
    private float lifetime = 0.7f;
    [SerializeField, Min(0f), Tooltip("Flight distance over which a new ring opens from bullet size, metres.")]
    private float openDistance = 0.3f;
    [SerializeField, Range(0f, 1f), Tooltip("Ring strength is how slow time runs at its birth; weaker rings are not born.")]
    private float minStrength = 0.1f;

    private static int trails;

    private float nextDistance;

    // Identifies this flight's rings, so they can be cleared together.
    public int Owner { get; private set; }

    public float MaxGrowth => maxGrowth;

    public void Restart()
    {
        nextDistance = spacing;
        Owner = ++trails;
    }

    // Leaves the rings due up to the flown distance along a straight path from launch. The bullet's size factor
    // runs from startGrowth at launch to endGrowth at growthDistance; rings follow it up to maxGrowth.
    public void Emit(Vector3 launch, Vector3 velocity, float flown, float startGrowth, float endGrowth, float growthDistance)
    {
        float pathSpeed = velocity.magnitude;
        if (pathSpeed <= 0f) return;
        Vector3 direction = velocity / pathSpeed;
        float strength = 1f - TimeScaleController.Flow;
        while (nextDistance <= flown)
        {
            float distance = nextDistance;
            nextDistance += spacing;
            if (strength < minStrength) continue;

            float along = growthDistance > 0f ? Mathf.Clamp01(distance / growthDistance) : 1f;
            float growth = Mathf.Min(maxGrowth, Mathf.Lerp(startGrowth, endGrowth, along));
            // Sizes follow a wave along the path, like the rings around Neo's bullets, not the distance to the gun.
            float wave = 0.5f + 0.5f * Mathf.Sin(distance * 2f * Mathf.PI / waveLength);
            float ringRadius = Mathf.Lerp(radius.x, radius.y, wave) * growth * UnityEngine.Random.Range(0.9f, 1.1f);
            float birthTime = Time.time - (flown - distance) / pathSpeed;
            ShotEffects.AddRing(Owner, launch + direction * distance, direction, ringRadius, lineWidth * growth,
                strength, birthTime, lifetime, openDistance / pathSpeed);
        }
    }
}
