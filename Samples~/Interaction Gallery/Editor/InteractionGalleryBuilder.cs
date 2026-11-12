using System.IO;
using EasyInteraction.UI;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace EasyInteraction.Samples.Gallery.Editor
{
    public static class InteractionGalleryBuilder
    {
        [MenuItem("Tools/Easy Interaction/Create Interaction Gallery")]
        public static void Create()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            Directory.CreateDirectory("Assets/EasyInteractionGallery");
            CreateScene(AssetDatabase.GenerateUniqueAssetPath("Assets/EasyInteractionGallery/InteractionGallery.unity"));
        }

        public static void CreateScene(string path)
        {
            string folder = Path.GetDirectoryName(path).Replace('\\', '/');
            Directory.CreateDirectory(folder);
            AssetDatabase.Refresh();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cameraObject = new GameObject("Interaction Camera", typeof(Camera), typeof(AudioListener));
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = new Vector3(0, 1.5f, -4);
            var camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.045f, .07f, .11f);
            var actor = cameraObject.AddComponent<InteractionActor>();
            actor.View = camera.transform;
            actor.CursorCamera = camera;
            actor.Range = 8;
            actor.SelectionMode = InteractionSelectionMode.Focus;
            actor.FocusAngle = 80;
            cameraObject.AddComponent<InteractionInput>();
            cameraObject.AddComponent<InteractionPresenter>();
            var hud = cameraObject.AddComponent<InteractionHUD>();
            var sun = new GameObject("Light", typeof(Light)).GetComponent<Light>();
            sun.type = LightType.Directional;
            sun.transform.rotation = Quaternion.Euler(45, -30, 0);
            sun.intensity = 1.2f;
            RenderSettings.ambientLight = Color.gray;
            Cube("Floor", new Vector3(0, -.15f, 5), new Vector3(18, .3f, 22));
            var doorRoot = new GameObject("Door");
            doorRoot.transform.position = new Vector3(-2.5f, 0, 0);
            var panel = Cube("Door panel", new Vector3(-1.9f, 1.1f, 0), new Vector3(1.2f, 2.2f, .15f));
            panel.transform.SetParent(doorRoot.transform, true);
            var door = doorRoot.AddComponent<DoorAction>();
            door.ActionId = "door";
            Identity(door.Target, "gallery.door", "Door");
            var crateObject = Cube("Battery crate", new Vector3(.4f, .55f, 0), Vector3.one);
            crateObject.AddComponent<Rigidbody>().mass = 2;
            var grab = crateObject.AddComponent<GrabAction>();
            grab.GrabRange = 8;
            grab.ActionId = "grab";
            crateObject.AddComponent<Socketable>().category = "battery";
            crateObject.AddComponent<InspectAction>().ActionId = "inspect";
            var light = crateObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 4;
            light.intensity = 2;
            light.enabled = false;
            var use = crateObject.AddComponent<EventAction>();
            use.ActionId = "use";
            use.ActionLabel = "Switch light";
            use.Channel = "";
            var toggle = crateObject.AddComponent<GalleryLightSwitch>();
            toggle.lightSource = light;
            UnityEventTools.AddPersistentListener(use.OnExecuteEvent, toggle.Toggle);
            Identity(grab.Target, "gallery.item", "Battery crate");
            var socketObject = Cube("Battery socket", new Vector3(2.5f, .65f, 0), new Vector3(1.2f, 1.3f, .4f));
            var socket = socketObject.AddComponent<SocketAction>();
            socket.ActionId = "socket";
            socket.AcceptedCategories = new[]
            {
                "battery"
            };
            var anchor = new GameObject("Insert point").transform;
            anchor.SetParent(socketObject.transform, false);
            anchor.localPosition = Vector3.back * 2;
            socket.Anchor = anchor;
            Identity(socket.Target, "gallery.socket", "Battery socket");
            var timedObject = Cube("Service panel", new Vector3(-4, 1.1f, 5), new Vector3(1.4f, 1.4f, .25f));
            var instant = timedObject.AddComponent<EventAction>();
            instant.ActionId = "press";
            instant.ActionLabel = "Press button";
            var repair = timedObject.AddComponent<EventAction>();
            repair.ActionId = "repair";
            repair.ActionLabel = "Repair";
            repair.InputGesture = InteractionInputGesture.HoldToConfirm;
            repair.Execution = InteractionExecutionMode.Timed;
            repair.Duration = 3;
            repair.RequireRangeWhileRunning = true;
            var animation = timedObject.AddComponent<EventAction>();
            animation.ActionId = "animate";
            animation.ActionLabel = "Animated press";
            animation.Execution = InteractionExecutionMode.AnimationEvent;
            animation.Timeout = 3;
            var sequence = timedObject.AddComponent<SequenceAction>();
            sequence.ActionId = "sequence";
            sequence.ActionLabel = "Run sequence";
            sequence.Stages = new[]
            {
                new InteractionStage
                {
                    label = "Prepare",
                    seconds = .6f
                },
                new InteractionStage
                {
                    label = "Work",
                    seconds = 1.5f
                },
                new InteractionStage
                {
                    label = "Finish",
                    seconds = .4f
                }
            };
            Identity(instant.Target, "gallery.panel", "Service panel");
            var leverObject = EasyInteraction.Editor.InteractionSetup.CreatePhysicsLever(new Vector3(0, .5f, 5));
            var lever = leverObject.GetComponent<MechanicalAction>();
            lever.ActionId = "lever";
            Identity(lever.Target, "gallery.lever", "Physics lever");
            Cube("Lever base", new Vector3(0, .25f, 5), new Vector3(1, .5f, 1));
            var drawerRoot = new GameObject("Drawer");
            drawerRoot.transform.position = new Vector3(2, 1, 5);
            var drawerPanel = Cube("Drawer panel", drawerRoot.transform.position, new Vector3(1.3f, .5f, .8f));
            drawerPanel.transform.SetParent(drawerRoot.transform, true);
            var drawer = drawerRoot.AddComponent<MechanicalAction>();
            drawer.ActionId = "drawer";
            drawer.MovingPart = drawerPanel.transform;
            drawer.Motion = MechanicalMotion.Linear;
            drawer.Axis = Vector3.back;
            drawer.Minimum = 0;
            drawer.Maximum = .8f;
            Identity(drawer.Target, "gallery.drawer", "Drawer");
            var station = Cube("Cooperative generator", new Vector3(4, 1, 9), new Vector3(1.7f, 2, .8f));
            var cooperative = station.AddComponent<CooperativeAction>();
            cooperative.ActionId = "work";
            cooperative.MinimumParticipants = 2;
            cooperative.WorkSeconds = 6;
            Identity(cooperative.Target, "gallery.work", "Cooperative generator");
            var playerPlace = Station("Player position", new Vector3(4, 1.5f, 6), Vector3.zero);
            playerPlace.SetParent(station.transform, true);
            playerPlace.gameObject.AddComponent<InteractionPoint>().participantPosition = true;
            var helperPlace = Station("Helper position", new Vector3(5.4f, 1.2f, 7.5f), Vector3.zero);
            helperPlace.SetParent(station.transform, true);
            helperPlace.gameObject.AddComponent<InteractionPoint>().participantPosition = true;
            var helper = new GameObject("Helper actor").AddComponent<InteractionActor>();
            helper.transform.position = new Vector3(5.4f, 1.2f, 7.5f);
            helper.View = helper.transform;
            helper.Range = 5;
            helper.SelectTarget(cooperative.Target);
            var wall = Cube("Visibility wall", new Vector3(0, 1.5f, -1.8f), new Vector3(8, 3, .2f));
            wall.SetActive(false);
            var hand = Cube("Animated hand", Vector3.zero, new Vector3(.12f, .12f, .35f));
            hand.transform.SetParent(cameraObject.transform, false);
            hand.transform.localPosition = new Vector3(.35f, -.3f, .65f);
            Object.DestroyImmediate(hand.GetComponent<Collider>());
            var animator = hand.AddComponent<Animator>();
            animator.runtimeAnimatorController = CreateAnimation(folder);
            var bridge = hand.AddComponent<InteractionAnimationBridge>();
            bridge.Actor = actor;
            bridge.Animator = animator;
            var controls = new GameObject("Gallery controls").AddComponent<InteractionGalleryControls>();
            controls.actor = actor;
            controls.helper = helper;
            controls.door = door;
            controls.item = grab;
            controls.cooperative = cooperative;
            controls.wall = wall;
            controls.hud = hud;
            controls.stations = new[]
            {
                Station("F1 - door and item", new Vector3(0, 1.5f, -4), Vector3.zero),
                Station("F2 - actions", new Vector3(-4, 1.5f, 2), Vector3.zero),
                Station("F3 - mechanics", new Vector3(.7f, 1.6f, 2), Vector3.zero),
                Station("F4 - cooperation", new Vector3(4, 1.5f, 6), Vector3.zero)
            };
            Directory.CreateDirectory(folder + "/Prefabs");
            AssetDatabase.Refresh();
            PrefabUtility.SaveAsPrefabAsset(cameraObject, folder + "/Prefabs/InteractionCamera.prefab");
            PrefabUtility.SaveAsPrefabAsset(doorRoot, folder + "/Prefabs/Door.prefab");
            PrefabUtility.SaveAsPrefabAsset(crateObject, folder + "/Prefabs/CarryItem.prefab");
            PrefabUtility.SaveAsPrefabAsset(socketObject, folder + "/Prefabs/Socket.prefab");
            EditorSceneManager.SaveScene(scene, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeGameObject = cameraObject;
        }

        private static RuntimeAnimatorController CreateAnimation(string folder)
        {
            string path = folder + "/InteractionHand.controller";
            var existing = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (existing)
                return existing;
            var clip = new AnimationClip
            {
                name = "Press"
            };
            clip.SetCurve("", typeof(Transform), "m_LocalPosition.z", AnimationCurve.EaseInOut(0, .65f, .55f, .9f));
            AnimationUtility.SetAnimationEvents(clip, new[] { new AnimationEvent { time = .55f, functionName = "CommitInteraction" } });
            AssetDatabase.CreateAsset(clip, folder + "/Press.anim");
            var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            controller.AddParameter("Interact", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("CancelInteraction", AnimatorControllerParameterType.Trigger);
            var machine = controller.layers[0].stateMachine;
            var idle = machine.AddState("Idle");
            machine.defaultState = idle;
            var active = machine.AddState("Press");
            active.motion = clip;
            var enter = idle.AddTransition(active);
            enter.hasExitTime = false;
            enter.duration = .05f;
            enter.AddCondition(AnimatorConditionMode.If, 0, "Interact");
            var leave = active.AddTransition(idle);
            leave.hasExitTime = true;
            leave.exitTime = 1;
            leave.duration = .05f;
            var cancel = active.AddTransition(idle);
            cancel.hasExitTime = false;
            cancel.duration = .05f;
            cancel.AddCondition(AnimatorConditionMode.If, 0, "CancelInteraction");
            return controller;
        }

        private static Transform Station(string name, Vector3 position, Vector3 rotation)
        {
            var go = new GameObject(name);
            go.transform.position = position;
            go.transform.eulerAngles = rotation;
            return go.transform;
        }

        private static void Identity(InteractionTarget target, string id, string label)
        {
            target.PersistentId = id;
            target.DisplayName = label;
        }

        private static GameObject Cube(string name, Vector3 position, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.position = position;
            go.transform.localScale = scale;
            return go;
        }
    }
}
