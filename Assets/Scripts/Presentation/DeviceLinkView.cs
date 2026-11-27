using System;
using System.Collections.Generic;
using MarblesECS.PhysX;
using UnityEngine;

namespace MarblesECS.Presentation
{
    public sealed class DeviceLinkView : MonoBehaviour
    {
        private readonly List<LineRenderer> lines = new List<LineRenderer>();
        private Material material;
        private DeviceBody source;
        private IReadOnlyList<DeviceBody> devices;
        private Func<int, GameObject> getModel;

        internal void Initialize(DeviceBody body, IReadOnlyList<DeviceBody> placed, Func<int, GameObject> findModel)
        {
            source = body;
            devices = placed;
            getModel = findModel;
        }

        private void LateUpdate()
        {
            int count = 0;
            if (source != null && source.enabled && source.InstanceId > 0)
                for (int i = 0; i < devices.Count; i++)
                {
                    var other = devices[i];
                    if (other == null || other.LinkGroup != source.LinkGroup || other.InstanceId <= source.InstanceId ||
                        Vector3.Distance(transform.position, other.transform.position) > source.Range) continue;
                    var model = getModel(other.InstanceId);
                    if (model == null || !model.activeInHierarchy) continue;
                    if (count == lines.Count)
                    {
                        if (material == null) material = Resources.Load<Material>("DeviceLink");
                        var line = new GameObject("Device connection").AddComponent<LineRenderer>();
                        line.transform.SetParent(transform, false); line.sharedMaterial = material;
                        line.positionCount = 2; line.startWidth = line.endWidth = .015f; lines.Add(line);
                    }
                    var current = lines[count++]; current.enabled = true;
                    current.SetPosition(0,transform.position + source.Up * .04f);
                    current.SetPosition(1,other.transform.position + other.Up * .04f);
                }
            for (int i = count; i < lines.Count; i++) lines[i].enabled = false;
        }
    }
}
