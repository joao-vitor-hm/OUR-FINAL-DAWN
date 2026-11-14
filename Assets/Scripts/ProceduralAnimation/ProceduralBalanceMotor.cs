
using UnityEngine;

[DefaultExecutionOrder(-120)]
public class ProceduralBalanceMotor : MonoBehaviour {

    [Header("References")]
    [SerializeField] private LayerMask GroundLayer;
    [SerializeField] private Transform Camera;
    [SerializeField] private Rigidbody Hips;

    [Header("Balance")]
    [SerializeField] private float MaxVerticalAcceleration = 100f;
    [SerializeField] private float StandingHeight = 0.95f;
    [SerializeField] private float HeightStrength = 80f;
    [SerializeField] private float HeightDamping = 16f;
    [SerializeField] private float RayDistance = 3f;
    [SerializeField] private float MaxSupportRiseSpeed = 0.7f;
    [SerializeField] private bool AutoStandingHeight = true;
    [Range(0f, 1f)]
    [SerializeField] private float SupportAssistance = 1f;

    [Header("Movement")]
    [SerializeField] private float Acceleration = 8f;
    [SerializeField] private float Speed = 1.5f;
    [SerializeField] private float BrakingStrength = 14f;
    [SerializeField] private float VelocityResponse = 6f;
    [SerializeField] private float ReverseSpeedMultiplier = 0.45f;
    [SerializeField] private float TurnSpeedMultiplier = 0.25f;

    private Vector3 MovementDirection = Vector3.zero;
    private Vector3 GroundNormal = Vector3.up;
    private Vector3 GroundPoint = Vector3.zero;
    private float CalibratedHeight;
    private Player Input;
    private ProceduralBalance Balance;
    private ActiveRagdollMuscle Muscles;
    private ProceduralPosture Posture;
    private float CurrentHeight;
    private Vector3 MovementAcceleration;

    public bool HasGround { get; private set; }
    public bool HasNearbyGround { get; private set; }
    public int GroundMask => GroundLayer.value;
    public float Height => CurrentHeight > 0f ? CurrentHeight : StandingHeight;
    public float PlannedHeight { get; private set; }
    public Vector3 DesiredVelocity { get; private set; }
    public float AssistanceFraction => HasGround ? SupportAssistance : 0f;

    private void Start() {
        Input = GetComponent<Player>();
        Balance = GetComponent<ProceduralBalance>();
        Muscles = GetComponent<ActiveRagdollMuscle>();
        Posture = GetComponent<ProceduralPosture>();

        if (Hips == null && Input != null) {
            Hips = Input.Hips;
        }

        if (GroundLayer.value == 0) {
            GroundLayer = LayerMask.GetMask("Ground");
        }

        CalibratedHeight = StandingHeight;

        if (AutoStandingHeight && Hips != null && Physics.Raycast(Hips.position, Vector3.down, out RaycastHit hit, RayDistance, GroundLayer, QueryTriggerInteraction.Ignore)) {
            CalibratedHeight = Mathf.Max(0.3f, Hips.position.y - hit.point.y - 0.02f);
        }

        CurrentHeight = PlannedHeight = CalibratedHeight;

        if (Hips == null || Balance == null) {
            Debug.LogError("Balance motor requires physical hips and ProceduralBalance.", this);
            enabled = false;
        }
    }

    private void FixedUpdate() {
        if (Hips == null || Hips.isKinematic || Balance == null || Game.isGamePaused) {
            return;
        }

        UpdateGround();

        MovementDirection = Input != null ? Input.GetMovementDirection() : Vector3.zero;

        if (Input == null && Camera != null) {
            Vector3 input = GameControlls.GetMovement();
            MovementDirection = Vector3.ClampMagnitude(Vector3.ProjectOnPlane(Camera.forward, Vector3.up).normalized * input.z + Vector3.ProjectOnPlane(Camera.right, Vector3.up).normalized * input.x, 1f);
        }

        if (!HasGround || (Input != null && !Input.CanControl)) {
            DesiredVelocity = Vector3.zero;
            return;
        }

        UpdateMovement();

        float halfStride = Muscles != null ? DesiredVelocity.magnitude * Muscles.GetStepDuration(DesiredVelocity.magnitude) * 0.5f : 0f;
        PlannedHeight = Mathf.Min(CalibratedHeight, Balance.GetHeightLimit(halfStride));
        CurrentHeight = Mathf.MoveTowards(CurrentHeight, PlannedHeight, 0.4f * Time.fixedDeltaTime);

        UpdateHeight();
    }

    private void UpdateGround() {
        Vector3 origin = Hips.position + Vector3.up * 0.2f;
        HasNearbyGround = Physics.Raycast(origin, Vector3.down, out RaycastHit hit, RayDistance, GroundLayer, QueryTriggerInteraction.Ignore) && Vector3.Angle(hit.normal, Vector3.up) <= GroundContact.MaxGroundAngle;
        HasGround = Balance.HasGroundSupport;

        if (HasGround) {
            GroundPoint = Balance.SupportPoint;
            GroundNormal = Balance.SupportNormal;
        } else if (HasNearbyGround) {
            GroundPoint = hit.point;
            GroundNormal = hit.normal;
        }
    }

    private void UpdateHeight() {
        float currentHeight = Hips.position.y - GroundPoint.y;
        float heightError = Height - currentHeight;
        float velocity = Balance.Velocity.y - Balance.SupportVelocity.y;
        float acceleration = heightError * HeightStrength + (DesiredVelocity.y - velocity) * HeightDamping - Physics.gravity.y;
        float maximum = (MaxSupportRiseSpeed + Mathf.Max(0f, DesiredVelocity.y) - velocity) / Time.fixedDeltaTime - Physics.gravity.y - MovementAcceleration.y;

        maximum = Mathf.Clamp(maximum, -MaxVerticalAcceleration, MaxVerticalAcceleration);
        acceleration = Mathf.Clamp(acceleration, -MaxVerticalAcceleration, maximum);

        Hips.AddForce(Vector3.up * acceleration * Balance.TotalMass * SupportAssistance, ForceMode.Force);
    }

    private void UpdateMovement() {
        Vector3 currentVelocity = Vector3.ProjectOnPlane(Balance.Velocity - Balance.SupportVelocity, GroundNormal);
        Vector3 targetVelocity = Vector3.ProjectOnPlane(MovementDirection, GroundNormal).normalized * MovementDirection.magnitude * (Input != null ? Input.MoveSpeed : Speed);
        
        if (Posture != null && targetVelocity.sqrMagnitude > 0.01f) {
            float angle = Vector3.Angle(Posture.Forward, targetVelocity);
            targetVelocity *= Mathf.Lerp(TurnSpeedMultiplier, 1f, Mathf.InverseLerp(120f, 30f, angle));
        }

        if (Vector3.Dot(Balance.SupportError, targetVelocity) > 0f) {
            targetVelocity *= Mathf.Lerp(1f, 0.75f, Mathf.InverseLerp(0.25f, 0.5f, Balance.SupportError.magnitude));
        }

        if (currentVelocity.sqrMagnitude > 0.25f && Vector3.Dot(currentVelocity.normalized, targetVelocity.normalized) < 0f) {
            targetVelocity *= ReverseSpeedMultiplier;
        }

        DesiredVelocity = targetVelocity;

        Vector3 difference = targetVelocity - currentVelocity;

        float response = MovementDirection.sqrMagnitude < 0.01f ? BrakingStrength : VelocityResponse;

        Vector3 acceleration = Vector3.ClampMagnitude(difference * response, Input != null ? Input.MoveAcceleration : Acceleration);

        MovementAcceleration = acceleration;
        Hips.AddForce(acceleration * Balance.TotalMass, ForceMode.Force);
    }

    public void ResetMotor() {
        MovementDirection = Vector3.zero;
        HasGround = false;
        HasNearbyGround = false;
        DesiredVelocity = Vector3.zero;
        MovementAcceleration = Vector3.zero;
        CurrentHeight = PlannedHeight = CalibratedHeight;
    }

    private void OnDrawGizmos() {
        if (!Game.isDebugGame || !HasGround || Hips == null) {
            return;
        }

        Gizmos.color = Color.green;
        Gizmos.DrawSphere(GroundPoint, 0.05f);
        Gizmos.DrawLine(GroundPoint, GroundPoint + Vector3.up * Height);
    }
}
