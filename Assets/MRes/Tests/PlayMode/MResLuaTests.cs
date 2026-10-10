using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MRes.Tests
{
    public sealed class MResLuaTests
    {
        private MResShaderRegistry registry;
        private readonly List<MResLoadHandle> handles = new List<MResLoadHandle>();
        private GameObject other;
        private bool autoSimulation;
        [SetUp] public void SetUp()
        {
            registry = ScriptableObject.CreateInstance<MResShaderRegistry>();
            registry.entries = new[] { new MResShaderRegistry.Entry { id = "MRes/Unlit", shader = Shader.Find("MRes/DemoUnlit") } };
            autoSimulation = Physics.autoSimulation;
        }
        [UnityTearDown] public IEnumerator TearDown()
        {
            foreach (var handle in handles) handle.Dispose(); handles.Clear();
            if (other != null) Object.Destroy(other);
            Object.Destroy(registry); Physics.autoSimulation = autoSimulation;
            MResLuaRuntime.Dispose(); yield return null;
        }
        private MResLoadHandle Load(string lua, string nodes = null)
        {
            var h = MResLoader.Load(MResFixture.Create(lua: lua, nodes: nodes), registry); handles.Add(h); return h;
        }
        [UnityTest] public IEnumerator StartUpdateFixedUpdateAndInstanceIsolationUseOneVm()
        {
            const string lua = "counter=(counter or 0)+1; function Start() self:SetPosition(counter,0,0) end " +
                "function Update(dt) metadata.value=metadata.value+dt end function FixedUpdate(dt) self:Translate(0,dt,0) end " +
                "function OnDestroy() metadata.description='destroyed' end";
            var a = Load(lua, MResFixture.Node(1,-1,"A")); var vm = MResLuaRuntime.Env;
            var b = Load(lua, MResFixture.Node(1,-1,"B")); Assert.AreSame(vm, MResLuaRuntime.Env);
            Assert.AreEqual(2, MResLuaRuntime.ActiveBehaviourCount);
            yield return null; yield return new WaitForFixedUpdate(); yield return null;
            Assert.AreEqual(1, a.Root.transform.position.x); Assert.AreEqual(1, b.Root.transform.position.x);
            Assert.Greater(a.Root.transform.position.y, 0); Assert.Greater(a.Metadata.value, 120);
            Assert.IsTrue(a.Root.GetComponents<MonoBehaviour>().Any(c => c.GetType().Name == "MResRuntimeBehaviour"));
            Assert.AreEqual(1, Object.FindObjectsOfType<MResLuaRuntimeDriver>().Length);
            a.Dispose(); Assert.AreEqual("destroyed", a.Metadata.description); Assert.AreEqual(1, MResLuaRuntime.ActiveBehaviourCount);
            Assert.AreSame(vm, MResLuaRuntime.Env); float value = b.Metadata.value; yield return null;
            Assert.Greater(b.Metadata.value, value);
        }
        [UnityTest] public IEnumerator RealCollisionCallsLuaAndAppliesForceToOtherBody()
        {
            Physics.autoSimulation = false;
            var h = Load("function OnCollisionEnter(other) metadata.value=1; other:AddForce(0,600,0) end");
            other = GameObject.CreatePrimitive(PrimitiveType.Sphere); other.transform.localScale = Vector3.one * 0.5f;
            other.transform.position = new Vector3(0,0.24f,0); var body = other.AddComponent<Rigidbody>(); body.useGravity = false;
            yield return null;
            Physics.SyncTransforms(); Physics.Simulate(0.02f); Physics.Simulate(0.02f);
            Assert.AreEqual(1, h.Metadata.value); Assert.Greater(body.velocity.y, 1);
        }
        [UnityTest] public IEnumerator InactiveColliderRestoresContactSettingsWhenActivated()
        {
            var h = Load(null, MResFixture.Node(1,-1,"InactivePad",MResFixture.Box,active:false));
            h.Root.SetActive(true); yield return null;
            var box = h.Root.GetComponent<BoxCollider>();
            Assert.AreEqual(0.03f, box.contactOffset);
            Assert.IsTrue(box.hasModifiableContacts); Assert.IsTrue(box.providesContacts);
            h.Root.SetActive(false); yield return null; h.Root.SetActive(true); yield return null;
            Assert.AreEqual(0.03f,box.contactOffset);
        }
        [UnityTest] public IEnumerator ChildTriggerForwardsEnterStayExit()
        {
            Physics.autoSimulation = false;
            string trigger = "{\"type\":\"BoxCollider\",\"size\":{\"x\":2,\"y\":2,\"z\":2},\"isTrigger\":true}";
            var h = Load("function OnTriggerEnter(o) metadata.value=1 end function OnTriggerStay(o) metadata.description='stay' end function OnTriggerExit(o) metadata.name='exit' end",
                MResFixture.Node(1,-1,"Root") + "," + MResFixture.Node(2,1,"Trigger",trigger));
            other = GameObject.CreatePrimitive(PrimitiveType.Sphere); other.transform.position = Vector3.zero;
            other.AddComponent<Rigidbody>().useGravity = false; yield return null;
            Physics.SyncTransforms(); Physics.Simulate(0.02f); Physics.Simulate(0.02f);
            Assert.AreEqual(1,h.Metadata.value); Assert.AreEqual("stay",h.Metadata.description);
            other.transform.position = Vector3.one * 20; Physics.SyncTransforms(); Physics.Simulate(0.02f);
            Assert.AreEqual("exit", h.Metadata.name);
        }
        [UnityTest] public IEnumerator RealCollisionForwardsStayAndExit()
        {
            Physics.autoSimulation = false;
            var h = Load("function OnCollisionStay(o) metadata.description='stay' end function OnCollisionExit(o) metadata.name='exit' end");
            other = GameObject.CreatePrimitive(PrimitiveType.Sphere); other.transform.localScale = Vector3.one * 0.5f;
            other.transform.position = new Vector3(0,0.24f,0); other.AddComponent<Rigidbody>(); yield return null;
            Physics.SyncTransforms();
            for (int i = 0; i < 5; i++) Physics.Simulate(0.02f);
            Assert.AreEqual("stay",h.Metadata.description);
            other.transform.position = Vector3.one * 20; Physics.SyncTransforms(); Physics.Simulate(0.02f);
            Assert.AreEqual("exit",h.Metadata.name);
        }
        [UnityTest] public IEnumerator DestroyRootReclaimsAssetsAndVmShutdownReleasesInactiveInstances()
        {
            var h = Load("function OnDestroy() metadata.value=77 end");
            var mesh = h.Root.GetComponent<MeshFilter>().sharedMesh;
            var material = h.Root.GetComponent<MeshRenderer>().sharedMaterial;
            var texture = material.GetTexture("_MainTex"); var physics = h.Root.GetComponent<Collider>().sharedMaterial;
            Object.Destroy(h.Root); yield return null; yield return null;
            Assert.IsTrue(h.IsDisposed); Assert.AreEqual(77,h.Metadata.value);
            Assert.IsTrue(mesh == null); Assert.IsTrue(material == null); Assert.IsTrue(texture == null); Assert.IsTrue(physics == null);
            var inactive = Load("function OnDestroy() metadata.value=88 end", MResFixture.Node(1,-1,"Inactive",active:false));
            MResLuaRuntime.Dispose(); Assert.AreEqual(88,inactive.Metadata.value); Assert.AreEqual(0,MResLuaRuntime.ActiveBehaviourCount);
        }
    }
}
