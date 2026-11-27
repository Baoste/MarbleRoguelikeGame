using System;
using System.Collections.Generic;
using MarblesECS.PhysX;
using UnityEngine;

namespace MarblesECS.Presentation
{
    internal sealed class DevicePresentation : IDisposable
    {
        private readonly MarblePhysicsBridge bridge;
        private readonly Dictionary<int, (DeviceBody Body, DeviceViewHandle Model)> models =
            new Dictionary<int, (DeviceBody, DeviceViewHandle)>();
        private readonly Dictionary<int, DeviceViewHandle> transients = new Dictionary<int, DeviceViewHandle>();
        private readonly List<int> removed = new List<int>();
        private int revision = -1;

        internal DevicePresentation(MarblePhysicsBridge bridge) => this.bridge = bridge;

        internal GameObject GetModel(int id) =>
            models.TryGetValue(id, out var entry) ? entry.Model?.Root : null;

        internal void Sync(SimulationContext context)
        {
            SyncModels();
            var active = context.Transients;
            if (transients.Count == 0 && active.Count == 0) return;
            removed.Clear();
            foreach (var pair in transients)
                if (!active.Contains(pair.Key)) removed.Add(pair.Key);
            foreach (int id in removed)
            {
                transients[id]?.Dispose();
                transients.Remove(id);
            }
            double time = context.Round.Time;
            foreach (var item in active.Items)
            {
                if (string.IsNullOrEmpty(item.NodePath)) continue;
                if (!transients.TryGetValue(item.Id, out var visual))
                {
                    visual = CreateTransient(item, time);
                    transients.Add(item.Id, visual);
                }
                if (visual?.Root != null) visual.Root.transform.position = item.PositionAt(time);
            }
        }

        internal void SyncModels()
        {
            if (revision == bridge.DeviceRevision) return;
            removed.Clear();
            foreach (var pair in models)
                if (!bridge.TryGetDeviceBody(pair.Key, out var body) || body != pair.Value.Body)
                    removed.Add(pair.Key);
            foreach (int id in removed)
            {
                ReleaseModel(models[id]);
                models.Remove(id);
            }
            foreach (var body in bridge.PlacedDeviceBodies)
                if (body != null && !models.ContainsKey(body.InstanceId))
                    models.Add(body.InstanceId, (body, CreateModel(body)));
            revision = bridge.DeviceRevision;
        }

        private DeviceViewHandle CreateModel(DeviceBody body)
        {
            DeviceViewHandle view = null;
            try
            {
                view = DeviceView.CreateModel(body.Resource.Prefab.transform, body.transform, "Device Model");
                body.Script?.AttachPresentation(body.gameObject, view.Root.transform);
                if (!string.IsNullOrEmpty(body.LinkGroup))
                    view.Root.AddComponent<DeviceLinkView>().Initialize(body, bridge.PlacedDeviceBodies, GetModel);
                view.Root.SetActive(true);
                return view;
            }
            catch (Exception error)
            {
                view?.Dispose();
                Debug.LogException(error);
                return null;
            }
        }

        private DeviceViewHandle CreateTransient(DeviceTransientSystem.Item item, double time)
        {
            var model = GetModel(item.Source);
            var template = model != null ? model.transform.Find(item.NodePath) : null;
            if (template == null || template.GetComponentInChildren<MeshFilter>(true) == null) return null;
            var scale = model.transform.lossyScale;
            if (Mathf.Abs(scale.x * scale.y * scale.z) < 1e-12f) return null;
            DeviceViewHandle view = null;
            try
            {
                view = DeviceView.CreateModel(template, model.transform.parent, "TransientVisual " + item.Id);
                var worldScale = template.lossyScale;
                var transform = view.Root.transform;
                transform.localScale = new Vector3(worldScale.x / scale.x, worldScale.y / scale.y, worldScale.z / scale.z);
                transform.SetPositionAndRotation(item.PositionAt(time), template.rotation);
                view.Root.SetActive(true);
                return view;
            }
            catch (Exception error)
            {
                view?.Dispose();
                Debug.LogException(error);
                return null;
            }
        }

        private static void ReleaseModel((DeviceBody Body, DeviceViewHandle Model) entry)
        {
            if (entry.Body != null) entry.Body.Script?.AttachPresentation(entry.Body.gameObject, null);
            entry.Model?.Dispose();
        }

        public void Dispose()
        {
            foreach (var view in transients.Values) view?.Dispose();
            foreach (var entry in models.Values) ReleaseModel(entry);
            transients.Clear();
            models.Clear();
            revision = -1;
        }
    }
}
