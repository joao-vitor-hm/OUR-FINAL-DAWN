
using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(-50)]
public class ActiveRagdollMuscle : MonoBehaviour {

	[SerializeField] private float FootPositionStrength = 120f;
	[SerializeField] private float FootPositionDamping = 22f;
	[SerializeField] private float MaxFootAcceleration = 60f;
    [SerializeField] private float PredictionTime = 0.12f;
    [SerializeField] private float StepThreshold = 0.20f;
    [SerializeField] private float StepDuration = 0.28f;
    [SerializeField] private float StepOverlap = 0.65f;
    [SerializeField] private float StepHeight = 0.12f;
    [SerializeField] private float Rigidity = 2f;
    [SerializeField] private float Strength = 100f;
    [SerializeField] private float MaxForce = 200f;
    [SerializeField] private float Damping = 15f;

    [SerializeField] private bool ConfigureAnatomicalLimits = true;

	[Range(0f, 1f)]
	[SerializeField] private float GravityCompensation = 1f;
	[SerializeField] private float MaximumSupportTwist = 40f;
    [SerializeField] private float FallenMuscleWeight = 0.08f;

    private class Muscle {
		public ConfigurableJoint Joint;
        public Quaternion Space;
        public Quaternion Rest;
		public Quaternion RelativeRest;
		public Quaternion DesiredRotation;
		public Rigidbody Body;
		public Rigidbody[] SupportedBodies;
		public JointDrive Drive;
	}

	private class Leg {
		public Transform Upper;
		public Transform Lower;
		public Transform Foot;

		public Rigidbody FootBody;

        public Quaternion TargetRotation;
        public Quaternion FromRotation;
        public Quaternion UpperRest;
		public Quaternion LowerRest;
        public Quaternion Rotation;
        public Quaternion FootRest;

		public float UpperLength;
		public float LowerLength;
        public float Progress = 1f;
        public float Spacing;
		public float Sole;
		public float Duration;

		public Vector3 BendDirection;
        public Vector3 UpperAxis;
        public Vector3 LowerAxis;
        public Vector3 Position;
        public Vector3 Velocity;
        public Vector3 Target;
        public Vector3 Normal = Vector3.up;
        public Vector3 From;

        public bool Anchored;

	}

	private readonly Dictionary<Transform, Muscle> Muscles = new Dictionary<Transform, Muscle>();

	private readonly Leg[] Legs = new Leg[2];

	private readonly Transform[][] Arms = new Transform[2][];

	private readonly Vector3[] ArmDirections = new Vector3[2];

	private Player Input;

    private ProceduralBalanceMotor Motor;
    private ProceduralPosture Posture;
    private ProceduralBalance Balance;
    private Rigidbody Hips;

    private Quaternion InitialHeading;

	private int NextFoot;

    private float MuscleWeight = 1f;

    private bool Ready;

	public enum FootState { Airborne, Planted, Lifting, Swinging, Landing }
	public FootState LeftState => GetFootState(0);
	public FootState RightState => GetFootState(1);

    public int CompletedSteps { get; private set; }

    public float FootTargetError => Ready && Legs[0] != null && Legs[1] != null ? Mathf.Max(Vector3.Distance(Legs[0].Foot.position, Legs[0].Position), Vector3.Distance(Legs[1].Foot.position, Legs[1].Position)) : 0f;

	private void Start() {
		Input = GetComponent<Player>();

		Posture = GetComponent<ProceduralPosture>();
		Motor = GetComponent<ProceduralBalanceMotor>();
		Balance = GetComponent<ProceduralBalance>();

		Animator animator = GetComponent<Animator>();

		if (Input == null || Input.Hips == null || Posture == null || Motor == null || animator == null || !animator.isHuman) {
			Debug.LogError("Active ragdoll requires a humanoid avatar, hips, posture and balance motor.", this);
			enabled = false;
			return;
		}

		InitialHeading = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
		Hips = Input.Hips;

		animator.enabled = false;
		Transform spine = animator.GetBoneTransform(HumanBodyBones.Spine);

		foreach (ConfigurableJoint joint in GetComponentsInChildren<ConfigurableJoint>()) {
			ConfigureLimits(joint, animator);

			Vector3 forward = Vector3.Cross(joint.axis, joint.secondaryAxis).normalized;
			Quaternion space = Quaternion.LookRotation(forward, Vector3.Cross(forward, joint.axis).normalized);

			Muscles[joint.transform] = new Muscle {
				Joint = joint,
				Rest = joint.transform.rotation,
				RelativeRest = joint.connectedBody != null ? Quaternion.Inverse(joint.connectedBody.rotation) * joint.transform.rotation : joint.transform.rotation,
				Body = joint.GetComponent<Rigidbody>(),
				SupportedBodies = spine != null && (joint.transform == spine || joint.transform.IsChildOf(spine)) ? joint.GetComponentsInChildren<Rigidbody>() : null,
				Space = space
			};

			joint.configuredInWorldSpace = false;
			joint.rotationDriveMode = RotationDriveMode.Slerp;

			float rigidity = Mathf.Max(0.1f, Rigidity);

			joint.slerpDrive = new JointDrive { positionSpring = Strength * 6f * rigidity, positionDamper = Damping * 3f * Mathf.Sqrt(rigidity), maximumForce = MaxForce * 4f * rigidity };
			Muscles[joint.transform].Drive = joint.slerpDrive;

			joint.targetAngularVelocity = Vector3.zero;
			joint.projectionMode = JointProjectionMode.PositionAndRotation;
			joint.projectionDistance = 0.05f;
			joint.projectionAngle = 15f;
		}

		Legs[0] = CreateLeg(animator, HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot);
		Legs[1] = CreateLeg(animator, HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot);

		Arms[0] = new[] {
			animator.GetBoneTransform(HumanBodyBones.LeftUpperArm),
			animator.GetBoneTransform(HumanBodyBones.LeftLowerArm),
			animator.GetBoneTransform(HumanBodyBones.LeftHand)
		};

		Arms[1] = new[] {
			animator.GetBoneTransform(HumanBodyBones.RightUpperArm),
			animator.GetBoneTransform(HumanBodyBones.RightLowerArm),
			animator.GetBoneTransform(HumanBodyBones.RightHand)
		};
		
		for (int i = 0; i < 2; i++) {
			if (Arms[i][0] != null && Arms[i][1] != null) {
				ArmDirections[i] = Arms[i][1].position - Arms[i][0].position;
			}
		}
		
		Ready = Legs[0] != null && Legs[1] != null;

		if (!Ready) {
			Debug.LogError("Physical leg chain is incomplete.", this);
		}

		ResetGait();
	}

	private Leg CreateLeg(Animator animator, HumanBodyBones upper, HumanBodyBones lower, HumanBodyBones foot) {
		Transform a = animator.GetBoneTransform(upper);
		Transform b = animator.GetBoneTransform(lower);
		Transform c = animator.GetBoneTransform(foot);

		if (a == null || b == null || c == null || !Muscles.ContainsKey(a) || !Muscles.ContainsKey(b) || !Muscles.ContainsKey(c)) {
			return null;
		}

		Leg leg = new Leg {
			Upper = a,
			Lower = b,
			Foot = c,
			FootBody = c.GetComponent<Rigidbody>(),
			
			UpperRest = a.rotation,
			LowerRest = b.rotation,
			FootRest = c.rotation
		};

		leg.UpperAxis = b.position - a.position;
		leg.LowerAxis = c.position - b.position;

		leg.UpperLength = leg.UpperAxis.magnitude;
		leg.LowerLength = leg.LowerAxis.magnitude;

		Vector3 axis = (c.position - a.position).normalized;
		Vector3 bend = Vector3.ProjectOnPlane(b.position - a.position, axis);
		Vector3 anterior = Vector3.ProjectOnPlane(InitialHeading * Vector3.forward, axis).normalized;

		leg.BendDirection = Quaternion.Inverse(InitialHeading) * (bend.sqrMagnitude > 0.00001f && Vector3.Dot(bend, anterior) > 0f ? bend.normalized : anterior);
		leg.Spacing = (Quaternion.Inverse(InitialHeading) * (c.position - Input.Hips.position)).x;
		leg.Sole = 0.08f;

		Collider shape = c.GetComponentInChildren<Collider>();

		if (shape != null) {
			leg.Sole = Mathf.Clamp(c.position.y - shape.bounds.min.y, 0.03f, 0.18f);
		}

		return leg;
	}

	public void ResetGait() {
		if (!Ready || Legs[0] == null || Legs[1] == null) {
			return;
		}

		NextFoot = 0;
		MuscleWeight = 1f;

		foreach (Leg leg in Legs) {
			leg.Position = leg.From = leg.Target = leg.Foot.position;
			leg.Progress = 1f;
			leg.Normal = Vector3.up;
			leg.Rotation = leg.FromRotation = leg.TargetRotation = leg.Foot.rotation;
			leg.Velocity = Vector3.zero;
			leg.Anchored = false;
		}

		foreach (Muscle muscle in Muscles.Values) {
			muscle.DesiredRotation = muscle.Rest;
			muscle.Joint.targetRotation = Quaternion.identity;
			muscle.Joint.targetAngularVelocity = Vector3.zero;
			muscle.Joint.slerpDrive = muscle.Drive;
		}
	}

	private void FixedUpdate() {
		if (!Ready || Game.isGamePaused || Legs[0] == null || Legs[1] == null || Input == null || Posture == null || Motor == null) {
			return;
		}

		Quaternion delta = Posture.Heading * Quaternion.Inverse(InitialHeading);

		foreach (Muscle muscle in Muscles.Values) {
			SetWorldRotation(muscle, delta * muscle.Rest);
		}

		for (int i = 0; i < 2; i++) {
			if (ArmDirections[i].sqrMagnitude < 0.001f) {
				continue;
			}

			float swing = Vector3.Dot(Legs[1 - i].Position - Input.Hips.position, Posture.Heading * Vector3.forward);

			Vector3 relaxed = Posture.Heading * new Vector3(i == 0 ? -0.2f : 0.2f, -1f, 0.1f + Mathf.Clamp(swing, -0.3f, 0.3f));
			Quaternion lower = Quaternion.FromToRotation(delta * ArmDirections[i], relaxed);

			foreach (Transform bone in Arms[i]) {
				if (bone != null && Muscles.TryGetValue(bone, out Muscle muscle)) {
					SetWorldRotation(muscle, lower * delta * muscle.Rest);
				}
			}
		}

		Vector3 velocity = Vector3.ProjectOnPlane(Input.Hips.linearVelocity, Vector3.up);

		for (int i = 0; i < Legs.Length; i++) {
			Leg leg = Legs[i];

			bool supported = Balance != null && (i == 0 ? Balance.LeftSupported : Balance.RightSupported);

			if (leg.Progress >= 1f && supported && !leg.Anchored) {
				leg.Position = leg.From = leg.Target = leg.Foot.position;
				leg.Anchored = true;
			}

			Vector3 previousPosition = leg.Position;
			leg.Velocity = Vector3.zero;

			if (leg.Progress < 1f) {
				leg.Progress = Mathf.Min(1f, leg.Progress + Time.fixedDeltaTime / leg.Duration);

				if (leg.Progress >= 1f) {
					CompletedSteps++;
				}

				float t = leg.Progress * leg.Progress * (3f - 2f * leg.Progress);
				float lift = Mathf.Sin(leg.Progress * Mathf.PI);

				leg.Position = Vector3.Lerp(leg.From, leg.Target, t) + Vector3.up * (lift * lift * StepHeight);
				leg.Rotation = Quaternion.Slerp(leg.FromRotation, leg.TargetRotation, t);
				leg.Velocity = (leg.Position - previousPosition) / Time.fixedDeltaTime;
			}
		}

		float overlap = Mathf.Lerp(1f, Mathf.Clamp01(StepOverlap), Mathf.InverseLerp(2f, 3.5f, velocity.magnitude));
		bool otherSupported = Balance != null && (NextFoot == 0 ? Balance.RightSupported : Balance.LeftSupported);

		if (Input.CanControl && Motor.HasGround && otherSupported && Legs[NextFoot].Progress >= 1f && Legs[1 - NextFoot].Progress >= overlap) {
			if (!TryStep(NextFoot, velocity)) {
				bool nextSupported = NextFoot == 0 ? Balance.LeftSupported : Balance.RightSupported;

				if (nextSupported) {
					TryStep(1 - NextFoot, velocity);
				}
			}
		}

		foreach (Leg leg in Legs) {
			if (!Motor.HasGround || !Input.CanControl) {
				leg.Position = leg.Foot.position;
				leg.Velocity = Vector3.zero;
				leg.Progress = 1f;
				leg.Anchored = false;
			}

			SolveLeg(leg, delta);

			if (Input.CanControl && Motor.HasGround && leg.FootBody != null && !leg.FootBody.isKinematic) {
				Vector3 correction = (leg.Position - leg.FootBody.position) * FootPositionStrength + (leg.Velocity - leg.FootBody.linearVelocity) * FootPositionDamping;
				leg.FootBody.AddForce(Vector3.ClampMagnitude(correction, MaxFootAcceleration), ForceMode.Acceleration);
			}
		}

		MuscleWeight = Mathf.MoveTowards(MuscleWeight, Input.CanControl ? 1f : FallenMuscleWeight, Time.fixedDeltaTime * 5f);

		foreach (Muscle muscle in Muscles.Values) {
			Quaternion parentRotation = muscle.Joint.connectedBody != null ? muscle.Joint.connectedBody.rotation : Quaternion.identity;

			if (muscle.Joint.connectedBody == Hips) {
				parentRotation = Posture.DesiredRotation;
			} else if (muscle.Joint.connectedBody != null && Muscles.TryGetValue(muscle.Joint.connectedBody.transform, out Muscle parent)) {
				parentRotation = parent.DesiredRotation;
			}

			Quaternion relative = Quaternion.Inverse(parentRotation) * muscle.DesiredRotation;
			muscle.Joint.targetRotation = Quaternion.Inverse(muscle.Space) * Quaternion.Inverse(relative) * muscle.RelativeRest * muscle.Space;

			JointDrive drive = muscle.Drive;
			drive.positionSpring *= MuscleWeight;
			drive.positionDamper *= Mathf.Sqrt(MuscleWeight);
			drive.maximumForce *= MuscleWeight;
			muscle.Joint.slerpDrive = drive;

			if (Input.CanControl && muscle.SupportedBodies != null && muscle.Joint.connectedBody != null && GravityCompensation > 0f) {
				Vector3 torque = Vector3.zero;
				Vector3 anchor = muscle.Joint.transform.TransformPoint(muscle.Joint.anchor);

				foreach (Rigidbody body in muscle.SupportedBodies) {
					torque -= Vector3.Cross(body.worldCenterOfMass - anchor, Physics.gravity * body.mass);
				}

				torque = Vector3.ClampMagnitude(torque * GravityCompensation, MaxForce);
				muscle.Body.AddTorque(torque, ForceMode.Force);
				muscle.Joint.connectedBody.AddTorque(-torque, ForceMode.Force);
			}
		}
	}

	private bool TryStep(int index, Vector3 velocity) {
		Leg leg = Legs[index];

		if (leg.Progress < 1f) {
			return false;
		}

		float duration = GetStepDuration(velocity.magnitude);

		Quaternion landingHeading = Quaternion.RotateTowards(Posture.Heading, Posture.GetPredictedHeading(duration), MaximumSupportTwist);

		Vector3 intent = Motor.DesiredVelocity;
		Vector3 landingVelocity = Vector3.MoveTowards(velocity, intent, Input.MoveAcceleration * duration);
		Vector3 travel = (velocity + landingVelocity) * (0.5f * duration);
		Vector3 prediction = travel + Vector3.ClampMagnitude(landingVelocity * PredictionTime, (leg.UpperLength + leg.LowerLength) * 0.4f);
		Vector3 origin = Input.Hips.position + landingHeading * Vector3.right * leg.Spacing + prediction;
		origin.y += 0.25f;

		if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, Motor.Height + 0.7f, Motor.GroundMask, QueryTriggerInteraction.Ignore)) {
			return false;
		}

		if (Vector3.Angle(hit.normal, Vector3.up) > GroundContact.MaxGroundAngle) {
			return false;
		}

		Vector3 target = hit.point + Vector3.up * leg.Sole;

		Vector3 landingUpper = leg.Upper.position + travel;

		if (Balance != null && Balance.HasGroundSupport) {
			landingUpper.y += Motor.PlannedHeight - (Hips.position.y - Balance.SupportPoint.y);
		}

		float reach = (leg.UpperLength + leg.LowerLength) * 0.98f;

		if (Vector3.Distance(target, landingUpper) > reach) {
			float vertical = target.y - landingUpper.y;

			if (Mathf.Abs(vertical) >= reach) {
				return false;
			}

			Vector3 horizontal = Vector3.ProjectOnPlane(target - landingUpper, Vector3.up);

			float allowed = Mathf.Sqrt(reach * reach - vertical * vertical) * 0.98f;

			Vector3 clamped = landingUpper + Vector3.ClampMagnitude(horizontal, allowed);
			origin = new Vector3(clamped.x, origin.y, clamped.z);
			
			if (!Physics.Raycast(origin, Vector3.down, out hit, Motor.Height + 0.7f, Motor.GroundMask, QueryTriggerInteraction.Ignore) || Vector3.Angle(hit.normal, Vector3.up) > GroundContact.MaxGroundAngle) {
				return false;
			}

			target = hit.point + Vector3.up * leg.Sole;

			if (Vector3.Distance(target, landingUpper) > reach) {
				return false;
			}
		}

		Quaternion targetRotation = Quaternion.FromToRotation(Vector3.up, hit.normal) * landingHeading * Quaternion.Inverse(InitialHeading) * leg.FootRest;

		if (Vector3.Distance(target, leg.Position) < StepThreshold && Quaternion.Angle(leg.Rotation, targetRotation) < 20f) {
			return false;
		}

		leg.From = Vector3.Distance(leg.Position, leg.Foot.position) > leg.UpperLength ? leg.Foot.position : leg.Position;
		leg.Target = target;
		leg.Normal = hit.normal;
		leg.FromRotation = leg.Rotation;
		leg.TargetRotation = targetRotation;
		leg.Duration = duration;
		leg.Progress = 0f;
		leg.Anchored = false;

		NextFoot = 1 - index;

		return true;
	}

	private void SolveLeg(Leg leg, Quaternion delta) {
		Vector3 hip = leg.Upper.position;
		Vector3 offset = leg.Position - hip;

		float length = Mathf.Clamp(offset.magnitude, Mathf.Abs(leg.UpperLength - leg.LowerLength) + 0.01f, (leg.UpperLength + leg.LowerLength) * 0.98f);

		Vector3 direction = offset.sqrMagnitude > 0.0001f ? offset.normalized : Vector3.down;
		Vector3 bend = Vector3.ProjectOnPlane(Posture.Heading * leg.BendDirection, direction).normalized;

		if (bend.sqrMagnitude < 0.01f) {
			bend = Posture.Heading * Vector3.right;
		}

		float along = (leg.UpperLength * leg.UpperLength - leg.LowerLength * leg.LowerLength + length * length) / (2f * length);
		float height = Mathf.Sqrt(Mathf.Max(0f, leg.UpperLength * leg.UpperLength - along * along));

		Vector3 knee = hip + direction * along + bend * height;
		Vector3 ankle = hip + direction * length;

		SetWorldRotation(Muscles[leg.Upper], Quaternion.FromToRotation(delta * leg.UpperAxis, knee - hip) * delta * leg.UpperRest);
		SetWorldRotation(Muscles[leg.Lower], Quaternion.FromToRotation(delta * leg.LowerAxis, ankle - knee) * delta * leg.LowerRest);
		SetWorldRotation(Muscles[leg.Foot], leg.Rotation);
	}

	public float GetStepDuration(float speed) {
		return Mathf.Clamp(StepDuration / Mathf.Max(1f, speed / 2f), 0.16f, 0.4f);
	}

	public bool CanTurnTo(Quaternion heading) {
		if (!Ready || Balance == null || Legs[0] == null || Legs[1] == null) {
			return true;
		}

		for (int i = 0; i < Legs.Length; i++) {
			Leg leg = Legs[i];

			bool supported = i == 0 ? Balance.LeftSupported : Balance.RightSupported;

			if (!supported || leg.Progress < 1f) {
				continue;
			}

			Quaternion footHeading = leg.Foot.rotation * Quaternion.Inverse(leg.FootRest) * InitialHeading;
			Vector3 forward = Vector3.ProjectOnPlane(footHeading * Vector3.forward, Vector3.up);

			if (Vector3.Angle(forward, heading * Vector3.forward) > MaximumSupportTwist) {
				return false;
			}
		}

		return true;
	}

	private static void SetWorldRotation(Muscle muscle, Quaternion desired) {
		muscle.DesiredRotation = desired;
	}

	private FootState GetFootState(int index) {
		if (!Ready || Legs[index] == null) {
			return FootState.Airborne;
		}

		Leg leg = Legs[index];

		bool supported = Balance != null && (index == 0 ? Balance.LeftSupported : Balance.RightSupported);

		if (!Motor.HasGround && !supported) {
			return FootState.Airborne;
		}

		if (leg.Progress >= 1f) {
			return supported ? FootState.Planted : FootState.Landing;
		}

		return leg.Progress < 0.2f ? FootState.Lifting : leg.Progress < 0.8f ? FootState.Swinging : FootState.Landing;
	}

	private void ConfigureLimits(ConfigurableJoint joint, Animator animator) {
		if (!ConfigureAnatomicalLimits || joint.angularXMotion != ConfigurableJointMotion.Free || joint.angularYMotion != ConfigurableJointMotion.Free || joint.angularZMotion != ConfigurableJointMotion.Free) {
			return;
		}

		Transform bone = joint.transform;
		float low, high, twist, side;

		if (bone == animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg) || bone == animator.GetBoneTransform(HumanBodyBones.RightLowerLeg)) {
			low = -135f; high = 3f; twist = 5f; side = 5f;
		} else if (bone == animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg) || bone == animator.GetBoneTransform(HumanBodyBones.RightUpperLeg)) {
			low = -45f; high = 100f; twist = 35f; side = 45f;
		} else if (bone == animator.GetBoneTransform(HumanBodyBones.LeftFoot) || bone == animator.GetBoneTransform(HumanBodyBones.RightFoot)) {
			low = -40f; high = 45f; twist = 20f; side = 25f;
		} else if (bone == animator.GetBoneTransform(HumanBodyBones.Head) || bone == animator.GetBoneTransform(HumanBodyBones.Neck)) {
			low = -30f; high = 40f; twist = 75f; side = 35f;
		} else if (animator.GetBoneTransform(HumanBodyBones.Head).IsChildOf(bone)) {
			low = -25f; high = 25f; twist = 35f; side = 20f;
		} else {
			return;
		}

		joint.axis = bone.InverseTransformDirection(InitialHeading * Vector3.right);
		joint.secondaryAxis = bone.InverseTransformDirection(Vector3.up);
		joint.angularXMotion = joint.angularYMotion = joint.angularZMotion = ConfigurableJointMotion.Limited;
		joint.lowAngularXLimit = new SoftJointLimit { limit = low };
		joint.highAngularXLimit = new SoftJointLimit { limit = high };
		joint.angularYLimit = new SoftJointLimit { limit = twist };
		joint.angularZLimit = new SoftJointLimit { limit = side };
	}

	private void OnDrawGizmos() {
		if (!Game.isDebugGame || !Ready || Legs[0] == null || Legs[1] == null) {
			return;
		}

		foreach (Leg leg in Legs) {
			Gizmos.color = leg.Progress < 1f ? Color.yellow : Color.green;
			Gizmos.DrawWireSphere(leg.Position, 0.06f);
			Gizmos.DrawLine(leg.Foot.position, leg.Position);
		}
	}
}
