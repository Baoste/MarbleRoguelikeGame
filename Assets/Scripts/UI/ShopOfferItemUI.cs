using System;
using MarblesECS;
using MarblesECS.Presentation;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class ShopOfferItemUI : MonoBehaviour
{
    public TMP_Text NameText;
    public TMP_Text DescriptionText;
    public TMP_Text KindText;
    public TMP_Text MetadataText;
    public TMP_Text PriceText;
    public TMP_Text SoldText;
    public Button BuyButton;
    public TMP_Text BuyLabel;
    public ScrollRect DescriptionScroll;

    private int offerIndex;
    private Action<int> buyRequested;
    private ShopOfferSnapshot displayedOffer;
    private bool hasOffer;
    private bool wasAffordable;
    private bool descriptionDirty;
    private float descriptionWidth = -1f;
    private LayoutElement descriptionLayout;

    private void OnEnable()
    {
        if (BuyButton != null)
            BuyButton.onClick.AddListener(HandleBuyClicked);
    }

    private void OnDisable()
    {
        if (BuyButton != null)
            BuyButton.onClick.RemoveListener(HandleBuyClicked);
    }

    private void LateUpdate()
    {
        RefreshDescriptionLayout();
    }

    private void RefreshDescriptionLayout()
    {
        if (DescriptionText == null || DescriptionScroll == null || DescriptionScroll.content == null) return;
        float width = DescriptionText.rectTransform.rect.width;
        if (width <= 0 || (!descriptionDirty && Mathf.Abs(width - descriptionWidth) < .01f)) return;
        descriptionDirty = false;
        descriptionWidth = width;
        if (descriptionLayout == null)
            descriptionLayout = DescriptionText.GetComponent<LayoutElement>();
        if (descriptionLayout == null) return;

        // TMP 3 can underestimate CJK wrapping height. Include the actual rendered last line
        // so a ScrollRect can always reach the whole description after a resize.
        DescriptionText.ForceMeshUpdate();
        descriptionLayout.preferredHeight = Mathf.Ceil(Mathf.Max(DescriptionText.preferredHeight,
            -DescriptionText.textBounds.min.y)) + 8f;
        LayoutRebuilder.ForceRebuildLayoutImmediate(DescriptionScroll.content);
    }

    public void Bind(ShopOfferSnapshot offer, long availableCoins, Action<int> onBuyRequested)
    {
        offerIndex = offer.Index;
        buyRequested = onBuyRequested;
        bool affordable = availableCoins >= offer.Price;
        bool contentChanged = !hasOffer || displayedOffer.DefinitionId != offer.DefinitionId ||
            displayedOffer.Name != offer.Name || displayedOffer.Description != offer.Description ||
            displayedOffer.Rarity != offer.Rarity || displayedOffer.Durability != offer.Durability ||
            displayedOffer.SellPrice != offer.SellPrice;

        if (contentChanged)
        {
            if (NameText != null) NameText.text = offer.Name ?? string.Empty;
            if (DescriptionText != null) DescriptionText.text = offer.Description ?? string.Empty;
            if (KindText != null)
            {
                KindText.text = DeviceDisplayText.Rarity(offer.Rarity) + " · 装置";
                KindText.color = RarityColor(offer.Rarity);
            }
            if (MetadataText != null)
                MetadataText.text = (offer.Durability == -1 ? "无限耐久" : "耐久 " + offer.Durability) +
                    "  ·  出售 " + offer.SellPrice + " 金币";
            if (DescriptionScroll != null)
            {
                descriptionDirty = true;
                RefreshDescriptionLayout();
                DescriptionScroll.StopMovement();
                DescriptionScroll.verticalNormalizedPosition = 1f;
            }
        }

        if (!hasOffer || displayedOffer.Price != offer.Price)
            if (PriceText != null) PriceText.text = offer.Price + " 金币";
        if (!hasOffer || displayedOffer.Sold != offer.Sold || wasAffordable != affordable)
        {
            if (SoldText != null)
            {
                SoldText.text = "已加入背包";
                SoldText.enabled = offer.Sold;
            }
            if (BuyLabel != null)
                BuyLabel.text = offer.Sold ? "已售出" : affordable ? "购买装置" : "金币不足";
            if (BuyButton != null) BuyButton.interactable = !offer.Sold && affordable;
        }
        displayedOffer = offer;
        wasAffordable = affordable;
        hasOffer = true;
    }

    private static Color RarityColor(DeviceRarity rarity)
    {
        switch (rarity)
        {
            case DeviceRarity.Rare: return new Color32(119, 190, 221, 255);
            case DeviceRarity.Epic: return new Color32(199, 157, 229, 255);
            case DeviceRarity.Legendary: return new Color32(239, 193, 99, 255);
            default: return new Color32(181, 185, 166, 255);
        }
    }

    private void HandleBuyClicked()
    {
        buyRequested?.Invoke(offerIndex);
    }
}
