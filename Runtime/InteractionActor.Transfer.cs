namespace EasyInteraction
{
    public sealed partial class InteractionActor
    {
        internal bool TransferGrab(GrabAction grab, InteractionActor receiver)
        {
            if (!grab || grab.Owner != this || grab.OwnerCount != 1 || !receiver || mutationDepth > 0)
                return false;
            receiver.transferringFrom = this;
            try
            {
                if (!receiver.Begin(grab, false, false, out var ticket) || !ticket.IsActive)
                    return false;
                Cancel(grab, "Transferred.");
                return true;
            }
            finally
            {
                receiver.transferringFrom = null;
            }
        }

        private InteractionActor transferringFrom;
        private bool IsValidTransfer(InteractionAction action) => transferringFrom && action is GrabAction && action.Owner == transferringFrom && action.OwnerCount == 1;
    }
}
