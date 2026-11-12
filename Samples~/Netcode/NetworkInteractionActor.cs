using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Netcode;
using UnityEngine;

namespace EasyInteraction.Integrations.Netcode
{
    [DisallowMultipleComponent, RequireComponent(typeof(NetworkObject), typeof(InteractionActor), typeof(NetcodeInteractionCommands))]
    [AddComponentMenu("Easy Interaction/Integrations/Network Interaction Actor")]
    public sealed class NetworkInteractionActor : NetworkBehaviour
    {
        [SerializeField, Min(.05f)]
        private float updateInterval = .1f;
        private InteractionActor actor;
        private InteractionAuthority authority;
        private ulong sequence;
        private bool oldAutomatic, oldInputEnabled;
        private float previewAt;
        private readonly Dictionary<InteractionAction, RemoteState> remote = new Dictionary<InteractionAction, RemoteState>();
        private readonly Dictionary<InteractionAction, float> progressAt = new Dictionary<InteractionAction, float>();
        private readonly List<InteractionTarget> snapshotTargets = new List<InteractionTarget>();
        private readonly List<PendingState> pending = new List<PendingState>();
        private InteractionAction previewAction;
        private InteractionAvailability preview = InteractionAvailability.Available;
        public InteractionReply LastReply { get; private set; }

        public event Action<InteractionReply> Replied;
        private struct RemoteState
        {
            public ulong Operation;
            public bool Running;
            public float Progress;
        }

        private struct PendingState
        {
            public string Target, Json;
            public ulong Revision;
            public float Expires;
        }

        private sealed class WorldVersions
        {
            public WorldVersions()
            {
            }

            public ulong Next;
            public readonly Dictionary<string, ulong> Applied = new Dictionary<string, ulong>();
        }

        private static readonly ConditionalWeakTable<NetworkManager, WorldVersions> versions = new ConditionalWeakTable<NetworkManager, WorldVersions>();
        private void Awake()
        {
            actor = GetComponent<InteractionActor>();
            oldAutomatic = actor.AutomaticUpdate;
            actor.AutomaticUpdate = false;
            var commands = GetComponent<NetcodeInteractionCommands>();
            commands.Network = this;
            actor.CommandAdapter = commands;
            var input = GetComponent<InteractionInput>();
            if (input)
            {
                oldInputEnabled = input.enabled;
                input.enabled = false;
            }

            ConfigurePresentation(false);
        }

        private void ConfigurePresentation(bool local)
        {
            foreach (var component in GetComponentsInChildren<MonoBehaviour>(true))
                if (component is IInteractionLocalView view)
                    view.SetLocalActive(local);
            foreach (var camera in GetComponentsInChildren<Camera>(true))
                camera.enabled = local;
            foreach (var listener in GetComponentsInChildren<AudioListener>(true))
                listener.enabled = local;
        }

        public override void OnNetworkSpawn()
        {
            actor = GetComponent<InteractionActor>();
            actor.AutomaticUpdate = IsServer;
            var commands = GetComponent<NetcodeInteractionCommands>();
            commands.Network = this;
            actor.CommandAdapter = commands;
            var input = GetComponent<InteractionInput>();
            if (input)
                input.enabled = IsOwner && oldInputEnabled;
            ConfigurePresentation(IsOwner);
            if (IsServer)
            {
                authority = new InteractionAuthority();
                actor.ActionStateChanged += StateChanged;
            }

            if (IsOwner)
                RequestSnapshotRpc();
        }

        public override void OnNetworkDespawn()
        {
            if (actor)
            {
                actor.ActionStateChanged -= StateChanged;
                actor.AutomaticUpdate = oldAutomatic;
                actor.Cancel("Network participant disconnected.");
                actor.CommandAdapter = null;
            }

            var input = GetComponent<InteractionInput>();
            if (input)
                input.enabled = oldInputEnabled;
            authority?.Dispose();
            authority = null;
            remote.Clear();
            progressAt.Clear();
            pending.Clear();
        }

        private void Update()
        {
            if (!IsSpawned || !actor)
                return;
            if (IsOwner && !IsServer)
                actor.RefreshTarget();
            if (IsOwner && Time.unscaledTime >= previewAt)
            {
                previewAt = Time.unscaledTime + Mathf.Max(.1f, updateInterval);
                var selected = actor.SelectedAction;
                if (selected && !string.IsNullOrEmpty(selected.Target.PersistentId))
                    PreviewRpc(selected.Target.PersistentId, selected.ActionId);
            }

            for (int i = pending.Count - 1; i >= 0; i--)
            {
                var state = pending[i];
                if (ApplyWorld(state.Target, state.Revision, state.Json) || Time.unscaledTime >= state.Expires)
                    pending.RemoveAt(i);
            }
        }

        public bool Send(InteractionAction action, InteractionRequestKind kind, ulong operation = 0, float value = 0) => Submit(action, kind, operation, value) != 0;
        public ulong Submit(InteractionAction action, InteractionRequestKind kind, ulong operation = 0, float value = 0, ulong sourceSequence = 0)
        {
            if (!IsSpawned || !IsOwner || !action || string.IsNullOrEmpty(action.Target.PersistentId))
                return 0;
            ulong next = ++sequence;
            RequestRpc(next, action.Target.PersistentId, action.ActionId, (byte)kind, operation, value, sourceSequence);
            return next;
        }

        public bool IsRunning(InteractionAction action) => action && (IsServer ? actor.GetTicket(action).IsActive : remote.TryGetValue(action, out var state) && state.Running);
        public float Progress(InteractionAction action) => action ? (IsServer ? action.GetProgress(actor) : remote.TryGetValue(action, out var state) ? state.Progress : 0) : 0;
        public ulong Operation(InteractionAction action) => action ? (IsServer ? actor.GetTicket(action).Id : remote.TryGetValue(action, out var state) ? state.Operation : 0) : 0;
        public InteractionAction FirstRunning()
        {
            if (IsServer)
                return actor.ActiveAction;
            foreach (var entry in remote)
                if (entry.Key && entry.Value.Running)
                    return entry.Key;
            return null;
        }

        public InteractionAvailability Check(InteractionAction action) => IsRunning(action) ? InteractionAvailability.Available : action == previewAction ? preview : InteractionAvailability.Available;
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void RequestRpc(ulong id, string targetId, string actionId, byte kind, ulong operation, float value, ulong sourceSequence, RpcParams rpc = default)
        {
            if (!IsServer || rpc.Receive.SenderClientId != OwnerClientId)
                return;
            var reply = authority.Process(actor, new InteractionRequest(id, targetId, actionId, (InteractionRequestKind)kind, operation, value, sourceSequence));
            ReplyRpc(reply.Sequence, reply.Accepted, (byte)reply.Result.Code, reply.Result.Reason, reply.OperationId);
        }

        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        private void ReplyRpc(ulong id, bool accepted, byte code, string reason, ulong operation)
        {
            LastReply = new InteractionReply(id, accepted, accepted ? InteractionAvailability.Available : InteractionAvailability.Unavailable((InteractionFailure)code, reason), operation);
            try
            {
                Replied?.Invoke(LastReply);
            }
            catch (Exception error)
            {
                Debug.LogException(error, this);
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void PreviewRpc(string targetId, string actionId, RpcParams rpc = default)
        {
            if (!IsServer || rpc.Receive.SenderClientId != OwnerClientId || targetId == null || targetId.Length > 128 || actionId == null || actionId.Length > 128)
                return;
            var target = InteractionWorld.Find(targetId);
            var action = target ? target.FindAction(actionId) : null;
            var check = action ? (actor.GetTicket(action).IsActive ? InteractionAvailability.Available : actor.CheckDirect(action)) : InteractionAvailability.Unavailable(InteractionFailure.MissingTarget, "Object is unavailable.");
            PreviewResultRpc(targetId, actionId, check.IsAvailable, (byte)check.Code, check.Reason);
        }

        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        private void PreviewResultRpc(string targetId, string actionId, bool allowed, byte code, string reason)
        {
            var target = InteractionWorld.Find(targetId);
            previewAction = target ? target.FindAction(actionId) : null;
            preview = allowed ? InteractionAvailability.Available : InteractionAvailability.Unavailable((InteractionFailure)code, reason);
        }

        private void StateChanged(InteractionActionEvent e)
        {
            if (!IsServer || !IsSpawned || !e.Action || string.IsNullOrEmpty(e.Action.Target.PersistentId))
                return;
            if (e.State == InteractionActionState.Progress)
            {
                if (progressAt.TryGetValue(e.Action, out float next) && Time.unscaledTime < next)
                    return;
                progressAt[e.Action] = Time.unscaledTime + updateInterval;
            }

            string json = "";
            ulong revision = 0;
            if (e.State == InteractionActionState.Committed || e.State == InteractionActionState.Progress || e.State == InteractionActionState.Completed || e.State == InteractionActionState.Cancelled)
                CaptureWorld(e.Action.Target, out revision, out json);
            StateRpc(e.Action.Target.PersistentId, e.Action.ActionId, e.Ticket.Id, (byte)e.State, e.Progress, revision, json);
        }

        [Rpc(SendTo.NotServer, InvokePermission = RpcInvokePermission.Server)]
        private void StateRpc(string targetId, string actionId, ulong operation, byte state, float progress, ulong revision, string json)
        {
            var target = InteractionWorld.Find(targetId);
            var action = target ? target.FindAction(actionId) : null;
            if (action)
            {
                var status = (InteractionActionState)state;
                bool running = status != InteractionActionState.Completed && status != InteractionActionState.Cancelled && status != InteractionActionState.Failed;
                if (!remote.TryGetValue(action, out var existing) || operation >= existing.Operation)
                    remote[action] = new RemoteState
                    {
                        Operation = operation,
                        Progress = progress,
                        Running = running
                    };
            }

            ReceiveWorld(targetId, revision, json);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void RequestSnapshotRpc(RpcParams rpc = default)
        {
            if (!IsServer || rpc.Receive.SenderClientId != OwnerClientId)
                return;
            InteractionWorld.CopyTargets(snapshotTargets);
            for (int i = 0; i < snapshotTargets.Count; i++)
            {
                var target = snapshotTargets[i];
                CaptureWorld(target, out var revision, out var json);
                if (!string.IsNullOrEmpty(json))
                    SnapshotRpc(target.PersistentId, revision, json);
            }
        }

        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        private void SnapshotRpc(string targetId, ulong revision, string json)
        {
            if (!IsServer)
                ReceiveWorld(targetId, revision, json);
        }

        private void CaptureWorld(InteractionTarget target, out ulong revision, out string json)
        {
            revision = 0;
            json = "";
            bool stateful = false;
            for (int i = 0; i < target.Actions.Count; i++)
                if (target.Actions[i] is IInteractionState)
                {
                    stateful = true;
                    break;
                }

            if (!stateful)
                return;
            try
            {
                json = JsonUtility.ToJson(target.CaptureState(false));
                revision = ++versions.GetOrCreateValue(NetworkManager).Next;
            }
            catch (Exception error)
            {
                Debug.LogException(error, target);
            }
        }

        public void PublishTargetState(InteractionTarget target)
        {
            if (!IsServer || !IsSpawned || !target)
                return;
            CaptureWorld(target, out var revision, out var json);
            if (revision != 0)
                WorldStateRpc(target.PersistentId, revision, json);
        }

        [Rpc(SendTo.NotServer, InvokePermission = RpcInvokePermission.Server)]
        private void WorldStateRpc(string targetId, ulong revision, string json) => ReceiveWorld(targetId, revision, json);
        private void ReceiveWorld(string targetId, ulong revision, string json)
        {
            if (string.IsNullOrEmpty(json) || revision == 0 || IsServer)
                return;
            if (ApplyWorld(targetId, revision, json))
                return;
            for (int i = 0; i < pending.Count; i++)
                if (pending[i].Target == targetId)
                {
                    if (pending[i].Revision < revision)
                        pending[i] = new PendingState
                        {
                            Target = targetId,
                            Revision = revision,
                            Json = json,
                            Expires = Time.unscaledTime + 10
                        };
                    return;
                }

            pending.Add(new PendingState { Target = targetId, Revision = revision, Json = json, Expires = Time.unscaledTime + 10 });
        }

        private bool ApplyWorld(string targetId, ulong revision, string json)
        {
            var table = versions.GetOrCreateValue(NetworkManager);
            if (table.Applied.TryGetValue(targetId, out var current) && revision <= current)
                return true;
            var target = InteractionWorld.Find(targetId);
            if (!target)
                return false;
            try
            {
                var state = JsonUtility.FromJson<InteractionTargetState>(json);
                if (!target.TryRestoreState(state, out _, false))
                    return false;
                table.Applied[targetId] = revision;
                return true;
            }
            catch (Exception error)
            {
                Debug.LogException(error, target);
                return true;
            }
        }
    }
}
