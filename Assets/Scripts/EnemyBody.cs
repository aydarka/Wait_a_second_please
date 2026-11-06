using System;
using UnityEngine;

// Lower-body posing for EnemyGunman: crouching behind low cover and side-stepping out from an edge.
// The feet stay planted through two-bone IK, so the enemy bends its knees instead of sinking and
// lifts its feet instead of sliding. Pure bone math in game time, no animation clips.
[Serializable]
public sealed class EnemyBody
{
    [SerializeField] private Transform pelvis;
    [SerializeField, Tooltip("Lower spine bone that bends the torso forward in a crouch.")]
    private Transform lowerSpine;
    [SerializeField] private Transform neck;
    [SerializeField] private Transform leftThigh;
    [SerializeField] private Transform leftShin;
    [SerializeField] private Transform leftFoot;
    [SerializeField] private Transform rightThigh;
    [SerializeField] private Transform rightShin;
    [SerializeField] private Transform rightFoot;
    [SerializeField, Tooltip("Top bones of the coat tails, folded over the knees in a crouch.")]
    private Transform[] coatTails = Array.Empty<Transform>();

    [Header("Crouch")]
    [SerializeField, Min(0f), Tooltip("How far the hips drop, metres.")] private float crouchDrop = 0.65f;
    [SerializeField, Min(0f), Tooltip("How far the hips move back, metres.")] private float crouchSitBack = 0.15f;
    [SerializeField, Tooltip("Forward bend of the torso, degrees.")] private float crouchTorsoLean = 52f;
    [SerializeField, Tooltip("Head raised back against the torso bend, degrees.")] private float crouchHeadLift = 10f;
    [SerializeField, Tooltip("Coat tails swung forward over the knees, degrees.")] private float crouchCoatFold = 55f;

    [Header("Side step")]
    [SerializeField, Min(0.1f), Tooltip("Longest single side step, metres.")] private float maxStride = 0.8f;
    [SerializeField, Min(0f), Tooltip("Foot lift at the middle of a step, metres.")] private float stepLift = 0.12f;
    [SerializeField, Tooltip("Body lean into the step, degrees.")] private float stepRoll = 6f;

    private Vector3 pelvisPosition;
    private Quaternion pelvisRotation;
    private Quaternion lowerSpineRotation;
    private Quaternion neckRotation;
    private Quaternion[] legRotations;
    private Quaternion[] coatRotations;
    private Leg left;
    private Leg right;

    private struct Leg
    {
        public Transform Thigh;
        public Transform Shin;
        public Transform Foot;
        // Rest pose relative to the enemy root.
        public Vector3 FootPosition;
        public Quaternion FootRotation;
        public Vector3 KneeDirection;
    }

    public bool IsValid => pelvis != null && lowerSpine != null && neck != null &&
        leftThigh != null && leftShin != null && leftFoot != null &&
        rightThigh != null && rightShin != null && rightFoot != null;

    // Call once with the prefab's standing pose.
    public void Capture(Transform root)
    {
        pelvisPosition = pelvis.localPosition;
        pelvisRotation = pelvis.localRotation;
        lowerSpineRotation = lowerSpine.localRotation;
        neckRotation = neck.localRotation;
        legRotations = new[]
        {
            leftThigh.localRotation, leftShin.localRotation, leftFoot.localRotation,
            rightThigh.localRotation, rightShin.localRotation, rightFoot.localRotation,
        };
        coatRotations = new Quaternion[coatTails.Length];
        for (int i = 0; i < coatTails.Length; i++)
            if (coatTails[i] != null) coatRotations[i] = coatTails[i].localRotation;
        left = CaptureLeg(root, leftThigh, leftShin, leftFoot);
        right = CaptureLeg(root, rightThigh, rightShin, rightFoot);
    }

    private static Leg CaptureLeg(Transform root, Transform thigh, Transform shin, Transform foot)
    {
        Vector3 bend = shin.position - (thigh.position + foot.position) * 0.5f;
        Vector3 knee = root.InverseTransformDirection(bend);
        // Knees bend forward; a straight rest leg gives no direction of its own.
        knee.y = 0f;
        if (knee.z < 0.05f) knee = new Vector3(knee.x, 0f, 0.05f);
        return new Leg
        {
            Thigh = thigh,
            Shin = shin,
            Foot = foot,
            FootPosition = root.InverseTransformPoint(foot.position),
            FootRotation = Quaternion.Inverse(root.rotation) * foot.rotation,
            KneeDirection = knee.normalized,
        };
    }

    public void ResetPose()
    {
        pelvis.localPosition = pelvisPosition;
        pelvis.localRotation = pelvisRotation;
        lowerSpine.localRotation = lowerSpineRotation;
        neck.localRotation = neckRotation;
        leftThigh.localRotation = legRotations[0];
        leftShin.localRotation = legRotations[1];
        leftFoot.localRotation = legRotations[2];
        rightThigh.localRotation = legRotations[3];
        rightShin.localRotation = legRotations[4];
        rightFoot.localRotation = legRotations[5];
        for (int i = 0; i < coatTails.Length; i++)
            if (coatTails[i] != null) coatTails[i].localRotation = coatRotations[i];
    }

    public int StepCount(float distance)
    {
        return Mathf.Max(1, Mathf.CeilToInt(distance / maxStride - 0.001f));
    }

    // Weight 0 stands, 1 squats with the torso bent over the knees.
    public void ApplyCrouch(Transform root, float weight)
    {
        if (weight <= 0f) return;
        Vector3 across = root.right;
        pelvis.position += (-Vector3.up * crouchDrop - root.forward * crouchSitBack) * weight;
        // Positive turns about the right axis tip an upright bone forward and swing a hanging one back.
        lowerSpine.rotation = Quaternion.AngleAxis(crouchTorsoLean * weight, across) * lowerSpine.rotation;
        neck.rotation = Quaternion.AngleAxis(-crouchHeadLift * weight, across) * neck.rotation;
        Quaternion fold = Quaternion.AngleAxis(-crouchCoatFold * weight, across);
        foreach (var tail in coatTails)
            if (tail != null) tail.rotation = fold * tail.rotation;
    }

    // Leans the body towards the side it is stepping to, strongest mid-way.
    public void ApplyStepLean(Transform root, Vector3 stepVector, float progress)
    {
        if (progress <= 0f || progress >= 1f || stepVector.sqrMagnitude < 0.0001f) return;
        float side = Vector3.Dot(stepVector, root.right) >= 0f ? 1f : -1f;
        // A positive turn about the forward axis tips the top to the left.
        float angle = -side * stepRoll * Mathf.Sin(progress * Mathf.PI);
        lowerSpine.rotation = Quaternion.AngleAxis(angle, root.forward) * lowerSpine.rotation;
    }

    // Share of the side step the body has covered: it follows the average of the two feet.
    public float BodyProgress(float progress, int steps)
    {
        return (FootTravel(progress, steps, true, out _) + FootTravel(progress, steps, false, out _)) * 0.5f;
    }

    // Plants both feet: at their rest spots around the root, or along a side step from anchor towards anchor + stepVector.
    public void PlantFeet(Transform root, Vector3 anchor, Vector3 stepVector, float progress, int steps)
    {
        bool leftLeads = Vector3.Dot(root.rotation * (left.FootPosition - right.FootPosition), stepVector) >= 0f;
        Vector3 leftTarget = FootTarget(root, left, anchor, stepVector, progress, steps, leftLeads);
        Vector3 rightTarget = FootTarget(root, right, anchor, stepVector, progress, steps, !leftLeads);
        // A wide stance is out of reach of straight legs: the hips sink just enough for both feet
        // to stay where they stand instead of being dragged towards the body.
        float sink = Mathf.Max(SinkToReach(left, leftTarget), SinkToReach(right, rightTarget));
        if (sink > 0f) pelvis.position -= Vector3.up * sink;
        SolveFoot(root, left, leftTarget);
        SolveFoot(root, right, rightTarget);
    }

    private Vector3 FootTarget(Transform root, Leg leg, Vector3 anchor, Vector3 stepVector, float progress, int steps, bool leads)
    {
        float travel = FootTravel(progress, steps, leads, out float lift);
        return anchor + root.rotation * leg.FootPosition + stepVector * travel + Vector3.up * (lift * stepLift);
    }

    private static float SinkToReach(Leg leg, Vector3 target)
    {
        Vector3 hip = leg.Thigh.position;
        float reach = (Vector3.Distance(hip, leg.Shin.position) + Vector3.Distance(leg.Shin.position, leg.Foot.position)) * 0.97f;
        Vector3 toTarget = target - hip;
        float across = new Vector2(toTarget.x, toTarget.z).magnitude;
        if (across >= reach) return 0f;
        float drop = -toTarget.y;
        return Mathf.Max(0f, drop - Mathf.Sqrt(reach * reach - across * across));
    }

    private static void SolveFoot(Transform root, Leg leg, Vector3 target)
    {
        SolveLeg(leg.Thigh, leg.Shin, leg.Foot, target, root.TransformDirection(leg.KneeDirection));
        // Feet stay flat on the ground whatever the knees do.
        leg.Foot.rotation = root.rotation * leg.FootRotation;
    }

    // A side step alternates: the leading foot moves out, the other closes up, repeated per step.
    private static float FootTravel(float progress, int steps, bool leads, out float lift)
    {
        int moves = steps * 2;
        float phase = Mathf.Clamp01(progress) * moves;
        float travel = 0f;
        lift = 0f;
        for (int i = leads ? 0 : 1; i < moves; i += 2)
        {
            float local = Mathf.Clamp01(phase - i);
            travel += local * local * (3f - 2f * local);
            if (local > 0f && local < 1f) lift = Mathf.Sin(local * Mathf.PI);
        }
        return travel / steps;
    }

    // Two-bone IK: the knee goes towards the pole, the ankle reaches the target (or as close as the leg allows).
    private static void SolveLeg(Transform thigh, Transform shin, Transform foot, Vector3 target, Vector3 pole)
    {
        Vector3 hip = thigh.position;
        float upper = Vector3.Distance(hip, shin.position);
        float lower = Vector3.Distance(shin.position, foot.position);
        Vector3 toTarget = target - hip;
        float reach = Mathf.Clamp(toTarget.magnitude, Mathf.Abs(upper - lower) + 0.001f, upper + lower - 0.001f);
        Vector3 along = toTarget.sqrMagnitude > 1e-8f ? toTarget.normalized : Vector3.down;
        Vector3 bend = pole - Vector3.Dot(pole, along) * along;
        if (bend.sqrMagnitude < 1e-8f) bend = Vector3.Cross(along, Vector3.right);
        bend.Normalize();
        float alongKnee = (upper * upper - lower * lower + reach * reach) / (2f * reach);
        float outKnee = Mathf.Sqrt(Mathf.Max(0f, upper * upper - alongKnee * alongKnee));
        Vector3 knee = hip + along * alongKnee + bend * outKnee;
        thigh.rotation = Quaternion.FromToRotation(shin.position - hip, knee - hip) * thigh.rotation;
        Vector3 kneeNow = shin.position;
        shin.rotation = Quaternion.FromToRotation(foot.position - kneeNow, hip + along * reach - kneeNow) * shin.rotation;
    }
}
