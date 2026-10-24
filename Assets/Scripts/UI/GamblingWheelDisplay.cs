using MarblesECS;
using MarblesECS.PhysX;
using TMPro;
using UnityEngine;

/// <summary>A continuously rotating model; presentation never selects a result or delays settlement.</summary>
[DisallowMultipleComponent]
public sealed class GamblingWheelDisplay : MonoBehaviour
{
    public MarbleGameController Controller;
    public CanvasGroup DisplayGroup;
    public Transform Rotor;
    public Camera ModelCamera;
    public UnityEngine.UI.RawImage ModelImage;
    public TMP_Text PoolText, TimerText, ResultText;
    public UnityEngine.UI.Slider TimerBar;
    public Renderer[] Sectors;
    [Min(1)] public float DegreesPerSecond = 90;
    private RenderTexture texture;
    private MaterialPropertyBlock properties;
    private long shownDraw = -1;
    private int shownRound = -1;

    private void OnEnable()
    {
        if (ModelCamera && ModelImage)
        {
            texture = new RenderTexture(512, 512, 24, RenderTextureFormat.ARGB32)
            { name = "GamblingWheelPreview", antiAliasing = 4 };
            texture.Create();
            ModelCamera.targetTexture = texture;
            ModelCamera.enabled = true;
            ModelImage.texture = texture;
        }
        properties = new MaterialPropertyBlock();
        shownDraw = -1; shownRound = -1;
        Refresh();
    }

    private void Update()
    {
        Refresh();
        if (Rotor && Controller && Controller.IsReady && !Controller.Paused &&
            (Controller.Snapshot.Phase == RoundPhase.Playing || Controller.Snapshot.Phase == RoundPhase.Draining))
            Rotor.Rotate(Vector3.forward, -DegreesPerSecond * Time.deltaTime, Space.Self);
    }

    private void Refresh()
    {
        bool ready = Controller && Controller.IsReady;
        var phase = ready ? Controller.Snapshot.Phase : RoundPhase.Lost;
        bool visible = ready && (phase == RoundPhase.Playing || phase == RoundPhase.Draining);
        if (DisplayGroup)
        {
            DisplayGroup.alpha = visible ? 1 : 0;
            DisplayGroup.interactable = DisplayGroup.blocksRaycasts = false;
        }
        if (ModelCamera) ModelCamera.enabled = visible && texture;
        if (!ready) return;
        var wheel = Controller.GamblingWheel;
        if (PoolText) PoolText.text = $"奖池  {wheel.PoolValue:0.##}  ·  {wheel.BallCount} 球";
        if (TimerText) TimerText.text = wheel.BallCount > 0 ? $"{wheel.SecondsRemaining:0.00}s 后开奖" : "等待进球";
        if (TimerBar) TimerBar.SetValueWithoutNotify(wheel.WindowSeconds > 0 ?
            Mathf.Clamp01((float)(wheel.SecondsRemaining / wheel.WindowSeconds)) : 0);
        int round = Controller.Snapshot.RoundId;
        if (shownRound == round && shownDraw == wheel.DrawCount) return;
        shownRound = round; shownDraw = wheel.DrawCount;
        if (ResultText) ResultText.text = wheel.DrawCount > 0 ?
            $"×{wheel.LastMultiplier}   {wheel.LastScoreChange:+0;-0;0} 分" : "−2 / −1 / 0 / 1 / 5 / 10 / 20 / 50";
        if (Sectors != null)
            for (int i = 0; i < Sectors.Length; i++)
            {
                if (!Sectors[i]) continue;
                bool selected = wheel.DrawCount > 0 && GamblingWheelRules.Multiplier(i) == wheel.LastMultiplier;
                Color color = selected ? new Color(1f, .78f, .27f) : SectorColor(i);
                properties.Clear(); properties.SetColor("_BaseColor", color); properties.SetColor("_Color", color);
                Sectors[i].SetPropertyBlock(properties);
            }
    }

    public static Color SectorColor(int index) => index < 2 ? new Color(.48f, .16f, .18f) :
        index == 2 ? new Color(.2f, .23f, .25f) : index % 2 == 0 ? new Color(.12f, .39f, .34f) : new Color(.18f, .29f, .29f);

    private void OnDisable()
    {
        if (DisplayGroup) DisplayGroup.alpha = 0;
        if (ModelCamera) { ModelCamera.enabled = false; ModelCamera.targetTexture = null; }
        if (ModelImage) ModelImage.texture = null;
        if (texture) { texture.Release(); Destroy(texture); texture = null; }
    }
}
