using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BloodPachinko.Rendering
{
    [DisallowMultipleRendererFeature]
    public sealed class RetroCultRendererFeature : ScriptableRendererFeature
    {
        [SerializeField, HideInInspector] Shader shader;
        Material material;
        RetroPass pass;

        public void SetShader(Shader value) { shader = value; }

        public override void Create()
        {
            pass?.Dispose();
            CoreUtils.Destroy(material);
            material = shader ? CoreUtils.CreateEngineMaterial(shader) : null;
            pass = material ? new RetroPass(material) : null;
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            var camera = renderingData.cameraData;
            if (pass == null || !camera.postProcessEnabled || camera.renderType != CameraRenderType.Base ||
                camera.isPreviewCamera || camera.cameraType == CameraType.Reflection) return;
            var settings = VolumeManager.instance.stack.GetComponent<RetroCultVolume>();
            if (settings == null || !settings.IsActive() || (camera.isSceneViewCamera && !settings.sceneView.value)) return;
            pass.ConfigureInput(ScriptableRenderPassInput.Color);
            renderer.EnqueuePass(pass);
        }

        protected override void Dispose(bool disposing)
        {
            pass?.Dispose();
            pass = null;
            CoreUtils.Destroy(material);
            material = null;
        }

        sealed class RetroPass : ScriptableRenderPass
        {
            readonly Material material;
            readonly ProfilingSampler sampler = new ProfilingSampler("粗像素邪典 / Retro Cult");
            readonly Vector4[] rectangles = new Vector4[48];
            readonly Vector3[] corners = new Vector3[4];
            readonly List<CanvasGroup> groups = new List<CanvasGroup>();
            TMP_Text[] texts;
            double refreshAt;
            RTHandle temporary;

            public RetroPass(Material material)
            {
                this.material = material;
                // URP checks this exact event to retain the postprocessed intermediate target.
                renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
            }

            public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
            {
                var descriptor = renderingData.cameraData.cameraTargetDescriptor;
                descriptor.depthBufferBits = 0;
                descriptor.msaaSamples = 1;
                RenderingUtils.ReAllocateIfNeeded(ref temporary, descriptor, FilterMode.Bilinear,
                    TextureWrapMode.Clamp, name: "_RetroCultTemporary");
            }

            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                var settings = VolumeManager.instance.stack.GetComponent<RetroCultVolume>();
                if (settings == null || !settings.IsActive()) return;
                var data = renderingData.cameraData;
                int width = data.cameraTargetDescriptor.width, height = data.cameraTargetDescriptor.height;
                material.SetVector("_TargetSize", new Vector4(width, height, 1f/width, 1f/height));
                material.SetFloat("_Strength", settings.strength.value);
                material.SetFloat("_VirtualHeight", settings.virtualHeight.value);
                material.SetFloat("_PixelBase", settings.pixelInterior.value);
                material.SetFloat("_PixelEdge", settings.pixelEdges.value);
                material.SetFloat("_Jitter", settings.jitterAmount.value);
                material.SetFloat("_Tick", (float)(System.Math.Floor(Time.realtimeSinceStartupAsDouble * settings.jitterRate.value) % 4096));
                material.SetFloat("_CellSize", settings.jitterCellSize.value);
                material.SetFloat("_EdgeThreshold", settings.edgeThreshold.value);
                material.SetFloat("_EdgeSoftness", settings.edgeSoftness.value);
                material.SetFloat("_ColorSteps", settings.colorSteps.value);
                material.SetFloat("_LumaSteps", settings.luminanceSteps.value);
                material.SetFloat("_PosterMix", settings.posterization.value);
                material.SetFloat("_Dither", settings.dither.value);
                material.SetFloat("_Grain", settings.grain.value);
                material.SetFloat("_Scanline", settings.scanlines.value);
                material.SetFloat("_Flicker", settings.flicker.value);
                int count = settings.protectText.value ? GatherText(data.camera, width, height, settings.textPadding.value) : 0;
                material.SetInt("_RectCount", count);
                material.SetVectorArray("_ProtectedRects", rectangles);

                // Resolve after URP's postprocess swap, never sample and write the same target.
                var source = data.renderer.cameraColorTargetHandle;
                var cmd = CommandBufferPool.Get();
                using (new ProfilingScope(cmd, sampler))
                {
                    Blitter.BlitCameraTexture(cmd, source, temporary, material, 0);
                    Blitter.BlitCameraTexture(cmd, temporary, source);
                }
                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }

            int GatherText(Camera camera, int width, int height, float padding)
            {
                double now = Time.realtimeSinceStartupAsDouble;
                if (texts == null || now >= refreshAt)
                {
                    texts = Object.FindObjectsOfType<TMP_Text>();
                    refreshAt = now + .35;
                }
                int count = 0;
                float py = padding / 1080f, px = py * height / width;
                foreach (var text in texts)
                {
                    if (!text || !text.isActiveAndEnabled || string.IsNullOrEmpty(text.text) || text.color.a < .01f ||
                        (camera.cullingMask & (1 << text.gameObject.layer)) == 0) continue;
                    var canvas = text.GetComponentInParent<Canvas>();
                    if (canvas && (!canvas.isActiveAndEnabled || canvas.rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay)) continue;
                    text.GetComponentsInParent(false, groups);
                    bool hidden = false;
                    foreach (var group in groups) if (group.alpha < .01f) { hidden = true; break; }
                    if (hidden) continue;
                    text.rectTransform.GetWorldCorners(corners);
                    float x0 = 1, y0 = 1, x1 = 0, y1 = 0;
                    foreach (var corner in corners)
                    {
                        var p = camera.WorldToViewportPoint(corner);
                        if (p.z <= 0) { hidden = true; break; }
                        x0 = Mathf.Min(x0,p.x); y0 = Mathf.Min(y0,p.y);
                        x1 = Mathf.Max(x1,p.x); y1 = Mathf.Max(y1,p.y);
                    }
                    if (hidden || x1 <= 0 || x0 >= 1 || y1 <= 0 || y0 >= 1) continue;
                    rectangles[count++] = new Vector4(x0-px,y0-py,x1+px,y1+py);
                    if (count == rectangles.Length) break;
                }
                return count;
            }

            public void Dispose() { temporary?.Release(); temporary = null; }
        }
    }
}
