using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
#if UNITY_6000_0_OR_NEWER
using CombineType = UnityEngine.PhysicsMaterialCombine;
#else
using CombineType = UnityEngine.PhysicMaterialCombine;
#endif

namespace MResPacker.Tests
{
    public sealed class MResPackerTests
    {
        private string scratch, output;
        private MResBuildProfile profile;
        private GameObject root;
        [SetUp]
        public void SetUp()
        {
            string token = Guid.NewGuid().ToString("N"); scratch = "Assets/MResPacker/Tests/Generated_" + token;
            Directory.CreateDirectory(scratch); AssetDatabase.Refresh();
            string temp = Path.GetFullPath(Path.Combine(Application.dataPath, "../.utmp/MResPackerTests")); Directory.CreateDirectory(temp);
            output = Path.Combine(temp, token + ".mres");
            var example = MResExampleCreator.Create(); profile = UnityEngine.Object.Instantiate(example); profile.OutputPath = output;
            root = UnityEngine.Object.Instantiate(example.SourcePrefab); root.name = "TestPrefab";
            if (PrefabUtility.IsPartOfPrefabInstance(root)) PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        }
        [TearDown]
        public void TearDown()
        {
            if (root != null) UnityEngine.Object.DestroyImmediate(root);
            if (profile != null) UnityEngine.Object.DestroyImmediate(profile);
            if (File.Exists(output)) File.Delete(output);
            if (AssetDatabase.IsValidFolder(scratch)) AssetDatabase.DeleteAsset(scratch);
        }
        private void Save() { profile.SourcePrefab = PrefabUtility.SaveAsPrefabAsset(root, scratch + "/Source.prefab"); }
        private Dictionary<string, byte[]> Build()
        {
            Save(); var result = MResBuildPipeline.Build(profile); Assert.IsTrue(result.CanBuild, result.ToString());
            MResStructureValidator.ValidateFile(output); return Entries(File.ReadAllBytes(output));
        }
        private static Dictionary<string, byte[]> Entries(byte[] bytes)
        {
            var result = new Dictionary<string, byte[]>();
            using (var stream = new MemoryStream(bytes)) using (var r = new BinaryReader(stream))
            {
                Assert.AreEqual("MRES", System.Text.Encoding.ASCII.GetString(r.ReadBytes(4))); Assert.AreEqual(1, r.ReadUInt16()); Assert.AreEqual(0, r.ReadUInt16());
                uint n = r.ReadUInt32(); ulong toc = r.ReadUInt64(), size = r.ReadUInt64(); Assert.AreEqual((ulong)bytes.Length, toc + size); stream.Position = (long)toc;
                for (int i = 0; i < n; i++)
                {
                    string path = MResJson.Utf8.GetString(r.ReadBytes((int)r.ReadUInt32())); r.ReadByte(); Assert.AreEqual(0, r.ReadByte()); Assert.AreEqual(0, r.ReadUInt16());
                    ulong offset = r.ReadUInt64(), length = r.ReadUInt64(); var data = new byte[(int)length]; Buffer.BlockCopy(bytes, (int)offset, data, 0, data.Length); result.Add(path, data);
                }
            }
            return result;
        }
        private static Dictionary<string, object> Json(Dictionary<string, byte[]> entries, string path) => (Dictionary<string, object>)MResJson.Parse(entries[path]);
        private static List<Dictionary<string, object>> Nodes(Dictionary<string, byte[]> entries) => ((List<object>)Json(entries, "prefab/root.json")["nodes"]).Cast<Dictionary<string, object>>().ToList();
        private static Dictionary<string, object> Component(Dictionary<string, object> node, string type) =>
            ((List<object>)node["components"]).Cast<Dictionary<string, object>>().Single(c => (string)c["type"] == type);

        [Test] public void A_StaticPrefabAndSharedResources()
        {
            var entries = Build(); var manifest = Json(entries, "manifest.json");
            Assert.AreEqual(1, ((List<object>)manifest["meshes"]).Count);
            Assert.AreEqual(2, ((List<object>)manifest["materials"]).Count);
            Assert.AreEqual(1, ((List<object>)manifest["textures"]).Count);
            Assert.AreEqual(1, ((List<object>)manifest["physicsMaterials"]).Count);
            Assert.IsTrue(entries.ContainsKey("game/device.json"));
            Assert.AreEqual("TwoMaterialCube", ((Dictionary<string, object>)manifest["meshNames"])["meshes/0.meshbin"]);
        }
        [Test] public void PlacementSphereFitsModelAndPreservesAuthoredEdits()
        {
            var existing = root.transform.Find(MResPlacementAuthoring.NodeName);
            if (existing) UnityEngine.Object.DestroyImmediate(existing.gameObject);
            // Hidden effect templates must not enlarge the device's initial footprint.
            var hidden = UnityEngine.Object.Instantiate(root.transform.Find("Visual/Base").gameObject, root.transform);
            hidden.transform.localPosition = Vector3.right * 100; hidden.SetActive(false);
            var entries = Build();
            var volume = profile.SourcePrefab.transform.Find(MResPlacementAuthoring.NodeName);
            var sphere = volume.GetComponent<SphereCollider>();
            Assert.IsFalse(sphere.enabled); Assert.IsTrue(sphere.isTrigger);
            Assert.IsNull(volume.GetComponent<Renderer>());
            foreach (var filter in profile.SourcePrefab.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.name == hidden.name) continue;
                var matrix = volume.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                foreach (var vertex in filter.sharedMesh.vertices)
                    Assert.LessOrEqual(Vector3.Distance(matrix.MultiplyPoint3x4(vertex), sphere.center), sphere.radius + .0001f);
            }
            var serialized = Component(Nodes(entries).Single(n => (string)n["name"] == MResPlacementAuthoring.NodeName), "SphereCollider");
            Assert.AreEqual(true, serialized["placementOnly"]);
            Assert.Less(sphere.radius, 2);
            UnityEngine.Object.DestroyImmediate(hidden);
            MResPlacementAuthoring.AddToRoot(root);
            sphere = root.transform.Find(MResPlacementAuthoring.NodeName).GetComponent<SphereCollider>();
            sphere.center = new Vector3(.3f, .4f, -.2f); sphere.radius = .6f;
            sphere.transform.localScale = new Vector3(2, 1, 3);
            entries = Build();
            serialized = Component(Nodes(entries).Single(n => (string)n["name"] == MResPlacementAuthoring.NodeName), "SphereCollider");
            Assert.AreEqual(.6, (double)serialized["radius"], .00001);
            Assert.AreEqual(.3, (double)((Dictionary<string, object>)serialized["center"])["x"], .00001);
            Assert.AreEqual(new Vector3(2, 1, 3), profile.SourcePrefab.transform.Find(MResPlacementAuthoring.NodeName).localScale);
            Assert.AreEqual(1, Nodes(entries).Count(n => (string)n["name"] == MResPlacementAuthoring.NodeName));
        }
        [Test] public void UnassignedTextureArraysAreSkippedButAssignedArraysAreRejected()
        {
            string shaderPath = scratch + "/Array.shader";
            File.WriteAllText(shaderPath, "Shader \"MResPackerTests/Array\" { Properties { _Array(\"Array\", 2DArray) = \"\" {} } SubShader { Pass {} } }");
            AssetDatabase.ImportAsset(shaderPath, ImportAssetOptions.ForceSynchronousImport);
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);
            var registry = ScriptableObject.CreateInstance<MResPackerShaderRegistry>();
            registry.Entries = new[] { new MResPackerShaderRegistry.Entry { Shader = shader, ShaderId = "test-array" } };
            var material = new Material(shader); var texture = new Texture2DArray(2, 2, 1, TextureFormat.RGBA32, false);
            AssetDatabase.CreateAsset(material, scratch + "/Array.mat");
            AssetDatabase.CreateAsset(texture, scratch + "/Array.asset");
            try
            {
                profile.ShaderRegistry = registry;
                foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>()) renderer.sharedMaterials = new[] { material, material };
                var entries = Build();
                Assert.AreEqual(0, ((List<object>)Json(entries, "manifest.json")["textures"]).Count);
                material.SetTexture("_Array", texture); Save();
                Assert.IsTrue(MResBuildPipeline.Analyze(profile).Errors.Any(e => e.Contains("Only Texture2D")));
            }
            finally { UnityEngine.Object.DestroyImmediate(registry); }
        }
        [Test] public void B_HierarchyActiveTransformsAndMaterialSlots()
        {
            var visual = root.transform.Find("Visual"); visual.localPosition = new Vector3(.1f, .2f, .3f); visual.localRotation = Quaternion.Euler(10, 20, 30);
            visual.Find("ExitMarker").gameObject.SetActive(false);
            var renderer = visual.Find("Base").GetComponent<MeshRenderer>();
            var original = renderer.sharedMaterials; renderer.sharedMaterials = new[] { original[1], null, original[0] };
            var entries = Build(); var nodes = Nodes(entries);
            Assert.AreEqual(5, nodes.Count); Assert.AreEqual(-1d, nodes[0]["parentId"]);
            Assert.AreEqual(0d, nodes[1]["parentId"]); Assert.AreEqual(1d, nodes[2]["parentId"]); Assert.AreEqual(false, nodes[3]["active"]);
            var transform = (Dictionary<string, object>)nodes[1]["transform"];
            Assert.AreEqual(.2d, (double)((Dictionary<string, object>)transform["localPosition"])["y"], .00001);
            Assert.AreEqual(visual.localRotation.w, (double)((Dictionary<string, object>)transform["localRotation"])["w"], .00001);
            var slots = (List<object>)Component(nodes[2], "MeshRenderer")["materials"];
            Assert.AreEqual(3, slots.Count); Assert.IsNull(slots[1]); Assert.AreEqual(original[1].name, Json(entries, (string)slots[0])["name"]);
        }
        [Test] public void C_MeshBinaryPreservesChannelsBoundsSubmeshes()
        {
            var entries = Build(); var mesh = root.GetComponentInChildren<MeshFilter>().sharedMesh;
            using (var stream = new MemoryStream(entries["meshes/0.meshbin"])) using (var r = new BinaryReader(stream))
            {
                Assert.AreEqual(0x48534D4D, r.ReadUInt32()); Assert.AreEqual(1, r.ReadUInt16()); Assert.AreEqual((byte)mesh.indexFormat, r.ReadByte());
                byte flags = r.ReadByte(); Assert.AreEqual(31, flags); Assert.AreEqual(mesh.vertexCount, r.ReadUInt32()); Assert.AreEqual(2, r.ReadUInt32());
                Assert.AreEqual(mesh.bounds.center.x, r.ReadSingle()); r.ReadSingle(); r.ReadSingle();
                Assert.AreEqual(mesh.bounds.extents.x, r.ReadSingle()); r.ReadSingle(); r.ReadSingle();
                foreach (var v in mesh.vertices) { Assert.AreEqual(v.x, r.ReadSingle()); Assert.AreEqual(v.y, r.ReadSingle()); Assert.AreEqual(v.z, r.ReadSingle()); }
                stream.Position += mesh.vertexCount * (12 + 16 + 8 + 8 + 16);
                for (int s = 0; s < 2; s++)
                {
                    Assert.AreEqual((int)mesh.GetTopology(s), r.ReadInt32()); var indices = mesh.GetIndices(s); Assert.AreEqual(indices.Length, r.ReadUInt32());
                    foreach (int index in indices) Assert.AreEqual(index, r.ReadUInt16());
                }
                Assert.AreEqual(stream.Length, stream.Position);
            }
        }
        [Test] public void D_E_F_CollidersSharedPhysicsAndBodyParameters()
        {
            var physics = root.GetComponent<BoxCollider>().sharedMaterial;
            var sphere = root.AddComponent<SphereCollider>(); sphere.radius = .14f; sphere.center = Vector3.up * .12f; sphere.sharedMaterial = physics; sphere.isTrigger = true;
            var capsule = root.AddComponent<CapsuleCollider>(); capsule.radius = .08f; capsule.height = .3f; capsule.direction = 2; capsule.sharedMaterial = physics; capsule.enabled = false;
            var collider = root.AddComponent<MeshCollider>(); collider.sharedMesh = root.GetComponentInChildren<MeshFilter>().sharedMesh; collider.convex = true; collider.sharedMaterial = physics;
            var body = root.GetComponent<Rigidbody>(); body.mass = 3.5f; body.drag = .4f; body.angularDrag = .6f; body.constraints = RigidbodyConstraints.FreezeRotationX;
            var entries = Build(); var node = Nodes(entries)[0];
            foreach (var type in new[] { "BoxCollider", "SphereCollider", "CapsuleCollider", "MeshCollider" })
            {
                var c = Component(node, type);
                foreach (string key in new[] { "enabled", "isTrigger", "contactOffset", "physicsMaterial", "includeLayers", "excludeLayers", "layerOverridePriority", "providesContacts", "hasModifiableContacts" })
                    Assert.IsTrue(c.ContainsKey(key), type + ": " + key);
                Assert.AreEqual("physics_materials/0.json", c["physicsMaterial"]);
                Assert.IsFalse(c.ContainsKey("attachedRigidbody")); Assert.IsFalse(c.ContainsKey("bounds"));
            }
            Assert.AreEqual(.14, (double)Component(node, "SphereCollider")["radius"], .00001);
            Assert.AreEqual(2d, Component(node, "CapsuleCollider")["direction"]); Assert.AreEqual(false, Component(node, "CapsuleCollider")["enabled"]);
            Assert.AreEqual("meshes/0.meshbin", Component(node, "MeshCollider")["mesh"]);
            var p = Json(entries, "physics_materials/0.json"); Assert.AreEqual(.25, (double)p["dynamicFriction"], .00001);
            Assert.AreEqual(.35, (double)p["staticFriction"], .00001); Assert.AreEqual(.7, (double)p["bounciness"], .00001);
            Assert.AreEqual((double)CombineType.Multiply, p["frictionCombine"]); Assert.AreEqual((double)CombineType.Maximum, p["bounceCombine"]);
            var b = Component(node, "Rigidbody"); Assert.AreEqual(3.5d, (double)b["mass"], .00001); Assert.AreEqual(.4d, (double)b["drag"], .00001);
            Assert.AreEqual(.6d, (double)b["angularDrag"], .00001); Assert.AreEqual(true, b["isKinematic"]);
            Assert.AreEqual((double)RigidbodyConstraints.FreezeRotationX, b["constraints"]);
        }
        [Test] public void G_H_LuaAndFixedMetadataContainNoSourcePaths()
        {
            profile.Metadata.description = "中文描述\n\"quoted\""; var entries = Build();
            var script = File.ReadAllText(AssetDatabase.GetAssetPath(profile.LuaScript));
            Assert.AreEqual(script, MResJson.Utf8.GetString(entries["scripts/main.lua"]));
            var metadata = Json(entries, "metadata/metadata.json"); Assert.AreEqual(7, metadata.Count);
            Assert.AreEqual("common", metadata["rarity"]); Assert.AreEqual(20d, metadata["durability"]); Assert.AreEqual(5d, metadata["purchasePrice"]);
            Assert.AreEqual(profile.Metadata.description, metadata["description"]); Assert.AreEqual("device", metadata["type"]); Assert.AreEqual(120d, metadata["value"]);
            foreach (var item in entries.Where(e => e.Key.EndsWith(".json")))
            {
                string json = MResJson.Utf8.GetString(item.Value);
                StringAssert.DoesNotContain("Assets/", json); StringAssert.DoesNotContain(Application.dataPath, json);
                StringAssert.DoesNotContain("RuntimeBehaviour", json); StringAssert.DoesNotContain("LuaBehaviour", json);
            }
            Assert.IsFalse(entries.Keys.Any(k => k.EndsWith(".prefab") || k.EndsWith(".mat") || k.EndsWith(".meta") || k.EndsWith(".shader")));
        }
        [TestCase("")] [TestCase("Device")] [TestCase("other")]
        public void I_InvalidMetadataTypeRejected(string type)
        { Save(); profile.Metadata.type = type; Assert.IsFalse(MResBuildPipeline.Analyze(profile).CanBuild); Assert.Throws<InvalidOperationException>(() => MResBuildPipeline.Build(profile)); Assert.IsFalse(File.Exists(output)); }
        [Test] public void InvalidMetadataNameDescriptionValueRejected()
        {
            Save(); profile.Metadata.name = " "; Assert.IsFalse(MResBuildPipeline.Analyze(profile).CanBuild);
            profile.Metadata.name = "name"; profile.Metadata.description = ""; Assert.IsFalse(MResBuildPipeline.Analyze(profile).CanBuild);
            profile.Metadata.description = "description";
            foreach (float value in new[] { -1f, float.NaN, float.PositiveInfinity }) { profile.Metadata.value = value; Assert.IsFalse(MResBuildPipeline.Analyze(profile).CanBuild); }
        }
        [Test] public void J_UnregisteredShaderFailsWithoutReplacement()
        {
            Save(); var registry = ScriptableObject.CreateInstance<MResPackerShaderRegistry>();
            try { profile.ShaderRegistry = registry; var report = MResBuildPipeline.Analyze(profile); Assert.IsFalse(report.CanBuild); StringAssert.Contains("Unregistered shader", report.Errors.Single()); }
            finally { UnityEngine.Object.DestroyImmediate(registry); }
        }
        [Test] public void K_CorruptOffsetsSizesAndDuplicatePathsRejected()
        {
            Build(); var bytes = File.ReadAllBytes(output); long toc = (long)BitConverter.ToUInt64(bytes, 12); int length = (int)BitConverter.ToUInt32(bytes, (int)toc);
            foreach (int field in new[] { 0, 8 })
            {
                var broken = (byte[])bytes.Clone(); Buffer.BlockCopy(BitConverter.GetBytes(ulong.MaxValue), 0, broken, (int)toc + 4 + length + 4 + field, 8);
                Assert.Throws<FormatException>(() => MResStructureValidator.Validate(broken));
            }
            bytes[0] = 0; Assert.Throws<FormatException>(() => MResStructureValidator.Validate(bytes));
        }
        [Test] public void UnsupportedComponentsAndVariantsAreBlocked()
        {
            root.AddComponent<Light>(); root.AddComponent<Camera>(); Save();
            var report = MResBuildPipeline.Analyze(profile); Assert.AreEqual(2, report.UnsupportedComponents.Count); Assert.IsFalse(report.CanBuild);
            UnityEngine.Object.DestroyImmediate(root.GetComponent<Light>()); UnityEngine.Object.DestroyImmediate(root.GetComponent<Camera>()); Save();
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(profile.SourcePrefab);
            try
            {
                profile.SourcePrefab = PrefabUtility.SaveAsPrefabAsset(instance, scratch + "/Variant.prefab");
                Assert.AreEqual(PrefabAssetType.Variant, PrefabUtility.GetPrefabAssetType(profile.SourcePrefab)); Assert.IsFalse(MResBuildPipeline.Analyze(profile).CanBuild);
            }
            finally { UnityEngine.Object.DestroyImmediate(instance); }
        }
        [Test] public void UnreadableMeshAndBlendShapesAreBlocked()
        {
            var mesh = UnityEngine.Object.Instantiate(root.GetComponentInChildren<MeshFilter>().sharedMesh);
            AssetDatabase.CreateAsset(mesh, scratch + "/Mesh.asset"); root.GetComponentInChildren<MeshFilter>().sharedMesh = mesh; Save();
            var delta = new Vector3[mesh.vertexCount]; mesh.AddBlendShapeFrame("Unsupported", 100, delta, delta, delta);
            Assert.IsFalse(MResBuildPipeline.Analyze(profile).CanBuild); mesh.ClearBlendShapes(); mesh.UploadMeshData(true);
            var report = MResBuildPipeline.Analyze(profile); Assert.IsFalse(report.CanBuild); StringAssert.Contains("not readable", report.Errors.Single());
        }
        [Test] public void NonReadableTexturePreservesPixelsSettingsAndSourceImporter()
        {
            var source = root.GetComponentInChildren<MeshRenderer>().sharedMaterial.GetTexture("_BaseMap") as Texture2D;
            Assert.IsFalse(source.isReadable); var entries = Build();
            var manifest = Json(entries, "manifest.json"); var settings = (Dictionary<string, object>)((Dictionary<string, object>)manifest["textureSettings"])["textures/0.png"];
            Assert.AreEqual(false, settings["linear"]); Assert.AreEqual(true, settings["mipChain"]);
            var result = new Texture2D(2, 2); var expected = new Texture2D(2, 2);
            try
            {
                Assert.IsTrue(result.LoadImage(entries["textures/0.png"])); expected.LoadImage(File.ReadAllBytes(AssetDatabase.GetAssetPath(source)));
                var a = expected.GetPixels32(); var b = result.GetPixels32(); Assert.AreEqual(a.Length, b.Length);
                for (int i = 0; i < a.Length; i++) { Assert.LessOrEqual(Math.Abs(a[i].r - b[i].r), 2); Assert.LessOrEqual(Math.Abs(a[i].g - b[i].g), 2); Assert.AreEqual(a[i].a, b[i].a); }
                Assert.IsFalse(((TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(source))).isReadable);
            }
            finally { UnityEngine.Object.DestroyImmediate(result); UnityEngine.Object.DestroyImmediate(expected); }
        }
        [Test] public void MaterialAllPropertyTypesAndOrdering()
        {
            var entries = Build(); var material = Json(entries, "materials/0.json");
            Assert.AreEqual("MRes/ExampleSurface", material["shaderId"]);
            var properties = ((List<object>)material["properties"]).Cast<Dictionary<string, object>>().ToDictionary(p => (string)p["name"]);
            Assert.AreEqual(2d, properties["_Mode"]["value"]); Assert.AreEqual("Int", properties["_Mode"]["type"]);
            Assert.AreEqual(1.25d, properties["_Gain"]["value"]); Assert.AreEqual("Range", properties["_Blend"]["type"]);
            Assert.AreEqual("Color", properties["_BaseColor"]["type"]); Assert.AreEqual("Vector", properties["_Offset"]["type"]);
            Assert.AreEqual("textures/0.png", properties["_BaseMap"]["value"]); Assert.AreEqual(true, material["enableInstancing"]);
        }
        [Test] public void FailurePreservesExistingOutputAndCleansTemporaryFile()
        {
            Build(); byte[] original = File.ReadAllBytes(output); var entries = MResBuildPipeline.Analyze(profile).Entries;
            var invalid = entries.Where(e => e.Path != "meshes/0.meshbin").ToArray();
            Assert.Throws<FormatException>(() => MResWriter.Write(output, invalid)); CollectionAssert.AreEqual(original, File.ReadAllBytes(output));
            Assert.IsEmpty(Directory.GetFiles(Path.GetDirectoryName(output), Path.GetFileName(output) + ".*.tmp"));
            MResWriter.Write(output, entries); CollectionAssert.AreEqual(original, File.ReadAllBytes(output));
        }
        [Test] public void DuplicateEntryTraversalAndExtraCollisionRejected()
        {
            Save(); var entries = MResBuildPipeline.Analyze(profile).Entries;
            entries.Add(entries[0]); Assert.Throws<ArgumentException>(() => MResWriter.Write(output, entries));
            foreach (string path in new[] { "../bad", "/bad", "C:/bad", "a\\b", "a//b" }) Assert.Throws<FormatException>(() => MResStructureValidator.ValidatePath(path));
            profile.ExtraFiles[0].VirtualPath = "prefab/root.json"; Assert.IsFalse(MResBuildPipeline.Analyze(profile).CanBuild);
        }
        [Test] public void MedicineAndPackageWithoutLuaAreSupported()
        {
            profile.Metadata.type = "medicine"; profile.LuaScript = null; var entries = Build();
            Assert.IsFalse(Json(entries, "manifest.json").ContainsKey("lua")); Assert.AreEqual("medicine", Json(entries, "metadata/metadata.json")["type"]);
        }
        [Test] public void RarityPricesAndUnlimitedDurabilityRoundTrip()
        {
            foreach (string rarity in new[] { "common", "rare", "epic", "legendary" })
            {
                profile.Metadata.rarity = rarity; profile.Metadata.durability = -1;
                profile.Metadata.value = 24; profile.Metadata.purchasePrice = 5;
                var data = Json(Build(), "metadata/metadata.json");
                Assert.AreEqual(rarity, data["rarity"]); Assert.AreEqual(-1d, data["durability"]);
                Assert.AreEqual(24d, data["value"]); Assert.AreEqual(5d, data["purchasePrice"]);
            }
            foreach (int invalid in new[] { -2, 0, 10001 })
            { profile.Metadata.durability = invalid; Assert.IsFalse(MResBuildPipeline.Analyze(profile).CanBuild); }
        }
        [Test] public void InvalidUtf8LuaIsRejected()
        {
            File.WriteAllBytes(scratch + "/Invalid.lua", new byte[] { 0xff, 0xfe, 0xff }); AssetDatabase.ImportAsset(scratch + "/Invalid.lua");
            profile.LuaScript = AssetDatabase.LoadMainAssetAtPath(scratch + "/Invalid.lua"); Save(); Assert.IsFalse(MResBuildPipeline.Analyze(profile).CanBuild);
        }
        [Test] public void UInt32MeshAndAbsentOptionalChannels()
        {
            var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            try
            {
                mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.forward }; mesh.triangles = new[] { 0, 1, 2 };
                byte[] bytes = MResMeshSerializer.Serialize(mesh);
                Assert.AreEqual(1, bytes[6]); Assert.AreEqual(0, bytes[7]);
                using (var stream = new MemoryStream(bytes)) using (var r = new BinaryReader(stream))
                {
                    stream.Position = 40 + 3 * 12; Assert.AreEqual((int)MeshTopology.Triangles, r.ReadInt32()); Assert.AreEqual(3, r.ReadUInt32());
                    Assert.AreEqual(0, r.ReadUInt32()); Assert.AreEqual(1, r.ReadUInt32()); Assert.AreEqual(2, r.ReadUInt32()); Assert.AreEqual(stream.Length, stream.Position);
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(mesh); }
        }
    }
}
