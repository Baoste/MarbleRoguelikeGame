using MarblesECS;
using MarblesECS.PhysX;
using TMPro;
using UnityEngine;

public class ScoreListener : MonoBehaviour
{
    public MarbleGameController Controller;
    public TMP_Text RoundScoreText;
    public TMP_Text TotalScoreText;
    public TMP_Text TargetScoreText;
    public UnityEngine.UI.Slider BloodSlider;
    [Header("右上角状态进度条")]
    public CanvasGroup StatusHud;
    public TMP_Text RushText;
    public UnityEngine.UI.Slider RushSlider;
    public TMP_Text ScoreStackText;
    public UnityEngine.UI.Slider ScoreStackSlider;

    private void OnEnable()
    {
        if (Controller != null)
        {
            Controller.Scored += OnScored;
            Controller.BloodChanged += OnBloodChanged;
        }

        RefreshScoreText();
        RefreshBloodSlider();
        RefreshStatusBars();
    }

    private void OnDisable()
    {
        if (StatusHud != null) StatusHud.alpha = 0;
        if (Controller != null)
        {
            Controller.Scored -= OnScored;
            Controller.BloodChanged -= OnBloodChanged;
        }
    }

    private void OnScored(MarbleScoreEvent result)
    {
        //Debug.Log("本次得分：" + result.Score);
        //Debug.Log("当前回合总分：" + result.RoundScore);
        //Debug.Log("得分区 ID：" + result.ZoneId);
        RefreshScoreText();
    }

    private void OnBloodChanged(float blood)
    {
        if (BloodSlider == null)
            return;

        if (Controller != null && Controller.IsReady)
        {
            float capacity = Controller.EnableCampaign
                ? Controller.Balance.Marble.BloodCapacity
                : Mathf.Max(Controller.Tuning.BloodCapacity, Controller.StartingBlood);
            BloodSlider.maxValue = capacity;
        }

        BloodSlider.value = blood;
    }

    private void Update()
    {
        RefreshScoreText();
        RefreshStatusBars();
    }

    private void RefreshScoreText()
    {
        if (Controller == null || !Controller.IsReady)
            return;

        SessionSnapshot session = Controller.Session;
        if (RoundScoreText != null)
            RoundScoreText.text = session.RoundScore.ToString();
        if (TotalScoreText != null)
            TotalScoreText.text = session.TotalScore.ToString();
        if (TargetScoreText != null)
            TargetScoreText.text = session.TargetScore.ToString();
    }

    private void RefreshBloodSlider()
    {
        if (Controller == null || !Controller.IsReady)
            return;

        OnBloodChanged(Controller.Snapshot.Blood);
    }

    private void RefreshStatusBars()
    {
        bool ready = Controller != null && Controller.IsReady;
        var snapshot = ready ? Controller.Snapshot : default;
        bool playing = ready && (snapshot.Phase == RoundPhase.Playing || snapshot.Phase == RoundPhase.Draining);
        bool rushActive = playing && snapshot.RushSecondsRemaining > 0;
        bool rushCooling = playing && snapshot.RushCooldownSecondsRemaining > 0;
        bool stackActive = playing && snapshot.ScoreStackSecondsRemaining > 0;

        if (StatusHud != null)
        {
            StatusHud.alpha = playing ? 1 : 0;
            StatusHud.interactable = StatusHud.blocksRaycasts = false;
        }

        if (RushText != null)
        {
            RushText.enabled = playing;
            RushText.text = rushActive ? $"RUSH  {snapshot.RushSecondsRemaining:0.0}s · 锁血"
                : rushCooling ? $"RUSH 冷却  {snapshot.RushCooldownSecondsRemaining:0.0}s"
                : $"RUSH  {(playing ? snapshot.RushMeter : 0):0.#} / {snapshot.RushMaximum:0}";
        }

        SetProgress(RushSlider, playing, rushActive
            ? Fraction(snapshot.RushSecondsRemaining, snapshot.RushDurationSeconds)
            : rushCooling ? 0 : Fraction(snapshot.RushMeter, snapshot.RushMaximum));
        if (ScoreStackText != null)
        {
            ScoreStackText.enabled = playing;
            ScoreStackText.text = stackActive ? $"得分叠加 +{snapshot.ScoreStackBonus:0.00}  ·  {snapshot.ScoreStackSecondsRemaining:0.00}s"
                : "得分叠加 +0.00  ·  等待进球";
        }
        SetProgress(ScoreStackSlider, playing, stackActive
            ? Fraction(snapshot.ScoreStackSecondsRemaining, snapshot.ScoreStackWindowSeconds) : 0);
    }

    private static float Fraction(double value, double total) => total > 0 ? Mathf.Clamp01((float)(value / total)) : 0;

    private static void SetProgress(UnityEngine.UI.Slider slider, bool visible, float progress)
    {
        if (slider == null) return;
        if (slider.gameObject.activeSelf != visible) slider.gameObject.SetActive(visible);
        slider.minValue = 0;
        slider.maxValue = 1;
        slider.wholeNumbers = false;
        slider.interactable = false;
        slider.SetValueWithoutNotify(visible ? progress : 0);
        if (slider.navigation.mode != UnityEngine.UI.Navigation.Mode.None)
        {
            var navigation = slider.navigation;
            navigation.mode = UnityEngine.UI.Navigation.Mode.None;
            slider.navigation = navigation;
        }
    }
}
