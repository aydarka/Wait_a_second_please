using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Shot visuals that are not part of a bullet: the Matrix-style rings a bullet leaves hanging in the air,
// the red aim glint on a levelling gun and the yellow muzzle flash.
// Created on first use, one per scene. Rings age in game time inside WaitASecond/ShotRings,
// so once a ring is placed the CPU never touches it again.
[DisallowMultipleComponent]
public sealed class ShotEffects : MonoBehaviour
{
    private const int RingCapacity = 256;
    private const float FlashDuration = 0.06f;
    private const float ReferenceScreenHeight = 1080f;
    // Tells sit this far towards the camera so the gun never cuts into them, metres.
    private const float TowardCamera = 0.15f;
    // The aim glint shows once the barrel points within this many degrees of the player,
    // or at this share of the warning at the latest.
    private const float LevelledAngle = 2f;
    private const float LateLevelProgress = 0.8f;
    private const float BoundsSize = 400f;

    private static readonly int ShotTimeId = Shader.PropertyToID("_ShotTime");
    private static readonly Vector2[] Corners =
        { new Vector2(-1f, -1f), new Vector2(1f, -1f), new Vector2(1f, 1f), new Vector2(-1f, 1f) };

    private static ShotEffects instance;

    // Tells stay where they appeared: a mark riding the moving gun reads as a sticker, not a warning.
    private struct Flash
    {
        public Vector3 Position;
        public float Born;
        public int Seed;
    }

    // The raise from the waiting pose reads as lowering the gun, so the glint waits until it is levelled.
    private sealed class AimGlint
    {
        public EnemyGunman Enemy;
        public bool Levelled;
        public Vector3 Anchor;
        public float LevelProgress;
    }

    private Mesh ringMesh;
    private readonly List<Vector3> ringPositions = new List<Vector3>(new Vector3[RingCapacity * 4]);
    private readonly List<Vector4> ringShapes = new List<Vector4>(new Vector4[RingCapacity * 4]);
    private readonly List<Vector4> ringLives = new List<Vector4>(new Vector4[RingCapacity * 4]);
    private readonly List<Vector4> ringGrowth = new List<Vector4>(new Vector4[RingCapacity * 4]);
    private readonly int[] ringOwners = new int[RingCapacity];
    private int nextRing;
    private int ringSeed;
    private bool ringsDirty;

    private Mesh inkMesh;
    private readonly List<Vector3> inkVertices = new List<Vector3>();
    private readonly List<Color> inkColors = new List<Color>();
    private readonly List<int> inkTriangles = new List<int>();
    private readonly List<Vector2> starPoints = new List<Vector2>();
    private readonly List<AimGlint> glints = new List<AimGlint>();
    private readonly List<Flash> flashes = new List<Flash>();
    private EnemyGunman steppingShooter;
    private Color red = new Color(0.89f, 0.09f, 0.04f);
    private Color yellow = new Color(0.95f, 0.76f, 0.11f);
    private Color black = Color.black;

    // Leaves a ring across the flight path; birthTime is in Time.time (game) seconds, owner identifies the shot.
    public static void AddRing(int owner, Vector3 centre, Vector3 direction, float radius, float lineWidth,
        float strength, float birthTime, float lifetime, float openTime)
    {
        var effects = Current;
        if (effects != null) effects.PutRing(owner, centre, direction, radius, lineWidth, strength, birthTime, lifetime, openTime);
    }

    // Removes every ring a shot has left.
    public static void ClearRings(int owner)
    {
        if (instance != null) instance.RemoveRings(owner);
    }

    // Death: the world goes black and only the shooter stays, drawn as a flat silhouette on top.
    // Though time has stopped, he steps out to where he fired from, gun raised over the shoulder.
    public static void Spotlight(Transform shooter)
    {
        var effects = Current;
        if (effects != null && shooter != null) effects.ShowSpotlight(shooter);
    }

    private static ShotEffects Current
    {
        get
        {
            if (instance == null && Application.isPlaying)
                instance = new GameObject("Shot Effects").AddComponent<ShotEffects>();
            return instance;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Subscribe()
    {
        instance = null;
        EnemyGunman.AimStarted -= OnAimStarted;
        EnemyGunman.AimStarted += OnAimStarted;
        EnemyGunman.ShotFired -= OnShotFired;
        EnemyGunman.ShotFired += OnShotFired;
    }

    private static void OnAimStarted(EnemyGunman enemy, float warningSeconds)
    {
        var effects = Current;
        if (effects == null || enemy == null) return;
        effects.glints.RemoveAll(glint => glint.Enemy == enemy);
        effects.glints.Add(new AimGlint { Enemy = enemy });
    }

    private static void OnShotFired(EnemyGunman enemy)
    {
        var effects = Current;
        if (effects == null || enemy == null || enemy.Muzzle == null) return;
        effects.flashes.Add(new Flash { Position = enemy.Muzzle.position, Born = Time.time, Seed = Random.Range(0, int.MaxValue) });
    }

    // The game clock stands still on the death screen, so the shooter walks on real time: his own side
    // step or stand-up, ending where he fired from with the gun over the shoulder, because a gun levelled
    // at the camera hides behind the body.
    private void UpdateStepOut()
    {
        if (steppingShooter == null) return;
        if (steppingShooter.StepToReadyPose(Time.unscaledDeltaTime)) steppingShooter = null;
    }

    private void Awake()
    {
        var ringMaterial = Resources.Load<Material>("ShotRings");
        var inkMaterial = Resources.Load<Material>("ShotInk");
        if (ringMaterial == null || inkMaterial == null)
        {
            Debug.LogError("ShotEffects requires the ShotRings and ShotInk materials in a Resources folder.", this);
            enabled = false;
            return;
        }
        if (QualitySettings.activeColorSpace == ColorSpace.Linear)
        {
            // Vertex colours reach the shader untouched, so they are converted like material colours are.
            red = red.linear;
            yellow = yellow.linear;
        }

        ringMesh = CreateLayer("Rings", ringMaterial);
        var triangles = new int[RingCapacity * 6];
        for (int i = 0; i < RingCapacity; i++)
        {
            for (int corner = 0; corner < 4; corner++)
                ringLives[i * 4 + corner] = new Vector4(-1e6f, 1f, 0f, 0f);
            int v = i * 4;
            triangles[i * 6] = v;
            triangles[i * 6 + 1] = v + 1;
            triangles[i * 6 + 2] = v + 2;
            triangles[i * 6 + 3] = v;
            triangles[i * 6 + 4] = v + 2;
            triangles[i * 6 + 5] = v + 3;
        }
        UploadRings();
        ringMesh.SetTriangles(triangles, 0);
        inkMesh = CreateLayer("Tells", inkMaterial);
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
        if (ringMesh != null) Destroy(ringMesh);
        if (inkMesh != null) Destroy(inkMesh);
    }

    private Mesh CreateLayer(string layerName, Material material)
    {
        var layer = new GameObject(layerName, typeof(MeshFilter), typeof(MeshRenderer));
        layer.transform.SetParent(transform, false);
        var mesh = new Mesh { name = layerName };
        mesh.MarkDynamic();
        layer.GetComponent<MeshFilter>().sharedMesh = mesh;
        var meshRenderer = layer.GetComponent<MeshRenderer>();
        meshRenderer.sharedMaterial = material;
        meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;
        return mesh;
    }

    private void PutRing(int owner, Vector3 centre, Vector3 direction, float radius, float lineWidth,
        float strength, float birthTime, float lifetime, float openTime)
    {
        if (ringMesh == null || direction.sqrMagnitude <= 0f) return;
        ringOwners[nextRing] = owner;
        Vector3 axis = direction.normalized;
        Vector3 side = Vector3.Cross(Vector3.up, axis);
        if (side.sqrMagnitude < 1e-6f) side = Vector3.Cross(Vector3.right, axis);
        side.Normalize();
        Vector3 up = Vector3.Cross(axis, side);
        // The quad leaves room for the outline, which can be wider than the line on a distant ring.
        float halfSize = radius * 1.4f + lineWidth;
        ringSeed = (ringSeed + 1) % 10000;

        int first = nextRing * 4;
        nextRing = (nextRing + 1) % RingCapacity;
        for (int corner = 0; corner < 4; corner++)
        {
            Vector2 c = Corners[corner];
            ringPositions[first + corner] = centre + (side * c.x + up * c.y) * halfSize;
            ringShapes[first + corner] = new Vector4(c.x, c.y, halfSize, radius);
            ringLives[first + corner] = new Vector4(birthTime, lifetime, strength, ringSeed);
            ringGrowth[first + corner] = new Vector4(openTime, lineWidth, 0f, 0f);
        }
        ringsDirty = true;
    }

    private void RemoveRings(int owner)
    {
        for (int ring = 0; ring < RingCapacity; ring++)
        {
            if (ringOwners[ring] != owner) continue;
            ringOwners[ring] = 0;
            for (int corner = 0; corner < 4; corner++)
                ringLives[ring * 4 + corner] = new Vector4(-1e6f, 1f, 0f, 0f);
            ringsDirty = true;
        }
    }

    private void ShowSpotlight(Transform shooter)
    {
        var gunman = shooter.GetComponent<EnemyGunman>();
        if (gunman != null)
        {
            gunman.enabled = false;
            steppingShooter = gunman;
        }

        var blackout = Resources.Load<Material>("ShotBlackout");
        var killer = Resources.Load<Material>("ShotKiller");
        var view = Camera.main;
        if (blackout == null || killer == null || view == null)
        {
            Debug.LogError("ShotEffects needs the ShotBlackout and ShotKiller materials in a Resources folder and a main camera.", this);
            return;
        }

        // A black card just past the near plane hides the world; it draws after everything else in the scene.
        float distance = view.nearClipPlane * 1.5f;
        float halfHeight = distance * Mathf.Tan(view.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.5f;
        float halfWidth = halfHeight * view.aspect;
        var card = new GameObject("Blackout", typeof(MeshFilter), typeof(MeshRenderer));
        card.transform.SetParent(view.transform, false);
        card.transform.localPosition = new Vector3(0f, 0f, distance);
        var mesh = new Mesh { name = "Blackout" };
        mesh.SetVertices(new List<Vector3>
        {
            new Vector3(-halfWidth, -halfHeight, 0f), new Vector3(halfWidth, -halfHeight, 0f),
            new Vector3(halfWidth, halfHeight, 0f), new Vector3(-halfWidth, halfHeight, 0f)
        });
        mesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
        card.GetComponent<MeshFilter>().sharedMesh = mesh;
        var cardRenderer = card.GetComponent<MeshRenderer>();
        cardRenderer.sharedMaterial = blackout;
        cardRenderer.shadowCastingMode = ShadowCastingMode.Off;

        // The shooter is drawn once more over the card where it is not hidden behind cover.
        foreach (var part in shooter.GetComponentsInChildren<Renderer>())
        {
            var materials = part.sharedMaterials;
            System.Array.Resize(ref materials, materials.Length + 1);
            materials[materials.Length - 1] = killer;
            part.sharedMaterials = materials;
        }
    }

    private void UploadRings()
    {
        ringMesh.SetVertices(ringPositions);
        ringMesh.SetUVs(0, ringShapes);
        ringMesh.SetUVs(1, ringLives);
        ringMesh.SetUVs(2, ringGrowth);
    }

    private void LateUpdate()
    {
        Shader.SetGlobalFloat(ShotTimeId, Time.time);
        UpdateStepOut();
        var view = Camera.main;
        Vector3 focus = view != null ? view.transform.position : Vector3.zero;
        if (ringsDirty)
        {
            UploadRings();
            ringsDirty = false;
        }
        // Rings are stored in world space; the bounds just follow the camera so they are never culled.
        ringMesh.bounds = new Bounds(focus, Vector3.one * BoundsSize);
        BuildTells(view);
        inkMesh.bounds = new Bounds(focus, Vector3.one * BoundsSize);
    }

    private void BuildTells(Camera view)
    {
        for (int i = glints.Count - 1; i >= 0; i--)
        {
            var enemy = glints[i].Enemy;
            if (enemy == null || !enemy.isActiveAndEnabled || !enemy.IsAiming || enemy.Muzzle == null)
                glints.RemoveAt(i);
        }
        for (int i = flashes.Count - 1; i >= 0; i--)
        {
            if (Time.time - flashes[i].Born > FlashDuration)
                flashes.RemoveAt(i);
        }

        inkVertices.Clear();
        inkColors.Clear();
        inkTriangles.Clear();
        if (view != null)
        {
            float pixelAtOneMetre = 2f * Mathf.Tan(view.fieldOfView * 0.5f * Mathf.Deg2Rad) / ReferenceScreenHeight;
            foreach (var glint in glints)
            {
                if (!glint.Levelled) TryLevel(glint, view.transform.position);
                if (!glint.Levelled) continue;
                float closing = Mathf.InverseLerp(glint.LevelProgress, 1f, glint.Enemy.AimProgress);
                AddGlint(view.transform, glint.Anchor, closing, pixelAtOneMetre);
            }
            foreach (var flash in flashes) AddFlash(view.transform, flash.Position, flash.Seed, pixelAtOneMetre);
        }
        inkMesh.Clear();
        inkMesh.SetVertices(inkVertices);
        inkMesh.SetColors(inkColors);
        inkMesh.SetTriangles(inkTriangles, 0);
    }

    // Pins the glint where the muzzle is once the barrel lines up with the player.
    private static void TryLevel(AimGlint glint, Vector3 viewer)
    {
        Transform muzzle = glint.Enemy.Muzzle;
        float progress = glint.Enemy.AimProgress;
        Vector3 barrel = muzzle.parent != null ? muzzle.position - muzzle.parent.position : muzzle.forward;
        if (progress < LateLevelProgress && Vector3.Angle(barrel, viewer - muzzle.position) > LevelledAngle) return;
        glint.Levelled = true;
        glint.Anchor = muzzle.position;
        glint.LevelProgress = Mathf.Min(progress, 0.99f);
    }

    // Sizes are in metres but never drop below a size in pixels, so a distant tell stays readable.
    private static Vector3 TowardViewer(Transform view, Vector3 point, float pixelAtOneMetre, out float pixel)
    {
        Vector3 centre = point + (view.position - point).normalized * TowardCamera;
        pixel = pixelAtOneMetre * Mathf.Max(0.01f, Vector3.Dot(centre - view.position, view.forward));
        return centre;
    }

    // Red dot on the muzzle and a red ring closing in on it; the ring collapses as the bullet leaves.
    private void AddGlint(Transform view, Vector3 muzzle, float progress, float pixelAtOneMetre)
    {
        Vector3 centre = TowardViewer(view, muzzle, pixelAtOneMetre, out float pixel);
        float dot = Mathf.Max(0.05f, 4f * pixel);
        float ring = Mathf.Lerp(Mathf.Max(0.6f, 34f * pixel), dot * 1.5f, Mathf.Clamp01(progress));
        float halfWidth = 1.25f * pixel;
        AddDisc(centre, view.right, view.up, dot, red);
        AddAnnulus(centre, view.right, view.up, ring - halfWidth, ring + halfWidth, red);
    }

    // A flat jagged star with a black edge; every shot gets its own rays.
    private void AddFlash(Transform view, Vector3 muzzle, int seed, float pixelAtOneMetre)
    {
        Vector3 centre = TowardViewer(view, muzzle, pixelAtOneMetre, out float pixel);
        float outer = Mathf.Max(0.35f, 26f * pixel);
        var random = new System.Random(seed);
        const int points = 16;
        float step = 2f * Mathf.PI / points;
        starPoints.Clear();
        for (int i = 0; i < points; i++)
        {
            float angle = i * step + ((float)random.NextDouble() - 0.5f) * step * 0.6f;
            float radius = outer * (i % 2 == 0 ? 0.65f + 0.55f * (float)random.NextDouble()
                : 0.32f + 0.12f * (float)random.NextDouble());
            starPoints.Add(new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius);
        }
        AddStar(centre, view.right, view.up, 2.2f * pixel, black);
        AddStar(centre, view.right, view.up, 0f, yellow);
    }

    private void AddStar(Vector3 centre, Vector3 right, Vector3 up, float grow, Color color)
    {
        int hub = inkVertices.Count;
        AddVertex(centre, color);
        foreach (var point in starPoints)
        {
            Vector2 p = point + point.normalized * grow;
            AddVertex(centre + right * p.x + up * p.y, color);
        }
        for (int i = 0; i < starPoints.Count; i++)
            AddTriangle(hub, hub + 1 + i, hub + 1 + (i + 1) % starPoints.Count);
    }

    private void AddDisc(Vector3 centre, Vector3 right, Vector3 up, float radius, Color color)
    {
        const int segments = 20;
        int hub = inkVertices.Count;
        AddVertex(centre, color);
        for (int i = 0; i < segments; i++)
        {
            float angle = i * 2f * Mathf.PI / segments;
            AddVertex(centre + (right * Mathf.Cos(angle) + up * Mathf.Sin(angle)) * radius, color);
        }
        for (int i = 0; i < segments; i++)
            AddTriangle(hub, hub + 1 + i, hub + 1 + (i + 1) % segments);
    }

    private void AddAnnulus(Vector3 centre, Vector3 right, Vector3 up, float inner, float outer, Color color)
    {
        const int segments = 40;
        int first = inkVertices.Count;
        for (int i = 0; i < segments; i++)
        {
            float angle = i * 2f * Mathf.PI / segments;
            Vector3 direction = right * Mathf.Cos(angle) + up * Mathf.Sin(angle);
            AddVertex(centre + direction * Mathf.Max(0f, inner), color);
            AddVertex(centre + direction * outer, color);
        }
        for (int i = 0; i < segments; i++)
        {
            int a = first + i * 2, b = first + (i + 1) % segments * 2;
            AddTriangle(a, a + 1, b + 1);
            AddTriangle(a, b + 1, b);
        }
    }

    private void AddVertex(Vector3 position, Color color)
    {
        inkVertices.Add(position);
        inkColors.Add(color);
    }

    private void AddTriangle(int a, int b, int c)
    {
        inkTriangles.Add(a);
        inkTriangles.Add(b);
        inkTriangles.Add(c);
    }
}
