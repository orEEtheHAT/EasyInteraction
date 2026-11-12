using System;
using ECM2;
using EasyMovement.FirstPerson;
using UnityEngine;

namespace EasyInteraction.Integrations.Movement
{
    [DisallowMultipleComponent, RequireComponent(typeof(InteractionActor)), AddComponentMenu("Easy Interaction/Integrations/EasyMovement Interaction Motor")]
    public sealed class EasyMovementInteractionMotor : InteractionMotor
    {
        public FirstPersonCharacterController character;
        [HideInInspector]
        public FirstPersonCharacterControllerInput movementInput;
        [Min(1)]
        public float turnSpeed = 360;
        private Transform destination;
        private FirstPersonCharacterController controlledCharacter;
        private IDisposable inputBlock;
        private bool active;
        private Character.RotationMode previousRotation;
        public bool IsApproaching => active;

        private void Awake()
        {
            if (!character)
                character = GetComponent<FirstPersonCharacterController>();
            if (!movementInput)
                movementInput = GetComponent<FirstPersonCharacterControllerInput>();
            var actor = GetComponent<InteractionActor>();
            actor.Motor = this;
            if (character && character.LookPivot)
                actor.View = character.LookPivot;
            actor.PlayerRoot = character ? character.transform : transform;
        }

        public override void Begin(Transform point)
        {
            if (!isActiveAndEnabled || !character || !character.isActiveAndEnabled || !character.characterMovement)
                throw new InvalidOperationException("Assign an enabled, initialized EasyMovement First Person Character Controller and enable this motor.");
            if (active && controlledCharacter != character)
                Stop();
            if (!active)
            {
                controlledCharacter = character;
                previousRotation = controlledCharacter.GetRotationMode();
                inputBlock = controlledCharacter.AcquireInputBlock(this);
                controlledCharacter.InputBlockedChanged += BlockChanged;
                controlledCharacter.SetRotationMode(Character.RotationMode.None);
                active = true;
            }

            destination = point;
        }

        public override void Tick(float deltaTime)
        {
            if (!active)
                return;
            if (!destination || !controlledCharacter || !controlledCharacter.isActiveAndEnabled)
            {
                Stop();
                return;
            }

            var delta = Vector3.ProjectOnPlane(destination.position - controlledCharacter.GetPosition(), Vector3.up);
            controlledCharacter.SetMovementDirection(delta.sqrMagnitude > .0025f ? delta.normalized * Mathf.Clamp01(delta.magnitude / .4f) : Vector3.zero);
            var rotation = Quaternion.Euler(0, destination.eulerAngles.y, 0);
            controlledCharacter.SetRotation(Quaternion.RotateTowards(controlledCharacter.GetRotation(), rotation, turnSpeed * deltaTime));
        }

        private void BlockChanged(bool blocked)
        {
            if (!blocked && active)
                Stop();
        }

        public override void Stop()
        {
            if (!active && inputBlock == null)
                return;
            active = false;
            destination = null;
            var previous = controlledCharacter;
            controlledCharacter = null;
            var lease = inputBlock;
            inputBlock = null;
            if (previous)
                previous.InputBlockedChanged -= BlockChanged;
            try
            {
                if (previous)
                {
                    previous.SetMovementDirection(Vector3.zero);
                    previous.SetVelocity(Vector3.zero);
                    previous.SetRotationMode(previousRotation);
                }
            }
            finally
            {
                lease?.Dispose();
            }
        }

        private void OnDisable() => Stop();
        private void OnDestroy() => Stop();
    }
}
