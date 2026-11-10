using UnityEngine;

/// <summary>Hands a hit NPC over to physics so it can be pushed off the platform.</summary>
public class QH_NPCKnockback : MonoBehaviour
{
    [SerializeField] private QH_NPCIntent intent;
    [SerializeField] private Rigidbody body;
    [Tooltip("Assign this NPC's movement component later, such as its NavMeshAgent.")]
    [SerializeField] private Behaviour movementController;
    [SerializeField, Min(0f)] private float shoveStrength = 8f;
    [SerializeField, Min(0f)] private float liftStrength = 2f;

    private bool hasBeenShoved;

    private void Awake()
    {
        if (intent == null) intent = GetComponent<QH_NPCIntent>();
        if (body == null) body = GetComponent<Rigidbody>();
    }

    public void ApplyShove(Vector3 direction)
    {
        if (intent == null || body == null || intent.IsEliminated) return;

        if (!hasBeenShoved)
        {
            hasBeenShoved = true;
            if (movementController != null)
                movementController.enabled = false;

            body.isKinematic = false;
            body.useGravity = true;
        }
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.001f)
            direction = transform.forward;

        Vector3 shove = direction.normalized * shoveStrength + Vector3.up * liftStrength;
        body.AddForce(shove, ForceMode.Impulse);
    }

    public void ResetCharacter(Vector3 position, Quaternion rotation)
    {
        if (intent != null)
            intent.ResetIntent();

        hasBeenShoved = false;
        if (body != null)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.isKinematic = true;
            body.useGravity = false;
            body.position = position;
            body.rotation = rotation;
        }

        if (movementController != null)
            movementController.enabled = true;
    }
}
