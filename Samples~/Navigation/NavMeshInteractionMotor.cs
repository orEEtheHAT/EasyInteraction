using System;
using UnityEngine;
using UnityEngine.AI;

namespace EasyInteraction.Integrations.Navigation
{
    [DisallowMultipleComponent, RequireComponent(typeof(NavMeshAgent), typeof(InteractionActor)), AddComponentMenu("Easy Interaction/Integrations/NavMesh Interaction Motor")]
    public sealed class NavMeshInteractionMotor : InteractionMotor
    {
        public bool resumePreviousPath = true;
        [Min(1)]
        public float turnSpeed = 360;
        private NavMeshAgent agent;
        private Transform destination;
        private bool active, oldStopped, oldRotation, hadPath;
        private float oldStopping;
        private Vector3 oldDestination, lastDestination;
        private void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            GetComponent<InteractionActor>().Motor = this;
        }

        public override void Begin(Transform point)
        {
            if (!agent)
                agent = GetComponent<NavMeshAgent>();
            if (!agent.isOnNavMesh)
                throw new InvalidOperationException("Place this NavMeshAgent on a baked NavMesh before starting an interaction.");
            if (!active)
            {
                oldStopped = agent.isStopped;
                oldRotation = agent.updateRotation;
                oldStopping = agent.stoppingDistance;
                hadPath = agent.hasPath;
                oldDestination = agent.destination;
                active = true;
            }

            destination = point;
            agent.updateRotation = false;
            agent.stoppingDistance = .05f;
            agent.isStopped = false;
            if (point)
            {
                lastDestination = point.position;
                agent.SetDestination(lastDestination);
            }
        }

        public override void Tick(float deltaTime)
        {
            if (!active || !destination || !agent.isOnNavMesh)
                return;
            if ((destination.position - lastDestination).sqrMagnitude > .0001f)
            {
                lastDestination = destination.position;
                agent.SetDestination(lastDestination);
            }

            transform.rotation = Quaternion.RotateTowards(transform.rotation, destination.rotation, turnSpeed * deltaTime);
        }

        public override void Stop()
        {
            if (!active)
                return;
            active = false;
            destination = null;
            if (!agent)
                return;
            agent.updateRotation = oldRotation;
            agent.stoppingDistance = oldStopping;
            if (agent.isOnNavMesh)
            {
                if (resumePreviousPath && hadPath)
                    agent.SetDestination(oldDestination);
                else
                    agent.ResetPath();
                agent.isStopped = oldStopped;
            }
        }

        private void OnDisable() => Stop();
    }
}
