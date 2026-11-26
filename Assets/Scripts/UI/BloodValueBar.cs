using System.Collections.Generic;
using MarblesECS;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Presents the active Day's blood milestones on the existing world-space blood slider.</summary>
internal sealed class BloodValueBar
{
    private const float FlashSeconds = 1.2f;
    private static readonly Color Warm = new Color(.85f, .26f, .08f);
    private static readonly Color Highlight = new Color(1f, .85f, .42f);
    private static readonly Color FlashFill = new Color(.78f, .29f, .08f);
    private static readonly Color TextColor = new Color(.98f, .94f, .85f);
    private static readonly Color ReachedMarker = new Color(1f, .7f, .26f);
    private static readonly Color UpcomingMarker = new Color(.95f, .86f, .76f);

    internal Slider Slider { get; }
    private readonly RectTransform view;
    private readonly TMP_Text title;
    private readonly bool titleWasVisible;
    private readonly Graphic fill;
    private readonly Color normalFill;
    private readonly TMP_Text bloodText, valueText;
    private readonly List<Image> markers = new List<Image>();
    private BloodProgressionDefinition activeCurve;
    private int roundId, stage;
    private float flashEnd;

    internal BloodValueBar(Slider slider)
    {
        Slider = slider;
        var titleTransform = slider.transform.Find("BLOOD");
        title = titleTransform ? titleTransform.GetComponent<TMP_Text>() : null;
        titleWasVisible = title && title.gameObject.activeSelf;
        fill = slider.fillRect ? slider.fillRect.GetComponent<Graphic>() : null;
        normalFill = fill ? fill.color : Color.red;

        view = new GameObject("Blood Value Progression", typeof(RectTransform)).GetComponent<RectTransform>();
        view.SetParent(slider.transform, false);
        view.anchorMin = Vector2.zero;
        view.anchorMax = Vector2.one;
        view.offsetMin = new Vector2(10, 0);
        view.offsetMax = new Vector2(-10, 0);
        bloodText = CreateLabel("Blood Amount", .03f, .43f);
        valueText = CreateLabel("Next Marble Value", .51f, .97f);
    }

    internal void Refresh(MarbleSnapshot snapshot, BloodProgressionDefinition curve)
    {
        if (activeCurve != curve || roundId != snapshot.RoundId) flashEnd = 0;
        else if (snapshot.BloodValueStage > stage) flashEnd = Time.unscaledTime + FlashSeconds;
        activeCurve = curve;
        roundId = snapshot.RoundId;
        stage = snapshot.BloodValueStage;

        view.gameObject.SetActive(true);
        if (title) title.gameObject.SetActive(false);
        float flash = Mathf.Clamp01((flashEnd - Time.unscaledTime) / FlashSeconds);
        if (fill)
        {
            float progress = stage / (float)Mathf.Max(1, curve.Stages.Count - 1);
            fill.color = Color.Lerp(Color.Lerp(normalFill, Warm, progress), FlashFill, flash);
        }
        bloodText.text = $"血量 {snapshot.Blood:0.#} / {snapshot.BloodCapacity:0.#}";
        valueText.text = flash > 0 ? $"升值 · 下一颗 {snapshot.NextMarbleBaseValue:G4}"
            : $"下一颗基础价值 {snapshot.NextMarbleBaseValue:G4}";
        valueText.color = flash > 0 ? Highlight : TextColor;
        valueText.rectTransform.localScale = Vector3.one * (1 + .1f * flash);

        UpdateMarkers(snapshot, curve);
    }

    internal void Hide()
    {
        if (!view || !view.gameObject.activeSelf) return;
        view.gameObject.SetActive(false);
        if (title) title.gameObject.SetActive(titleWasVisible);
        if (fill) fill.color = normalFill;
        activeCurve = null;
    }

    internal void Dispose()
    {
        Hide();
        if (view) Object.Destroy(view.gameObject);
    }

    private void UpdateMarkers(MarbleSnapshot snapshot, BloodProgressionDefinition curve)
    {
        int count = curve.Stages.Count - 1;
        while (markers.Count < count)
        {
            var tick = new GameObject("Blood Value Milestone", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            tick.transform.SetParent(view, false);
            tick.rectTransform.sizeDelta = new Vector2(5, 15);
            tick.raycastTarget = false;
            markers.Add(tick);
        }

        // The total available so far includes refills, so future marks stay aligned with the live bar.
        double bloodSupply = snapshot.Blood + snapshot.BloodSpent;
        for (int i = 0; i < count; i++)
        {
            var marker = markers[i];
            marker.gameObject.SetActive(true);
            double remaining = bloodSupply - curve.Stages[i + 1].SpentBlood;
            float fraction = Mathf.Clamp01((float)(remaining / snapshot.BloodCapacity));
            var anchor = new Vector2(fraction, .13f);
            marker.rectTransform.anchorMin = marker.rectTransform.anchorMax = anchor;
            marker.rectTransform.anchoredPosition = Vector2.zero;
            marker.color = i + 1 <= stage ? ReachedMarker : UpcomingMarker;
        }
        for (int i = count; i < markers.Count; i++) markers[i].gameObject.SetActive(false);
    }

    private TMP_Text CreateLabel(string name, float left, float right)
    {
        var label = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
        label.rectTransform.SetParent(view, false);
        label.rectTransform.anchorMin = new Vector2(left, .27f);
        label.rectTransform.anchorMax = new Vector2(right, .95f);
        label.rectTransform.offsetMin = label.rectTransform.offsetMax = Vector2.zero;
        if (title) label.font = title.font;
        label.fontSize = 27;
        label.enableAutoSizing = true;
        label.fontSizeMin = 18;
        label.fontSizeMax = 27;
        label.enableWordWrapping = false;
        label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = false;
        label.color = TextColor;
        return label;
    }
}
