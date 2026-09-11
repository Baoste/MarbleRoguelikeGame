using UnityEngine;

namespace MarblesECS.PhysX
{
    public sealed partial class MarblePhysicsBridge
    {
        private void ConfigureMaterial(MarbleBody view, SphereCollider collider, MarbleSpawnData data)
        {
            if (!data.HasRestitution) { collider.sharedMaterial = material; return; }
            if (view.RuntimeMaterial == null)
                view.RuntimeMaterial = new PhysicMaterial("Marble restitution (owned by pooled body)");
            var target = view.RuntimeMaterial;
            target.dynamicFriction = material == null ? 0.15f : material.dynamicFriction;
            target.staticFriction = material == null ? 0.15f : material.staticFriction;
            target.frictionCombine = material == null ? PhysicMaterialCombine.Minimum : material.frictionCombine;
            target.bounceCombine = PhysicMaterialCombine.Maximum;
            target.bounciness = data.Restitution;
            collider.sharedMaterial = target;
        }
    }
}
