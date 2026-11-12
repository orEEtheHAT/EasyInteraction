using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace EasyInteraction.Samples.Editor
{
    public static class InteractionSampleBuilder
    {
        [MenuItem("Tools/Easy Interaction/Create Door and Crate Scene")]
        public static void Create()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            CreateScene("Assets/EasyInteractionSample/DoorAndCrate.unity");
        }

        public static void CreateScene(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cameraObject = new GameObject("Interaction Camera", typeof(Camera), typeof(AudioListener));
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = new Vector3(0, 1.5f, -3);
            var camera = cameraObject.GetComponent<Camera>();
            camera.backgroundColor = new Color(.08f, .11f, .17f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            var actor = cameraObject.AddComponent<InteractionActor>();
            actor.View = camera.transform;
            actor.Range = 6;
            cameraObject.AddComponent<InteractionInput>();
            var light = new GameObject("Key light", typeof(Light)).GetComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            light.transform.rotation = Quaternion.Euler(45, -30, 0);
            RenderSettings.ambientLight = new Color(.55f, .55f, .55f);
            Cube("Floor", new Vector3(0, -.15f, 1), new Vector3(10, .3f, 12));
            var hinge = new GameObject("Door - hinge and world state");
            hinge.transform.position = new Vector3(-1.8f, 0, 0);
            var panel = Cube("Door panel", new Vector3(-1.15f, 1.15f, 0), new Vector3(1.3f, 2.3f, .2f));
            panel.transform.SetParent(hinge.transform, true);
            var door = hinge.AddComponent<DoorAction>();
            var crateObject = Cube("Crate - dynamic Rigidbody", new Vector3(.9f, .55f, 0), Vector3.one);
            var body = crateObject.AddComponent<Rigidbody>();
            body.mass = 2;
            var crate = crateObject.AddComponent<GrabAction>();
            crate.GrabRange = 6;
            crate.HoldDistance = 2;
            var wall = Cube("Visibility wall - B toggles", new Vector3(0, 1.5f, -1.2f), new Vector3(5, 3, .2f));
            wall.SetActive(false);
            var stand = new GameObject("Sample controls and hints").AddComponent<InteractionSampleView>();
            stand.actor = actor;
            stand.door = door;
            stand.crate = crate;
            stand.wall = wall;
            EditorSceneManager.SaveScene(scene, path);
            AssetDatabase.Refresh();
            Selection.activeGameObject = cameraObject;
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
