using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.LiveCapture;
using Unity.LiveCapture.CompanionApp;
using Unity.LiveCapture.VirtualCamera;

namespace HandheldCamera.Editor
{
    [InitializeOnLoad]
    public static class HandheldProjectSetup
    {
        const string ScenePath = "Assets/Scenes/HandheldCameraTest.unity";
        const string LibraryPath = "Assets/Recordings/HandheldShots.asset";

        static HandheldProjectSetup()
        {
            EditorApplication.delayCall += PrepareIfMissing;
        }

        static void PrepareIfMissing()
        {
            ConfigureMonitorOnlyRendering();
            if (!ConnectionManager.Instance.TryGetConnection<CompanionAppServer>(out _))
                ConnectionManager.Instance.CreateConnection(typeof(CompanionAppServer));
            if (!File.Exists(ScenePath) && !EditorApplication.isPlayingOrWillChangePlaymode)
                Prepare();
        }

        static void ConfigureMonitorOnlyRendering()
        {
            // Live Capture's legacy streaming pass cannot run on URP 17 Render Graph.
            // The MVP uses the PC monitor, so leave that pass disabled.
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath("Assets/Settings/PC_Renderer.asset"))
            {
                if (asset is UnityEngine.Rendering.Universal.ScriptableRendererFeature feature &&
                    feature.GetType().FullName == "Unity.LiveCapture.InjectionPointRenderFeature" && feature.isActive)
                {
                    feature.SetActive(false);
                    EditorUtility.SetDirty(feature);
                    AssetDatabase.SaveAssets();
                    Debug.Log("Handheld: legacy Live Capture video streaming pass disabled for monitor-only capture.");
                }
            }
        }

        [MenuItem("Handheld Camera/Prepare Test Scene")]
        public static void Prepare()
        {
            if (File.Exists(ScenePath))
            {
                Debug.Log("Handheld test scene already exists: " + ScenePath);
                return;
            }

            Directory.CreateDirectory("Assets/Scenes");
            Directory.CreateDirectory("Assets/Recordings");
            Directory.CreateDirectory("Assets/Materials/Handheld");
            AssetDatabase.Refresh();
            var previousScene = SceneManager.GetActiveScene();
            var previousSelection = Selection.activeObject;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            try
            {
                RenderSettings.ambientLight = new Color(.45f, .45f, .45f);
                var light = new GameObject("Key Light", typeof(Light)).GetComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 2;
                light.transform.rotation = Quaternion.Euler(45, -30, 0);

                var floor = Material("Floor", new Color(.14f, .17f, .2f));
                var grid = Material("Meter Grid", new Color(.55f, .6f, .65f));
                var red = Material("X Red", new Color(.8f, .15f, .12f));
                var blue = Material("Z Blue", new Color(.12f, .35f, .85f));
                Cube("Floor 12m", new Vector3(0, -.06f, 0), new Vector3(12, .1f, 12), floor);
                for (var i = -6; i <= 6; i++)
                {
                    Cube("Grid X " + i, new Vector3(0, 0, i), new Vector3(12, .008f, .015f), i == 0 ? red : grid);
                    Cube("Grid Z " + i, new Vector3(i, 0, 0), new Vector3(.015f, .008f, 12), i == 0 ? blue : grid);
                }
                Cube("One Meter Reference", new Vector3(-2, .5f, 0), Vector3.one, red);
                Cube("Two Meter Reference", new Vector3(2, 1, 2), new Vector3(1, 2, 1), blue);
                Cube("Foreground Reference", new Vector3(0, .5f, -2), Vector3.one, grid);

                Selection.activeTransform = null;
                if (!EditorApplication.ExecuteMenuItem("GameObject/Live Capture/Camera/Virtual Camera Actor"))
                    throw new System.InvalidOperationException("Live Capture camera creation menu is unavailable.");
                var cameraObject = Selection.activeGameObject;
                cameraObject.name = "iPhone Camera";
                cameraObject.tag = "MainCamera";
                cameraObject.transform.position = new Vector3(0, 1.6f, -6);
                cameraObject.transform.rotation = Quaternion.identity;
                var camera = cameraObject.GetComponent<Camera>();
                camera.usePhysicalProperties = true;
                camera.nearClipPlane = .1f;
                camera.farClipPlane = 1000;
                cameraObject.AddComponent<AudioListener>();

                var device = new GameObject("iPhone Capture", typeof(VirtualCameraDevice)).GetComponent<VirtualCameraDevice>();
                device.Actor = cameraObject.GetComponent<VirtualCameraActor>();
                var serializedDevice = new SerializedObject(device);
                serializedDevice.FindProperty("m_Settings.MotionScale").vector3Value = Vector3.one;
                serializedDevice.FindProperty("m_Settings.Damping.Enabled").boolValue = false;
                serializedDevice.ApplyModifiedPropertiesWithoutUndo();

                var library = AssetDatabase.LoadAssetAtPath<ShotLibrary>(LibraryPath);
                if (library == null)
                {
                    library = ScriptableObject.CreateInstance<ShotLibrary>();
                    library.Shots = new[] { new Shot { Name = "HandheldTest", SceneNumber = 1, TakeNumber = 1, Directory = "Assets/Recordings", Duration = 10 } };
                    AssetDatabase.CreateAsset(library, LibraryPath);
                }
                var player = new GameObject("Take Playback", typeof(ShotPlayer)).GetComponent<ShotPlayer>();
                player.ShotLibrary = library;
                player.Selection = 0;
                TakeRecorder.FrameRate = StandardFrameRate.FPS_60_00;

                // The user enables the server when ready to connect the phone.
                EditorSceneManager.SaveScene(scene, ScenePath);
                EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
                AssetDatabase.SaveAssets();
                Directory.CreateDirectory("Logs");
                File.WriteAllText("Logs/HandheldSetup.txt", "Setup completed in Unity " + Application.unityVersion + "\nLive Capture 4.0.1\nCamera + device + shot library created\nMotion scale 1:1, damping disabled, capture 60 fps\nPhone connection and recording require device validation.\n");
                Debug.Log("Handheld test scene prepared: " + ScenePath);
            }
            finally
            {
                if (previousScene.IsValid()) SceneManager.SetActiveScene(previousScene);
                EditorSceneManager.CloseScene(scene, true);
                Selection.activeObject = previousSelection;
            }
        }

        static Material Material(string name, Color color)
        {
            var path = "Assets/Materials/Handheld/" + name + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name, color = color };
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        static void Cube(string name, Vector3 position, Vector3 scale, Material material)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.position = position;
            cube.transform.localScale = scale;
            cube.GetComponent<Renderer>().sharedMaterial = material;
        }
    }
}
