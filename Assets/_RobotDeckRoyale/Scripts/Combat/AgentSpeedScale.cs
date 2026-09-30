using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Owns a NavMeshAgent's authored speed while timed movement effects are on it.
///
/// The Pulse Tower slow and the Overdrive boost can overlap on the same body, so
/// neither may save and restore the agent's speed on its own: whichever ended
/// last would restore the other's modified value. This keeps the authored speed
/// once and always applies authored x slow x boost.
/// </summary>
[DisallowMultipleComponent]
public sealed class AgentSpeedScale : MonoBehaviour
{
    private NavMeshAgent agent;
    private float authoredSpeed;
    private float slow = 1f, boost = 1f;

    public static AgentSpeedScale For(NavMeshAgent agent)
    {
        if (agent == null) return null;
        var scale = agent.GetComponent<AgentSpeedScale>();
        if (scale == null)
        {
            scale = agent.gameObject.AddComponent<AgentSpeedScale>();
            scale.agent = agent;
            scale.authoredSpeed = agent.speed;
        }
        return scale;
    }

    public void SetSlow(float multiplier) { slow = multiplier; Apply(); }
    public void SetBoost(float multiplier) { boost = multiplier; Apply(); }

    private void Apply()
    {
        if (agent != null) agent.speed = authoredSpeed * slow * boost;
    }
}
