using System;
using UnityEngine;

namespace EasyInteraction
{
    [AddComponentMenu("Easy Interaction/Conditions/Condition Group")]
    public sealed class ConditionGroup : InteractionCondition
    {
        public InteractionConditionMatch match;
        public InteractionCondition[] conditions = Array.Empty<InteractionCondition>();
        private bool evaluating;
        public override InteractionAvailability Check(InteractionActor actor, InteractionAction action)
        {
            if (evaluating)
                return InteractionAvailability.Unavailable(InteractionFailure.InvalidConfiguration, "Requirements are misconfigured.", diagnostic: "Remove the cycle from Condition Group.");
            evaluating = true;
            try
            {
                var failure = InteractionAvailability.Unavailable("No requirement is satisfied.");
                int checkedCount = 0;
                foreach (var condition in conditions)
                {
                    if (!condition || !condition.isActiveAndEnabled)
                        continue;
                    var check = condition.Check(actor, action);
                    checkedCount++;
                    if (check.Code == InteractionFailure.InvalidConfiguration)
                        return check;
                    if (match == InteractionConditionMatch.All && !check.IsAvailable)
                        return check;
                    if (match == InteractionConditionMatch.Any && check.IsAvailable)
                        return check;
                    if (!check.IsAvailable)
                        failure = check;
                }

                return match == InteractionConditionMatch.All || checkedCount == 0 ? InteractionAvailability.Available : failure;
            }
            finally
            {
                evaluating = false;
            }
        }
    }
}
