using UnityEngine;

namespace MRes.Samples
{
    /// <summary>Standalone demonstration. Every frame of the pad's behaviour comes from the package Lua.</summary>
    public sealed class MResDemo : MonoBehaviour
    {
        public TextAsset packageBytes;
        public MResShaderRegistry shaderRegistry;
        private MResLoadHandle first, second;
        private GameObject ball, cameraObject;
        private Material ballMaterial;
        private float resetAt;

        private void Start()
        {
            first = MResLoader.Load(packageBytes.bytes, shaderRegistry);
            second = MResLoader.Load(packageBytes.bytes, shaderRegistry);
            first.Root.transform.position = new Vector3(-2.5f, 0, 0);
            second.Root.transform.position = new Vector3(2.5f, 0, 0);
            ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ball.name = "Ball - launched by package Lua";
            ball.transform.localScale = Vector3.one * 0.6f;
            ball.AddComponent<Rigidbody>().collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            ballMaterial = new Material(shaderRegistry.Resolve("MRes/Unlit"));
            ballMaterial.SetColor("_Color", new Color(1, 0.72f, 0.2f));
            ball.GetComponent<Renderer>().sharedMaterial = ballMaterial;
            cameraObject = new GameObject("MRes Demo Camera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.transform.position = new Vector3(8, 9, -12); camera.transform.LookAt(Vector3.zero);
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0.025f, 0.035f, 0.07f);
            ResetBall(-2.5f);
        }
        private void ResetBall(float x)
        {
            ball.transform.position = new Vector3(x, 2, 0);
            var body = ball.GetComponent<Rigidbody>();
#if UNITY_6000_0_OR_NEWER
            body.linearVelocity = Vector3.zero;
#else
            body.velocity = Vector3.zero;
#endif
            body.angularVelocity = Vector3.zero; resetAt = Time.time + 6;
        }
        private void Update()
        {
            if (ball != null && (Time.time >= resetAt || ball.transform.position.y < -3))
                ResetBall(ball.transform.position.x < 0 ? 2.5f : -2.5f);
        }
        private void OnGUI()
        {
            GUI.Label(new Rect(20, 20, 740, 30), "MRes runtime demo | Two packages, one xLua VM | Lua collision launches the ball");
            GUI.Label(new Rect(20, 50, 740, 30), "Loaded entirely from bytes. Ball alternates between the two pads every six seconds.");
            if (first != null) GUI.Label(new Rect(20, 80, 740, 30), "Metadata: " + first.Metadata.name + " / " + first.Metadata.type + " / " + first.Metadata.value);
        }
        private void OnDestroy()
        {
            first?.Dispose(); second?.Dispose();
            if (ball != null) Destroy(ball);
            if (cameraObject != null) Destroy(cameraObject);
            if (ballMaterial != null) Destroy(ballMaterial);
        }
    }
}
