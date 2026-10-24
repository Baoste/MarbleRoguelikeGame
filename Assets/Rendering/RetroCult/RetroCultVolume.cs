using System;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BloodPachinko.Rendering
{
    [Serializable, VolumeComponentMenuForRenderPipeline("血色柏青哥/粗像素邪典", typeof(UniversalRenderPipeline))]
    public sealed class RetroCultVolume : VolumeComponent, IPostProcessComponent
    {
        public RetroCultVolume() { displayName = "粗像素邪典 / 方案 2"; }
        // Zero by default: installing the renderer feature must not affect other scenes.
        public ClampedFloatParameter strength = new ClampedFloatParameter(0f, 0f, 1f);
        public BoolParameter sceneView = new BoolParameter(true);
        public ClampedIntParameter virtualHeight = new ClampedIntParameter(360, 180, 2160);
        public ClampedFloatParameter pixelInterior = new ClampedFloatParameter(.70f, 0f, 1f);
        public ClampedFloatParameter pixelEdges = new ClampedFloatParameter(1f, 0f, 1f);
        public ClampedFloatParameter jitterAmount = new ClampedFloatParameter(.70f, 0f, 1.5f);
        public ClampedFloatParameter jitterRate = new ClampedFloatParameter(6f, 0f, 30f);
        public ClampedIntParameter jitterCellSize = new ClampedIntParameter(8, 1, 32);
        public ClampedFloatParameter edgeThreshold = new ClampedFloatParameter(.035f, 0f, .5f);
        public ClampedFloatParameter edgeSoftness = new ClampedFloatParameter(.185f, .001f, .5f);
        public ClampedIntParameter colorSteps = new ClampedIntParameter(12, 4, 64);
        public ClampedIntParameter luminanceSteps = new ClampedIntParameter(8, 4, 64);
        public ClampedFloatParameter posterization = new ClampedFloatParameter(.92f, 0f, 1f);
        public ClampedFloatParameter dither = new ClampedFloatParameter(.55f, 0f, 1f);
        public ClampedFloatParameter grain = new ClampedFloatParameter(.030f, 0f, .15f);
        public ClampedFloatParameter scanlines = new ClampedFloatParameter(0f, 0f, .3f);
        public ClampedFloatParameter flicker = new ClampedFloatParameter(.010f, 0f, .08f);
        public BoolParameter protectText = new BoolParameter(true);
        public ClampedFloatParameter textPadding = new ClampedFloatParameter(3f, 0f, 16f);

        public bool IsActive() => active && strength.value > 0f;
        public bool IsTileCompatible() => false;

        public void ApplySchemeTwo()
        {
            active = true;
            SetAllOverridesTo(true);
            strength.value = 1f;
            sceneView.value = true;
            virtualHeight.value = 360;
            pixelInterior.value = .70f;
            pixelEdges.value = 1f;
            jitterAmount.value = .70f;
            jitterRate.value = 6f;
            jitterCellSize.value = 8;
            edgeThreshold.value = .035f;
            edgeSoftness.value = .185f;
            colorSteps.value = 12;
            luminanceSteps.value = 8;
            posterization.value = .92f;
            dither.value = .55f;
            grain.value = .030f;
            scanlines.value = 0f;
            flicker.value = .010f;
            protectText.value = true;
            textPadding.value = 3f;
        }
    }
}
