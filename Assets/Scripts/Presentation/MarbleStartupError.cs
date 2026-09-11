using UnityEngine;

namespace MarblesECS.Presentation
{
    public sealed class MarbleStartupError : MonoBehaviour
    {
        public string Message;
        private void OnGUI()
        {
            GUI.Box(new Rect(20, 20, Mathf.Max(300, Screen.width - 40), 160),
                "游戏初始化失败 / Initialization failed\n\n" + Message +
                "\n\n请检查 Console；修正配置后重新进入 Play Mode。\nCheck the Console and the balance JSON, then restart Play Mode.");
        }
    }
}
