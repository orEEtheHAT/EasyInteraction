using UnityEngine;

namespace EasyInteraction.Verification.Network
{
    public sealed class NetworkSmokeProbe : InteractionAction
    {
        public int Executions { get; private set; }

        protected override bool OnExecute(InteractionActor actor)
        {
            Executions++;
            return false;
        }
    }
}
