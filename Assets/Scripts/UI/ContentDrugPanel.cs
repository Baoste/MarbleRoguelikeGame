using MarblesECS;
using MarblesECS.PhysX;
using UnityEngine;

// Provides a usable drug inventory in existing scenes that do not use MarbleGameHud.
public sealed class ContentDrugPanel : MonoBehaviour
{
    public MarbleGameController Controller;
    private bool open;
    private Vector2 scroll;
    private void OnGUI()
    {
        if (Controller == null || !Controller.IsReady) return;
        float width = Mathf.Min(330, Screen.width * .4f);
        if (GUI.Button(new Rect(Screen.width - width - 12, 12, width, 32), open ? "收起药物" : "药物库存")) open = !open;
        if (!open) return;
        GUILayout.BeginArea(new Rect(Screen.width - width - 12, 48, width, Mathf.Min(480, Screen.height - 60)), GUI.skin.box);
        scroll = GUILayout.BeginScrollView(scroll);
        foreach (var drug in Controller.GetDrugInventory())
        {
            GUILayout.Label(drug.Name + " ×" + drug.Count + " · " + drug.DurationSeconds.ToString("0") + "秒");
            GUILayout.Label(drug.Description);
            if (drug.ActiveDoses > 0) GUILayout.Label("生效 " + drug.ActiveDoses + " 层 · 剩余 " + drug.SecondsRemaining.ToString("0.0") + "秒");
            GUI.enabled = !Controller.Paused && Controller.Session.Phase == RoundPhase.Playing && drug.Count > 0;
            if (GUILayout.Button("使用 " + drug.Name)) Controller.UseDrug(drug.DefinitionId);
            GUI.enabled = true; GUILayout.Space(8);
        }
        GUILayout.EndScrollView(); GUILayout.EndArea();
    }
}
