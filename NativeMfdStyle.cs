using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KellysTALONPublicUI;

// Copy presentation properties only; never clone native controllers or button listeners.
internal sealed class NativeMfdStyle
{
    private readonly RectTransform sourcePanel;
    private readonly TMP_Text title, body;
    private readonly Image[] frames;
    private readonly Image? box;
    private readonly float coordinates;
    private readonly Vector3[] corners = new Vector3[4];

    internal NativeMfdStyle(MFDScreen stock, float contentWidth)
    {
        sourcePanel = (RectTransform)stock.displayPanel.transform;
        coordinates = contentWidth / Math.Max(1f, sourcePanel.rect.width);
        var texts = stock.displayPanel.GetComponentsInChildren<TMP_Text>(true);
        TMP_Text? heading = null, label = null;
        foreach (var text in texts)
        {
            if (string.IsNullOrWhiteSpace(text.text)) continue;
            if (heading == null || text.fontSize > heading.fontSize) heading = text;
            if (text.text.Trim().Equals("MAP OPTIONS", StringComparison.OrdinalIgnoreCase)) heading = text;
            if (text.text.Trim().Equals("MARKERS", StringComparison.OrdinalIgnoreCase)) label = text;
        }
        title = heading ?? throw new InvalidOperationException("Stock map title unavailable.");
        if (label == null)
            foreach (var text in texts)
                if (text != title && !string.IsNullOrWhiteSpace(text.text)) { label = text; break; }
        body = label ?? title;
        var large = new List<Image>();
        foreach (var image in stock.displayPanel.GetComponentsInChildren<Image>(true))
        {
            if (!image.enabled) continue;
            var r = image.rectTransform;
            float width = r.rect.width * Mathf.Abs(r.lossyScale.x / sourcePanel.lossyScale.x);
            float height = r.rect.height * Mathf.Abs(r.lossyScale.y / sourcePanel.lossyScale.y);
            if (width >= sourcePanel.rect.width * .85f && height >= sourcePanel.rect.height * .85f)
                large.Add(image);
        }
        frames = large.ToArray();
        foreach (var selectable in stock.displayPanel.GetComponentsInChildren<Selectable>(true))
            if (selectable.targetGraphic is Image image && image.sprite != null)
            { box = image; break; }
        if (box == null)
            foreach (var image in stock.displayPanel.GetComponentsInChildren<Image>(true))
                if (image.sprite != null && image.type == Image.Type.Sliced && !image.fillCenter)
                { box = image; break; }
    }

    internal void TextStyle(TMP_Text target, bool isTitle)
    {
        var source = isTitle ? title : body;
        target.font = source.font;
        target.fontSharedMaterial = source.fontSharedMaterial;
        target.fontStyle = source.fontStyle;
        target.fontWeight = source.fontWeight;
        target.fontSize = source.fontSize * coordinates *
            Mathf.Abs(source.transform.lossyScale.y / sourcePanel.lossyScale.y);
        target.characterSpacing = source.characterSpacing;
        target.wordSpacing = source.wordSpacing;
        target.lineSpacing = source.lineSpacing;
        target.enableAutoSizing = false;
    }

    internal void Panel(RectTransform target, Color border, Color background)
    {
        // This transparent input surface must never become a solid grey backing rectangle.
        var hit = target.gameObject.AddComponent<Image>();
        hit.color = Color.clear;
        hit.raycastTarget = true;
        foreach (var source in frames)
        {
            var image = NewImage("Native " + source.name, target);
            CopyImage(source, image);
            var r = source.rectTransform;
            var relative = sourcePanel.InverseTransformPoint(r.TransformPoint(r.rect.center));
            var normalized = new Vector2((relative.x - sourcePanel.rect.xMin) / sourcePanel.rect.width,
                (relative.y - sourcePanel.rect.yMin) / sourcePanel.rect.height);
            var half = new Vector2(r.rect.width * Mathf.Abs(r.lossyScale.x / sourcePanel.lossyScale.x) / sourcePanel.rect.width,
                r.rect.height * Mathf.Abs(r.lossyScale.y / sourcePanel.lossyScale.y) / sourcePanel.rect.height) * .5f;
            image.rectTransform.anchorMin = normalized - half;
            image.rectTransform.anchorMax = normalized + half;
            image.rectTransform.offsetMin = image.rectTransform.offsetMax = Vector2.zero;
        }
        if (frames.Length == 0)
        {
            var fill = NewImage("Panel background", target);
            fill.color = background;
            Stretch(fill.rectTransform);
            var frame = NewImage("Panel frame", target);
            Box(frame);
            frame.color = border;
            Stretch(frame.rectTransform);
        }
    }

    internal void Box(Image target)
    {
        if (box != null) CopyImage(box, target);
        // A sliced native border has a transparent centre; never put an opaque fill behind it.
        target.type = Image.Type.Sliced;
        target.fillCenter = false;
    }

    internal void Layout(RectTransform target, Canvas sourceCanvas, float leftEdge, float contentWidth, float minimumHeight)
    {
        sourcePanel.GetWorldCorners(corners);
        Camera? camera = sourceCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : sourceCanvas.worldCamera;
        var lower = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
        var upper = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);
        float width = Mathf.Abs(upper.x - lower.x), height = Mathf.Abs(upper.y - lower.y);
        if (width < 1f || height < 1f) return;
        float contentHeight = Mathf.Max(minimumHeight, height / width * contentWidth);
        float scale = Mathf.Min(width / contentWidth,
            Mathf.Min(Mathf.Max(1f, leftEdge - 12f) / contentWidth, (Screen.height - 16f) / contentHeight));
        target.sizeDelta = new Vector2(contentWidth, contentHeight);
        target.localScale = Vector3.one * scale;
        float top = Mathf.Clamp(upper.y, contentHeight * scale + 8f, Screen.height - 8f);
        target.anchoredPosition = new Vector2(Mathf.Max(8f, leftEdge - contentWidth * scale - 4f), top - Screen.height);
    }

    private static void CopyImage(Image source, Image target)
    {
        target.sprite = source.sprite;
        target.type = source.type;
        target.fillCenter = source.fillCenter;
        target.pixelsPerUnitMultiplier = source.pixelsPerUnitMultiplier;
        target.material = source.material;
        target.color = source.color;
        target.raycastTarget = false;
    }
    private static Image NewImage(string name, Transform parent)
    {
        var obj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        obj.transform.SetParent(parent, false);
        return obj.GetComponent<Image>();
    }
    private static void Stretch(RectTransform r)
    { r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero; }
}
