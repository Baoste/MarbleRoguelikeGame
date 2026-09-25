using MarblesECS.PhysX;
using UnityEngine;

namespace MarblesECS.Presentation
{
    public static class ContentDeviceView
    {
        public static GameObject Create(OwnedDeviceSnapshot device, Transform parent)
        {
            var root = new GameObject(device.Name);
            root.transform.SetParent(parent, false);
            var collider = root.AddComponent<SphereCollider>();
            collider.radius = device.Radius;
            collider.center = new Vector3(0, device.Radius, 0);
            collider.isTrigger = true;
            var visual = GameObject.CreatePrimitive(device.Kind == DeviceKind.Portal ? PrimitiveType.Cylinder :
                device.Kind == DeviceKind.Paddle || device.Kind == DeviceKind.Slow ? PrimitiveType.Cube : PrimitiveType.Sphere);
            visual.name = "Visual";
            visual.transform.SetParent(root.transform, false);
            visual.transform.localPosition = new Vector3(0, device.Radius, 0);
            visual.transform.localScale = new Vector3(device.Radius * 2, device.Radius, device.Radius * 2);
            Object.Destroy(visual.GetComponent<Collider>());
            var renderer = visual.GetComponent<Renderer>();
            var properties = new MaterialPropertyBlock();
            Color color = Color.HSVToRGB(((int)device.Kind * .073f) % 1, .7f, 1);
            if (device.Kind == DeviceKind.Portal) color = Color.HSVToRGB((device.PairId * .173f) % 1, .8f, 1);
            properties.SetColor("_Color", color); properties.SetColor("_BaseColor", color); renderer.SetPropertyBlock(properties);
            var label = new GameObject("Label").AddComponent<TextMesh>();
            label.transform.SetParent(root.transform, false);
            label.transform.localPosition = new Vector3(0, device.Radius * 2 + .02f, 0);
            label.transform.localRotation = Quaternion.Euler(90, 0, 0);
            label.text = device.Name; label.characterSize = .025f; label.fontSize = 32;
            label.anchor = TextAnchor.MiddleCenter; label.color = Color.white;
            Configure(root, device);
            return root;
        }
        public static void Configure(GameObject item, OwnedDeviceSnapshot device)
        {
            if (device.Kind == DeviceKind.LegacyPin)
            {
                var pin = item.GetComponentInChildren<MarblePin>() ?? item.AddComponent<MarblePin>();
                pin.TargetId = 100000 + device.InstanceId; pin.ScoreMultiplier = (float)device.ScoreMultiplier;
                pin.RushChanceAdd = (float)device.RushChanceAdd; return;
            }
            foreach (var pin in item.GetComponentsInChildren<MarblePin>()) pin.enabled = false;
            var component = item.GetComponent<MarbleDevice>() ?? item.AddComponent<MarbleDevice>();
            component.InstanceId = device.InstanceId; component.PairId = device.PairId; component.Kind = device.Kind;
            component.Radius = device.Radius; component.Range = device.Range; component.Strength = device.Strength;
            var sensor = item.GetComponent<SphereCollider>();
            if (sensor != null && sensor.isTrigger)
            {
                sensor.radius = device.Radius;
                sensor.center = new Vector3(0, device.Radius, 0);
            }
            if (device.Kind == DeviceKind.Amplifier && item.GetComponent<AmplifierLinkView>() == null) item.AddComponent<AmplifierLinkView>();
        }
    }
}
