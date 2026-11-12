using System;
using System.Collections;
using System.IO;
using EasyInteraction.Integrations.Netcode;
using Unity.Netcode;
using UnityEngine;

namespace EasyInteraction.Verification.Network
{
    public sealed class NetworkSmoke : MonoBehaviour
    {
        public NetworkManager manager;
        public DoorAction door, initialDoor;
        public NetworkSmokeProbe confirmation, distant;
        public GrabAction item;
        public MechanicalAction lever;
        private bool server, finished, connected, sawHold, sawRejected;
        private string reportPath, step = "startup";
        private float started;
        [Serializable]
        private sealed class Report
        {
            public bool passed, server, connected, initialSnapshot, doorReplicated, fastRelease, outOfRangeRejected, held, mechanicalReplicated, disconnectedCleanup;
            public string failure, unity;
        }

        private Report result;
        private IEnumerator Start()
        {
            Application.runInBackground = true;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;
            var args = Environment.GetCommandLineArgs();
            server = Array.IndexOf(args, "--server") >= 0;
            int pathIndex = Array.IndexOf(args, "--report");
            reportPath = pathIndex >= 0 && pathIndex + 1 < args.Length ? args[pathIndex + 1] : Path.Combine(Application.persistentDataPath, server ? "network-server.json" : "network-client.json");
            result = new Report
            {
                server = server,
                unity = Application.unityVersion
            };
            started = Time.realtimeSinceStartup;
            if (server)
            {
                initialDoor.SetOpen(true);
                manager.OnClientConnectedCallback += id =>
                {
                    if (id != manager.LocalClientId)
                        connected = true;
                };
                manager.OnClientDisconnectCallback += id =>
                {
                    if (id != manager.LocalClientId && sawHold)
                        StartCoroutine(CheckCleanup());
                };
                if (!manager.StartHost())
                {
                    Finish("Host did not start.");
                    yield break;
                }

                step = "waiting for client and held item";
                while (!finished)
                {
                    if (item.IsHeld && item.OwnerCount == 1 && item.Owner.GetComponent<NetworkObject>().OwnerClientId != manager.LocalClientId)
                    {
                        sawHold = true;
                        var localActor = manager.LocalClient.PlayerObject.GetComponent<InteractionActor>();
                        if (localActor.TryExecuteDirect(item))
                        {
                            Finish("Exclusive item accepted a second participant.");
                            yield break;
                        }
                    }

                    yield return null;
                }

                yield break;
            }

            if (!manager.StartClient())
            {
                Finish("Client did not start.");
                yield break;
            }

            step = "connection";
            while (!manager.IsConnectedClient || manager.LocalClient?.PlayerObject == null)
                yield return null;
            connected = true;
            var network = manager.LocalClient.PlayerObject.GetComponent<NetworkInteractionActor>();
            step = "initial snapshot";
            while (!initialDoor.IsOpen)
                yield return null;
            result.initialSnapshot = true;
            step = "door request and replicated state";
            ulong request = network.Submit(door, InteractionRequestKind.Start);
            while (network.LastReply.Sequence < request || !door.IsOpen)
                yield return null;
            if (!network.LastReply.Accepted)
            {
                Finish("Door request rejected.");
                yield break;
            }

            result.doorReplicated = true;
            step = "release before acknowledgement";
            ulong start = network.Submit(confirmation, InteractionRequestKind.Start);
            ulong cancel = network.Submit(confirmation, InteractionRequestKind.Cancel, sourceSequence: start);
            while (network.LastReply.Sequence < cancel)
                yield return null;
            if (!network.LastReply.Accepted)
            {
                Finish("Fast release rejected: " + network.LastReply.Result.Reason);
                yield break;
            }

            yield return new WaitForSecondsRealtime(confirmation.ConfirmationSeconds + .25f);
            if (network.IsRunning(confirmation))
            {
                Finish("Fast release left a running action.");
                yield break;
            }

            result.fastRelease = true;
            step = "server geometry validation";
            ulong invalid = network.Submit(distant, InteractionRequestKind.Start);
            while (network.LastReply.Sequence < invalid)
                yield return null;
            sawRejected = !network.LastReply.Accepted && network.LastReply.Result.Code == InteractionFailure.OutOfRange;
            result.outOfRangeRejected = sawRejected;
            if (!sawRejected)
            {
                Finish("Server accepted an out-of-range action.");
                yield break;
            }

            step = "live mechanical state";
            ulong adjust = network.Submit(lever, InteractionRequestKind.Start);
            while (network.LastReply.Sequence < adjust || !network.IsRunning(lever))
                yield return null;
            network.Submit(lever, InteractionRequestKind.Value, network.Operation(lever), 1);
            while (lever.Value < .1f)
                yield return null;
            result.mechanicalReplicated = true;
            ulong stopAdjust = network.Submit(lever, InteractionRequestKind.Cancel, network.Operation(lever));
            while (network.LastReply.Sequence < stopAdjust || network.IsRunning(lever))
                yield return null;
            step = "authoritative grab";
            ulong grab = network.Submit(item, InteractionRequestKind.Start);
            while (network.LastReply.Sequence < grab || !network.IsRunning(item))
                yield return null;
            if (!network.LastReply.Accepted)
            {
                Finish("Grab rejected.");
                yield break;
            }

            result.held = true;
            yield return new WaitForSecondsRealtime(.5f);
            step = "client disconnect";
            manager.Shutdown();
            yield return new WaitForSecondsRealtime(.5f);
            Finish(null);
        }

        private IEnumerator CheckCleanup()
        {
            yield return null;
            yield return null;
            result.initialSnapshot = initialDoor.IsOpen;
            result.doorReplicated = door.IsOpen;
            result.fastRelease = confirmation.Executions == 0;
            result.outOfRangeRejected = distant.Executions == 0;
            result.held = sawHold;
            result.mechanicalReplicated = lever.Value >= .1f;
            result.disconnectedCleanup = !item.IsHeld && item.OwnerCount == 0 && item.Body.useGravity;
            Finish(connected && result.initialSnapshot && result.doorReplicated && result.fastRelease && result.outOfRangeRejected && result.mechanicalReplicated && result.held && result.disconnectedCleanup ? null : "Server state or disconnect cleanup failed.");
        }

        private void Update()
        {
            if (!finished && result != null && Time.realtimeSinceStartup - started > 60)
                Finish("Timeout: " + step);
        }

        private void Finish(string failure)
        {
            if (finished)
                return;
            finished = true;
            result.connected = connected;
            result.failure = failure ?? "";
            result.passed = failure == null;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath)));
            File.WriteAllText(reportPath, JsonUtility.ToJson(result, true));
            if (manager && manager.IsListening)
                manager.Shutdown();
            Application.Quit(result.passed ? 0 : 1);
        }
    }
}
