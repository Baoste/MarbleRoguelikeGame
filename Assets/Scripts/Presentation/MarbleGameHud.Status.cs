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
            var day = Controller.Day;
            string dayLabel = day.Status == DayCampaignStatus.NotStarted ? string.Empty : day.DayId + "　";
            string wallet = Controller.CurrentDayDefinition?.DailyLoadout != null && day.Phase != DayPhase.Production
                ? "　公司工票 " + session.CompanyScripBalance
                : "　血肉残料 " + session.BiomassScrapBalance;
            GUILayout.Label(dayLabel + PhaseName(session.Phase) + wallet);
            GUILayout.Label("血量 Blood  " + marble.Blood.ToString("0.0") + " / " + marble.BloodCapacity.ToString("0") +
                "　场内 " + marble.ActiveMarbles);
            GUILayout.Label("累计已结算 " + session.TotalScore);
            GUILayout.Label("当前确认分 " + session.ConfirmedScore + " / " + session.TargetScore + "（奖池未开奖不计入结算门槛）");
            GUILayout.Label("Rush " + marble.RushSecondsRemaining.ToString("0.0") + "s");
            GUILayout.Label("下一颗碰撞系数 e = " + marble.NextMarbleRestitution.ToString("0.00"));
            GUILayout.Space(6);
        }

        private void DrawResult()
        {
            bool won = Controller.Snapshot.Phase == RoundPhase.Won;
            GUILayout.Label(won ? "已达到目标！" : "挑战结束，未达到目标。");
            DrawSettlement();
        }

        private void DrawSettlement()
        {
            var session = Controller.Session;
            GUILayout.Label("本轮结算 " + session.RoundScore + "　累计 " + session.TotalScore);
            if (session.LastCompanyScripReward > 0)
                GUILayout.Label("通关奖励 +" + session.LastCompanyScripReward + " 公司工票");
        }

        private static string PhaseName(RoundPhase phase)
        {
            switch (phase)
            {
                case RoundPhase.Build: return "布置";
                case RoundPhase.Playing: return "发射中";
                case RoundPhase.Draining: return "结算中";
                case RoundPhase.Won: return "完成";
                default: return "结束";
            }
        }

        private void DrawHelp()
        {
            GUILayout.Label("操作：空格连发；A / D 或滑条移动发射口。提高抽血档位会提高单珠耗血与大小，基础分独立配置。");
            GUILayout.Label("金钉：该珠倍率 ×2；蓝钉：该珠入区 Rush 概率增加。同一颗血珠对同一枚钉子只生效一次。");
            GUILayout.Label("绿区即时得分；紫区积累待定分，回合排空后共同开奖一次；红区无分回收。达成当前目标才可结算。");
        }
    }
}
