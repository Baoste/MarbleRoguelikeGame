using UnityEngine;

namespace MarblesECS.Presentation
{
    public sealed partial class MarbleGameHud
    {
        private void DrawStatus()
        {
            var session = Controller.Session;
            var marble = Controller.Snapshot;
            GUILayout.Space(6);
            GUILayout.Label("阶段 " + Mathf.Min(session.StageIndex + 1, session.StageCount) + " / " + session.StageCount +
                "　" + PhaseName(session.Phase) + "　金币 " + session.Coins);
            GUILayout.Label("血量 Blood  " + marble.Blood.ToString("0.0") + " / " + Controller.Balance.Marble.BloodCapacity.ToString("0") +
                "　场内 " + marble.ActiveMarbles);
            GUILayout.Label("累计已结算 " + session.TotalScore + "　目标 " + session.TargetScore);
            GUILayout.Label("本回合得分 " + session.RoundScore + "　待定 " + session.PendingScore);
            GUILayout.Label("当前确认分 " + session.ConfirmedScore + " / " + session.TargetScore + "（待定分不计入提现门槛）");
            GUILayout.Label("Rush " + marble.RushSecondsRemaining.ToString("0.0") + "s　药效 " +
                marble.BounceDrugSecondsRemaining.ToString("0.0") + "s / " + marble.ActiveBounceDrugs + " 层");
            GUILayout.Label("下一颗碰撞系数 e = " + marble.NextMarbleRestitution.ToString("0.00"));
            GUILayout.Space(6);
        }

        private void DrawResult()
        {
            bool won = Controller.Snapshot.Phase == RoundPhase.Won;
            GUILayout.Label(won ? "全部阶段通过！" : "未达到当前累计目标，挑战结束。");
            DrawSettlement();
        }

        private void DrawSettlement()
        {
            var session = Controller.Session;
            GUILayout.Label("本轮结算 " + session.RoundScore + "　累计 " + session.TotalScore);
            if (session.GamblingResolved)
                GUILayout.Label(session.GamblingWon ? "待定分开奖：赢！ +" + session.GamblingPayout : "待定分开奖：未中奖，待定分清零。");
            GUILayout.Label("通关奖励 +" + session.LastRewardCoins + " 金币　提现 +" + session.LastCashoutCoins);
            if (session.LastSkippedStages > 0) GUILayout.Label("越级跳过 " + session.LastSkippedStages + " 个阶段，已计入奖励。");
        }

        private static string PhaseName(RoundPhase phase)
        {
            switch (phase)
            {
                case RoundPhase.Build: return "布置";
                case RoundPhase.Playing: return "发射中";
                case RoundPhase.Draining: return "结算中";
                case RoundPhase.Shop: return "商店";
                case RoundPhase.Won: return "完成";
                default: return "结束";
            }
        }

        private void DrawHelp()
        {
            GUILayout.Label("操作：空格连发；A / D 或滑条移动发射口。提高抽血档位会提高单珠耗血与大小，基础分独立配置。");
            GUILayout.Label("金钉：该珠倍率 ×2；蓝钉：该珠入区 Rush 概率增加。同一颗血珠对同一枚钉子只生效一次。");
            GUILayout.Label("绿区即时得分；紫区积累待定分，回合排空后共同开奖一次；红区无分回收。达成当前目标才可提现。");
        }
    }
}
