using UnityEngine;
using UnityEngine.Rendering;

namespace MarblesECS.Presentation
{
    /// <summary>Preserves the authored 16:9 machine framing in narrower Game views.</summary>
    [ExecuteAlways, RequireComponent(typeof(Camera)), DisallowMultipleComponent]
    public sealed class MachineCameraAspect : MonoBehaviour
    {
        [Range(10, 90)] public float ReferenceVerticalFieldOfView = 37;
        [Min(.1f)] public float ReferenceAspect = 16f / 9f;
        private Camera view;

        private void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering += BeforeRender;
            Apply();
        }
        private void OnDisable() => RenderPipelineManager.beginCameraRendering -= BeforeRender;
        private void BeforeRender(ScriptableRenderContext context, Camera renderingCamera)
        {
            if (renderingCamera == view) Apply();
        }
        private void LateUpdate() => Apply();
        private void OnPreCull() => Apply();
        private void OnValidate() => Apply();

        public void Apply()
        {
            if (!view) view = GetComponent<Camera>();
            if (!view || view.orthographic || view.pixelHeight <= 0) return;
            // In the Editor camera.pixelRect may still describe the Game view while a
            // RenderTexture is being rendered (previews / 4K captures).
            float aspect = view.targetTexture
                ? (float)view.targetTexture.width / view.targetTexture.height
                : (float)view.pixelWidth / view.pixelHeight;
            aspect = Mathf.Max(.1f, aspect);
            view.aspect = aspect;
            float scale = Mathf.Max(1, ReferenceAspect / aspect);
            view.fieldOfView = 2 * Mathf.Atan(Mathf.Tan(ReferenceVerticalFieldOfView * .5f * Mathf.Deg2Rad) * scale) * Mathf.Rad2Deg;
        }
    }
}
