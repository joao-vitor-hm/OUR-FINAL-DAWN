
using System;
using UnityEngine;

public class ProceduralWalking : MonoBehaviour {

    [Header("References")]
    [SerializeField] private Transform Body;
    [SerializeField] private LayerMask TerrainLayer;

    [Header("Walking")]
    [SerializeField] private float StepDistance = 0.4f;
    [SerializeField] private float FootSpacing = 0.2f;
    [SerializeField] private float RayDistance = 2f;
    [SerializeField] private float StepHeight = 2f;
    [SerializeField] private float Speed = 2f;

    private Vector3 MovementDirection;
    private Vector3 CurrentPosition;
    private Vector3 OldPosition;
    private Vector3 NewPosition;

    private float lerp;

    public void SetMovementDirection(Vector3 direction) {
        MovementDirection = direction.normalized;
    }

    private void Start() {
        CurrentPosition = transform.position;
        NewPosition = CurrentPosition;
        OldPosition = CurrentPosition;
    }

    private void Update() {
        transform.position = CurrentPosition;

        Vector3 rayPosition = Body.position + Body.right * FootSpacing + MovementDirection * StepDistance;

        Ray ray = new Ray(rayPosition, Vector3.down);

        if (MovementDirection.sqrMagnitude > 0.01f) {
            if (Physics.Raycast(ray, out RaycastHit hit, RayDistance, TerrainLayer)) {
                if (Vector3.Distance(NewPosition, hit.point) > StepDistance && lerp >= 1f) {
                    OldPosition = CurrentPosition;
                    NewPosition = hit.point;
                    lerp = 0;
                }
            }
        }

        if (lerp < 1) {
            Vector3 footPosition = Vector3.Lerp(OldPosition, NewPosition, lerp);
            footPosition.y += Mathf.Sin(lerp * Mathf.PI) * StepHeight;

            CurrentPosition = footPosition;
            lerp = Mathf.MoveTowards(lerp, 1f, Time.deltaTime * Speed);
        } else {
            OldPosition = NewPosition;
        }
    }

    private void OnDrawGizmos() {
        if (!Game.isDebugGame) {
            return;
        }

        Gizmos.color = Color.yellow;
        Gizmos.DrawSphere(NewPosition, 0.08f);
    }
}
