using MarblesECS;
using MarblesECS.PhysX;
using TMPro;
using UnityEngine;

public class ScoreListener : MonoBehaviour
{
    public MarbleGameController Controller;
    public TMP_Text RoundScoreText;
    public TMP_Text TotalScoreText;
    public TMP_Text PendingScoreText;

    private void OnEnable()
    {
        if (Controller != null)
            Controller.Scored += OnScored;

        RefreshScoreText();
    }

    private void OnDisable()
    {
        if (Controller != null)
            Controller.Scored -= OnScored;
    }

    private void OnScored(MarbleScoreEvent result)
    {
        //Debug.Log("本次得分：" + result.Score);
        //Debug.Log("当前回合总分：" + result.RoundScore);
        //Debug.Log("得分区 ID：" + result.ZoneId);
        RefreshScoreText();
    }

    private void Update()
    {
        RefreshScoreText();
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
    }
}
