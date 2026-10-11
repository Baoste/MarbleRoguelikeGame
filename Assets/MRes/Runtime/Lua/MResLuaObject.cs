using UnityEngine;
using UnityEngine.Scripting;

namespace MRes
{
    [Preserve]
    public sealed class MResLuaObject
    {
        private readonly GameObject gameObject;
        private readonly MResMetadata metadata;
        public MResLuaObject(GameObject gameObject, MResMetadata metadata) { this.gameObject = gameObject; this.metadata = metadata; }
        public Vector3 GetPosition() => gameObject.transform.position;
        public void SetPosition(float x, float y, float z) => gameObject.transform.position = new Vector3(x, y, z);
        public void Translate(float x, float y, float z) => gameObject.transform.Translate(x, y, z, Space.World);
        public void Rotate(float x, float y, float z) => gameObject.transform.Rotate(x, y, z, Space.Self);
        public void AddForce(float x, float y, float z)
        {
            var body = gameObject.GetComponentInParent<Rigidbody>();
            if (body != null) body.AddForce(x, y, z, ForceMode.Force);
        }
        public void SetVelocity(float x, float y, float z)
        {
            var body = gameObject.GetComponentInParent<Rigidbody>();
            if (body == null) return;
#if UNITY_6000_0_OR_NEWER
            body.linearVelocity = new Vector3(x, y, z);
#else
            body.velocity = new Vector3(x, y, z);
#endif
        }
        public string GetName() => metadata?.name;
        public string GetDescription() => metadata?.description;
        public float GetValue() => metadata?.value ?? 0;
        public float GetPurchasePrice() => metadata?.purchasePrice ?? 5;
        public string GetRarity() => metadata?.rarity ?? "common";
        public int GetDurability() => metadata?.durability ?? 20;
        public string GetItemType() => metadata?.type.ToString();
        internal static MResLuaObject Other(GameObject go)
        {
            var owner = go.GetComponentInParent<MResLifetime>();
            return new MResLuaObject(go, owner?.Handle?.Metadata);
        }
    }
}
