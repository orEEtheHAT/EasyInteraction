using EasyCombat;
using UnityEngine;

namespace EasyInteraction.Integrations.Combat
{
    [DisallowMultipleComponent, RequireComponent(typeof(InteractionActor)), AddComponentMenu("Easy Interaction/Integrations/Combat Interaction Guard")]
    public sealed class CombatInteractionGuard : MonoBehaviour
    {
        public Health health;
        public bool cancelOnDamage = true;
        public bool cancelOnDowned = true;
        private InteractionActor actor;
        private Health subscribedHealth;
        private bool disabledActor;
        private void OnEnable()
        {
            actor = GetComponent<InteractionActor>();
            if (!health)
                health = GetComponentInParent<Health>();
            subscribedHealth = health;
            if (!subscribedHealth)
                return;
            subscribedHealth.Damaged += Damaged;
            subscribedHealth.Died += Died;
            subscribedHealth.Downed += Downed;
            subscribedHealth.Revived += Revived;
            subscribedHealth.StateRestored += Restored;
            SynchronizeLife();
        }

        private void OnDisable()
        {
            if (subscribedHealth)
            {
                subscribedHealth.Damaged -= Damaged;
                subscribedHealth.Died -= Died;
                subscribedHealth.Downed -= Downed;
                subscribedHealth.Revived -= Revived;
                subscribedHealth.StateRestored -= Restored;
            }

            subscribedHealth = null;
            RestoreOwnedDisable();
        }

        private void Damaged(DamageResult _)
        {
            if (subscribedHealth && cancelOnDamage && actor)
                actor.Cancel("Interrupted by damage.");
        }

        private void Downed(Health _)
        {
            if (subscribedHealth && cancelOnDowned && actor)
                actor.Cancel("Participant downed.");
        }

        private void Died(Health _) => SynchronizeLife();
        private void Revived(Health _) => SynchronizeLife();
        private void Restored(Health _)
        {
            if (!subscribedHealth)
                return;
            if (actor)
                actor.Cancel("Combat state restored.");
            SynchronizeLife();
        }

        private void SynchronizeLife()
        {
            if (!actor || !subscribedHealth)
                return;
            if (subscribedHealth.IsAlive)
            {
                RestoreOwnedDisable();
                return;
            }

            actor.Cancel("Participant died.");
            // A repeated death/dead restore must not erase our existing ownership.
            // An actor that another system had already disabled remains disabled after revive/restore.
            if (!actor.enabled)
                return;
            disabledActor = true;
            actor.enabled = false;
        }

        private void RestoreOwnedDisable()
        {
            if (!disabledActor)
                return;
            disabledActor = false;
            if (actor)
                actor.enabled = true;
        }
    }
}
