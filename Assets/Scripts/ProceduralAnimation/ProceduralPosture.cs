using UnityEngine;

[DefaultExecutionOrder(-90)]
public class ProceduralPosture : MonoBehaviour {

    [Header("References")]
    [SerializeField] private Rigidbody Hips;

    [Header("Posture")]
    [SerializeField] private float Strength = 80f;
    [SerializeField] private float Damping = 12f;
    [SerializeField] private float TurnSpeed = 300f;
    [SerializeField] private float TurnAcceleration = 900f;
    [SerializeField] private float MaximumTorque = 400f;
    [SerializeField] private float MaximumBalanceLean = 8f;

    private Quaternion InitialRotation;
    private Quaternion InitialHeading;

    private Player Input;
    private float HeadingVelocity;
    private ProceduralBalance Balance;
    private float TiltInertia;
    private float YawInertia;
    private ActiveRagdollMuscle Muscles;
    private Quaternion TargetHeading;

    public Quaternion Heading { get; private set; }
    public Quaternion DesiredRotation { get; private set; }
    public Vector3 Forward => Hips != null ? Vector3.ProjectOnPlane(Hips.rotation * Quaternion.Inverse(InitialRotation) * InitialHeading * Vector3.forward, Vector3.up).normalized : Heading * Vector3.forward;
    public float TiltAngle => Hips != null ? Vector3.Angle((Hips.rotation * Quaternion.Inverse(InitialRotation)) * Vector3.up, Vector3.up) : 0f;

    private void Start() {
        if (Hips == null) {
            enabled = false;
            return;
        }

        Input = GetComponent<Player>();
        Balance = GetComponent<ProceduralBalance>();
        Muscles = GetComponent<ActiveRagdollMuscle>();

        InitialRotation = Hips.rotation;
        InitialHeading = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);

        foreach (Rigidbody body in GetComponentsInChildren<Rigidbody>()) {
            Vector3 offset = body.worldCenterOfMass - Hips.worldCenterOfMass;
            float ownInertia = (body.inertiaTensor.x + body.inertiaTensor.y + body.inertiaTensor.z) / 3f;

            TiltInertia += body.mass * offset.y * offset.y + ownInertia;
            YawInertia += body.mass * (offset.x * offset.x + offset.z * offset.z) + ownInertia;
        }

        ResetHeading();
    }

    public void ResetHeading() {
        Heading = InitialHeading;
        HeadingVelocity = 0f;
        DesiredRotation = InitialRotation;
        TargetHeading = InitialHeading;
    }

    private void FixedUpdate() {
        if (Hips == null || Hips.isKinematic || Game.isGamePaused) {
            return;
        }

        Vector3 direction = Input != null ? Input.GetMovementDirection() : Vector3.zero;

        if (direction.sqrMagnitude > 0.01f) {
            TargetHeading = Quaternion.LookRotation(direction, Vector3.up);

            float headingAngle = Vector3.SignedAngle(Heading * Vector3.forward, direction, Vector3.up);
            float targetVelocity = Mathf.Clamp(headingAngle / Time.fixedDeltaTime, -TurnSpeed, TurnSpeed);

            HeadingVelocity = Mathf.MoveTowards(HeadingVelocity, targetVelocity, TurnAcceleration * Time.fixedDeltaTime);

            float step = Mathf.Clamp(HeadingVelocity * Time.fixedDeltaTime, -Mathf.Abs(headingAngle), Mathf.Abs(headingAngle));

            Quaternion proposed = Quaternion.AngleAxis(step, Vector3.up) * Heading;

            if (Muscles == null || Muscles.CanTurnTo(proposed)) {
                Heading = proposed;
            } else {
                HeadingVelocity = 0f;
            }
        } else {
            HeadingVelocity = 0f;
        }

        Quaternion lean = Quaternion.identity;

        if (Balance != null && Balance.HasGroundSupport) {
            Vector3 correction = Vector3.ClampMagnitude(-Balance.SupportError, Mathf.Tan(MaximumBalanceLean * Mathf.Deg2Rad));
            lean = Quaternion.FromToRotation(Vector3.up, (Vector3.up + correction).normalized);
        }

        DesiredRotation = lean * Heading * Quaternion.Inverse(InitialHeading) * InitialRotation;

        if (Balance == null || !Balance.HasGroundSupport || (Input != null && !Input.CanControl)) {
            return;
        }

        Quaternion rotationError = DesiredRotation * Quaternion.Inverse(Hips.rotation);
        rotationError.ToAngleAxis(out float angle, out Vector3 axis);

        if (angle > 180f) {
            angle -= 360f;
        }

        Vector3 torque = -Hips.angularVelocity * Damping;

        if (axis.sqrMagnitude > 0.0001f && Mathf.Abs(angle) > 0.01f) {
            torque += axis.normalized * (angle * Mathf.Deg2Rad * Strength);
        }

        Vector3 yaw = Vector3.Project(torque, Vector3.up) * YawInertia;
        Vector3 tilt = Vector3.ProjectOnPlane(torque, Vector3.up) * TiltInertia;

        Hips.AddTorque(Vector3.ClampMagnitude(yaw + tilt, MaximumTorque), ForceMode.Force);
    }

    private void OnDrawGizmos() {
        if (!Game.isDebugGame || Hips == null) {
            return;
        }

        Gizmos.color = Color.cyan;
        Gizmos.DrawRay(Hips.worldCenterOfMass, Hips.transform.up * 0.5f);
    }

    public Quaternion GetPredictedHeading(float seconds) {
        float angle = Vector3.SignedAngle(Heading * Vector3.forward, TargetHeading * Vector3.forward, Vector3.up);
        float speed = Mathf.MoveTowards(HeadingVelocity, Mathf.Sign(angle) * TurnSpeed, TurnAcceleration * seconds);
        float step = Mathf.Clamp((HeadingVelocity + speed) * 0.5f * seconds, -Mathf.Abs(angle), Mathf.Abs(angle));

        return Quaternion.AngleAxis(step, Vector3.up) * Heading;
    }
}
