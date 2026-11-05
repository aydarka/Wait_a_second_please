using UnityEngine;

// Camera-attached rain of white streaks. It runs on game time, so slow motion hangs the drops
// in the air and a sharp tilt sets them falling again.
[DisallowMultipleComponent]
public sealed class NoirRain : MonoBehaviour
{
    [SerializeField, Tooltip("Uses Sprites/Default when empty.")] private Material material;
    [SerializeField, Min(0)] private int maxDrops = 900;
    [SerializeField, Min(0f)] private float dropsPerSecond = 700f;
    [SerializeField, Tooltip("Size of the box drops are born in, metres.")] private Vector3 area = new Vector3(26f, 1f, 34f);
    [SerializeField, Tooltip("Centre of that box relative to the camera.")] private Vector3 areaOffset = new Vector3(0f, 9f, 14f);
    [SerializeField, Min(0.1f)] private float fallSpeed = 18f;
    [SerializeField, Tooltip("Sideways drift, metres per second.")] private float wind = -2f;
    [SerializeField] private Color color = new Color(0.95f, 0.94f, 0.9f, 0.8f);
    [SerializeField, Min(0f)] private float dropWidth = 0.025f;
    [SerializeField, Min(0f), Tooltip("Streak length per metre per second of fall.")] private float streakScale = 0.05f;
    [SerializeField, Range(0f, 0.05f), Tooltip("Largest drop width as a share of the screen, so drops by the lens stay thin.")]
    private float maxScreenWidth = 0.004f;

    private ParticleSystem rain;

    private void Awake()
    {
        var holder = new GameObject("Rain");
        holder.transform.SetParent(transform, false);
        holder.transform.localPosition = areaOffset;
        rain = holder.AddComponent<ParticleSystem>();
        rain.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = rain.main;
        main.loop = true;
        main.prewarm = true;
        main.startLifetime = (areaOffset.y + 2f) / fallSpeed;
        main.startSpeed = 0f;
        main.startSize = dropWidth;
        main.startColor = color;
        main.maxParticles = maxDrops;
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        var emission = rain.emission;
        emission.rateOverTime = dropsPerSecond;

        var shape = rain.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = area;

        var velocity = rain.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;
        velocity.x = wind;
        velocity.y = -fallSpeed;
        velocity.z = 0f;

        var renderer = holder.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.velocityScale = streakScale;
        renderer.lengthScale = 1f;
        renderer.maxParticleSize = maxScreenWidth;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.sharedMaterial = material != null ? material : new Material(Shader.Find("Sprites/Default")) { name = "Rain (Fallback)" };

        rain.Play();
    }
}
