using UnityEngine;

namespace MarblesECS.Presentation
{
    public sealed partial class MarbleGameHud
    {
        private void DrawShop()
        {
            DrawSettlement();
            GUILayout.Space(5);
            GUILayout.Label("商店 SHOP · 每个货位本次刷新只能买一次");
            foreach (var offer in Controller.GetShopOffers())
            {
                string kind = offer.Kind == ShopItemKind.Device ? "钉子" : "药物";
                GUI.enabled = !offer.Sold && Controller.Session.Coins >= offer.Price;
                if (GUILayout.Button((offer.Sold ? "已售出 " : "购买 " + kind + " ") + offer.Name + "　" + offer.Price + " 金"))
                    feedback = Controller.BuyOffer(offer.Index) ? "购买成功，已加入库存。" : "购买失败：金币不足、货位已售出或库存已满。";
                GUI.enabled = true;
            }
            long cost = Controller.Balance.Campaign.ShopRefreshCost;
            GUI.enabled = Controller.Session.Coins >= cost;
            if (GUILayout.Button("刷新商品　" + cost + " 金币"))
                feedback = Controller.RefreshShop() ? "商品已刷新并扣除金币。" : "刷新失败，金币不足。";
            GUI.enabled = true;
            DrawOwnedDevices(false);
            DrawDrugs(false);
            if (GUILayout.Button("购买完成 → 布置装置")) Controller.EnterBuild();
        }

        private void DrawBuild()
        {
            GUILayout.Label("布置 BUILD · 钉子可在盘面内自由摆放");
            GUILayout.Label("先选下方库存，再点击盘面。点击已放钉子可选中，下一次点击空白位置即可移动。按住 Shift 可对齐网格，右键取消选择。");
            DrawOwnedDevices(true);
            if (Board != null) GUILayout.Label(Board.Feedback);
            DrawDrugs(false);
            if (GUILayout.Button("开始回合 → 补满血量"))
            {
                if (Controller.BeginRound())
                { Controller.SetFlow(flow); feedback = "按住空格，或点击发射按钮开始。"; }
                else feedback = "暂时不能开始回合。";
            }
        }

        private void DrawOwnedDevices(bool editable)
        {
            GUILayout.Space(5);
            GUILayout.Label("装置库存（布局跨回合保留）");
            var owned = Controller.GetOwnedDevices();
            if (owned.Length == 0) GUILayout.Label("暂无钉子。");
            foreach (var device in owned)
            {
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.Label(device.Name + " #" + device.InstanceId + (device.Placed ? " · 已布置" : " · 库存中"));
                GUILayout.Label(device.RushChanceAdd > 0 ? "入区 Rush 概率 +" + (device.RushChanceAdd * 100).ToString("0") + "%" :
                    "碰撞后本珠得分倍率 ×" + device.ScoreMultiplier.ToString("0.##"));
                GUILayout.BeginHorizontal();
                if (editable && Board != null && GUILayout.Button(Board.SelectedDeviceId == device.InstanceId ? "已选中" : "选择 / 移动"))
                    Board.Select(device.InstanceId);
                if (editable && device.Placed && GUILayout.Button("收回"))
                    feedback = Controller.RemoveDevicePlacement(device.InstanceId) ? "已收回库存。" : "收回失败。";
                if (GUILayout.Button("出售 " + device.SellPrice + " 金"))
                    feedback = Controller.SellDevice(device.InstanceId) ? "已出售并获得金币。" : "当前不能出售。";
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
            }
        }
    }
}
