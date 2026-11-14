
using UnityEngine;

public class FootRight : MonoBehaviour {

    [Header("References")]
    [SerializeField] private LayerMask TerrainLayer;
    [SerializeField] private Transform RightFoot;
    [SerializeField] private Transform Body;

    [Header("Walking")]
    [SerializeField] private float ForwardOffset = 0.3f;
    [SerializeField] private float StepDistance = 0.4f;
    [SerializeField] private float FootSpacing = 0.1f;
    [SerializeField] private float RayDistance = 2f;
    [SerializeField] private float StepHeight = 0.2f;
    [SerializeField] private float Speed = 5f;

    private Quaternion FootRotationOffset;

    private Vector3 MovementDirection = Vector3.zero;
    private Vector3 CurrentPosition = Vector3.zero;
    private Vector3 GroundNormal = Vector3.up;
    private Vector3 OldPosition = Vector3.zero;
    private Vector3 NewPosition = Vector3.zero;
    private Vector3 rayPosition = Vector3.zero;

    private float Lerp;

    public void SetMovementDirection(Vector3 direction) {
        MovementDirection = direction;
    }

    public bool IsStepping() {
        return Lerp < 1f;
    }

    public void StartStep(Vector3 targetPosition) {
        OldPosition = CurrentPosition;
        NewPosition = targetPosition;
        Lerp = 0f;
    }

    public float GetStepProgress() {
        return Lerp;
    }

    private void Start() {
        Quaternion bodyRotation = Quaternion.Euler(0f, Body.eulerAngles.y, 0f);
        FootRotationOffset = Quaternion.Inverse(bodyRotation) * RightFoot.rotation;

        transform.rotation = RightFoot.rotation;

        CurrentPosition = transform.position;
        OldPosition = CurrentPosition;
        NewPosition = CurrentPosition;
        Lerp = 1f;
    }

    private void Update() {
        if (IsStepping()) {
            Vector3 footPosition = Vector3.Lerp(OldPosition, NewPosition, Lerp);
            footPosition.y += Mathf.Sin(Lerp * Mathf.PI) * StepHeight;

            CurrentPosition = footPosition;
            Lerp = Mathf.MoveTowards(Lerp, 1f, Time.deltaTime * Speed);
        } else {
            CurrentPosition = NewPosition;
        }

        Quaternion groundRotation = Quaternion.FromToRotation(Vector3.up, GroundNormal);
        Quaternion bodyRotation = Quaternion.Euler(0f, Body.eulerAngles.y, 0f);

        transform.rotation = groundRotation * bodyRotation * FootRotationOffset;
        transform.position = CurrentPosition;
    }

    public Vector3 GetStepPosition() {
        rayPosition = Body.position + Body.right * FootSpacing + MovementDirection * (StepDistance + ForwardOffset);
        rayPosition.y = Body.position.y + 1f;

        if (Physics.Raycast(rayPosition, Vector3.down, out RaycastHit hit, RayDistance + 1f, TerrainLayer)) {
            GroundNormal = hit.normal;
            return hit.point;
        }

        GroundNormal = Vector3.up;

        return CurrentPosition;
    }

    private void OnDrawGizmos() {
        if (!Game.isDebugGame) {
            return;
        }

        Gizmos.color = Color.red;
        Gizmos.DrawRay(rayPosition, Vector3.down * (RayDistance + 1f));
        Gizmos.DrawWireSphere(rayPosition, 0.05f);
    }
}
