using System.Collections;
using MarblesECS;
using MarblesECS.PhysX;
using TMPro;
using UnityEngine;

public sealed class RandomScoreResultUI : MonoBehaviour
{
    public MarbleGameController Controller;
    public TMP_Text ResultText;
    [Min(0.1f)] public float RevealDuration = 3f;
    [Min(0.02f)] public float FlashInterval = 0.15f;
    public string DrawingText = "Drawing...";
    public string WinText = "WIN! +{0}";
    public string LoseText = "NO WIN";

    private Coroutine animationRoutine;

    private void Awake()
    {
        if (ResultText != null)
            ResultText.enabled = false;
    }

    private void OnEnable()
    {
        if (Controller != null)
            Controller.RandomScoreSettled += Play;
    }

    private void OnDisable()
    {
        if (Controller != null)
            Controller.RandomScoreSettled -= Play;

        if (animationRoutine != null)
        {
            StopCoroutine(animationRoutine);
            animationRoutine = null;
        }
    }

    private void Play(RandomScoreResult result)
    {
        if (ResultText == null) return;
        if (animationRoutine != null)
            StopCoroutine(animationRoutine);
        animationRoutine = StartCoroutine(PlayRoutine(result));
    }

    private IEnumerator PlayRoutine(RandomScoreResult result)
    {
        ResultText.text = DrawingText;
        float elapsed = 0f;
        bool visible = true;
        float duration = Controller != null ? Controller.RandomScoreRevealDelay : RevealDuration;

        while (elapsed < duration)
        {
            visible = !visible;
            ResultText.enabled = visible;
            yield return new WaitForSecondsRealtime(FlashInterval);
            elapsed += FlashInterval;
        }

        ResultText.enabled = true;
        ResultText.text = result.Multiplier > 0 ? (result.Multiplier == 1 ? "返还 ×1" : "中奖 ×" + result.Multiplier) + " +" + result.Payout : result.Won
            ? string.Format(WinText, result.Payout)
            : LoseText;
        animationRoutine = null;
    }
}
