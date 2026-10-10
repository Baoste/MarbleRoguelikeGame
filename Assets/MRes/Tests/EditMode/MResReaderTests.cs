using System;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace MRes.Tests
{
    public sealed class MResReaderTests
    {
        private MResShaderRegistry registry;
        [SetUp] public void SetUp()
        {
            registry = ScriptableObject.CreateInstance<MResShaderRegistry>();
            registry.entries = new[] { new MResShaderRegistry.Entry { id = "MRes/Unlit", shader = Shader.Find("MRes/DemoUnlit") } };
            MResLoader.ShaderRegistry = registry;
        }
        [TearDown] public void TearDown()
        {
            MResLuaRuntime.Dispose(); MResLoader.ShaderRegistry = null;
            UnityEngine.Object.DestroyImmediate(registry);
        }
        [Test] public void ReadIsPureAndStreamsRemainOpen()
        {
            int count = Resources.FindObjectsOfTypeAll<GameObject>().Length;
            byte[] bytes = MResFixture.Create();
            var p = MResLoader.Read(bytes);
            Assert.AreEqual("弹射装置", p.Metadata.name); Assert.AreEqual(120, p.Metadata.value);
            Assert.AreEqual(MResItemType.Device, p.Metadata.type);
            Assert.AreEqual(count, Resources.FindObjectsOfTypeAll<GameObject>().Length);
            Array.Clear(bytes, 0, bytes.Length);
            Assert.AreEqual((byte)'{', p.GetBytes("metadata/metadata.json")[0]);
            using (var stream = new MemoryStream(MResFixture.Create()))
            { Assert.AreEqual("BouncePad", MResLoader.Read(stream).Name); Assert.IsTrue(stream.CanRead); }
            using (var stream = new NonSeekableStream(MResFixture.Create()))
                Assert.AreEqual("BouncePad", MResLoader.Read(stream).Name);
            using (var stream = new MemoryStream())
            {
                stream.Write(new byte[9], 0, 9); var valid = MResFixture.Create(); stream.Write(valid, 0, valid.Length); stream.Position = 9;
                Assert.AreEqual("BouncePad", MResLoader.Read(stream).Name);
            }
        }
        [Test] public void RestoresHierarchyMeshMaterialsAndPhysics()
        {
            using (var h = MResLoader.Load(MResFixture.Create()))
            {
                var child = h.Root.transform.Find("Child"); var grandchild = child.Find("Grandchild");
                Assert.AreEqual(new Vector3(1, 2, 3), grandchild.localPosition); Assert.IsFalse(grandchild.gameObject.activeSelf);
                var mesh = h.Root.GetComponent<MeshFilter>().sharedMesh;
                Assert.AreEqual(4, mesh.vertexCount); Assert.AreEqual(IndexFormat.UInt16, mesh.indexFormat);
                Assert.AreEqual(2, mesh.subMeshCount); CollectionAssert.AreEqual(new[] { 0, 2, 3 }, mesh.GetIndices(1));
                Assert.AreEqual(MeshTopology.Triangles, mesh.GetTopology(0));
                Assert.AreEqual(Vector3.up, mesh.normals[0]); Assert.AreEqual(new Vector4(1, 0, 0, 1), mesh.tangents[0]);
                Assert.AreEqual(Vector2.one, mesh.uv[2]); Assert.AreEqual(Vector2.one, mesh.uv2[2]);
                Assert.AreEqual(Color.white, mesh.colors[0]); Assert.AreEqual(new Vector3(4, 0, 4), mesh.bounds.size);
                var slots = h.Root.GetComponent<MeshRenderer>().sharedMaterials;
                Assert.AreEqual(3, slots.Length); Assert.AreEqual("SecondSlot", slots[1].name); Assert.IsNull(slots[2]);
                var material = slots[0]; var texture = (Texture2D)material.GetTexture("_MainTex");
                Assert.AreEqual("MRes/DemoUnlit", material.shader.name); Assert.AreEqual(2100, material.renderQueue);
                Assert.IsTrue(material.enableInstancing, "enableInstancing");
                Assert.IsTrue(material.doubleSidedGI, "doubleSidedGI");
                Assert.IsTrue(material.IsKeywordEnabled("MRES_TEST"), "MRES_TEST keyword");
                Assert.AreEqual(0.75f, material.GetFloat("_Amount")); Assert.AreEqual(0.25f, material.GetFloat("_Range"));
                Assert.AreEqual(7, material.GetInteger("_Count")); Assert.AreEqual(new Vector4(1, 2, 3, 4), material.GetVector("_Direction"));
                Assert.AreEqual(new Vector2(2, 3), material.GetTextureScale("_MainTex"));
                Assert.AreEqual(new Vector2(0.1f, 0.2f), material.GetTextureOffset("_MainTex"));
                Assert.AreEqual("FixtureTexture", texture.name); Assert.AreEqual(TextureWrapMode.Clamp, texture.wrapModeU);
                Assert.AreEqual(TextureWrapMode.Mirror, texture.wrapModeV); Assert.AreEqual(FilterMode.Point, texture.filterMode);
                Assert.AreEqual(3, texture.anisoLevel);
                var box = h.Root.GetComponent<BoxCollider>(); var sphere = child.GetComponent<SphereCollider>();
                Assert.AreEqual(new Vector3(4, 0.5f, 4), box.size); Assert.AreEqual(new Vector3(0, -0.25f, 0), box.center);
                Assert.AreEqual(0.03f, box.contactOffset); Assert.IsFalse(box.isTrigger); Assert.IsTrue(box.enabled);
                Assert.AreSame(box.sharedMaterial, sphere.sharedMaterial); Assert.AreEqual(0.7f, sphere.radius); Assert.IsFalse(sphere.enabled);
                Assert.AreEqual(0.2f, box.sharedMaterial.dynamicFriction); Assert.AreEqual(0.3f, box.sharedMaterial.staticFriction);
                Assert.AreEqual(0.5f, box.sharedMaterial.bounciness); Assert.AreEqual(2, (int)box.sharedMaterial.frictionCombine);
                Assert.AreEqual(3, (int)box.sharedMaterial.bounceCombine);
                Assert.AreEqual(4, box.includeLayers.value); Assert.AreEqual(8, box.excludeLayers.value);
                Assert.AreEqual(3, box.layerOverridePriority); Assert.IsTrue(box.providesContacts); Assert.IsTrue(box.hasModifiableContacts);
                var capsule = grandchild.GetComponent<CapsuleCollider>();
                Assert.AreEqual(0.25f, capsule.radius); Assert.AreEqual(1.5f, capsule.height); Assert.AreEqual(2, capsule.direction);
                Assert.IsNull(capsule.sharedMaterial); Assert.AreSame(mesh, grandchild.GetComponent<MeshCollider>().sharedMesh);
                Assert.IsFalse(h.Root.GetComponents<MonoBehaviour>().Any(c => c.GetType().Name == "MResRuntimeBehaviour"));
            }
        }
        [Test] public void RestoresRigidbodyAndMedicineMetadata()
        {
            var node = MResFixture.Node(0, -1, "Body", "{\"type\":\"Rigidbody\",\"mass\":2.5,\"drag\":0.2,\"angularDrag\":0.3,\"useGravity\":false,\"isKinematic\":true,\"interpolation\":1,\"collisionDetectionMode\":3,\"constraints\":10}");
            using (var h = MResLoader.Load(MResFixture.Create(nodes: node, metadata: MResFixture.Metadata.Replace("device", "medicine"))))
            {
                Assert.AreEqual(MResItemType.Medicine, h.Metadata.type);
                var b = h.Root.GetComponent<Rigidbody>(); Assert.AreEqual(2.5f, b.mass);
                Assert.AreEqual(0.2f, b.drag); Assert.AreEqual(0.3f, b.angularDrag); Assert.IsFalse(b.useGravity); Assert.IsTrue(b.isKinematic);
                Assert.AreEqual(RigidbodyInterpolation.Interpolate, b.interpolation); Assert.AreEqual(CollisionDetectionMode.ContinuousSpeculative, b.collisionDetectionMode);
                Assert.AreEqual((RigidbodyConstraints)10, b.constraints);
            }
        }
        [TestCase("Device")][TestCase("weapon")][TestCase("")][TestCase("1")]
        public void InvalidMetadataTypeIsRejected(string type)
            => Assert.Throws<MResFormatException>(() => MResLoader.Read(MResFixture.Create(metadata: MResFixture.Metadata.Replace("device", type))));
        [Test] public void MissingMetadataValueAndDuplicateFieldsAreRejected()
        {
            Assert.Throws<MResFormatException>(() => MResLoader.Read(MResFixture.Create(metadata: "{\"name\":\"a\",\"description\":\"b\",\"type\":\"device\"}")));
            Assert.Throws<MResFormatException>(() => MResLoader.Read(MResFixture.Create(metadata: "{\"name\":\"a\",\"name\":\"b\"}")));
            Assert.Throws<MResFormatException>(() => MResLoader.Read(MResFixture.Create(metadata: MResFixture.Metadata.Replace("120", "1e999"))));
        }
        [TestCase(0)][TestCase(4)][TestCase(6)]
        public void InvalidHeaderIsRejected(int offset)
        { var bytes = MResFixture.Create(); bytes[offset] = 99; Assert.Throws<MResFormatException>(() => MResLoader.Read(bytes)); }
        [Test] public void TocRangesCompressionPathsAndOverlapAreRejected()
        {
            foreach (Action<byte[], int> mutate in new Action<byte[], int>[] {
                (b,t) => Write64(b,12,ulong.MaxValue), (b,t) => Write64(b,20,ulong.MaxValue),
                (b,t) => Write64(b,t+4+13+4,ulong.MaxValue), (b,t) => Write64(b,t+4+13+12,ulong.MaxValue),
                (b,t) => b[t+4+13+1] = 1, (b,t) => b[t+4+13+2] = 1,
                (b,t) => b[t+4] = (byte)'/', (b,t) => b[t+4] = (byte)'\\',
                (b,t) => Write64(b,t+4+13+4,(ulong)t),
                (b,t) => { int second=t+24+13; Write64(b,second+4+16+4,28); },
                (b,t) => { b[8]=0; b[9]=0; b[10]=0; b[11]=0; }
            })
            {
                var bytes = MResFixture.Create(); int toc = (int)BitConverter.ToUInt64(bytes, 12); mutate(bytes, toc);
                Assert.Throws<MResFormatException>(() => MResLoader.Read(bytes));
            }
        }
        [Test] public void DuplicatePathAndInvalidUtf8AreRejected()
        {
            var bytes = MResFixture.Create(); int toc = (int)BitConverter.ToUInt64(bytes, 12);
            // Both material paths have the same UTF-8 length.
            int first = Find(bytes, Encoding.UTF8.GetBytes("materials/0.json"), toc);
            int second = Find(bytes, Encoding.UTF8.GetBytes("materials/1.json"), toc);
            Buffer.BlockCopy(bytes, first, bytes, second, 16);
            Assert.Throws<MResFormatException>(() => MResLoader.Read(bytes));
            bytes = MResFixture.Create(); bytes[toc + 4] = 0xff;
            Assert.Throws<MResFormatException>(() => MResLoader.Read(bytes));
        }
        [Test] public void GraphAndReferencesAreValidatedBeforeInstantiation()
        {
            Assert.Throws<MResFormatException>(() => MResLoader.Read(MResFixture.Create(nodes: MResFixture.Node(1,-1,"R") + "," + MResFixture.Node(2,3,"A") + "," + MResFixture.Node(3,2,"B"))));
            Assert.Throws<MResFormatException>(() => MResLoader.Read(MResFixture.Create(nodes: MResFixture.Node(1,-1,"R") + "," + MResFixture.Node(2,99,"A"))));
            Assert.Throws<MResFormatException>(() => MResLoader.Read(MResFixture.Create(nodes: MResFixture.Node(1,-1,"R", "{\"type\":\"MResRuntimeBehaviour\"}"))));
            Assert.Throws<MResMissingResourceException>(() => MResLoader.Read(MResFixture.Create(nodes: MResFixture.Node(1,-1,"R", "{\"type\":\"MeshFilter\",\"mesh\":\"missing.meshbin\"}"))));
        }
        [Test] public void ShaderFailureAndLuaFailureRollBackResources()
        {
            int meshes = Resources.FindObjectsOfTypeAll<Mesh>().Length;
            int textures = Resources.FindObjectsOfTypeAll<Texture2D>().Length;
            int objects = Resources.FindObjectsOfTypeAll<GameObject>().Length;
            registry.entries = Array.Empty<MResShaderRegistry.Entry>();
            Assert.Throws<MResShaderException>(() => MResLoader.Load(MResFixture.Create()));
            Assert.AreEqual(meshes, Resources.FindObjectsOfTypeAll<Mesh>().Length);
            Assert.AreEqual(textures, Resources.FindObjectsOfTypeAll<Texture2D>().Length);
            registry.entries = new[] { new MResShaderRegistry.Entry { id = "MRes/Unlit", shader = Shader.Find("MRes/DemoUnlit") } };
            Assert.Throws<MResLuaException>(() => MResLoader.Load(MResFixture.Create(lua: "error('expected failure')")));
            Assert.AreEqual(meshes, Resources.FindObjectsOfTypeAll<Mesh>().Length);
            Assert.AreEqual(objects, Resources.FindObjectsOfTypeAll<GameObject>().Length);
            Assert.AreEqual(0, MResLuaRuntime.ActiveBehaviourCount);
        }
        [Test] public void DisposeIsIdempotentAndCleansInactiveRoots()
        {
            var h = MResLoader.Load(MResFixture.Create(lua: "function OnDestroy() metadata.value = 999 end", nodes: MResFixture.Node(1,-1,"Inactive", active:false)));
            var root = h.Root; var metadata = h.Metadata;
            h.Dispose(); h.Dispose(); Assert.IsTrue(root == null); Assert.IsTrue(h.IsDisposed); Assert.AreEqual(999, metadata.value);
            Assert.AreEqual(0, MResLuaRuntime.ActiveBehaviourCount);
        }
        [Test] public void RuntimeAssemblyDoesNotReferenceEditorOrPacker()
        {
            var names = typeof(MResLoader).Assembly.GetReferencedAssemblies().Select(a => a.Name).ToArray();
            Assert.Contains("XLua.Runtime", names); Assert.IsFalse(names.Any(n => n.Contains("UnityEditor") || n.Contains("Packer")));
        }
        [Test] public void FileAndStreamOverloadsLoadTheSamePackage()
        {
            using (var file = MResLoader.Load(Path.Combine(Application.dataPath, "MRes/Samples/BouncePad.mres.bytes")))
                Assert.AreEqual("弹射装置", file.Metadata.name);
            using (var stream = new NonSeekableStream(MResFixture.Create()))
            {
                using (var handle = MResLoader.Load(stream)) Assert.IsNotNull(handle.Root);
                Assert.IsTrue(stream.CanRead);
            }
        }
        [Test] public void InvalidMeshDataAndMissingTextureAreRejected()
        {
            var bytes = MResFixture.Create(); var package = MResLoader.Read(bytes);
            int mesh = (int)package.Entries["meshes/0.meshbin"].Offset;
            Buffer.BlockCopy(BitConverter.GetBytes(float.NaN), 0, bytes, mesh + 40, 4);
            Assert.Throws<MResFormatException>(() => MResLoader.Read(bytes));
            bytes = MResFixture.Create(); bytes[mesh + (int)package.Entries["meshes/0.meshbin"].Size - 2] = 99;
            Assert.Throws<MResFormatException>(() => MResLoader.Read(bytes));
            Assert.Throws<MResMissingResourceException>(() => MResLoader.Read(MResFixture.Create(material:
                "{\"shaderId\":\"MRes/Unlit\",\"properties\":[{\"name\":\"_MainTex\",\"type\":\"Texture\",\"value\":\"textures/missing.png\"}]}")));
        }
        [Test] public void RepeatedLoadDisposeDoesNotAccumulateDynamicResources()
        {
            int meshes = Resources.FindObjectsOfTypeAll<Mesh>().Length;
            int textures = Resources.FindObjectsOfTypeAll<Texture2D>().Length;
            int materials = Resources.FindObjectsOfTypeAll<Material>().Length;
            var package = MResLoader.Read(MResFixture.Create(lua:"function OnDestroy() end"));
            for (int i = 0; i < 10; i++) using (var h = MResLoader.Load(package, registry)) Assert.IsNotNull(h.Root);
            Assert.AreEqual(meshes, Resources.FindObjectsOfTypeAll<Mesh>().Length);
            Assert.AreEqual(textures, Resources.FindObjectsOfTypeAll<Texture2D>().Length);
            Assert.AreEqual(materials, Resources.FindObjectsOfTypeAll<Material>().Length);
            Assert.AreEqual(0, MResLuaRuntime.ActiveBehaviourCount);
        }
        [Test] public void PreviewDoesNotExecuteEvenTopLevelLuaAndKeepsRootInactive()
        {
            var package = MResLoader.Read(MResFixture.Create(lua: "error('preview must never execute this')"));
            using (var handle = MResLoader.Load(package, registry, new MResLoadOptions { ExecuteLua = false, ActivateRoot = false }))
            {
                Assert.IsFalse(handle.Root.activeSelf);
                Assert.AreEqual(0, MResLuaRuntime.ActiveBehaviourCount);
                Assert.IsNotNull(handle.Root.GetComponent<MeshFilter>().sharedMesh);
            }
        }
        [Test] public void SharedScriptRetainsInstanceStateAcrossVisualRebuilds()
        {
            var package = MResLoader.Read(MResFixture.Create(lua: "count=0; function Increment() count=count+1; metadata.value=count end; function OnDestroy() metadata.description='released' end"));
            using (var script = new MResLuaScript(package))
            {
                var options = new MResLoadOptions { SharedScript = script, ActivateRoot = false };
                using (MResLoader.Load(package, registry, options)) { script.Invoke("Increment"); }
                Assert.IsFalse(script.IsDisposed);
                Assert.AreEqual(1, MResLuaRuntime.ActiveBehaviourCount);
                using (MResLoader.Load(package, registry, options)) { script.Invoke("Increment"); }
                Assert.IsFalse(script.IsDisposed);
                Assert.AreEqual(2, script.Metadata.value);
            }
            Assert.AreEqual(0, MResLuaRuntime.ActiveBehaviourCount);
        }
        private static void Write64(byte[] bytes, int offset, ulong value) => Buffer.BlockCopy(BitConverter.GetBytes(value), 0, bytes, offset, 8);
        private static int Find(byte[] bytes, byte[] pattern, int start)
        {
            for (int i = start; i <= bytes.Length - pattern.Length; i++)
                if (bytes.Skip(i).Take(pattern.Length).SequenceEqual(pattern)) return i;
            throw new Exception("Fixture pattern missing.");
        }
        private sealed class NonSeekableStream : MemoryStream
        {
            internal NonSeekableStream(byte[] bytes) : base(bytes) { }
            public override bool CanSeek => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        }
    }
}
