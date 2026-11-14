using System.Collections;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

[DefaultExecutionOrder(-100)]
public class Camera : MonoBehaviour {

    [SerializeField] private Transform CameraTarget;
    [SerializeField] private Transform m_Player;

    [Tooltip("Degrees per mouse pixel; mouse delta must not be multiplied by deltaTime.")]
    [SerializeField] private float FollowHeight = 0.65f;
    [SerializeField] private float Sensitivity = 0.3f;
    [SerializeField] private float MinAngle = -40f;
    [SerializeField] private float MaxAngle = 70f;
    [SerializeField] private float CameraDistance = 2.8f;
    [SerializeField] private float AimDistance = 1.7f;
    [SerializeField] private float MinDistance = 1.8f;
    [SerializeField] private float MaxDistance = 4.5f;
    [SerializeField] private float ZoomSpeed = 0.002f;
    [SerializeField] private float CameraResponse = 8f;
    [SerializeField] private Vector3 ShoulderOffset = new Vector3(0.35f, 0.1f, 0f);
    [SerializeField] private Vector3 FollowDamping = new Vector3(0.12f, 0.2f, 0.12f);

    [Tooltip("Scene geometry only. Ground is layer 3; Default also contains the ragdoll.")]
    [SerializeField] private LayerMask ObstacleLayers = 1 << 3;

    private Transform FollowHips;
    private float XRotation;
    private float YRotation;
    private bool WasLooking;
    private CinemachineThirdPersonFollow Follow;
    private CinemachineCamera VirtualCamera;
    private float ZoomDistance;
    public bool IsAiming { get; private set; }

    public Vector3 Forward => Quaternion.Euler(0f, YRotation, 0f) * Vector3.forward;
    public Vector3 Right => Quaternion.Euler(0f, YRotation, 0f) * Vector3.right;

    private void Start() {
        if (CameraTarget == null) {
            Debug.LogWarning("Camera requires a CameraTarget.", this);
            enabled = false;
            return;
        }

        FollowHips = ResolveHips();

        if (FollowHips == null) {
            Debug.LogWarning("Camera could not find physical hips; following m_Player instead.", this);
        }

        YRotation = CameraTarget.eulerAngles.y;
        XRotation = Mathf.Clamp(Mathf.DeltaAngle(0f, CameraTarget.eulerAngles.x), MinAngle, MaxAngle);

        UnityEngine.Camera outputCamera = UnityEngine.Camera.main;

        if (outputCamera != null && outputCamera.TryGetComponent<CinemachineBrain>(out var brain)) {
            brain.UpdateMethod = CinemachineBrain.UpdateMethods.LateUpdate;
        }

        if (TryGetComponent<CinemachineThirdPersonFollow>(out var follow)) {
            Follow = follow;
            follow.Damping = FollowDamping;
            follow.ShoulderOffset = ShoulderOffset;
            follow.VerticalArmLength = 0.15f;
            follow.CameraSide = 1f;
            follow.CameraDistance = CameraDistance;
            follow.AvoidObstacles.Enabled = true;
            follow.AvoidObstacles.CollisionFilter = ObstacleLayers;
            follow.AvoidObstacles.CameraRadius = Mathf.Max(0.4f, follow.AvoidObstacles.CameraRadius);
        }
        ZoomDistance = Mathf.Clamp(CameraDistance, MinDistance, MaxDistance);
        VirtualCamera = GetComponent<CinemachineCamera>();
    }

    private Transform ResolveHips() {
        Animator animator = m_Player != null ? m_Player.GetComponentInParent<Animator>() : null;

        if (animator == null && m_Player != null) {
            animator = m_Player.GetComponentInChildren<Animator>();
        }

        if (animator == null) {
            animator = CameraTarget.GetComponentInParent<Animator>();
        }

        if (animator != null && animator.isHuman && animator.avatar != null && animator.avatar.isValid) {
            Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);

            if (hips != null && hips.TryGetComponent<Rigidbody>(out _)) {
                return hips;
            }
        }

        if (m_Player != null) {
            if (m_Player.TryGetComponent<Rigidbody>(out _)) {
                return m_Player;
            }

            foreach (Rigidbody body in m_Player.GetComponentsInChildren<Rigidbody>(true)) {
                if (body.name.Equals("Hips", System.StringComparison.OrdinalIgnoreCase)) {
                    return body.transform;
                }
            }
        }
        return null;
    }

    private void LateUpdate() {
        if (CameraTarget == null) {
            return;
        }

        if (Game.isGamePaused) {
            WasLooking = false;
            IsAiming = false;
            return;
        }

        bool canLook = !Game.isMouseGame && Cursor.lockState == CursorLockMode.Locked && Application.isFocused && Mouse.current != null;

        if (canLook && WasLooking) {
            Vector2 MouseDelta = Mouse.current.delta.ReadValue();

            YRotation = Mathf.Repeat(YRotation + MouseDelta.x * Sensitivity, 360f);
            XRotation -= MouseDelta.y * Sensitivity;
            XRotation = Mathf.Clamp(XRotation, MinAngle, MaxAngle);
        }

        WasLooking = canLook;
        IsAiming = canLook && GameControlls.IsAiming();
        if (canLook) {
            ZoomDistance = Mathf.Clamp(ZoomDistance - GameControlls.GetZoom() * ZoomSpeed, MinDistance, MaxDistance);
        }
        float blend = 1f - Mathf.Exp(-CameraResponse * Time.deltaTime);
        if (Follow != null) {
            Follow.CameraDistance = Mathf.Lerp(Follow.CameraDistance, IsAiming ? AimDistance : ZoomDistance, blend);
        }
        if (VirtualCamera != null) {
            VirtualCamera.Lens.FieldOfView = Mathf.Lerp(VirtualCamera.Lens.FieldOfView, IsAiming ? 50f : 60f, blend);
        }

        if (FollowHips != null) {
            CameraTarget.position = FollowHips.position + Vector3.up * FollowHeight;
        } else if (m_Player != null) {
            CameraTarget.position = m_Player.position + Vector3.up * 1.6f;
        }

        CameraTarget.rotation = Quaternion.Euler(XRotation, YRotation, 0f);
    }

    public void ResetFollow() {
        WasLooking = false;
        if (FollowHips != null && CameraTarget != null) {
            CameraTarget.position = FollowHips.position + Vector3.up * FollowHeight;
        }
        if (VirtualCamera != null) {
            VirtualCamera.PreviousStateIsValid = false;
        }
    }
}
