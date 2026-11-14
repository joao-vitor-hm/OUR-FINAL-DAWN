using UnityEngine;
using UnityEngine.InputSystem;

[DefaultExecutionOrder(-200)]
public class Player : MonoBehaviour {

	[SerializeField] private Transform m_Camera;
	[SerializeField] private Rigidbody m_Hips;

    [SerializeField] private float SprintMultiplier = 1.5f;
    [SerializeField] private float Acceleration = 10f;
	[SerializeField] private float Speed = 3f;
	[SerializeField] private float WalkSpeedMultiplier = 0.6f;
	[SerializeField] private float SpeedChangeRate = 5f;
	[SerializeField] private bool IgnoreSelfCollisions = true;
	[SerializeField] private float FallingAngle = 65f;
	[SerializeField] private float FallingDelay = 0.35f;

	private Vector3 MovementDirection;
	private Vector3? DebugMovement;
    private Vector3 SpawnHips;

    private Rigidbody[] Bodies;

	private Vector3[] SpawnPositions;

	private Quaternion[] SpawnRotations;

	private ProceduralBalanceMotor Motor;
	private ProceduralBalance Balance;
	private ProceduralPosture Posture;
	private ActiveRagdollMuscle Muscles;
	private Camera CameraControl;
	private float UnstableTime;
	private float RecoverUntil;

	public enum MovementState { Idle, Moving, Airborne, Falling, Recovering }
	public MovementState State { get; private set; } = MovementState.Recovering;
	public string RecoveryReason { get; private set; } = "Spawn";
	public bool CanControl => State != MovementState.Falling;

	public Rigidbody Hips => m_Hips;

	public float MoveAcceleration => Acceleration;
	public float MoveSpeed { get; private set; }

	private void Awake() {
		Motor = GetComponent<ProceduralBalanceMotor>();
		Balance = GetComponent<ProceduralBalance>();
		Posture = GetComponent<ProceduralPosture>();
		Muscles = GetComponent<ActiveRagdollMuscle>();
		CameraControl = m_Camera != null ? m_Camera.GetComponent<Camera>() : null;
		RecoverUntil = Time.fixedTime + 0.25f;

		if (m_Hips == null || Motor == null || Balance == null || Posture == null || Muscles == null) {
			Debug.LogError("Player requires physical hips and ProceduralBalanceMotor.", this);
			enabled = false;
			return;
		}

		Bodies = GetComponentsInChildren<Rigidbody>();

		SpawnPositions = new Vector3[Bodies.Length];
		SpawnRotations = new Quaternion[Bodies.Length];
		SpawnHips = m_Hips.position;

		for (int i = 0; i < Bodies.Length; i++) {
			SpawnPositions[i] = Bodies[i].position;
			SpawnRotations[i] = Bodies[i].rotation;

			Bodies[i].interpolation = RigidbodyInterpolation.Interpolate;
			Bodies[i].collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
			Bodies[i].solverIterations = 12;
			Bodies[i].solverVelocityIterations = 4;
			Bodies[i].maxAngularVelocity = 15f;
		}

		Collider[] shapes = GetComponentsInChildren<Collider>();

		for (int i = 0; IgnoreSelfCollisions && i < shapes.Length; i++) {
			for (int j = i + 1; j < shapes.Length; j++) {
				Physics.IgnoreCollision(shapes[i], shapes[j]);
			}
		}

		MoveSpeed = Speed * WalkSpeedMultiplier;
	}

	private void Update() {
		if (Game.isGamePaused) {
			MovementDirection = Vector3.zero;
			return;
		}

		Keyboard keyboard = Keyboard.current;
		float targetSpeed = Speed * WalkSpeedMultiplier * (keyboard != null && keyboard[GameControlls.Sprint].isPressed ? SprintMultiplier : 1f);
		MoveSpeed = Mathf.MoveTowards(MoveSpeed, targetSpeed, SpeedChangeRate * Time.deltaTime);

		Vector3 forward = m_Camera != null ? Vector3.ProjectOnPlane(m_Camera.forward, Vector3.up).normalized : Vector3.forward;

		if (forward.sqrMagnitude < 0.01f) {
			forward = Vector3.forward;
		}

		Vector3 input = GameControlls.GetMovement();

		MovementDirection = Vector3.ClampMagnitude(DebugMovement ?? forward * input.z + Vector3.Cross(Vector3.up, forward) * input.x, 1f);

		if ((keyboard != null && keyboard[GameControlls.Recover].wasPressedThisFrame) || m_Hips.position.y < SpawnHips.y - 15f) {
			Respawn();
		}
	}

	public Vector3 GetMovementDirection() {
		return MovementDirection;
	}

	private void FixedUpdate() {
		if (Game.isGamePaused || m_Hips == null || Balance == null || Posture == null) {
			return;
		}

		float position = m_Hips.position.sqrMagnitude;

		if (float.IsNaN(position) || float.IsInfinity(position)) {
			Respawn();
			RecoveryReason = "Invalid physics state";
			return;
		}

		if (State == MovementState.Falling || Time.fixedTime < RecoverUntil) {
			return;
		}

		UnstableTime = Posture.TiltAngle > FallingAngle ? UnstableTime + Time.fixedDeltaTime : 0f;

		if (UnstableTime > FallingDelay || m_Hips.linearVelocity.y < -8f) {
			State = MovementState.Falling;
			RecoveryReason = $"Fall: tilt {Posture.TiltAngle:F1}, vy {m_Hips.linearVelocity.y:F1} - press R";
			return;
		}

		State = !Balance.HasGroundSupport ? MovementState.Airborne : MovementDirection.sqrMagnitude > 0.01f ? MovementState.Moving : MovementState.Idle;
	}

	public void SetDebugMovement(Vector3? direction) {
		DebugMovement = direction;
	}

	public void Respawn() {
		if (Bodies == null || SpawnPositions == null) {
			return;
		}

		for (int i = 0; i < Bodies.Length; i++) {
			Bodies[i].position = SpawnPositions[i];
			Bodies[i].rotation = SpawnRotations[i];

			Bodies[i].linearVelocity = Vector3.zero;
			Bodies[i].angularVelocity = Vector3.zero;
			Bodies[i].WakeUp();
		}

		MovementDirection = Vector3.zero;
		State = MovementState.Recovering;
		RecoveryReason = "Manual recovery";
		RecoverUntil = Time.fixedTime + 0.25f;
		UnstableTime = 0f;

		Physics.SyncTransforms();

		Balance?.ResetBalance();
		Motor?.ResetMotor();
		Posture?.ResetHeading();
		Muscles?.ResetGait();
		CameraControl?.ResetFollow();
	}

	private void OnGUI() {
		if (!Game.isDebugGame || m_Hips == null) {
			return;
		}

		GUI.Box(new Rect(12, 12, 420, 145), "OUR FINAL DAWN - Physics V1");
		GUI.Label(new Rect(24, 38, 400, 115), $"WASD mover | Shift correr | Mouse olhar\nEsc pausa | V cursor | L debug | R recuperar\nEstado: {State} | Velocidade: {Vector3.ProjectOnPlane(m_Hips.linearVelocity, Vector3.up).magnitude:F2} m/s\nApoio E/D: {Balance.LeftSupported}/{Balance.RightSupported} | COM: {Balance.SupportError.magnitude:F2} m\nPes: {Muscles.LeftState}/{Muscles.RightState} | Erro: {Muscles.FootTargetError:F2} m\nRecuperacao: {RecoveryReason}");
	}
}
