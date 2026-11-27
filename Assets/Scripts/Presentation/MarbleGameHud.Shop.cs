using UnityEngine;

namespace MarblesECS.Presentation
{
    public sealed partial class MarbleGameHud
    {
        private void DrawBuild()
        {
            if (Controller.Session.Production.Supply == DeviceSupply.SelectedTypes)
                GUILayout.Label("从机台物品板选择装置，使用血肉残料安装。");
            else
            {
                GUILayout.Label("布置 BUILD · 钉子可在盘面内自由摆放");
                GUILayout.Label("先选下方库存，再点击盘面。点击已放钉子可选中，下一次点击空白位置即可移动。按住 Shift 可对齐网格，右键取消选择。");
                DrawOwnedDevices();
            }
            if (Board != null) GUILayout.Label(Board.Feedback);
            if (GUILayout.Button("开始回合 → 补满血量"))
            {
                if (Controller.BeginRound())
                { Controller.SetFlow(flow); feedback = "按住空格，或点击发射按钮开始。"; }
                else feedback = "暂时不能开始回合。";
            }
        }

        private void DrawOwnedDevices()
        {
            GUILayout.Space(5);
            GUILayout.Label("装置库存（布局跨回合保留）");
            var owned = Controller.GetOwnedDevices();
            if (owned.Length == 0) GUILayout.Label("暂无钉子。");
            foreach (var device in owned)
            {
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.Label(device.Name + " #" + device.InstanceId + (device.Placed ? " · 已布置" : " · 库存中"));
                GUILayout.Label(device.Description);
                GUILayout.Label(DeviceDisplayText.Details(device));
                GUILayout.BeginHorizontal();
                if (!device.RotationLocked && GUILayout.Button("旋转 45°")) Controller.RotateDevice(device.InstanceId, 45);
                if (Board != null && GUILayout.Button(Board.SelectedDeviceId == device.InstanceId ? "已选中" : "选择 / 移动"))
                    Board.Select(device.InstanceId);
                if (device.Placed && GUILayout.Button("收回"))
                    feedback = Controller.RemoveDevicePlacement(device.InstanceId) ? "已收回库存。" : "收回失败。";
                if (Controller.Session.Production.AllowDeviceSale &&
                    GUILayout.Button((device.PairId != 0 ? "出售整对 " : "出售 ") + device.SellPrice + " 血肉残料"))
                    feedback = Controller.SellDevice(device.InstanceId) ? "已出售。" : "当前不能出售。";
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
            }
        }
    }
}
