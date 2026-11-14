
using UnityEngine;

public class ProceduralController : MonoBehaviour {

    [Header("References")]
    [SerializeField] private FootRight RightFoot;
    [SerializeField] private FootLeft LeftFoot;

    [Header("Walking")]
    [SerializeField] private float MinMovementSpeed = 0.1f;
    [SerializeField] private float MinStepDistance = 0.35f;
    [SerializeField] private float MaxStepDistance = 0.65f;
    [SerializeField] private float SlowStepOverlap = 0.9f;
    [SerializeField] private float FastStepOverlap = 0.7f;
    [SerializeField] private float ReferenceSpeed = 3f;

    private Vector3 MovementDirection;
    private float MovementSpeed;

    private bool LeftFootNext = true;

    public void SetMovement(Vector3 direction, float speed) {
        MovementDirection = direction;
        MovementSpeed = speed;

        RightFoot.SetMovementDirection(direction);
        LeftFoot.SetMovementDirection(direction);
    }

    private void Update() {
        if (MovementSpeed < MinMovementSpeed) {
            return;
        }

        float SpeedFactor = Mathf.InverseLerp(0f, ReferenceSpeed, MovementSpeed);

        float StepDistance = Mathf.Lerp(MinStepDistance, MaxStepDistance, SpeedFactor);
        float StepOverlap = Mathf.Lerp(SlowStepOverlap, FastStepOverlap, SpeedFactor);

        if (LeftFootNext) {
            if (!LeftFoot.IsStepping() && (!RightFoot.IsStepping() || RightFoot.GetStepProgress() >= StepOverlap)) {
                Vector3 target = LeftFoot.GetStepPosition();

                Vector3 distance = target - LeftFoot.transform.position;
                distance.y = 0f;

                if (distance.magnitude > StepDistance) {
                    LeftFoot.StartStep(target);
                    LeftFootNext = false;
                }
            }
        } else {
            if (!RightFoot.IsStepping() && (!LeftFoot.IsStepping() || LeftFoot.GetStepProgress() >= StepOverlap)) {
                Vector3 target = RightFoot.GetStepPosition();

                Vector3 distance = target - RightFoot.transform.position;
                distance.y = 0f;

                if (distance.magnitude > StepDistance) {
                    RightFoot.StartStep(target);
                    LeftFootNext = true;
                }
            }
        }
    }
}
