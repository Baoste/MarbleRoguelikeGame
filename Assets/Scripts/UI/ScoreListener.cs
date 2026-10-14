using MarblesECS;
using MarblesECS.PhysX;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ScoreListener : MonoBehaviour
{
    public MarbleGameController Controller;
    public TMP_Text RoundScoreText;
    public TMP_Text TotalScoreText;
    public TMP_Text PendingScoreText;
    public TMP_Text TargetScoreText;
    public Slider BloodSlider;
    public TMP_Text RushText;
    public Slider RushSlider;

    private void OnEnable()
    {
        if (Controller != null)
        {
            Controller.Scored += OnScored;
            Controller.BloodChanged += OnBloodChanged;
        }

        RefreshScoreText();
        RefreshBloodSlider();
        RefreshRushText();
    }

    private void OnDisable()
    {
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
        RefreshRushText();
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
        if (PendingScoreText != null)
            PendingScoreText.text = session.PendingScore.ToString();
        if (TargetScoreText != null)
            TargetScoreText.text = session.TargetScore.ToString();
    }

    private void RefreshBloodSlider()
    {
        if (Controller == null || !Controller.IsReady)
            return;

        OnBloodChanged(Controller.Snapshot.Blood);
    }

    private void RefreshRushText()
    {
        bool ready = Controller != null && Controller.IsReady;
        var snapshot = ready ? Controller.Snapshot : default;
        bool rushActive = ready && snapshot.RushSecondsRemaining > 0;

        if (RushText != null)
        {
            RushText.enabled = ready;
            if (ready) RushText.text = rushActive ? $"RUSH {snapshot.RushSecondsRemaining:0.0}s · 锁血"
                : $"RUSH {snapshot.RushMeter:0.#} / {snapshot.RushMaximum:0}";
        }

        if (RushSlider != null)
        {
            if (RushSlider.gameObject.activeSelf != ready)
                RushSlider.gameObject.SetActive(ready);
            if (ready)
            {
                RushSlider.minValue = 0f;
                RushSlider.maxValue = (float)snapshot.RushMaximum;
                RushSlider.value = rushActive ? RushSlider.maxValue : (float)snapshot.RushMeter;
            }
            else
            {
                RushSlider.value = 0f;
            }
        }
    }
}
