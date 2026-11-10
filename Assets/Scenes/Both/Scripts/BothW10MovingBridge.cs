using Unity.AI.Navigation;
using UnityEngine;

/// <summary>A scene-authored bridge that opens a NavMesh route only when its collider spans the gap.</summary>
[DisallowMultipleComponent]
public sealed class BothW10MovingBridge : MonoBehaviour
{
    private enum Phase { Closed, Extending, Open, Retracting }

    [Header("Scene references")]
    [SerializeField] private NavMeshLink crossingLink;
    [SerializeField] private BoxCollider bridgeSurface;
    [SerializeField] private QH_NPCGroup group;
    [SerializeField] private Rigidbody boxBody;

    [Header("World positions")]
    [SerializeField] private Vector3 closedPosition;
    [SerializeField] private Vector3 openPosition;

    [Header("Timing")]
    [Min(0.1f)] [SerializeField] private float travelSeconds = 2f;
    [Min(0f)] [SerializeField] private float closedSeconds = 4f;
    [Min(0.1f)] [SerializeField] private float openSeconds = 20f;

    private Phase phase;
    private float phaseTime;

    public bool IsOpen => phase == Phase.Open;
    public string Status => phase == Phase.Open ? "OPEN" :
        phase == Phase.Extending ? "opening" :
        phase == Phase.Retracting ? "closing" : "closed";

    private void Start()
    {
        if (crossingLink == null || bridgeSurface == null || group == null || boxBody == null)
        {
            Debug.LogError("Assign this bridge's link, surface, NPC group and box in the Inspector.", this);
            enabled = false;
            return;
        }
        transform.position = closedPosition;
        crossingLink.activated = false;
        phase = Phase.Closed;
        phaseTime = 0f;
    }

    private void Update()
    {
        phaseTime += Time.deltaTime;
        switch (phase)
        {
            case Phase.Closed:
                if (phaseTime >= closedSeconds) ChangePhase(Phase.Extending);
                break;
            case Phase.Extending:
                transform.position = Vector3.Lerp(closedPosition, openPosition,
                    Mathf.Clamp01(phaseTime / travelSeconds));
                if (phaseTime >= travelSeconds)
                {
                    transform.position = openPosition;
                    if (crossingLink != null) crossingLink.activated = true;
                    ChangePhase(Phase.Open);
                }
                break;
            case Phase.Open:
                // Keep the bridge in place while a character or the box is crossing.
                if (phaseTime >= openSeconds && !IsOccupied())
                {
                    if (crossingLink != null) crossingLink.activated = false;
                    ChangePhase(Phase.Retracting);
                }
                break;
            case Phase.Retracting:
                transform.position = Vector3.Lerp(openPosition, closedPosition,
                    Mathf.Clamp01(phaseTime / travelSeconds));
                if (phaseTime >= travelSeconds)
                {
                    transform.position = closedPosition;
                    ChangePhase(Phase.Closed);
                }
                break;
        }
    }

    private void ChangePhase(Phase next)
    {
        phase = next;
        phaseTime = 0f;
    }

    private bool IsOccupied()
    {
        if (crossingLink != null && crossingLink.occupied) return true;
        if (bridgeSurface == null) return false;
        Bounds bounds = bridgeSurface.bounds;
        bool OverSurface(Vector3 point)
        {
            return point.x >= bounds.min.x - 0.5f && point.x <= bounds.max.x + 0.5f &&
                   point.z >= bounds.min.z - 0.5f && point.z <= bounds.max.z + 0.5f &&
                   point.y >= bounds.max.y - 1f && point.y <= bounds.max.y + 2f;
        }
        if (boxBody != null && OverSurface(boxBody.position)) return true;
        if (group == null) return false;
        foreach (QH_NPCNavigation npc in group.Npcs)
            if (npc != null && npc.gameObject.activeInHierarchy && OverSurface(npc.transform.position))
                return true;
        return false;
    }

    private void OnValidate()
    {
        travelSeconds = Mathf.Max(0.1f, travelSeconds);
        closedSeconds = Mathf.Max(0f, closedSeconds);
        openSeconds = Mathf.Max(0.1f, openSeconds);
    }
}
