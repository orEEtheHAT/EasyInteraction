using System.IO;
using EasyInteraction.Integrations.Netcode;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace EasyInteraction.Verification.Network.Editor
{
    public static class NetworkSmokeBuilder
    {
        public static void Create(string folder)
        {
            Directory.CreateDirectory(folder);
            AssetDatabase.Refresh();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var player = new GameObject("Interaction player");
            player.AddComponent<NetworkObject>();
            var actor = player.AddComponent<InteractionActor>();
            actor.Range = 6;
            actor.RequireVisibility = false;
            player.AddComponent<NetworkInteractionActor>();
            var prefab = PrefabUtility.SaveAsPrefabAsset(player, folder + "/Player.prefab");
            Object.DestroyImmediate(player);
            var manager = new GameObject("Network manager").AddComponent<NetworkManager>();
            var transport = manager.gameObject.AddComponent<UnityTransport>();
            transport.SetConnectionData("127.0.0.1", 17843, "127.0.0.1");
            manager.NetworkConfig.NetworkTransport = transport;
            manager.NetworkConfig.PlayerPrefab = prefab;
            manager.NetworkConfig.EnableSceneManagement = false;
            var smoke = new GameObject("Network smoke").AddComponent<NetworkSmoke>();
            smoke.manager = manager;
            smoke.door = Target<DoorAction>("smoke.door", new Vector3(-1, 0, 2));
            smoke.initialDoor = Target<DoorAction>("smoke.initial", new Vector3(-2, 0, 3));
            smoke.confirmation = Target<NetworkSmokeProbe>("smoke.confirmation", new Vector3(1, 0, 3));
            smoke.confirmation.InputGesture = InteractionInputGesture.HoldToConfirm;
            smoke.confirmation.ConfirmationSeconds = 1;
            smoke.distant = Target<NetworkSmokeProbe>("smoke.distant", new Vector3(100, 0, 0));
            smoke.item = Target<GrabAction>("smoke.item", new Vector3(0, 0, 2));
            smoke.item.Body.constraints = RigidbodyConstraints.FreezeAll;
            smoke.lever = Target<MechanicalAction>("smoke.lever", new Vector3(2, 0, 3));
            smoke.lever.Motion = MechanicalMotion.Angular;
            EditorSceneManager.SaveScene(scene, folder + "/NetworkSmoke.unity");
            AssetDatabase.SaveAssets();
        }

        private static T Target<T>(string id, Vector3 position)
            where T : InteractionAction
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = id;
            go.transform.position = position;
            go.transform.localScale = Vector3.one * .5f;
            var action = go.AddComponent<T>();
            action.ActionId = "use";
            action.Target.PersistentId = id;
            return action;
        }
    }
}
