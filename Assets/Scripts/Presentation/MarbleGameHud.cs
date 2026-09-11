using MarblesECS.PhysX;
using UnityEngine;

namespace MarblesECS.Presentation
{
    /// <summary>Read-only snapshots and command buttons; no gameplay rules live in the HUD.</summary>
    public sealed partial class MarbleGameHud : MonoBehaviour
    {
        public MarbleGameController Controller;
        public MarbleBoardView Board;
        public static float UiScale => Mathf.Max(.45f, Mathf.Min(Screen.width / 1150f, Screen.height / 850f));
        public static float PanelPixels => 376 * UiScale;
        public static bool ContainsPointer(Vector2 point) => point.x < PanelPixels;
        private GUISkin skin;
        private Font font;
        private Vector2 scroll;
        private float flow = .25f;
        private bool initialized;
        private bool autoFire;
        private string feedback = "先布置赠送的钉子，然后开始回合。";
        private string lastScore = "绿色：即时得分　紫色：待定分　红色：落空";
        private RoundPhase lastPhase;

        private void Update()
        {
            if (Controller == null || !Controller.IsReady) return;
            if (!initialized)
            {
                Controller.Scored += OnScored;
                Controller.SetFlow(flow);
                initialized = true;
                lastPhase = Controller.Snapshot.Phase;
            }
            if (lastPhase != Controller.Snapshot.Phase)
            {
                lastPhase = Controller.Snapshot.Phase;
                autoFire = false;
                if (lastPhase != RoundPhase.Playing) Controller.SetFireHeld(false);
                scroll = Vector2.zero;
                feedback = "";
            }
        }

        private void OnScored(MarbleScoreEvent score)
        { lastScore = (score.IsPending ? "最近待定：+" : "最近得分：+") + score.Score + "　区域 " + score.ZoneId; }

        private void OnGUI()
        {
            CaptureKeyboard();
            EnsureSkin();
            GUISkin oldSkin = GUI.skin;
            Matrix4x4 oldMatrix = GUI.matrix;
            GUI.skin = skin;
            GUI.matrix = Matrix4x4.Scale(Vector3.one * UiScale);
            GUILayout.BeginArea(new Rect(12, 12, 352, Screen.height / UiScale - 24), GUI.skin.box);
            scroll = GUILayout.BeginScrollView(scroll, false, false);
            GUILayout.Label("血珠回路 / BLOOD MARBLE", GUI.skin.GetStyle("Title"));
            GUILayout.Label("布置钉子 · 抽血发射 · 达标提现");
            if (Controller == null || !Controller.IsReady)
                GUILayout.Label("正在初始化……若长时间停留，请检查 Console。\nInitializing: check the Console if this persists.");
            else DrawReady();
            GUILayout.EndScrollView();
            GUILayout.EndArea();
            GUI.matrix = oldMatrix;
            GUI.skin = oldSkin;
        }

        private void DrawReady()
        {
            DrawStatus();
            RoundPhase phase = Controller.Snapshot.Phase;
            if (phase == RoundPhase.Playing) DrawLaunch();
            else if (phase == RoundPhase.Draining)
                GUILayout.Label("结算中：停止发射，等待场内血珠排空后统一开奖。\n剩余 " + Controller.Snapshot.ActiveMarbles + " 颗");
            else if (phase == RoundPhase.Shop) DrawShop();
            else if (phase == RoundPhase.Build) DrawBuild();
            else DrawResult();
            if (!string.IsNullOrEmpty(feedback)) GUILayout.Label(feedback);
            GUILayout.Space(8);
            DrawHelp();
            if (phase == RoundPhase.Playing || phase == RoundPhase.Draining)
            {
                if (GUILayout.Button(Controller.Paused ? "继续 Resume" : "暂停 Pause"))
                { autoFire = false; Controller.SetFireHeld(false); Controller.SetPaused(!Controller.Paused); }
            }
            if (Controller.RequiresRestart) GUILayout.Label("模拟异常，请重开并检查 Console。");
            if (GUILayout.Button("重新开始整局 Restart"))
            {
                autoFire = false;
                Controller.SetFireHeld(false);
                Controller.RestartRound();
                Controller.SetFlow(flow);
                feedback = "已重开，金币、库存、布局和累计分数重置。";
            }
        }

        private void EnsureSkin()
        {
            if (skin != null) return;
            skin = Instantiate(GUI.skin);
            font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "Noto Sans CJK SC", "Arial" }, 16);
            skin.font = font;
            skin.label.fontSize = skin.button.fontSize = skin.toggle.fontSize = 15;
            skin.label.wordWrap = true;
            skin.label.padding = new RectOffset(3, 3, 4, 4);
            skin.button.padding = new RectOffset(7, 7, 9, 9);
            skin.box.padding = new RectOffset(12, 12, 12, 12);
            skin.customStyles = new[] { new GUIStyle(skin.label) { name = "Title", fontSize = 22, fontStyle = FontStyle.Bold } };
        }

        private void CaptureKeyboard()
        {
            if (Controller == null || !Controller.IsReady) return;
            Event input = Event.current;
            if (input.keyCode == KeyCode.Space && (input.type == EventType.KeyDown || input.type == EventType.KeyUp))
            { Controller.SetFireHeld(autoFire || input.type == EventType.KeyDown); input.Use(); }
        }

        private void OnApplicationFocus(bool focused)
        { if (!focused) { autoFire = false; if (Controller != null) Controller.SetFireHeld(false); } }

        private void OnDisable()
        { if (Controller != null) Controller.SetFireHeld(false); autoFire = false; }

        private void OnDestroy()
        {
            if (Controller != null && initialized) Controller.Scored -= OnScored;
            if (skin != null) Destroy(skin);
            if (font != null) Destroy(font);
        }
    }
}
