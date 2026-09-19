using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace MarblesECS.PhysX
{
    [DisallowMultipleComponent]
    public sealed class WorldTextEffectPool : MonoBehaviour
    {
        private sealed class ActiveText
        {
            public TextMeshPro Text;
            public Vector3 StartPosition;
            public Color StartColor;
            public float Age;
        }

        public static WorldTextEffectPool Instance { get; private set; }

        public TextMeshPro TextPrefab;
        public Camera ViewCamera;
        [Min(0)] public int PrewarmCount = 16;
        [Min(1)] public int MaxPoolSize = 128;
        [Min(0.05f)] public float Duration = 0.8f;
        [Min(0f)] public float RiseDistance = 0.6f;
        public bool UseUnscaledTime;

        private readonly Queue<TextMeshPro> available = new Queue<TextMeshPro>();
        private readonly List<ActiveText> active = new List<ActiveText>();
        private int createdCount;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogError("Only one WorldTextEffectPool may be active.", this);
                enabled = false;
                return;
            }

            Instance = this;
            if (ViewCamera == null)
                ViewCamera = Camera.main;
            if (TextPrefab == null)
            {
                Debug.LogWarning("WorldTextEffectPool requires a 3D TextMeshPro prefab.", this);
                return;
            }
            int count = Mathf.Min(PrewarmCount, MaxPoolSize);
            for (int i = 0; i < count; i++)
                available.Enqueue(CreateText());
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        public static bool TryShow(string value, Vector3 worldPosition)
        {
            return Instance != null && Instance.Show(value, worldPosition);
        }

        public bool Show(string value, Vector3 worldPosition)
        {
            if (!enabled || TextPrefab == null || string.IsNullOrEmpty(value))
                return false;

            TextMeshPro text;
            if (available.Count > 0)
                text = available.Dequeue();
            else if (createdCount < MaxPoolSize)
                text = CreateText();
            else
                return false;

            text.text = value;
            text.transform.position = worldPosition;
            text.color = TextPrefab.color;
            text.gameObject.SetActive(true);
            FaceCamera(text.transform);
            active.Add(new ActiveText
            {
                Text = text,
                StartPosition = worldPosition,
                StartColor = text.color
            });
            return true;
        }

        private void LateUpdate()
        {
            if (ViewCamera == null)
                ViewCamera = Camera.main;
            float deltaTime = UseUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;

            for (int i = active.Count - 1; i >= 0; i--)
            {
                ActiveText item = active[i];
                item.Age += deltaTime;
                float t = Mathf.Clamp01(item.Age / Duration);
                item.Text.transform.position = item.StartPosition + Vector3.up * (RiseDistance * t);
                Color color = item.StartColor;
                color.a *= 1f - t;
                item.Text.color = color;
                FaceCamera(item.Text.transform);

                if (item.Age >= Duration)
                {
                    item.Text.gameObject.SetActive(false);
                    available.Enqueue(item.Text);
                    active.RemoveAt(i);
                }
            }
        }

        private TextMeshPro CreateText()
        {
            TextMeshPro text = Instantiate(TextPrefab, transform);
            text.gameObject.SetActive(false);
            createdCount++;
            return text;
        }

        private void FaceCamera(Transform target)
        {
            if (ViewCamera == null) return;
            Vector3 direction = target.position - ViewCamera.transform.position;
            if (direction.sqrMagnitude > 0.000001f)
                target.rotation = Quaternion.LookRotation(direction, ViewCamera.transform.up);
        }
    }
}
