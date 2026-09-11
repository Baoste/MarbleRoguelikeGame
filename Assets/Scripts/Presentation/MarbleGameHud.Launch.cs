using UnityEngine;

namespace MarblesECS.Presentation
{
    public sealed partial class MarbleGameHud
    {
        private void DrawLaunch()
        {
            var quote = MarbleRules.Quote(Controller.Balance.Marble, flow);
            GUILayout.Label("抽血档位 " + Mathf.RoundToInt(flow * 100) + "%　每珠 " + quote.Cost.ToString("0.0") + " 血 / " +
                quote.BaseValue.ToString("0.0") + " 基础分");
            float next = GUILayout.HorizontalSlider(flow, 0, 1);
            if (!Mathf.Approximately(next, flow)) { flow = next; Controller.SetFlow(flow); }
            bool autoMove = GUILayout.Toggle(Controller.AutoMoveLauncher, "发射口自动往返");
            Controller.AutoMoveLauncher = autoMove;
            GUILayout.Label("发射口横移 / A D");
            GUI.enabled = !autoMove;
            float x = Controller.LauncherLocalPosition.x;
            float half = Controller.LauncherMoveHalfWidth;
            float aim = GUILayout.HorizontalSlider(x, Controller.Balance.Board.LauncherX - half,
                Controller.Balance.Board.LauncherX + half);
            if (!Mathf.Approximately(aim, x)) Controller.SetLauncherPosition(aim);
            GUI.enabled = !Controller.Paused;
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("发射一颗 Fire")) Controller.FireOnce();
            if (GUILayout.Button(autoFire ? "停止连发" : "持续发射"))
            { autoFire = !autoFire; Controller.SetFireHeld(autoFire); }
            GUILayout.EndHorizontal();
            GUI.enabled = !Controller.Paused && Controller.Session.CanCashOut;
            if (GUILayout.Button("提现并结束回合 Cash out"))
            { autoFire = false; Controller.SetFireHeld(false); Controller.CashOut(); }
            GUI.enabled = true;
            GUILayout.Label(Controller.Session.CanCashOut ? "目标已达成；提现把剩余血量换成金币，仍会等待场内球排空。" : "继续得分达到当前累计目标后，可提前提现。");
            GUILayout.Label(lastScore);
            DrawDrugs(true);
        }

        private void DrawDrugs(bool usable)
        {
            GUILayout.Space(5);
            GUILayout.Label("药物库存（只影响药效期间新发射的球）");
            var drugs = Controller.GetDrugInventory();
            if (drugs.Length == 0) GUILayout.Label("暂无药物。通关后可在商店购买。");
            foreach (var drug in drugs)
            {
                GUILayout.Label(drug.Name + " ×" + drug.Count + "　碰撞系数 ×" + drug.RestitutionMultiplier.ToString("0.##") +
                    " / " + drug.DurationSeconds.ToString("0") + "s");
                if (!usable) continue;
                GUI.enabled = !Controller.Paused && drug.Count > 0;
                if (GUILayout.Button("使用 " + drug.Name))
                    feedback = Controller.UseDrug(drug.DefinitionId) ? "药物已使用；多份效果可叠加，系数封顶为 1。" : "当前不能使用（库存不足或叠加已满）。";
                GUI.enabled = true;
            }
        }
    }
}
