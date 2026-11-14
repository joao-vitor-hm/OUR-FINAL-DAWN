
using System.Collections.Generic;
using UnityEngine;

public class GroundContact : MonoBehaviour {

    public const float MaxGroundAngle = 50f;

    [SerializeField] private LayerMask GroundLayer;

    private readonly HashSet<Collider> GroundColliders = new HashSet<Collider>();

    public bool IsGrounded {
        get {
            foreach (Collider collider in GroundColliders) {
                if (collider != null && collider.enabled && IsGround(collider.gameObject.layer)) {
                    return true;
                }
            }
            return false;
        }
    }

    public Vector3 GroundPoint { get; private set; }
    public Vector3 GroundNormal { get; private set; } = Vector3.up;
    public Vector3 GroundVelocity { get; private set; }

    private void Awake() {
        if (GroundLayer.value == 0) {
            GroundLayer = LayerMask.GetMask("Ground");
        }
    }

    private void OnCollisionEnter(Collision collision) {
        UpdateContact(collision);
    }

    private void OnCollisionStay(Collision collision) {
        UpdateContact(collision);
    }

    private void UpdateContact(Collision collision) {
        Vector3 point = Vector3.zero;
        Vector3 normal = Vector3.zero;
        int count = 0;

        if (IsGround(collision.collider.gameObject.layer)) {
            for (int i = 0; i < collision.contactCount; i++) {
                ContactPoint contact = collision.GetContact(i);
                if (Vector3.Dot(contact.normal, Vector3.up) >= Mathf.Cos(MaxGroundAngle * Mathf.Deg2Rad)) {
                    point += contact.point;
                    normal += contact.normal;
                    count++;
                }
            }
        }

        if (count > 0) {
            GroundColliders.Add(collision.collider);
            GroundPoint = point / count;
            GroundNormal = normal.normalized;
            GroundVelocity = collision.rigidbody != null ? collision.rigidbody.GetPointVelocity(GroundPoint) : Vector3.zero;
        } else {
            GroundColliders.Remove(collision.collider);
        }
    }

    private void OnCollisionExit(Collision collision) {
        GroundColliders.Remove(collision.collider);
    }

    private bool IsGround(int layer) {
        return (GroundLayer.value & (1 << layer)) != 0;
    }

    public void ClearContacts() {
        GroundColliders.Clear();
        GroundPoint = transform.position;
        GroundNormal = Vector3.up;
        GroundVelocity = Vector3.zero;
    }

    private void OnDisable() {
        ClearContacts();
    }
}
