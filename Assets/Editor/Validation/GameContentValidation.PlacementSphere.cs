using System;
using System.Linq;
using System.Text;
using MRes;
using UnityEngine;

namespace MarblesECS.Editor
{
    public static partial class GameContentValidation
    {
        private static void PlacementSphereValidation()
        {
            int start = assertions;
            Check(!System.IO.File.ReadAllText("Assets/Resources/GameContent.json").Contains("\"Radius\""), "content snapshot has no legacy device radius");
            var catalog = DeviceResourceCatalog.Current;
            foreach (var definition in catalog.Definitions())
            {
                var package = catalog.Get(definition.Id).Package;
                Check(!Encoding.UTF8.GetString(package.GetBytes("game/device.json")).Contains("\"Radius\""), "no legacy radius in device package");
                Check(MResPlacementSphere.TryRead(package, out var center, out float radius), "package has a placement sphere");
                Near(radius, definition.PlacementRadius, "runtime radius comes from collider geometry");
                Check(!JsonUtility.ToJson(definition).Contains("Radius"), "derived geometry is not written back as configuration");
                var copy = definition.Copy(); Near(radius, copy.PlacementRadius, "copy preserves derived geometry");
                using (var view = MResLoader.Load(package, catalog.Shaders, new MResLoadOptions { ExecuteLua = false, ActivateRoot = false }))
                {
                    var root = view.Root; root.transform.position = Vector3.zero; root.transform.rotation = Quaternion.identity;
                    var volume = root.transform.Find("PlacementVolume");
                    Check(volume && volume.GetComponents<Collider>().Length == 1, "one authored placement collider");
                    var sphere = volume.GetComponent<SphereCollider>();
                    Check(sphere && !sphere.enabled && !volume.GetComponent<Renderer>(), "placement volume is invisible and nonphysical");
                    Check((volume.TransformPoint(sphere.center) - center).magnitude < .0001f, "serialized hierarchy gives the real sphere center");
                    var scale = volume.lossyScale;
                    Near(sphere.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)), radius,
                        "serialized hierarchy gives the real scaled sphere radius");
                    root.SetActive(true); Check(!sphere.enabled, "activation never enables placement collisions");
                    root.SetActive(false); root.SetActive(true); Check(!sphere.enabled, "reenabling the device keeps placement nonphysical");
                }
            }
            using (var f = new Fixture())
            {
                var a = f.Add(f.C.Balance.Devices.Single(d => d.Id == 9002));
                var b = f.Add(f.C.Balance.Devices.Single(d => d.Id == 9001));
                var first = f.C.Balance.Devices.Single(d => d.Id == a.DefinitionId);
                var second = f.C.Balance.Devices.Single(d => d.Id == b.DefinitionId);
                first.SetPlacementSphere(.4f, .2f, 0, .25f); second.SetPlacementSphere(0, .2f, 0, .25f);
                BagBuild(f.C);
                Check(f.Simulation.RemoveDevicePlacement(a.InstanceId) && f.Simulation.RemoveDevicePlacement(b.InstanceId), "prepare geometric placements");
                Check(f.Simulation.PlaceDevice(a.InstanceId, 0, 0), "first sphere can be placed");
                Check(f.Simulation.PlaceDevice(b.InstanceId, 1.1f, 0), "separated sphere footprints are accepted");
                Check(!f.Simulation.PlaceDevice(a.InstanceId, .2f, 0), "offset sphere overlaps although root positions are separated");
                Check(f.Simulation.PlaceDevice(a.InstanceId, .6f, 0, angle: 180), "rotating the offset sphere changes its placement center");
                var placed = f.Simulation.GetOwnedDevices().Single(d => d.InstanceId == a.InstanceId);
                Near(.2, placed.PlacementX, "snapshot exposes the rotated footprint center");
                Near(.25, placed.Radius, "snapshot retains a measured read-only radius for existing consumers");
                first.SetPlacementSphere(.4f, .2f, 0, .8f);
                Check(!f.Simulation.PlaceDevice(a.InstanceId, .6f, 0, angle: 180), "larger collider geometry increases blocked area");
                ExpectArgument(() => first.SetPlacementSphere(0, 0, 0, 0));
                ExpectArgument(() => first.SetPlacementSphere(float.NaN, 0, 0, 1));
            }
            var obsolete = JsonUtility.FromJson<DeviceDefinition>("{\"Radius\":999}");
            Near(0, obsolete.PlacementRadius, "old Radius JSON cannot override collider geometry");
            Debug.Log("PLACEMENT_SPHERE_VALIDATION_PASSED: " + (assertions - start) + " assertions.");
        }
    }
}
