using System;
using System.Collections.Generic;
using System.Linq;
using MarblesECS;
using MarblesECS.PhysX;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Presentation only: every action is submitted to the existing campaign controller.</summary>
[DisallowMultipleComponent]
public sealed class DiegeticMachineUI : MonoBehaviour
{

    public MarbleGameController Controller;
    public DevicePlacementUI Placement;
    public Camera ViewCamera;
    public TMP_FontAsset Font;
    [Header("Physical anchors — adjust these to move the interface")]
    public Transform[] ScoreAnchors = new Transform[3];
    public Transform BloodAnchor, LeverAnchor, LeverPivot;
    public Vector3 LeverAxis = Vector3.right;
    public float LeverOffAngle = 65;
    public GameObject LeverHighlight;
    public Transform ItemBoard, ItemUIAnchor;
    public Vector3 BoardOpenPosition, BoardClosedPosition;
    [Range(.15f, 1.5f)] public float SlideSeconds = .55f;
    public Material OutlineMaterial;
    [Header("Interface size")]
    public Vector2 ScoreWorldSize = new Vector2(2.45f,.38f);
    public Vector2 BloodWorldSize = new Vector2(6.2f,.43f);
    public Vector2 ItemWorldSize = new Vector2(3.0f,5.6f);
    [HideInInspector] public Transform Visuals;
    [HideInInspector] public CanvasGroup ItemGroup;
    [HideInInspector] public TMP_Text[] ScoreValues;
    [HideInInspector] public Slider BloodBar;
    [HideInInspector] public Button Continue;
    [HideInInspector] public TMP_Text PendingValue, LeverState, DevicePageLabel;
    [HideInInspector] public Button[] DeviceButtons;
    [HideInInspector] public TMP_Text[] DeviceNames, DeviceDescriptions;
    [HideInInspector] public RectTransform Tooltip;
    [HideInInspector] public TMP_Text TooltipText;
    [HideInInspector] public RectTransform Overlay;
    [HideInInspector] public GameObject DevicePager;
    readonly List<OwnedDeviceSnapshot> unplacedDevices = new List<OwnedDeviceSnapshot>();
    readonly int[] deviceIds = new int[6];
    Quaternion leverOnRotation;
    float slide, nextRefresh;
    int devicePage, hoverKind=-1, hoverIndex;
    bool initialized;
    float deviceFeedbackUntil;
    string deviceFeedback;
    public bool IsBoardOpen => slide >= .999f;
    public int VisibleDeviceCount { get; private set; }

    void Awake()
    {
        if (!Visuals) BuildVisuals();
        if (!ViewCamera) ViewCamera=Camera.main;
        foreach(var c in GetComponentsInChildren<Canvas>(true)) if(c.renderMode==RenderMode.WorldSpace)c.worldCamera=ViewCamera;
        leverOnRotation=LeverPivot ? LeverPivot.localRotation : Quaternion.identity;
        initialized=true; slide=0;
        if(ItemBoard) ItemBoard.localPosition=BoardClosedPosition;
        if(ItemGroup){ItemGroup.alpha=0;ItemGroup.blocksRaycasts=false;ItemGroup.interactable=false;}
        Tooltip.gameObject.SetActive(false);
        if(LeverHighlight) LeverHighlight.SetActive(false);
        BindScoreListener();
    }
    public void BindScoreListener()
    {
        var listener=FindObjectOfType<ScoreListener>();
        if(!listener || ScoreValues==null || ScoreValues.Length!=3)return;
        listener.RoundScoreText=ScoreValues[0];listener.TargetScoreText=ScoreValues[1];listener.TotalScoreText=ScoreValues[2];
        listener.PendingScoreText=PendingValue;listener.BloodSlider=BloodBar;
    }
    void Update()
    {
        if(!initialized)return;
        bool ready=Controller && Controller.IsReady;
        bool build=ready && Controller.Session.Phase==RoundPhase.Build;
        slide=Mathf.MoveTowards(slide,build?1:0,Time.unscaledDeltaTime/Mathf.Max(.01f,SlideSeconds));
        if(ItemBoard)ItemBoard.localPosition=Vector3.Lerp(BoardClosedPosition,BoardOpenPosition,Mathf.SmoothStep(0,1,slide));
        bool show=build && IsBoardOpen;
        if(ItemGroup){ItemGroup.alpha=show?1:0;ItemGroup.blocksRaycasts=show;ItemGroup.interactable=show && !Controller.Paused;}
        if(Continue)Continue.interactable=show && !Controller.Paused;
        bool on=ready && Controller.Session.Phase==RoundPhase.Playing;
        if(LeverPivot) LeverPivot.localRotation=Quaternion.RotateTowards(LeverPivot.localRotation,
            leverOnRotation*Quaternion.AngleAxis(on?0:LeverOffAngle,LeverAxis.normalized),Time.unscaledDeltaTime*240);
        if(LeverState)LeverState.text=on?(CanCashOut()?"CASH OUT  /  向下结算":"ON  /  目标未达成"):"OFF";
        if(Time.unscaledTime>=nextRefresh){RefreshInventory();nextRefresh=Time.unscaledTime+.1f;}
        if(hoverKind>=0 || (Placement && Placement.IsDraggingInventory) || deviceFeedbackUntil>0)UpdateTooltip();
    }
    public bool CanCashOut() => Controller && Controller.IsReady && !Controller.Paused && Controller.Session.Phase==RoundPhase.Playing && Controller.Session.CanCashOut;
    public bool TryCashOut()
    {
        if(!CanCashOut())return false;
        bool accepted=Controller.CashOut();
        if(accepted)RefreshInventory();
        return accepted;
    }
    public bool TryToggleLever()
    {
        if(!Controller||!Controller.IsReady)return false;
        return Controller.Session.Phase==RoundPhase.Build?TryContinue():TryCashOut();
    }

    public bool TryContinue()
    {
        if(!Controller||!Controller.IsReady||Controller.Paused||Controller.Session.Phase!=RoundPhase.Build||!IsBoardOpen)return false;
        if(Placement)Placement.CancelPlacement();
        return Controller.BeginRound();
    }
    public void SelectDevice(int index)
    {
        if(!Controller||!Controller.IsReady||Controller.Paused||Controller.Session.Phase!=RoundPhase.Build||!IsBoardOpen||index<0||index>=deviceIds.Length)return;
        if(Placement && deviceIds[index]!=0)Placement.BeginPlacement(deviceIds[index]);
    }

    public bool BeginDeviceDrag(int index, Vector2 pointer)
    {
        if(!Controller||!Controller.IsReady||Controller.Paused||Controller.Session.Phase!=RoundPhase.Build||!IsBoardOpen||!Placement||index<0||index>=deviceIds.Length)return false;
        return Placement.BeginInventoryDrag(deviceIds[index],pointer);
    }
    public void UpdateDeviceDrag(Vector2 pointer){if(Placement)Placement.UpdateInventoryDrag(pointer);}
    public void EndDeviceDrag(Vector2 pointer, GameObject pointerTarget = null)
    {
        if(!Placement)return;
        Placement.EndInventoryDrag(pointer,pointerTarget);
        deviceFeedback=Placement.LastFeedback;deviceFeedbackUntil=Time.unscaledTime+3;
        RefreshInventory();UpdateTooltip();
    }
    public void CancelDeviceDrag(){if(Placement && Placement.IsDraggingInventory)Placement.CancelPlacement();}
    public void ChangeDevicePage(int step)
    {
        int pages=Mathf.Max(1,(unplacedDevices.Count+5)/6);devicePage=(devicePage+step+pages)%pages;ClearHover();RefreshInventory();
    }
    public void RefreshInventory()
    {
        if(!Controller||!Controller.IsReady)return;
        unplacedDevices.Clear();unplacedDevices.AddRange(Controller.GetOwnedDevices().Where(d=>!d.Placed));
        devicePage=Mathf.Clamp(devicePage,0,Mathf.Max(0,(unplacedDevices.Count-1)/6));
        VisibleDeviceCount=0;
        for(int i=0;i<6;i++)
        {
            int n=devicePage*6+i;bool occupied=n<unplacedDevices.Count;deviceIds[i]=occupied?unplacedDevices[n].InstanceId:0;
            if(DeviceButtons!=null && DeviceButtons.Length>i)
            {
                DeviceButtons[i].gameObject.SetActive(occupied);
                DeviceButtons[i].interactable=occupied && IsBoardOpen && Controller.Session.Phase==RoundPhase.Build && !Controller.Paused;
                if(occupied){VisibleDeviceCount++;DeviceNames[i].text=unplacedDevices[n].Name;DeviceDescriptions[i].text=MarblesECS.Presentation.DeviceDisplayText.Details(unplacedDevices[n])+"\n"+(unplacedDevices[n].Description??"");}
            }
        }
        int devicePages=Mathf.Max(1,(unplacedDevices.Count+5)/6);
        if(DevicePager)DevicePager.SetActive(devicePages>1);
        if(DevicePageLabel)DevicePageLabel.text=$"{devicePage+1} / {devicePages}";
    }
    public void Hover(int kind,int index,bool entering)
    {
        if(!entering){if(hoverKind==kind && hoverIndex==index)ClearHover();return;}
        ClearHover();hoverKind=kind;hoverIndex=index;
        if(kind==0 && LeverHighlight)LeverHighlight.SetActive(true);
        UpdateTooltip();
    }
    public void ClearHover()
    {
        hoverKind=-1;
        if(Tooltip)Tooltip.gameObject.SetActive(false);
        if(LeverHighlight)LeverHighlight.SetActive(false);
    }
    void OnDisable(){ClearHover();CancelDeviceDrag();}
    void UpdateTooltip()
    {
        if(!Tooltip||!Controller||!Controller.IsReady)return;
        string text="";
        if(hoverKind==0)
            text=CanCashOut()?"<b>结算开关</b>\n向下拨动 / 点击结算\n关闭供血，按原规则结算本回合。":
                Controller.Session.Phase==RoundPhase.Playing?"<b>供血已开启</b>\n达到目标分后才能向下拨动并结算。":
                Controller.Session.Phase==RoundPhase.Build?"<b>供血已关闭</b>\n布置完成后点击或向上拨动，开始回合。\n也可使用物品板上的“开始回合”。":"<b>供血已关闭</b>\n等待结算和购买阶段结束。";
        else if(hoverKind==2 && hoverIndex>=0 && hoverIndex<deviceIds.Length)
        {
            var d=unplacedDevices.Find(x=>x.InstanceId==deviceIds[hoverIndex]);
            if(d.InstanceId!=0)text=$"<b>{d.Name}</b>\n{MarblesECS.Presentation.DeviceDisplayText.Details(d)}\n{d.Description}\n拖到盘面空位放置；F1 可查完整道具库。";
        }
        if(Placement && Placement.IsDraggingInventory)text=Placement.LastFeedback;
        else if(deviceFeedbackUntil>Time.unscaledTime)text=deviceFeedback;
        else deviceFeedbackUntil=0;
        if(string.IsNullOrEmpty(text)){Tooltip.gameObject.SetActive(false);return;}
        TooltipText.text=text;Tooltip.gameObject.SetActive(true);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(Overlay,Input.mousePosition,null,out var p);
        var rect=Overlay.rect;var size=Tooltip.sizeDelta;
        Tooltip.anchoredPosition=new Vector2(Mathf.Clamp(p.x+22,rect.xMin+12,rect.xMax-size.x-12),Mathf.Clamp(p.y-18,rect.yMin+size.y+12,rect.yMax-12));
    }

    // Called by the scene installer too, so all UI objects/anchors can be edited in the Hierarchy.
    public void BuildVisuals()
    {
        if(Visuals)return;
        Visuals=new GameObject("实体UI_界面").transform;Visuals.SetParent(transform,false);
        ScoreValues=new TMP_Text[3];string[] labels={"回合分","目标分","总分"};
        for(int i=0;i<3;i++)
        {
            var c=WorldCanvas("信息板_"+i,ScoreAnchors[i],ScoreWorldSize,new Vector2(520,82));
            var panel=Panel(c,"显示底色",new Color(.055f,.035f,.03f,.92f),Vector2.zero,new Vector2(520,82));panel.raycastTarget=false;
            Label(c,"标签",labels[i],34,new Vector2(-240,0),new Vector2(270,70),TextAlignmentOptions.MidlineLeft,new Color(.84f,.8f,.64f));
            ScoreValues[i]=Label(c,"数值","0",40,new Vector2(240,0),new Vector2(210,70),TextAlignmentOptions.MidlineRight,Color.white);
            ScoreValues[i].enableAutoSizing=true;ScoreValues[i].fontSizeMin=20;ScoreValues[i].fontSizeMax=40;
        }
        var b=WorldCanvas("顶部血条",BloodAnchor,BloodWorldSize,new Vector2(900,70));
        Panel(b,"血条背板",new Color(.065f,.012f,.015f,1),Vector2.zero,new Vector2(900,70)).raycastTarget=false;
        var fill=Panel(b,"血液",new Color(.54f,.045f,.055f,1),Vector2.zero,new Vector2(880,50));fill.raycastTarget=false;
        fill.rectTransform.anchorMin=Vector2.zero;fill.rectTransform.anchorMax=Vector2.one;fill.rectTransform.sizeDelta=new Vector2(-20,-20);
        BloodBar=b.gameObject.AddComponent<Slider>();BloodBar.fillRect=fill.rectTransform;BloodBar.direction=Slider.Direction.LeftToRight;BloodBar.minValue=0;BloodBar.maxValue=100;BloodBar.value=100;BloodBar.interactable=false;BloodBar.transition=Selectable.Transition.None;
        Label(b,"BLOOD","B L O O D   /   血量",24,Vector2.zero,new Vector2(860,60),TextAlignmentOptions.Center,new Color(.94f,.9f,.8f));
        var lever=WorldCanvas("结算开关_触控",LeverAnchor,new Vector2(1.35f,1.3f),new Vector2(180,180));
        var leverHit=Panel(lever,"点击实体开关",Color.clear,Vector2.zero,new Vector2(180,180));AttachHit(leverHit,0,0);
        LeverState=Label(lever,"开关状态","ON",17,new Vector2(0,-113),new Vector2(290,42),TextAlignmentOptions.Center,new Color(.9f,.78f,.57f));
        var board=WorldCanvas("物品板_界面",ItemUIAnchor,ItemWorldSize,new Vector2(600,1100));ItemGroup=board.gameObject.AddComponent<CanvasGroup>();ItemGroup.alpha=0;ItemGroup.blocksRaycasts=false;
        Label(board,"标题","装 置 布 置",34,new Vector2(0,510),new Vector2(550,65),TextAlignmentOptions.Center,new Color(.85f,.79f,.64f));
        DeviceButtons=new Button[6];DeviceNames=new TMP_Text[6];DeviceDescriptions=new TMP_Text[6];
        for(int i=0;i<6;i++)
        {
            Vector2 p=new Vector2((i%2==0?-1:1)*143,320-(i/2)*270);
            var card=ActionButton(board,"装置格_"+i,"",p,new Vector2(258,242),2,i);DeviceButtons[i]=card;
            DeviceNames[i]=Label(card.transform,"名称","装置",30,new Vector2(0,80),new Vector2(232,60),TextAlignmentOptions.Center,new Color(.95f,.88f,.72f));
            DeviceDescriptions[i]=Label(card.transform,"详情","",25,new Vector2(0,-24),new Vector2(230,146),TextAlignmentOptions.TopLeft,new Color(.8f,.77f,.7f));
            DeviceDescriptions[i].enableAutoSizing=true;DeviceDescriptions[i].fontSizeMin=21;DeviceDescriptions[i].fontSizeMax=25;
        }
        var dp=new GameObject("装置翻页",typeof(RectTransform)).GetComponent<RectTransform>();dp.SetParent(board,false);dp.anchoredPosition=new Vector2(0,-388);DevicePager=dp.gameObject;
        DevicePageLabel=Label(dp,"页码","1 / 1",26,Vector2.zero,new Vector2(160,40),TextAlignmentOptions.Center,Color.white);
        ActionButton(dp,"上一页","‹",new Vector2(-120,0),new Vector2(75,45),4,-1);ActionButton(dp,"下一页","›",new Vector2(120,0),new Vector2(75,45),4,1);
        Continue=ActionButton(board,"开始回合","开始回合  /  CONTINUE",new Vector2(0,-485),new Vector2(530,70),5,0);
        var overlay=new GameObject("提示层_1080p至4K",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));overlay.transform.SetParent(Visuals,false);
        overlay.GetComponent<Canvas>().renderMode=RenderMode.ScreenSpaceOverlay;overlay.GetComponent<Canvas>().sortingOrder=90;
        var scaler=overlay.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);scaler.matchWidthOrHeight=.5f;
        Overlay=overlay.GetComponent<RectTransform>();
        Tooltip=Panel(Overlay,"开关与装置说明",new Color(.07f,.045f,.04f,.98f),Vector2.zero,new Vector2(380,220)).rectTransform;Tooltip.pivot=new Vector2(0,1);Tooltip.GetComponent<Image>().raycastTarget=false;
        TooltipText=Label(Tooltip,"详细说明","",23,Vector2.zero,new Vector2(346,190),TextAlignmentOptions.TopLeft,new Color(.95f,.9f,.79f));
        TooltipText.rectTransform.anchorMin=Vector2.zero;TooltipText.rectTransform.anchorMax=Vector2.one;TooltipText.rectTransform.pivot=new Vector2(.5f,.5f);
        TooltipText.rectTransform.offsetMin=new Vector2(18,16);TooltipText.rectTransform.offsetMax=new Vector2(-18,-16);
        TooltipText.enableAutoSizing=true;TooltipText.fontSizeMin=17;TooltipText.fontSizeMax=23;
        Tooltip.gameObject.SetActive(false);
        // Keep pending score available without a fourth large readout obscuring the board.
        var pending=WorldCanvas("待结算提示",ScoreAnchors[2],new Vector2(2.5f,.2f),new Vector2(520,42));pending.localPosition+=new Vector3(0,-.32f,-.03f);
        Label(pending,"标签","PENDING / 待结算",20,new Vector2(-245,0),new Vector2(360,40),TextAlignmentOptions.MidlineLeft,new Color(.7f,.65f,.55f));
        PendingValue=Label(pending,"数值","0",22,new Vector2(245,0),new Vector2(130,40),TextAlignmentOptions.MidlineRight,new Color(.8f,.75f,.63f));
    }
    RectTransform WorldCanvas(string name,Transform anchor,Vector2 worldSize,Vector2 size)
    {
        var g=new GameObject(name,typeof(RectTransform),typeof(Canvas),typeof(GraphicRaycaster));g.transform.SetParent(anchor,false);
        var r=g.GetComponent<RectTransform>();r.sizeDelta=size;r.localScale=new Vector3(worldSize.x/size.x,worldSize.y/size.y,.01f);
        var c=g.GetComponent<Canvas>();c.renderMode=RenderMode.WorldSpace;c.worldCamera=ViewCamera;c.sortingOrder=2;
        return r;
    }
    Image Panel(Transform parent,string name,Color color,Vector2 position,Vector2 size)
    {
        var g=new GameObject(name,typeof(RectTransform),typeof(CanvasRenderer),typeof(Image));g.transform.SetParent(parent,false);
        var r=g.GetComponent<RectTransform>();r.sizeDelta=size;r.anchoredPosition=position;
        var image=g.GetComponent<Image>();image.color=color;return image;
    }
    TMP_Text Label(Transform parent,string name,string text,float fontSize,Vector2 pos,Vector2 size,TextAlignmentOptions alignment,Color color)
    {
        var g=new GameObject(name,typeof(RectTransform),typeof(CanvasRenderer),typeof(TextMeshProUGUI));g.transform.SetParent(parent,false);
        var r=g.GetComponent<RectTransform>();r.sizeDelta=size;r.anchoredPosition=pos;
        if(alignment==TextAlignmentOptions.MidlineLeft)r.pivot=new Vector2(0,.5f);
        if(alignment==TextAlignmentOptions.MidlineRight)r.pivot=new Vector2(1,.5f);
        var t=g.GetComponent<TextMeshProUGUI>();t.font=Font;t.text=text;t.fontSize=fontSize;t.alignment=alignment;t.color=color;t.raycastTarget=false;t.enableWordWrapping=true;
        return t;
    }
    void AttachHit(Image image,int kind,int index)
    {
        var hit=image.gameObject.AddComponent<DiegeticUIHit>();hit.Owner=this;hit.Kind=kind;hit.Index=index;
    }
    Button ActionButton(Transform parent,string name,string text,Vector2 pos,Vector2 size,int kind,int index)
    {
        var image=Panel(parent,name,new Color(.10f,.065f,.055f,.93f),pos,size);
        var b=image.gameObject.AddComponent<Button>();b.targetGraphic=image;
        var colors=b.colors;colors.normalColor=Color.white;colors.highlightedColor=new Color(1.55f,1.35f,1.12f,1);colors.pressedColor=new Color(.85f,.38f,.27f,1);b.colors=colors;
        var outline=image.gameObject.AddComponent<Outline>();outline.effectColor=new Color(.43f,.30f,.22f,.85f);outline.effectDistance=new Vector2(1.5f,-1.5f);
        AttachHit(image,kind,index);
        if(text.Length>0)Label(image.transform,"文字",text,27,Vector2.zero,size-new Vector2(8,6),TextAlignmentOptions.Center,new Color(.94f,.86f,.7f));
        return b;
    }
}
