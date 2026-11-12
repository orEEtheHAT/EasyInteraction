using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace EasyInteraction.Editor
{
    public static class InteractionSetup
    {
        [MenuItem("Tools/Easy Interaction/Add Player Components to Selection")]
        public static void AddPlayer()
        {
            if (!Selection.activeGameObject)
            {
                Debug.LogWarning("Select the player root or Camera first.");
                return;
            }

            Add<InteractionActor>(Selection.activeGameObject);
            Add<InteractionInput>(Selection.activeGameObject);
            Add<InteractionPresenter>(Selection.activeGameObject);
        }

        private static T Add<T>(GameObject go)
            where T : Component => go.TryGetComponent<T>(out var component) ? component : Undo.AddComponent<T>(go);
        [MenuItem("GameObject/Easy Interaction/Interaction Camera", false, 10)]
        public static void Camera()
        {
            var go = new GameObject("Interaction Camera", typeof(UnityEngine.Camera), typeof(AudioListener));
            Undo.RegisterCreatedObjectUndo(go, "Create interaction camera");
            go.AddComponent<InteractionActor>().View = go.transform;
            go.AddComponent<InteractionInput>();
            go.AddComponent<InteractionPresenter>();
            Selection.activeGameObject = go;
        }

        [MenuItem("GameObject/Easy Interaction/Grab Item", false, 11)]
        public static void GrabItem()
        {
            var go = Primitive("Grab item");
            go.AddComponent<GrabAction>();
            Selection.activeGameObject = go;
        }

        [MenuItem("GameObject/Easy Interaction/Door", false, 12)]
        public static void Door()
        {
            var root = new GameObject("Door");
            Undo.RegisterCreatedObjectUndo(root, "Create door");
            var panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            panel.name = "Panel";
            panel.transform.SetParent(root.transform, false);
            panel.transform.localPosition = new Vector3(.5f, 1, 0);
            panel.transform.localScale = new Vector3(1, 2, .15f);
            root.AddComponent<DoorAction>();
            Selection.activeGameObject = root;
        }

        [MenuItem("GameObject/Easy Interaction/Socket", false, 13)]
        public static void Socket()
        {
            var root = Primitive("Socket");
            root.transform.localScale = Vector3.one * .5f;
            var anchor = new GameObject("Insert point").transform;
            anchor.SetParent(root.transform, false);
            anchor.localPosition = Vector3.back;
            root.AddComponent<SocketAction>().Anchor = anchor;
            Selection.activeGameObject = root;
        }

        [MenuItem("GameObject/Easy Interaction/Physics Lever", false, 14)]
        public static void PhysicsLever() => Selection.activeGameObject = CreatePhysicsLever(Vector3.zero);
        public static GameObject CreatePhysicsLever(Vector3 position)
        {
            var root = new GameObject("Physics lever");
            root.transform.position = position;
            Undo.RegisterCreatedObjectUndo(root, "Create physics lever");
            var handle = GameObject.CreatePrimitive(PrimitiveType.Cube);
            handle.name = "Handle";
            handle.transform.SetParent(root.transform, false);
            handle.transform.localPosition = Vector3.up * .5f;
            handle.transform.localScale = new Vector3(.15f, 1, .15f);
            var body = root.AddComponent<Rigidbody>();
            body.useGravity = false;
            var joint = root.AddComponent<ConfigurableJoint>();
            joint.autoConfigureConnectedAnchor = false;
            joint.connectedAnchor = position;
            joint.anchor = Vector3.zero;
            joint.xMotion = joint.yMotion = joint.zMotion = ConfigurableJointMotion.Locked;
            joint.angularXMotion = ConfigurableJointMotion.Limited;
            joint.angularYMotion = joint.angularZMotion = ConfigurableJointMotion.Locked;
            joint.lowAngularXLimit = new SoftJointLimit
            {
                limit = -45
            };
            joint.highAngularXLimit = new SoftJointLimit
            {
                limit = 45
            };
            joint.rotationDriveMode = RotationDriveMode.XYAndZ;
            joint.angularXDrive = new JointDrive
            {
                positionSpring = 100,
                positionDamper = 15,
                maximumForce = 1000
            };
            var action = root.AddComponent<MechanicalAction>();
            action.Motion = MechanicalMotion.Angular;
            action.Axis = Vector3.right;
            action.Minimum = -45;
            action.Maximum = 45;
            action.Joint = joint;
            action.SetValue(.5f);
            return root;
        }

        private static GameObject Primitive(string name)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            Undo.RegisterCreatedObjectUndo(go, "Create " + name);
            return go;
        }

        [MenuItem("Tools/Easy Interaction/Validate Open Scenes")]
        public static void ValidateScenes()
        {
            var targets = UnityEngine.Object.FindObjectsByType<InteractionTarget>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            int issues = 0;
            var identities = new System.Collections.Generic.Dictionary<string, InteractionTarget>();
            foreach (var target in targets)
            {
                if (EditorUtility.IsPersistent(target))
                    continue;
                if (!string.IsNullOrEmpty(target.PersistentId))
                {
                    if (identities.ContainsKey(target.PersistentId))
                    {
                        Debug.LogWarning("Duplicate Interaction Target Persistent ID. Assign a new ID to the new instance.", target);
                        issues++;
                    }
                    else
                        identities.Add(target.PersistentId, target);
                }

                var ids = new System.Collections.Generic.HashSet<string>();
                foreach (var action in target.GetComponents<InteractionAction>())
                    if (!ids.Add(action.ActionId))
                    {
                        Debug.LogWarning("Duplicate Action ID: " + action.ActionId, target);
                        issues++;
                    }

                if (target.GetComponentsInChildren<Collider>().Length == 0)
                {
                    Debug.LogWarning("Interaction Target needs a Collider.", target);
                    issues++;
                }
            }

            Debug.Log("EasyInteraction validation: " + targets.Length + " targets, " + issues + " issues.");
        }

        [MenuItem("Tools/Easy Interaction/Import Interaction Gallery")]
        public static void ImportGallery()
        {
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(InteractionActor).Assembly);
            foreach (var sample in UnityEditor.PackageManager.UI.Sample.FindByPackage(package.name, package.version))
                if (sample.displayName == "Interaction Gallery")
                {
                    if (!sample.isImported)
                        sample.Import();
                    Debug.Log("Open " + sample.importPath + "/InteractionGallery.unity after import finishes.");
                    return;
                }
        }
    }
}
