
using UnityEngine;

[DefaultExecutionOrder(-220)]
public class ProceduralBalance : MonoBehaviour {

    [Header("References")]
    [SerializeField] private Rigidbody RightFoot;
    [SerializeField] private Rigidbody LeftFoot;
    [SerializeField] private Rigidbody Hips;

    [SerializeField] private GroundContact RightGroundContact;
    [SerializeField] private GroundContact LeftGroundContact;

    [Header("Support")]
    [SerializeField] private float SupportRadius = 0.12f;

    private Rigidbody[] Bodies;
    private float LeftLength;
    private float RightLength;
    private float LeftHipOffset;
    private float RightHipOffset;
    private float LeftSole;
    private float RightSole;

    public Vector3 CenterOfMass { get; private set; }
    public Vector3 Velocity { get; private set; }
    public float TotalMass { get; private set; }
    public bool HasGroundSupport { get; private set; }
    public bool LeftSupported => LeftGroundContact != null && LeftGroundContact.IsGrounded;
    public bool RightSupported => RightGroundContact != null && RightGroundContact.IsGrounded;
    public Vector3 SupportPoint { get; private set; }
    public Vector3 SupportNormal { get; private set; } = Vector3.up;
    public Vector3 SupportVelocity { get; private set; }
    public Vector3 SupportError { get; private set; }

    private void Awake() {
        Bodies = GetComponentsInChildren<Rigidbody>();
        Animator animator = GetComponent<Animator>();
        if (animator != null && animator.isHuman && Hips != null) {
            MeasureLeg(animator, HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, LeftFoot, out LeftLength, out LeftHipOffset, out LeftSole);
            MeasureLeg(animator, HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, RightFoot, out RightLength, out RightHipOffset, out RightSole);
        }
    }

    private void MeasureLeg(Animator animator, HumanBodyBones upper, HumanBodyBones lower, Rigidbody foot, out float length, out float hipOffset, out float sole) {
        Transform a = animator.GetBoneTransform(upper);
        Transform b = animator.GetBoneTransform(lower);
        length = a != null && b != null && foot != null ? Vector3.Distance(a.position, b.position) + Vector3.Distance(b.position, foot.position) : 0f;
        hipOffset = a != null ? Hips.position.y - a.position.y : 0f;
        Collider shape = foot != null ? foot.GetComponentInChildren<Collider>() : null;
        sole = shape != null ? Mathf.Clamp(foot.position.y - shape.bounds.min.y, 0.03f, 0.18f) : 0.08f;
    }

    public float GetHeightLimit(float halfStride) {
        float left = float.PositiveInfinity;
        float right = float.PositiveInfinity;
        if (LeftSupported && LeftLength > 0f) {
            float reach = LeftLength * 0.98f;
            left = Mathf.Sqrt(Mathf.Max(0f, reach * reach - halfStride * halfStride)) + LeftHipOffset + LeftSole + LeftGroundContact.GroundPoint.y - SupportPoint.y;
        }
        if (RightSupported && RightLength > 0f) {
            float reach = RightLength * 0.98f;
            right = Mathf.Sqrt(Mathf.Max(0f, reach * reach - halfStride * halfStride)) + RightHipOffset + RightSole + RightGroundContact.GroundPoint.y - SupportPoint.y;
        }
        return Mathf.Min(left, right);
    }

    private void FixedUpdate() {
        Refresh();
    }

    public void Refresh() {
        CalculateCenterOfMass();
        HasGroundSupport = LeftSupported || RightSupported;
        SupportError = Vector3.zero;
        SupportVelocity = Vector3.zero;
        if (!HasGroundSupport) {
            return;
        }

        Vector3 a = LeftSupported ? LeftGroundContact.GroundPoint : RightGroundContact.GroundPoint;
        Vector3 b = RightSupported ? RightGroundContact.GroundPoint : a;
        SupportPoint = (a + b) * 0.5f;
        SupportNormal = ((LeftSupported ? LeftGroundContact.GroundNormal : Vector3.zero) + (RightSupported ? RightGroundContact.GroundNormal : Vector3.zero)).normalized;
        SupportVelocity = ((LeftSupported ? LeftGroundContact.GroundVelocity : Vector3.zero) + (RightSupported ? RightGroundContact.GroundVelocity : Vector3.zero)) / (LeftSupported && RightSupported ? 2f : 1f);

        Vector3 segment = Vector3.ProjectOnPlane(b - a, Vector3.up);
        Vector3 projection = Vector3.ProjectOnPlane(CenterOfMass - a, Vector3.up);
        float t = segment.sqrMagnitude > 0.0001f ? Mathf.Clamp01(Vector3.Dot(projection, segment) / segment.sqrMagnitude) : 0f;
        Vector3 outside = projection - segment * t;
        SupportError = outside.magnitude > SupportRadius ? outside.normalized * (outside.magnitude - SupportRadius) : Vector3.zero;
    }

    private void CalculateCenterOfMass() {
        Vector3 weightedPosition = Vector3.zero;
        Vector3 weightedVelocity = Vector3.zero;
        float totalMass = 0f;

        foreach (Rigidbody body in Bodies) {
            if (body == null || body.isKinematic) {
                continue;
            }

            weightedPosition += body.worldCenterOfMass * body.mass;
            weightedVelocity += body.linearVelocity * body.mass;
            totalMass += body.mass;
        }

        TotalMass = totalMass;
        if (totalMass > 0f) {
            CenterOfMass = weightedPosition / totalMass;
            Velocity = weightedVelocity / totalMass;
        }
    }

    public void ResetBalance() {
        LeftGroundContact?.ClearContacts();
        RightGroundContact?.ClearContacts();
        Refresh();
    }

    private void OnDrawGizmos() {
        if (!Game.isDebugGame) {
            return;
        }

        Gizmos.color = HasGroundSupport ? Color.green : Color.red;
        Gizmos.DrawSphere(CenterOfMass, 0.05f);
        if (HasGroundSupport) {
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(SupportPoint, CenterOfMass);
            Gizmos.DrawWireSphere(SupportPoint, SupportRadius);
        }
    }
}
