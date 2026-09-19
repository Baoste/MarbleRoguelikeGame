using System.Collections.Generic;
using MarblesECS;
using MarblesECS.PhysX;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class ShopUI : MonoBehaviour
{
    [Header("References")]
    public MarbleGameController Controller;
    public CanvasGroup ShopCanvasGroup;
    public Transform OfferContainer;
    public ShopOfferItemUI OfferPrefab;

    [Header("Header")]
    public TMP_Text CoinsText;
    public TMP_Text RefreshCostText;
    public Button RefreshButton;
    public Button ContinueButton;

    private readonly List<ShopOfferItemUI> offerItems = new List<ShopOfferItemUI>();
    private bool wasOpen;
    private int displayedRefreshCount = -1;

    private void OnEnable()
    {
        if (RefreshButton != null)
            RefreshButton.onClick.AddListener(HandleRefreshClicked);
        if (ContinueButton != null)
            ContinueButton.onClick.AddListener(HandleContinueClicked);
        RefreshView(true);
    }

    private void OnDisable()
    {
        if (RefreshButton != null)
            RefreshButton.onClick.RemoveListener(HandleRefreshClicked);
        if (ContinueButton != null)
            ContinueButton.onClick.RemoveListener(HandleContinueClicked);
    }

    private void Update()
    {
        RefreshView(false);
    }

    private void RefreshView(bool forceOffers)
    {
        bool open = Controller != null && Controller.IsReady &&
            Controller.Session.Phase == RoundPhase.Shop;
        SetShopVisible(open);

        if (!open)
        {
            wasOpen = false;
            return;
        }

        SessionSnapshot session = Controller.Session;
        long refreshCost = Controller.Balance.Campaign.ShopRefreshCost;

        if (CoinsText != null)
            CoinsText.text = session.Coins.ToString();
        if (RefreshCostText != null)
            RefreshCostText.text = refreshCost.ToString();
        if (RefreshButton != null)
            RefreshButton.interactable = session.Coins >= refreshCost;
        if (ContinueButton != null)
            ContinueButton.interactable = true;

        bool offersChanged = forceOffers || !wasOpen ||
            displayedRefreshCount != session.ShopRefreshCount;
        if (offersChanged)
        {
            RebuildOffers();
            displayedRefreshCount = session.ShopRefreshCount;
        }
        else
        {
            RefreshOfferStates();
        }

        wasOpen = true;
    }

    private void SetShopVisible(bool visible)
    {
        if (ShopCanvasGroup == null) return;
        ShopCanvasGroup.alpha = visible ? 1f : 0f;
        ShopCanvasGroup.interactable = visible;
        ShopCanvasGroup.blocksRaycasts = visible;
    }

    private void RebuildOffers()
    {
        if (Controller == null || OfferContainer == null || OfferPrefab == null) return;

        ShopOfferSnapshot[] offers = Controller.GetShopOffers();
        while (offerItems.Count < offers.Length)
        {
            ShopOfferItemUI item = Instantiate(OfferPrefab, OfferContainer);
            item.gameObject.SetActive(true);
            offerItems.Add(item);
        }

        for (int i = 0; i < offerItems.Count; i++)
        {
            bool active = i < offers.Length;
            offerItems[i].gameObject.SetActive(active);
            if (active)
                offerItems[i].Bind(offers[i], Controller.Session.Coins, HandleBuyRequested);
        }
    }

    private void RefreshOfferStates()
    {
        if (Controller == null) return;
        ShopOfferSnapshot[] offers = Controller.GetShopOffers();
        if (offers.Length != offerItems.Count)
        {
            RebuildOffers();
            return;
        }

        long coins = Controller.Session.Coins;
        for (int i = 0; i < offers.Length; i++)
            offerItems[i].Bind(offers[i], coins, HandleBuyRequested);
    }

    private void HandleBuyRequested(int offerIndex)
    {
        if (Controller != null && Controller.BuyOffer(offerIndex))
            RefreshView(true);
    }

    private void HandleRefreshClicked()
    {
        if (Controller != null && Controller.RefreshShop())
            RefreshView(true);
    }

    private void HandleContinueClicked()
    {
        if (Controller != null)
            Controller.EnterBuild();
        RefreshView(false);
    }
}
