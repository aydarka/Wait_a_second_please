using System.Collections.Generic;
using UnityEngine;

// Builds an endless noir street around the player from recycled segments of unit cubes:
// road, pavements, building fronts with setbacks, side alleys, bridges, fire escapes, street lamps,
// and the cover enemies hide behind — cars, barriers, door niches, balconies — each with an EnemySpot.
// The NoirCity shader turns each cube's scale into windows and inked edges, so pieces need no own meshes.
[DisallowMultipleComponent]
public sealed class CityStreetSpawner : MonoBehaviour
{
    [SerializeField] private Transform player;

    [Header("Materials")]
    [SerializeField, Tooltip("Facades and bridges: windows and inked edges.")]
    private Material buildingMaterial;
    [SerializeField, Tooltip("Barriers, parapets, niches, fire escapes: inked edges, no windows.")]
    private Material propMaterial;
    [SerializeField, Tooltip("Road and pavements: plain ink.")] private Material groundMaterial;
    [SerializeField, Tooltip("Kerbs and road markings: plain paper.")] private Material lineMaterial;
    [SerializeField, Tooltip("Lamp posts and arms: thin inked outline.")] private Material poleMaterial;
    [SerializeField, Tooltip("Lamp heads: bright paper that blooms.")] private Material bulbMaterial;
    [SerializeField, Tooltip("Lit doorways and balcony windows behind enemies.")] private Material lightPanelMaterial;
    [SerializeField, Tooltip("Light pools and wet streaks on the road.")] private Material lightPoolMaterial;
    [SerializeField, Tooltip("Parked cars: inked rim instead of box edges.")] private Material carMaterial;
    [SerializeField] private Mesh carMesh;

    [Header("Street")]
    [SerializeField, Min(1f)] private float segmentLength = 12f;
    [SerializeField, Min(0f)] private float distanceAhead = 130f;
    [SerializeField, Min(0f)] private float distanceBehind = 14f;
    [SerializeField, Min(0.5f)] private float roadHalfWidth = 5f;
    [SerializeField, Min(0f)] private float pavementWidth = 3f;
    [SerializeField, Min(0f)] private float pavementHeight = 0.15f;
    [SerializeField, Min(0f), Tooltip("Cover never reaches closer than this to the middle of the road, metres.")]
    private float clearLane = 2.5f;

    [Header("Buildings")]
    [SerializeField] private Vector2 buildingHeight = new Vector2(10f, 30f);
    [SerializeField] private Vector2 buildingDepth = new Vector2(8f, 16f);
    [SerializeField, Range(0f, 1f)] private float setbackChance = 0.4f;
    [SerializeField, Range(0f, 1f), Tooltip("Chance that a side opens into an alley in a segment.")]
    private float alleyChance = 0.12f;
    [SerializeField, Range(0f, 1f)] private float bridgeChance = 0.12f;
    [SerializeField, Range(0f, 1f)] private float fireEscapeChance = 0.3f;
    [SerializeField, Min(1)] private int fireEscapeFloors = 4;
    [SerializeField, Min(0.5f), Tooltip("Matches the window rows of the building material.")]
    private float floorHeight = 3.2f;
    [SerializeField, Min(0f), Tooltip("Height of the first upper floor, matches the ground-floor margin.")]
    private float firstFloor = 3.5f;

    [Header("Cover")]
    [SerializeField, Range(0f, 1f)] private float parkedCarChance = 0.45f;
    [SerializeField, Range(0f, 1f), Tooltip("Share of cars left at an angle instead of parked along the kerb.")]
    private float abandonedCarShare = 0.25f;
    [SerializeField, Range(0f, 1f)] private float barrierChance = 0.2f;
    [SerializeField, Range(0f, 1f)] private float doorChance = 0.35f;
    [SerializeField, Range(0f, 1f)] private float balconyChance = 0.4f;
    [SerializeField, Min(1)] private int balconyFloors = 3;

    [Header("Lamps")]
    [SerializeField, Min(1), Tooltip("Lamps alternate sides, one every this many segments.")]
    private int lampEverySegments = 1;
    [SerializeField, Min(1f)] private float lampHeight = 5.5f;
    [SerializeField, Min(0f)] private float lampArm = 1.5f;
    [SerializeField] private Vector2 poolSize = new Vector2(5f, 3.4f);

    [Header("Light at the end of the street")]
    [SerializeField, Tooltip("A white glow at the vanishing point that silhouettes enemies on the road. Empty to skip.")]
    private Material backlightMaterial;
    [SerializeField, Min(0f)] private float backlightDistance = 150f;
    [SerializeField] private Vector2 backlightSize = new Vector2(30f, 14f);

    private sealed class Segment
    {
        public Transform Root;
        public float StartZ;
        public readonly List<Transform> Pieces = new List<Transform>();
        public readonly List<EnemySpot> Spots = new List<EnemySpot>();
        public int Used;
        public int UsedSpots;
    }

    private readonly Queue<Segment> activeSegments = new Queue<Segment>();
    private readonly Stack<Segment> spareSegments = new Stack<Segment>();
    private Mesh cubeMesh;
    private Mesh quadMesh;
    private Vector3 carCentre;
    private Vector3 carHalfSize;
    private Transform backlight;
    private float nextSegmentZ;
    private int segmentIndex;
    private bool configurationValid;

    private void Awake()
    {
        if (player == null && Camera.main != null) player = Camera.main.transform;
        configurationValid = player != null && buildingMaterial != null && propMaterial != null &&
            groundMaterial != null && lineMaterial != null && poleMaterial != null && bulbMaterial != null &&
            lightPanelMaterial != null && lightPoolMaterial != null;
        if (!configurationValid)
        {
            Debug.LogError("CityStreetSpawner requires a player and all street materials.", this);
            return;
        }
        cubeMesh = BorrowPrimitiveMesh(PrimitiveType.Cube);
        quadMesh = BorrowPrimitiveMesh(PrimitiveType.Quad);
        if (carMesh != null)
        {
            // The car model's pivot is off its middle; placement works from the mesh bounds instead.
            carCentre = carMesh.bounds.center;
            carHalfSize = carMesh.bounds.extents;
        }
        if (backlightMaterial != null)
        {
            backlight = CreatePiece(transform, quadMesh, backlightMaterial).transform;
            backlight.name = "Street End Light";
            backlight.localScale = new Vector3(backlightSize.x, backlightSize.y, 1f);
        }
    }

    private void OnEnable()
    {
        if (!configurationValid) return;
        nextSegmentZ = Mathf.Floor((player.position.z - distanceBehind) / segmentLength) * segmentLength;
        MaintainStreet();
    }

    private void LateUpdate()
    {
        if (configurationValid && player != null)
            MaintainStreet();
    }

    private void OnDisable()
    {
        while (activeSegments.Count > 0)
            Recycle(activeSegments.Dequeue());
    }

    private void MaintainStreet()
    {
        float playerZ = player.position.z;
        while (activeSegments.Count > 0 && activeSegments.Peek().StartZ + segmentLength < playerZ - distanceBehind)
            Recycle(activeSegments.Dequeue());

        // Skip segments the player jumped past instead of building a long trail behind them.
        float firstNeeded = Mathf.Floor((playerZ - distanceBehind) / segmentLength) * segmentLength;
        if (nextSegmentZ < firstNeeded) nextSegmentZ = firstNeeded;

        while (nextSegmentZ < playerZ + distanceAhead)
        {
            Segment segment = spareSegments.Count > 0 ? spareSegments.Pop() : CreateSegment();
            segment.StartZ = nextSegmentZ;
            segment.Root.gameObject.SetActive(true);
            Layout(segment, segmentIndex++);
            activeSegments.Enqueue(segment);
            nextSegmentZ += segmentLength;
        }

        if (backlight != null)
            backlight.SetPositionAndRotation(new Vector3(0f, backlightSize.y * 0.5f, playerZ + backlightDistance), Quaternion.identity);
    }

    private Segment CreateSegment()
    {
        var root = new GameObject("Street Segment");
        root.transform.SetParent(transform, false);
        return new Segment { Root = root.transform };
    }

    private void Recycle(Segment segment)
    {
        segment.Root.gameObject.SetActive(false);
        spareSegments.Push(segment);
    }

    private void Layout(Segment segment, int index)
    {
        segment.Used = 0;
        segment.UsedSpots = 0;
        float z0 = segment.StartZ;
        float zMid = z0 + segmentLength * 0.5f;
        float frontX = roadHalfWidth + pavementWidth;

        Cube(segment, groundMaterial, new Vector3(0f, -0.25f, zMid), new Vector3(roadHalfWidth * 2f, 0.5f, segmentLength));
        Cube(segment, lineMaterial, new Vector3(0f, 0.005f, zMid), new Vector3(0.15f, 0.01f, segmentLength * 0.3f));
        for (int side = -1; side <= 1; side += 2)
        {
            // The pavement runs a metre under the buildings so recessed fronts never show a gap.
            Cube(segment, groundMaterial, new Vector3(side * (roadHalfWidth + (pavementWidth + 1f) * 0.5f), pavementHeight - 0.4f, zMid),
                new Vector3(pavementWidth + 1f, 0.8f, segmentLength));
            Cube(segment, lineMaterial, new Vector3(side * (roadHalfWidth + 0.06f), (pavementHeight + 0.01f) * 0.5f, zMid),
                new Vector3(0.12f, pavementHeight + 0.01f, segmentLength));
        }

        bool leftWall = LayoutSide(segment, -1f, z0, frontX);
        bool rightWall = LayoutSide(segment, 1f, z0, frontX);
        if (leftWall && rightWall && Random.value < bridgeChance)
            LayoutBridge(segment, zMid, frontX);

        bool lampHere = index % lampEverySegments == 0;
        float lampSide = (index / lampEverySegments) % 2 == 0 ? -1f : 1f;
        if (lampHere)
            LayoutLamp(segment, lampSide, z0 + 2f);
        for (int side = -1; side <= 1; side += 2)
            LayoutRoadCover(segment, side, z0);

        for (int i = segment.Used; i < segment.Pieces.Count; i++)
            segment.Pieces[i].gameObject.SetActive(false);
        for (int i = segment.UsedSpots; i < segment.Spots.Count; i++)
            segment.Spots[i].gameObject.SetActive(false);
    }

    // Returns whether this side has a continuous wall along the segment (bridges need one on both sides).
    private bool LayoutSide(Segment segment, float side, float z0, float frontX)
    {
        float z1 = z0 + segmentLength;
        if (Random.value < alleyChance)
        {
            // An alley: the street wall opens and a far facade closes the view a few metres in.
            float depth = Random.Range(buildingDepth.x, buildingDepth.y);
            float height = Random.Range(buildingHeight.x, buildingHeight.y);
            float inner = frontX + Random.Range(10f, 16f);
            Cube(segment, buildingMaterial, new Vector3(side * (inner + depth * 0.5f), height * 0.5f, z0 + segmentLength * 0.5f),
                new Vector3(depth, height, segmentLength));
            return false;
        }

        int count = Random.value < 0.55f ? 1 : 2;
        float cursor = z0;
        for (int i = 0; i < count; i++)
        {
            float length = i == count - 1 ? z1 - cursor : Random.Range(0.35f, 0.65f) * segmentLength;
            float depth = Random.Range(buildingDepth.x, buildingDepth.y);
            float height = Random.Range(buildingHeight.x, buildingHeight.y);
            float inner = frontX + Random.Range(0f, 0.6f);
            float zCentre = cursor + length * 0.5f;
            Cube(segment, buildingMaterial, new Vector3(side * (inner + depth * 0.5f), height * 0.5f, zCentre),
                new Vector3(depth, height, length));

            if (Random.value < setbackChance)
            {
                float inset = Random.Range(1.5f, 3f);
                float extra = Random.Range(4f, 12f);
                Cube(segment, buildingMaterial, new Vector3(side * (inner + inset + (depth - inset) * 0.5f), height + extra * 0.5f, zCentre),
                    new Vector3(depth - inset, extra, Mathf.Max(2f, length - inset)));
            }

            if (length >= 5f && Random.value < fireEscapeChance)
            {
                LayoutFireEscape(segment, side, inner, zCentre, height);
            }
            else if (length >= 4f && height > firstFloor + floorHeight && Random.value < balconyChance)
            {
                LayoutBalcony(segment, side, inner, zCentre + Random.Range(-0.3f, 0.3f) * (length - 3f), height);
            }
            if (length >= 4f && Random.value < doorChance)
                LayoutDoor(segment, side, inner, zCentre + Random.Range(-0.3f, 0.3f) * (length - 3f));
            cursor += length;
        }
        return true;
    }

    // Platforms, rails and ladders thin enough that the inked edges draw them as white lines.
    private void LayoutFireEscape(Segment segment, float side, float facadeX, float zCentre, float height)
    {
        int floors = Mathf.Min(fireEscapeFloors, Mathf.FloorToInt((height - firstFloor - 1f) / floorHeight));
        const float platformDepth = 1.1f;
        const float platformLength = 3.2f;
        float platformX = side * (facadeX - platformDepth * 0.5f);
        float ladderRun = platformLength - 0.6f;
        float ladderLength = Mathf.Sqrt(ladderRun * ladderRun + floorHeight * floorHeight);
        float ladderAngle = Mathf.Atan2(floorHeight, ladderRun) * Mathf.Rad2Deg;
        for (int k = 0; k < floors; k++)
        {
            float y = firstFloor + 0.2f + k * floorHeight;
            Cube(segment, propMaterial, new Vector3(platformX, y, zCentre), new Vector3(platformDepth, 0.08f, platformLength));
            Cube(segment, propMaterial, new Vector3(side * (facadeX - platformDepth + 0.03f), y + 1f, zCentre),
                new Vector3(0.05f, 0.05f, platformLength));
            // An open perch: the rail is no cover, but an enemy up here is framed by the lattice.
            if (k < 2) Spot(segment, new Vector3(platformX, y + 0.04f, zCentre + (k == 0 ? 0.9f : -0.9f)));
            if (k == floors - 1) continue;
            float slope = k % 2 == 0 ? ladderAngle : -ladderAngle;
            Cube(segment, propMaterial, new Vector3(platformX, y + floorHeight * 0.5f, zCentre),
                new Vector3(0.5f, 0.05f, ladderLength), Quaternion.Euler(-slope, 0f, 0f));
        }
    }

    // A balcony with a 1 m parapet in front of a lit window: low cover with paper behind the enemy.
    private void LayoutBalcony(Segment segment, float side, float facadeX, float z, float height)
    {
        int floors = Mathf.Clamp(Mathf.FloorToInt((height - firstFloor) / floorHeight), 1, balconyFloors);
        int floor = Random.Range(0, floors);
        const float depth = 1.2f;
        const float length = 2.4f;
        const float parapet = 1f;
        // The slab sits just under a window row so the parapet top meets the sill.
        float slabTop = firstFloor + floor * floorHeight + 0.65f;
        float x = side * (facadeX - depth * 0.5f);
        Cube(segment, propMaterial, new Vector3(x, slabTop - 0.08f, z), new Vector3(depth, 0.16f, length));
        Cube(segment, propMaterial, new Vector3(side * (facadeX - depth + 0.06f), slabTop + parapet * 0.5f, z),
            new Vector3(0.12f, parapet, length));
        for (int end = -1; end <= 1; end += 2)
            Cube(segment, propMaterial, new Vector3(x, slabTop + parapet * 0.5f, z + end * (length * 0.5f - 0.06f)),
                new Vector3(depth, parapet, 0.12f));
        Cube(segment, lightPanelMaterial, new Vector3(side * (facadeX - 0.02f), slabTop + 1.1f, z), new Vector3(0.04f, 2.2f, 1.3f));
        Spot(segment, new Vector3(side * (facadeX - depth * 0.45f), slabTop, z));
    }

    // A doorway niche between two piers, lit from inside; the enemy waits in the shadow of the nearer pier.
    private void LayoutDoor(Segment segment, float side, float facadeX, float z)
    {
        const float pierDepth = 0.8f;
        const float pierWidth = 0.5f;
        const float opening = 1.4f;
        const float doorHeight = 2.4f;
        float pierX = side * (facadeX - pierDepth * 0.5f);
        for (int end = -1; end <= 1; end += 2)
            Cube(segment, propMaterial, new Vector3(pierX, (doorHeight + 0.35f) * 0.5f, z + end * (opening + pierWidth) * 0.5f),
                new Vector3(pierDepth, doorHeight + 0.35f, pierWidth));
        Cube(segment, propMaterial, new Vector3(pierX, doorHeight + 0.175f, z), new Vector3(pierDepth, 0.35f, opening));
        Cube(segment, lightPanelMaterial, new Vector3(side * (facadeX - 0.02f), doorHeight * 0.5f + pavementHeight, z),
            new Vector3(0.04f, doorHeight - pavementHeight, opening));
        Spot(segment, new Vector3(side * (facadeX - 0.3f), pavementHeight, z - opening * 0.25f));
    }

    private void LayoutBridge(Segment segment, float z, float frontX)
    {
        const float deckDepth = 3f;
        const float deckThickness = 1.3f;
        float y = Random.Range(8f, 13f);
        float span = frontX * 2f + 2f;
        Cube(segment, buildingMaterial, new Vector3(0f, y, z), new Vector3(span, deckThickness, deckDepth));
        float deckTop = y + deckThickness * 0.5f;
        for (int end = -1; end <= 1; end += 2)
            Cube(segment, propMaterial, new Vector3(0f, deckTop + 0.5f, z + end * (deckDepth * 0.5f - 0.08f)), new Vector3(span, 1f, 0.16f));
        Spot(segment, new Vector3(Random.Range(-frontX + 1f, frontX - 1f), deckTop, z + 0.4f));
    }

    // Cars and barriers along the kerb on one side of the road, never inside the player's lane.
    private void LayoutRoadCover(Segment segment, float side, float z0)
    {
        float z = z0 + Random.Range(3f, segmentLength - 3f);
        if (carMesh != null && Random.value < parkedCarChance)
        {
            if (Random.value < abandonedCarShare)
            {
                // Left at an angle, nose to the kerb: seen side-on, so the hood is low cover.
                float angle = Random.Range(25f, 40f);
                float yaw = side * angle + (Random.value < 0.5f ? 0f : 180f);
                float reach = carHalfSize.z * Mathf.Sin(angle * Mathf.Deg2Rad) + carHalfSize.x * Mathf.Cos(angle * Mathf.Deg2Rad);
                float x = side * Mathf.Max(clearLane + reach, roadHalfWidth - reach * 0.6f);
                Car(segment, new Vector3(x, 0f, z), yaw);
                Spot(segment, new Vector3(x + side * 0.4f, 0f, z + carHalfSize.z * 0.55f + 1.1f));
            }
            else
            {
                // Parked along the kerb: seen end-on, the cabin hides a standing enemy, who leans out to the road side.
                float x = side * (roadHalfWidth - 0.2f - carHalfSize.x);
                Car(segment, new Vector3(x, 0f, z), Random.value < 0.5f ? 0f : 180f);
                Spot(segment, new Vector3(x + side * 0.2f, 0f, z + carHalfSize.z + 0.45f));
            }
        }
        else if (Random.value < barrierChance)
        {
            // A concrete block across the shoulder, square to the player: low cover.
            float x = side * (clearLane + 1.2f);
            Cube(segment, propMaterial, new Vector3(x, 0.45f, z), new Vector3(2f, 0.9f, 0.6f));
            Spot(segment, new Vector3(x, 0f, z + 0.75f));
        }
    }

    private void LayoutLamp(Segment segment, float side, float z)
    {
        float poleX = side * (roadHalfWidth + 0.45f);
        float headX = poleX - side * (lampArm - 0.1f);
        Cube(segment, poleMaterial, new Vector3(poleX, lampHeight * 0.5f, z), new Vector3(0.14f, lampHeight, 0.14f));
        Cube(segment, poleMaterial, new Vector3(poleX - side * lampArm * 0.5f, lampHeight, z), new Vector3(lampArm, 0.1f, 0.1f));
        Cube(segment, bulbMaterial, new Vector3(headX, lampHeight - 0.12f, z), new Vector3(0.4f, 0.2f, 0.4f));

        float poolX = headX - side * 0.3f;
        Quad(segment, new Vector3(poolX, 0.012f, z), new Vector2(poolSize.x, poolSize.y));
        // Wet streaks run from the pool towards the approaching player, thinning out.
        for (int i = 0; i < 3; i++)
            Quad(segment, new Vector3(poolX, 0.012f, z - poolSize.y * 0.5f - 1.6f - i * 2.4f),
                new Vector2(0.55f - i * 0.12f, 1.3f + i * 0.5f));
    }

    private void Car(Segment segment, Vector3 groundCentre, float yaw)
    {
        Quaternion rotation = Quaternion.Euler(0f, yaw, 0f);
        // Put the middle of the body on groundCentre and the wheels on the road.
        Vector3 offset = rotation * new Vector3(-carCentre.x, 0f, -carCentre.z);
        offset.y = carHalfSize.y - carCentre.y;
        Place(segment, carMesh, carMaterial, groundCentre + offset, rotation, Vector3.one);
    }

    private void Cube(Segment segment, Material material, Vector3 position, Vector3 scale)
    {
        Cube(segment, material, position, scale, Quaternion.identity);
    }

    private void Cube(Segment segment, Material material, Vector3 position, Vector3 scale, Quaternion rotation)
    {
        Place(segment, cubeMesh, material, position, rotation, scale);
    }

    private void Quad(Segment segment, Vector3 position, Vector2 size)
    {
        Place(segment, quadMesh, lightPoolMaterial, position, Quaternion.Euler(90f, 0f, 0f), new Vector3(size.x, size.y, 1f));
    }

    private void Spot(Segment segment, Vector3 feet)
    {
        EnemySpot spot;
        if (segment.UsedSpots < segment.Spots.Count)
        {
            spot = segment.Spots[segment.UsedSpots];
        }
        else
        {
            spot = new GameObject("Enemy Spot").AddComponent<EnemySpot>();
            spot.transform.SetParent(segment.Root, false);
            segment.Spots.Add(spot);
        }
        segment.UsedSpots++;
        spot.transform.position = feet;
        // Re-enabling re-registers the spot and frees it for a new enemy.
        spot.gameObject.SetActive(false);
        spot.gameObject.SetActive(true);
    }

    private void Place(Segment segment, Mesh mesh, Material material, Vector3 position, Quaternion rotation, Vector3 scale)
    {
        Transform piece;
        if (segment.Used < segment.Pieces.Count)
        {
            piece = segment.Pieces[segment.Used];
            piece.GetComponent<MeshFilter>().sharedMesh = mesh;
            piece.GetComponent<MeshRenderer>().sharedMaterial = material;
        }
        else
        {
            piece = CreatePiece(segment.Root, mesh, material).transform;
            segment.Pieces.Add(piece);
        }
        segment.Used++;
        piece.gameObject.SetActive(true);
        piece.SetPositionAndRotation(position, rotation);
        piece.localScale = scale;
    }

    private static GameObject CreatePiece(Transform parent, Mesh mesh, Material material)
    {
        var go = new GameObject("Piece", typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.SetParent(parent, false);
        go.GetComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = go.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        return go;
    }

    private static Mesh BorrowPrimitiveMesh(PrimitiveType type)
    {
        var primitive = GameObject.CreatePrimitive(type);
        Mesh mesh = primitive.GetComponent<MeshFilter>().sharedMesh;
        Destroy(primitive);
        return mesh;
    }

    private void OnValidate()
    {
        segmentLength = Mathf.Max(1f, segmentLength);
        buildingHeight.y = Mathf.Max(buildingHeight.x, buildingHeight.y);
        buildingDepth.y = Mathf.Max(buildingDepth.x, buildingDepth.y);
        poolSize = Vector2.Max(poolSize, Vector2.zero);
        backlightSize = Vector2.Max(backlightSize, Vector2.zero);
    }
}
