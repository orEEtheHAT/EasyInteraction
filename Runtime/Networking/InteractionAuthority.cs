using System;
using System.Collections.Generic;
using UnityEngine;

namespace EasyInteraction
{
    public enum InteractionRequestKind : byte
    {
        Start,
        Press,
        Cancel,
        Pause,
        Resume,
        Value
    }

    public readonly struct InteractionRequest
    {
        public ulong Sequence { get; }
        public string TargetId { get; }
        public string ActionId { get; }
        public InteractionRequestKind Kind { get; }
        public ulong OperationId { get; }
        public float Value { get; }
        public ulong SourceSequence { get; }

        public InteractionRequest(ulong sequence, string targetId, string actionId, InteractionRequestKind kind = InteractionRequestKind.Start, ulong operationId = 0, float value = 0, ulong sourceSequence = 0)
        {
            Sequence = sequence;
            TargetId = targetId;
            ActionId = actionId;
            Kind = kind;
            OperationId = operationId;
            Value = value;
            SourceSequence = sourceSequence;
        }

        internal bool Same(InteractionRequest other) => Sequence == other.Sequence && TargetId == other.TargetId && ActionId == other.ActionId && Kind == other.Kind && OperationId == other.OperationId && Value == other.Value && SourceSequence == other.SourceSequence;
    }

    public readonly struct InteractionReply
    {
        public ulong Sequence { get; }
        public bool Accepted { get; }
        public InteractionAvailability Result { get; }
        public ulong OperationId { get; }

        public InteractionReply(ulong sequence, bool accepted, InteractionAvailability result, ulong operationId = 0)
        {
            Sequence = sequence;
            Accepted = accepted;
            Result = result;
            OperationId = operationId;
        }
    }

    /// <summary>Main-thread authoritative gate. The transport supplies its authenticated actor, never an actor ID from a message.</summary>
    public sealed class InteractionAuthority : IDisposable
    {
        private sealed class Client
        {
            public InteractionRequest Last;
            public InteractionReply Reply;
            public float WindowStart;
            public int Requests;
            public bool Processing;
            public readonly Dictionary<InteractionAction, StartRequest> Starts = new Dictionary<InteractionAction, StartRequest>();
            public readonly List<InteractionAction> Expired = new List<InteractionAction>();
        }

        private struct StartRequest
        {
            public ulong Sequence, Operation;
        }

        private readonly Dictionary<InteractionActor, Client> clients = new Dictionary<InteractionActor, Client>();
        private bool disposed;
        public int MaxRequestsPerSecond { get; set; } = 120;
        public Func<InteractionActor, InteractionAction, bool> Permission { get; set; }

        public InteractionReply Process(InteractionActor authenticatedActor, InteractionRequest request)
        {
            if (disposed || !authenticatedActor || !authenticatedActor.isActiveAndEnabled)
                return Reject(request, InteractionFailure.Unauthorized, "Actor is not authorized.");
            if (request.Sequence == 0 || string.IsNullOrEmpty(request.TargetId) || request.TargetId.Length > 128 || string.IsNullOrEmpty(request.ActionId) || request.ActionId.Length > 128 || float.IsNaN(request.Value) || float.IsInfinity(request.Value))
                return Reject(request, InteractionFailure.InvalidConfiguration, "Invalid interaction request.");
            if (!clients.TryGetValue(authenticatedActor, out var client))
            {
                client = new Client
                {
                    WindowStart = Time.unscaledTime
                };
                clients.Add(authenticatedActor, client);
            }

            if (client.Processing)
                return Reject(request, InteractionFailure.Busy, "Another request is being processed.");
            if (request.Sequence == client.Last.Sequence)
                return request.Same(client.Last) ? client.Reply : Reject(request, InteractionFailure.StaleRequest, "Sequence already used for another request.");
            if (request.Sequence < client.Last.Sequence)
                return Reject(request, InteractionFailure.StaleRequest, "Outdated interaction request.");
            if (Time.unscaledTime - client.WindowStart >= 1)
            {
                client.WindowStart = Time.unscaledTime;
                client.Requests = 0;
            }

            if (++client.Requests > MaxRequestsPerSecond)
                return Reject(request, InteractionFailure.Unauthorized, "Too many interaction requests.");
            client.Expired.Clear();
            foreach (var start in client.Starts)
                if (!start.Key || authenticatedActor.GetTicket(start.Key).Id != start.Value.Operation)
                    client.Expired.Add(start.Key);
            foreach (var expired in client.Expired)
                client.Starts.Remove(expired);
            InteractionReply reply;
            client.Processing = true;
            try
            {
                reply = Execute(authenticatedActor, request, client);
            }
            catch (Exception error)
            {
                Debug.LogException(error, authenticatedActor);
                reply = Reject(request, InteractionFailure.Failed, "Interaction request failed.");
            }
            finally
            {
                client.Processing = false;
            }

            client.Last = request;
            client.Reply = reply;
            return reply;
        }

        private InteractionReply Execute(InteractionActor actor, InteractionRequest request, Client client)
        {
            var target = InteractionWorld.Find(request.TargetId);
            if (!target)
                return Reject(request, InteractionFailure.MissingTarget, "Target is missing or its ID is duplicated.");
            var action = target.FindAction(request.ActionId);
            if (!action)
                return Reject(request, InteractionFailure.MissingTarget, "Action is missing or its ID is duplicated.");
            if (Permission != null && !Permission(actor, action))
                return Reject(request, InteractionFailure.Unauthorized, "Interaction is not permitted.");
            var active = actor.GetTicket(action);
            if (request.Kind == InteractionRequestKind.Start || request.Kind == InteractionRequestKind.Press)
            {
                if (request.Kind == InteractionRequestKind.Press && active.IsActive)
                    return new InteractionReply(request.Sequence, active.Cancel(), InteractionAvailability.Available, active.Id);
                bool accepted = actor.TryBeginInput(action, out var started);
                if (accepted && started.IsActive)
                    client.Starts[action] = new StartRequest
                    {
                        Sequence = request.Sequence,
                        Operation = started.Id
                    };
                return new InteractionReply(request.Sequence, accepted, accepted ? InteractionAvailability.Available : actor.LastFailure, started.Id);
            }

            ulong expected = request.OperationId;
            if (expected == 0 && request.SourceSequence > 0 && client.Starts.TryGetValue(action, out var start) && start.Sequence == request.SourceSequence)
                expected = start.Operation;
            if (!active.IsActive || active.Id != expected)
                return Reject(request, InteractionFailure.StaleRequest, "The action operation has already ended.");
            bool result;
            switch (request.Kind)
            {
                case InteractionRequestKind.Cancel:
                    result = active.Cancel();
                    break;
                case InteractionRequestKind.Pause:
                    result = actor.Pause(action);
                    break;
                case InteractionRequestKind.Resume:
                    result = actor.Resume(action);
                    break;
                case InteractionRequestKind.Value:
                    result = actor.SetValue(action, request.Value);
                    break;
                default:
                    return Reject(request, InteractionFailure.InvalidConfiguration, "Unknown interaction request.");
            }

            return new InteractionReply(request.Sequence, result, result ? InteractionAvailability.Available : InteractionAvailability.Unavailable(InteractionFailure.Busy, "Action cannot accept this command."), active.Id);
        }

        private static InteractionReply Reject(InteractionRequest request, InteractionFailure code, string reason) => new InteractionReply(request.Sequence, false, InteractionAvailability.Unavailable(code, reason));
        public void Disconnect(InteractionActor actor)
        {
            if (ReferenceEquals(actor, null))
                return;
            clients.Remove(actor);
            if (actor)
                actor.Cancel("Participant disconnected.");
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            var actors = new InteractionActor[clients.Count];
            clients.Keys.CopyTo(actors, 0);
            clients.Clear();
            foreach (var actor in actors)
                if (actor)
                    actor.Cancel("Authority stopped.");
        }
    }
}
