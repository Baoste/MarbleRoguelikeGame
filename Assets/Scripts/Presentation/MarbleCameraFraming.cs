using UnityEngine;

namespace MarblesECS.Presentation
{
    [RequireComponent(typeof(Camera))]
    public sealed class MarbleCameraFraming : MonoBehaviour
    {
        public float BoardWidth = 10;
        public float BoardLength = 16;
        private Camera view;

        private void LateUpdate()
        {
            if (view == null) view = GetComponent<Camera>();
            float left = Mathf.Clamp01(MarbleGameHud.PanelPixels / Mathf.Max(1, Screen.width));
            view.rect = new Rect(left, 0, 1 - left, 1);
            float aspect = Mathf.Max(.1f, Screen.width * (1 - left) / Mathf.Max(1, Screen.height));
            view.orthographicSize = Mathf.Max(BoardLength * .57f, BoardWidth * .6f / aspect);
        }
    }
}
