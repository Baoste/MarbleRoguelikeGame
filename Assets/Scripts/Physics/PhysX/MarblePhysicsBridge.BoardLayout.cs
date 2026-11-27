using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace MarblesECS.PhysX
{
    public sealed partial class MarblePhysicsBridge : IMarbleBoardLayoutPhysics
    {
        private const string BoardLayoutName = "Day Board Layout";
        private const float CollisionPinBounce = .8f;
        private static readonly int ColorProperty = Shader.PropertyToID("_Color");
        private static readonly int BaseColorProperty = Shader.PropertyToID("_BaseColor");
        private BoardPlacementSurface layoutSurface;
        private Transform boardRoot;
        private Transform boardLayoutRoot;
        private MarblePin[] scenePins;
        private MarbleZone[] sceneZones;
        private Material pinVisualMaterial;
        private PhysicMaterial pinPhysicsMaterial;
        private PhysicMaterial collisionPinPhysicsMaterial;
        private Material zoneVisualMaterial;
        private readonly List<GameObject> hiddenBoardObjects = new List<GameObject>();

        internal Transform GeneratedBoardLayout => boardLayoutRoot;

        internal void ConfigureBoardLayout(Transform root, BoardPlacementSurface surface)
        {
            boardRoot = root;
            layoutSurface = surface;
        }

        public void ValidateBoardLayout(BoardLayoutDefinition layout)
        {
            if (layout == null) throw new ArgumentNullException(nameof(layout));
            if (!boardRoot || !layoutSurface || !layoutSurface.transform.IsChildOf(boardRoot) ||
                (layoutSurface.transform.lossyScale - Vector3.one).sqrMagnitude > .000001f)
                throw new ArgumentException($"{layout.Source} requires a unit-scale BoardPlacementSurface under PhysicsRoot.");
            layoutSurface.LayoutProjection.Validate(layout);
        }

        public void ApplyBoardLayout(BoardLayoutDefinition layout)
        {
            if (disposed) throw new ObjectDisposedException(nameof(MarblePhysicsBridge));
            if (bodies.Count != 0) throw new InvalidOperationException("A board layout cannot change while marbles are in play.");
            if (layout == null)
            {
                if (boardLayoutRoot != null || hiddenBoardObjects.Count > 0) RemoveBoardLayout();
                return;
            }

            ValidateBoardLayout(layout);
            CaptureSceneBoard();
            var ids = new HashSet<int>();
            foreach (var pin in scenePins)
                if (pin) ids.Add(pin.TargetId);
            foreach (var zone in sceneZones)
                if (zone) ids.Add(zone.TargetId);

            var group = new GameObject(BoardLayoutName);
            group.SetActive(false);
            SceneManager.MoveGameObjectToScene(group, scene);
            group.transform.SetParent(layoutSurface.transform, false);
            try
            {
                int pinId = TargetIdAllocator.FirstPinId;
                int zoneId = TargetIdAllocator.FirstZoneId;
                var projection = layoutSurface.LayoutProjection;
                foreach (var pin in layout.Pins) AddLayoutPin(group.transform, projection, pin, ids, ref pinId);
                foreach (var zone in layout.Zones)
                    AddLayoutZone(group.transform, projection, zone, TargetIdAllocator.TakeNext(ids, ref zoneId));
            }
            catch
            {
                DestroyLayoutObject(group);
                throw;
            }

            if (boardLayoutRoot == null) HideSceneBoard();
            else DestroyGeneratedBoard();
            boardLayoutRoot = group.transform;
            group.SetActive(true);
            Physics.SyncTransforms();
        }

        private void CaptureSceneBoard()
        {
            if (scenePins != null) return;
            scenePins = boardRoot.GetComponentsInChildren<MarblePin>(true);
            sceneZones = boardRoot.GetComponentsInChildren<MarbleZone>(true);
            foreach (var pin in scenePins)
                if (pin.TryGetComponent<Renderer>(out var renderer) && renderer.sharedMaterial != null)
                {
                    pinVisualMaterial = renderer.sharedMaterial;
                    if (pin.TryGetComponent<Collider>(out var collider)) pinPhysicsMaterial = collider.sharedMaterial;
                    break;
                }
            foreach (var zone in sceneZones)
                if (zone.Kind == MarbleContactKind.Score && zone.TryGetComponent<Renderer>(out var renderer) && renderer.sharedMaterial != null)
                { zoneVisualMaterial = renderer.sharedMaterial; break; }
        }

        private void HideSceneBoard()
        {
            foreach (var pin in scenePins)
                if (pin) HideBoardObject(pin.gameObject);
            foreach (var zone in sceneZones)
                if (zone && zone.Kind != MarbleContactKind.Drain) HideBoardObject(zone.gameObject);
        }

        private void HideBoardObject(GameObject node)
        {
            if (!node.activeSelf) return;
            hiddenBoardObjects.Add(node);
            node.SetActive(false);
        }

        private void AddLayoutPin(Transform parent, BoardLayoutProjection projection, BoardLayoutPin data,
            HashSet<int> ids, ref int nextPinId)
        {
            var node = createVisuals ? GameObject.CreatePrimitive(PrimitiveType.Cylinder) :
                new GameObject("Pin", typeof(CapsuleCollider));
            node.name = (data.collisionOnly ? "Collision Pin " : "Pin ") + parent.childCount;
            node.transform.SetParent(parent, false);
            node.transform.localPosition = projection.ToSurfaceLocal(data.x, data.y, data.radius * 2);
            node.transform.localScale = Vector3.one * (data.radius * 2);
            if (createVisuals && pinVisualMaterial) node.GetComponent<Renderer>().sharedMaterial = pinVisualMaterial;
            var collider = node.GetComponent<CapsuleCollider>();
            if (data.collisionOnly)
            {
                collider.sharedMaterial = GetCollisionPinMaterial();
                return;
            }
            if (pinPhysicsMaterial) collider.sharedMaterial = pinPhysicsMaterial;
            var pin = node.AddComponent<MarblePin>();
            pin.TargetId = TargetIdAllocator.TakeNext(ids, ref nextPinId);
            pin.ScoreMultiplier = data.scoreMultiplier;
            pin.RushChanceAdd = data.rushChanceAdd;
        }

        private PhysicMaterial GetCollisionPinMaterial()
        {
            if (collisionPinPhysicsMaterial) return collisionPinPhysicsMaterial;
            collisionPinPhysicsMaterial = pinPhysicsMaterial ? Object.Instantiate(pinPhysicsMaterial) : new PhysicMaterial();
            collisionPinPhysicsMaterial.name = "Collision Pin";
            collisionPinPhysicsMaterial.bounciness = CollisionPinBounce;
            collisionPinPhysicsMaterial.bounceCombine = PhysicMaterialCombine.Maximum;
            return collisionPinPhysicsMaterial;
        }

        internal float GetCollisionPinBounce(Collider collider) =>
            collisionPinPhysicsMaterial && collider.sharedMaterial == collisionPinPhysicsMaterial
                ? collisionPinPhysicsMaterial.bounciness : 0f;

        private void AddLayoutZone(Transform parent, BoardLayoutProjection projection, BoardLayoutZone data, int targetId)
        {
            var node = new GameObject(data.type + " Zone " + parent.childCount);
            node.transform.SetParent(parent, false);
            node.transform.localPosition = projection.ToSurfaceLocal(data.x, data.y, 0);
            var trigger = node.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = new Vector3(data.depth, data.triggerHeight, data.width);
            trigger.center = Vector3.up * (data.triggerHeight * .5f);
            var zone = node.AddComponent<MarbleZone>();
            zone.Kind = data.Kind;
            zone.TargetId = targetId;
            zone.Priority = data.priority;
            zone.Multiplier = data.multiplier;
            zone.FlatScore = data.flatScore;
            zone.RushEnabled = data.rushEnabled;
            zone.ScoreStackEnabled = data.scoreStackEnabled;

            if (!createVisuals) return;
            var marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            marker.name = "Zone Marker";
            marker.transform.SetParent(node.transform, false);
            marker.transform.localPosition = Vector3.up * .015f;
            marker.transform.localScale = new Vector3(data.depth, .02f, data.width);
            var markerCollider = marker.GetComponent<Collider>();
            markerCollider.enabled = false;
            DestroyLayoutObject(markerCollider);
            var renderer = marker.GetComponent<Renderer>();
            if (zoneVisualMaterial) renderer.sharedMaterial = zoneVisualMaterial;
            Color color = data.Kind == MarbleContactKind.Score ? new Color(.17f, .74f, .43f) : new Color(.62f, .31f, .91f);
            var properties = new MaterialPropertyBlock();
            properties.SetColor(ColorProperty, color);
            properties.SetColor(BaseColorProperty, color);
            renderer.SetPropertyBlock(properties);
        }

        private void RemoveBoardLayout()
        {
            DestroyGeneratedBoard();
            foreach (var node in hiddenBoardObjects)
                if (node) node.SetActive(true);
            hiddenBoardObjects.Clear();
            Physics.SyncTransforms();
        }

        private void DestroyGeneratedBoard()
        {
            if (!boardLayoutRoot) return;
            var node = boardLayoutRoot.gameObject;
            node.SetActive(false);
            foreach (var collider in node.GetComponentsInChildren<Collider>(true)) brokenPins.Remove(collider);
            DestroyLayoutObject(node);
            boardLayoutRoot = null;
        }

        private static void DestroyLayoutObject(Object target)
        {
            if (Application.isPlaying) Object.Destroy(target);
            else Object.DestroyImmediate(target);
        }
    }
}
