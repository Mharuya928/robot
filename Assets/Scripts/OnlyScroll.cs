using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Viewport等にアタッチして使用
public class OnlyScroll : MonoBehaviour, IDragHandler, IBeginDragHandler, IEndDragHandler, IScrollHandler
{
    private ScrollRect parentScrollRect;

    void Start()
    {
        // 自分の親（またはさらに上）にある ScrollRect を探して記憶しておく
        parentScrollRect = GetComponentInParent<ScrollRect>();
    }

    // 1. ドラッグ操作が来たら...
    public void OnBeginDrag(PointerEventData eventData) { /* 何もしない（握りつぶす） */ }
    public void OnDrag(PointerEventData eventData)      { /* 何もしない（握りつぶす） */ }
    public void OnEndDrag(PointerEventData eventData)   { /* 何もしない（握りつぶす） */ }

    // 2. マウススクロール操作が来たら...
    public void OnScroll(PointerEventData data)
    {
        if (parentScrollRect != null)
        {
            // 親の ScrollRect にそのまま渡してあげる（中継）
            parentScrollRect.OnScroll(data);
        }
    }
}