using System.Collections.Generic;
using MarblesECS.PhysX;
using UnityEngine;

namespace MarblesECS.Presentation
{
    public sealed class AmplifierLinkView : MonoBehaviour
    {
        private readonly List<LineRenderer> lines = new List<LineRenderer>();
        private Material material;
        private void LateUpdate()
        {
            var source = GetComponent<MarbleDevice>();
            int count = 0;
            if (source != null && source.enabled && source.InstanceId > 0)
                foreach (var other in MarbleDevice.Active)
                {
                    if (other == null || other.Kind != DeviceKind.Amplifier || other.InstanceId <= source.InstanceId ||
                        other.gameObject.scene != gameObject.scene || Vector3.Distance(transform.position, other.transform.position) > source.Range) continue;
                    if (count == lines.Count)
                    {
                        if (material == null)
                        {
                            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
                            material = new Material(shader); material.color = Color.cyan;
                        }
                        var line = new GameObject("Amplifier connection").AddComponent<LineRenderer>();
                        line.transform.SetParent(transform, false); line.sharedMaterial = material;
                        line.positionCount = 2; line.startWidth = line.endWidth = .015f; lines.Add(line);
                    }
                    var current = lines[count++]; current.enabled = true;
                    current.SetPosition(0,transform.position + source.Up * .04f);
                    current.SetPosition(1,other.transform.position + other.Up * .04f);
                }
            for (int i = count; i < lines.Count; i++) lines[i].enabled = false;
        }
        private void OnDestroy() { if (material != null) Destroy(material); }
    }
}
