using System;
using MarblesECS;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class ShopOfferItemUI : MonoBehaviour
{
    public TMP_Text NameText;
    public TMP_Text KindText;
    public TMP_Text PriceText;
    public TMP_Text SoldText;
    public Button BuyButton;

    private int offerIndex;
    private Action<int> buyRequested;

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

    public void Bind(ShopOfferSnapshot offer, long availableCoins, Action<int> onBuyRequested)
    {
        offerIndex = offer.Index;
        buyRequested = onBuyRequested;

        if (NameText != null)
            NameText.text = offer.Name;
        if (KindText != null)
            KindText.text = offer.Kind == ShopItemKind.Device ? "DEVICE" : "DRUG";
        if (PriceText != null)
            PriceText.text = offer.Price.ToString();
        if (SoldText != null)
        {
            SoldText.text = "SOLD";
            SoldText.enabled = offer.Sold;
        }
        if (BuyButton != null)
            BuyButton.interactable = !offer.Sold && availableCoins >= offer.Price;
    }

    private void HandleBuyClicked()
    {
        buyRequested?.Invoke(offerIndex);
    }
}
