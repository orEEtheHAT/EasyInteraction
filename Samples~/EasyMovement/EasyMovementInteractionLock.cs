using System;
using System.Collections.Generic;
using EasyMovement.FirstPerson;
using UnityEngine;

namespace EasyInteraction.Integrations.Movement
{
    [DisallowMultipleComponent, RequireComponent(typeof(InteractionActor)), AddComponentMenu("Easy Interaction/Integrations/EasyMovement Interaction Lock")]
    public sealed class EasyMovementInteractionLock : MonoBehaviour
    {
        public FirstPersonCharacterController character;
        [HideInInspector]
        public FirstPersonCharacterControllerInput movementInput;
        public string channel = "body";
        public bool lockAtApproachPoint = true;
        private InteractionActor actor;
        private readonly HashSet<InteractionTicket> locks = new HashSet<InteractionTicket>();
        private FirstPersonCharacterController blockedCharacter;
        private IDisposable inputBlock;
        private void OnEnable()
        {
            actor = GetComponent<InteractionActor>();
            if (!character)
                character = GetComponent<FirstPersonCharacterController>();
            if (!movementInput)
                movementInput = GetComponent<FirstPersonCharacterControllerInput>();
            actor.ActionStateChanged += Changed;
        }

        private void OnDisable()
        {
            if (actor)
                actor.ActionStateChanged -= Changed;
            locks.Clear();
            ReleaseInput();
        }

        private void OnDestroy() => ReleaseInput();
        private void Changed(InteractionActionEvent e)
        {
            if (!this || !isActiveAndEnabled)
                return;
            if (e.State == InteractionActionState.Started && e.Action && (e.Action.Channel == channel || (lockAtApproachPoint && actor.GetUsePoint(e.Action))))
            {
                locks.Add(e.Ticket);
                EnsureInput();
            }
            else if (e.State == InteractionActionState.Completed || e.State == InteractionActionState.Cancelled)
            {
                if (locks.Remove(e.Ticket) && locks.Count == 0)
                    ReleaseInput();
            }
        }

        private void EnsureInput()
        {
            if (!character || !character.isActiveAndEnabled)
            {
                ReleaseInput();
                return;
            }

            if (blockedCharacter != character)
            {
                ReleaseInput();
                blockedCharacter = character;
                blockedCharacter.InputBlockedChanged += BlockChanged;
            }

            if (inputBlock == null)
                inputBlock = blockedCharacter.AcquireInputBlock(this);
        }

        private void BlockChanged(bool blocked)
        {
            if (!blocked)
                inputBlock = null;
        }

        private void FixedUpdate()
        {
            if (locks.Count == 0)
                return;
            EnsureInput();
            if (!character || !character.isActiveAndEnabled)
                return;
            if (actor.Motor is EasyMovementInteractionMotor approach && approach.IsApproaching && approach.character == character)
                return;
            character.SetMovementDirection(Vector3.zero);
            character.SetVelocity(new Vector3(0, character.GetVelocity().y, 0));
        }

        private void ReleaseInput()
        {
            if (blockedCharacter)
                blockedCharacter.InputBlockedChanged -= BlockChanged;
            blockedCharacter = null;
            var lease = inputBlock;
            inputBlock = null;
            lease?.Dispose();
        }
    }
}
