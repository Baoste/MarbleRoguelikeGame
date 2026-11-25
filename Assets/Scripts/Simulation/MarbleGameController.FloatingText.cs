using System;
using UnityEngine;

namespace MarblesECS.PhysX
{
    public sealed partial class MarbleGameController
    {
        [Header("Floating Text")]
        [Tooltip("Leave empty to use Resources/FloatingText/Config.json.")]
        [SerializeField] private TextAsset floatingTextJson;
        [Tooltip("Leave empty to use the MainCamera.")]
        [SerializeField] private Camera floatingTextCamera;

        private FloatingTextSystem floatingText;
        private Camera cachedTextCamera;

        private void InitializeFloatingText()
        {
            try
            {
                var config = FloatingTextConfig.Load(floatingTextJson);
                if (!config.Enabled) return;
                floatingText = new FloatingTextSystem(transform, config);
                simulation.Context.FloatingText = floatingText.Events;
            }
            catch (Exception error) { Debug.LogException(error, this); }
        }

        /// <summary>Submits optional presentation feedback on the simulation thread.</summary>
        public void EmitFloatingText(in FloatingTextEvent text)
        {
            if (IsReady) simulation.Context.FloatingText?.Enqueue(text);
        }

        private void OnEnable()
        {
            if (floatingText != null && IsReady) simulation.Context.FloatingText = floatingText.Events;
        }

        private void LateUpdate()
        {
            if (floatingText == null || !IsReady) return;
            try
            {
                if (!cachedTextCamera || !cachedTextCamera.isActiveAndEnabled)
                    cachedTextCamera = floatingTextCamera ? floatingTextCamera : Camera.main;
                bool scoresOnly = !simulation.Context.IsRunning ||
                    (simulation.HasDayFlow && simulation.Day.Phase != DayPhase.Production);
                floatingText.Tick(Time.unscaledTimeAsDouble, cachedTextCamera, scoresOnly);
            }
            catch (Exception error)
            {
                ReleaseFloatingText();
                Debug.LogException(error, this);
            }
        }

        private void SuspendFloatingText()
        {
            if (simulation != null) simulation.Context.FloatingText = null;
            floatingText?.ClearAll();
        }

        private void ReleaseFloatingText()
        {
            if (simulation != null) simulation.Context.FloatingText = null;
            var released = floatingText;
            floatingText = null;
            cachedTextCamera = null;
            released?.Dispose();
        }
    }
}
