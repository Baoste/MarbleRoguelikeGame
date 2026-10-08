using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>Transparent hit surfaces live over physical props; no duplicate gameplay authority.</summary>
public sealed class DiegeticUIHit : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler, ISelectHandler, IDeselectHandler, ISubmitHandler, IPointerDownHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public DiegeticMachineUI Owner;
    public int Kind, Index;
    bool dragged;
    Vector2 dragStart;
    public void OnPointerEnter(PointerEventData e){if(Owner)Owner.Hover(Kind,Index,true);}
    public void OnPointerExit(PointerEventData e){if(Owner)Owner.Hover(Kind,Index,false);}
    public void OnSelect(BaseEventData e){if(Owner)Owner.Hover(Kind,Index,true);}
    public void OnDeselect(BaseEventData e){if(Owner)Owner.Hover(Kind,Index,false);}
    public void OnPointerClick(PointerEventData e){if(!dragged && e.button==PointerEventData.InputButton.Left)Activate();}
    public void OnPointerDown(PointerEventData e){dragged=false;}
    public void OnBeginDrag(PointerEventData e){if(Kind==0){dragged=true;dragStart=e.position;}}
    public void OnDrag(PointerEventData e){}
    public void OnEndDrag(PointerEventData e)
    {
        if(!Owner||Kind!=0||e.button!=PointerEventData.InputButton.Left)return;
        float dy=e.position.y-dragStart.y;
        float threshold=Mathf.Max(12,Screen.height*.012f);
        if(dy < -threshold)Owner.TryCashOut();else if(dy > threshold)Owner.TryContinue();
    }
    public void OnSubmit(BaseEventData e){Activate();}
    void Activate()
    {
        if(!Owner || !isActiveAndEnabled)return;
        var button=GetComponent<Button>();if(button && !button.IsInteractable())return;
        switch(Kind){case 0:Owner.TryToggleLever();break;case 1:Owner.TryUseDrug(Index);break;case 2:Owner.SelectDevice(Index);break;
            case 3:Owner.ChangeDrugPage(Index);break;case 4:Owner.ChangeDevicePage(Index);break;case 5:Owner.TryContinue();break;}
    }
    void OnDisable(){if(Owner)Owner.Hover(Kind,Index,false);}
}
