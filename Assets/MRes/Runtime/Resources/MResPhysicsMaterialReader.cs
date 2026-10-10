using UnityEngine;
#if UNITY_6000_0_OR_NEWER
using PhysicsMaterialType = UnityEngine.PhysicsMaterial;
using CombineType = UnityEngine.PhysicsMaterialCombine;
#else
using PhysicsMaterialType = UnityEngine.PhysicMaterial;
using CombineType = UnityEngine.PhysicMaterialCombine;
#endif

namespace MRes
{
    internal sealed class MResPhysicsMaterialData
    {
        internal string Name;
        internal float DynamicFriction, StaticFriction, Bounciness;
        internal CombineType FrictionCombine, BounceCombine;
    }
    internal static class MResPhysicsMaterialReader
    {
        internal static MResPhysicsMaterialData Parse(MResFields f)
        {
            var d = new MResPhysicsMaterialData { Name = f.String("name"),
                DynamicFriction = MResValues.Nonnegative(f, "dynamicFriction", 0.6f),
                StaticFriction = MResValues.Nonnegative(f, "staticFriction", 0.6f),
                Bounciness = MResValues.Nonnegative(f, "bounciness", 0),
                FrictionCombine = MResValues.Enum<CombineType>(f, "frictionCombine", 0),
                BounceCombine = MResValues.Enum<CombineType>(f, "bounceCombine", 0) };
            if (d.Bounciness > 1) throw f.Error("bounciness"); return d;
        }
        internal static PhysicsMaterialType Create(MResPhysicsMaterialData d, MResLoadHandle handle)
        {
            var m = handle.Own(new PhysicsMaterialType(d.Name));
            m.dynamicFriction = d.DynamicFriction; m.staticFriction = d.StaticFriction; m.bounciness = d.Bounciness;
            m.frictionCombine = d.FrictionCombine; m.bounceCombine = d.BounceCombine; return m;
        }
    }
}
