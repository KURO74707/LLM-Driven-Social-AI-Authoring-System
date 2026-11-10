using UnityEngine;
using UnityEngine.AI;

public class AITarget : MonoBehaviour
{
    public Transform Target;
    public float attackDistance;

    private NavMeshAgent m_Agent;
    private float m_Distance;

    void Start()
    {
        m_Agent = GetComponent<NavMeshAgent>();
    }

    // Update is called once per frame
    void Update()
    {
        m_Distance = Vector3.Distance(m_Agent.transform.position, Target.position);
        if (m_Distance < attackDistance )
        {
            m_Agent.isStopped = true;
        }
        else
        {
            m_Agent.isStopped = false;
            m_Agent.destination = Target.position;
        }



    }
}
