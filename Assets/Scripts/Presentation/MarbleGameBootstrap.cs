using System;
using MarblesECS.PhysX;
using UnityEngine;

namespace MarblesECS.Presentation
{
    /// <summary>A saved scene needs only this component. All gameplay remains in the ECS world.</summary>
    [DefaultExecutionOrder(-200)]
    public sealed class MarbleGameBootstrap : MonoBehaviour
    {
        public TextAsset BalanceJson;
        public Material SurfaceMaterial;
        public bool CreateCamera = true;
        public MarbleGameController Controller { get; private set; }
        public MarbleBoardView BoardView { get; private set; }
        private GameObject boardObject;
        private GameObject viewObject;
        private MarbleVisualPalette palette;

        private void Awake()
        {
            try
            {
                GameBalance balance = GameBalanceLoader.Load(BalanceJson);
                palette = new MarbleVisualPalette(SurfaceMaterial);
                boardObject = new GameObject("Blood Marble / physical board");
                Transform board = boardObject.transform;
                board.position = new Vector3(0, 4, 0);
                board.rotation = Quaternion.Euler(-balance.Board.TiltDegrees, 0, 0);
                Transform launcher = MarbleBoardGeometry.Create(board, balance, palette);
                Controller = gameObject.AddComponent<MarbleGameController>();
                Controller.ConfigureCampaign(balance);
                Controller.LaunchPoint = launcher;
                Controller.PhysicsRoot = board;
                var marbleTemplate = MarbleBoardGeometry.Primitive("Blood marble template", PrimitiveType.Sphere,
                    transform, Vector3.zero, Vector3.one, palette.Blood);
                marbleTemplate.SetActive(false);
                Controller.MarblePrefab = marbleTemplate;
                Controller.FixedStep = balance.Board.FixedStepSeconds;
                Controller.MaxStepsPerFrame = balance.Board.MaxStepsPerFrame;
                Controller.AutoMoveLauncher = false;
                Controller.LauncherMoveHalfWidth = balance.Board.LauncherMoveHalfWidth;
                Controller.LauncherMoveSpeed = balance.Board.LauncherMoveSpeed;
                Controller.AutoStart = true;
                BoardView = gameObject.AddComponent<MarbleBoardView>();
                BoardView.Initialize(Controller, board, palette);
                var hud = gameObject.AddComponent<MarbleGameHud>();
                hud.Controller = Controller;
                hud.Board = BoardView;
                if (CreateCamera) CreateView(board, balance);
                if (MarblePreviewCapture.IsRequested)
                    gameObject.AddComponent<MarblePreviewCapture>().Controller = Controller;
            }
            catch (Exception error)
            {
                Debug.LogException(error, this);
                gameObject.AddComponent<MarbleStartupError>().Message = error.Message;
            }
        }

        private void CreateView(Transform board, GameBalance balance)
        {
            viewObject = new GameObject("Blood Marble / camera and light");
            var cameraObject = new GameObject("Main Camera");
            cameraObject.transform.SetParent(viewObject.transform, false);
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.AddComponent<Camera>();
            cameraObject.AddComponent<AudioListener>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.025f, .035f, .055f);
            camera.nearClipPlane = .1f;
            camera.farClipPlane = 100;
            camera.orthographic = true;
            camera.transform.position = board.TransformPoint(new Vector3(0, 23, -10));
            camera.transform.LookAt(board.position, board.forward);
            var framing = cameraObject.AddComponent<MarbleCameraFraming>();
            framing.BoardWidth = balance.Board.Width;
            framing.BoardLength = balance.Board.Length;
            BoardView.ViewCamera = camera;
            var lightObject = new GameObject("Board light");
            lightObject.transform.SetParent(viewObject.transform, false);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.25f;
            light.shadows = LightShadows.Soft;
            light.transform.rotation = Quaternion.Euler(55, -25, 0);
            RenderSettings.ambientLight = new Color(.36f, .4f, .48f);
        }

        private void OnDestroy()
        {
            if (Controller != null) Destroy(Controller);
            if (boardObject != null) Destroy(boardObject);
            if (viewObject != null) Destroy(viewObject);
            palette?.Dispose();
        }
    }
}
