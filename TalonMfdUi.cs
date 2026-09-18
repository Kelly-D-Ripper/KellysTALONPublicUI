using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KellysTALONPublicUI;

// Original TALON presentation using the installed game's MFDScreen contract.
// Only the display panel intercepts pointer input; map selection remains native.
internal sealed class TalonMfdUi : IDisposable
{
    private const float Width = 460f, Height = 650f;
    private Color Green, Blue, Muted, Border, Dark;
    private readonly Plugin plugin;
    private readonly Action<string> log;
    private readonly List<Button> commandButtons = new List<Button>();
    private VirtualMFD? owner;
    private GameplayUI? gameplay;
    private MFDScreen? screen;
    private MfdSlotLease<MFDScreen>? lease;
    private RectTransform? root, panel;
    private Canvas? canvas, nativeCanvas;
    private GameObject? registration;
    private Button? bezel;
    private Button.ButtonClickedEvent? savedClick, ownClick;
    private string savedLabel = "";
    private bool savedEnabled, savedInteractable, wasVisible;
    private float nextBind, nextRefresh;
    private NativeMfdStyle? appearance;
    private GameObject? purchasePage, flightPage;
    private TMP_Text? aircraft, preset, stores, livery, allocation, capacity, feedback, gate, scope, roster, rosterStatus;
    private Button? purchase, cancel, purchaseTab, wingTab;
    private Button? jam;
    private TMP_Text? jamLabel, attackModeLabel;
    private string lastFailure = "";
    private readonly Vector3[] corners = new Vector3[4];

    internal TalonMfdUi(Plugin plugin, Action<string> log) { this.plugin = plugin; this.log = log; }

    internal void Tick(bool enabled)
    {
        GameplayUI? current = SceneSingleton<GameplayUI>.i;
        if (!enabled || current != gameplay || owner == null || (lease != null && !lease.Owned))
        {
            Release();
            gameplay = current;
            if (!enabled) return;
        }
        if (screen == null && current != null && Time.unscaledTime >= nextBind)
        {
            nextBind = Time.unscaledTime + 2f;
            try { Bind(current); }
            catch (Exception error)
            {
                Release();
                string reason = error.GetType().Name + ": " + error.Message;
                if (lastFailure != reason) { log("MFD unavailable; F7 remains available. " + reason); lastFailure = reason; }
            }
        }
        bool shown = screen != null && screen.isActive && DynamicMap.mapMaximized &&
            owner != null && owner.isActiveAndEnabled && owner.activeLeft == screen && lease?.Owned == true;
        if (canvas != null) canvas.enabled = shown;
        if (root != null) root.gameObject.SetActive(shown);
        if (!shown && screen != null && screen.isActive && owner != null && owner.activeLeft != screen)
            screen.CloseScreen(Vector3.zero);
        if (shown)
        {
            // Native MFDScreen movement affects only registration, never the overlay canvas.
            Layout();
            if (!wasVisible || Time.unscaledTime >= nextRefresh)
            {
                nextRefresh = Time.unscaledTime + .25f;
                Refresh();
            }
        }
        wasVisible = shown;
    }

    private void Bind(GameplayUI current)
    {
        var mfds = current.GetComponentsInChildren<VirtualMFD>(true);
        if (mfds.Length != 1) return;
        owner = mfds[0];
        ReadVanillaPalette();
        appearance = new NativeMfdStyle(owner!.leftScreens[1], Width);
        nativeCanvas = owner.GetComponentInParent<Canvas>();
        if (nativeCanvas == null) { owner = null; return; }
        // Separate native registration from rendering. Native Show/Close moves its transform;
        // inherited map canvases can also clip, scale or sort an otherwise active panel away.
        registration = Rect("TALON MFD registration", owner.transform).gameObject;
        screen = registration.AddComponent<MFDScreen>();
        var overlay = new GameObject("TALON MFD overlay", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
        root = overlay.GetComponent<RectTransform>();
        canvas = overlay.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.targetDisplay = nativeCanvas.targetDisplay;
        int sortOrder = nativeCanvas.rootCanvas.sortingOrder;
        foreach (var existing in current.GetComponentsInChildren<Canvas>(true))
            sortOrder = Math.Max(sortOrder, existing.sortingOrder);
        canvas.sortingOrder = Math.Min(32767, sortOrder + 1);
        canvas.enabled = false;
        root.gameObject.SetActive(false);
        lease = MfdSlotLease<MFDScreen>.TryClaim(owner.leftScreens, owner.leftButtons.Count, screen,
            index => owner.leftButtons[index] != null &&
                owner.leftButtons[index].GetComponentInChildren<TextMeshProUGUI>(true) != null);
        if (lease == null)
        {
            Release();
            if (lastFailure != "full") log("No unused left MFD button; F7 remains available.");
            lastFailure = "full";
            return;
        }
        bezel = owner.leftButtons[lease.Index];
        var label = bezel.GetComponentInChildren<TextMeshProUGUI>(true);
        savedLabel = label.text;
        savedEnabled = bezel.enabled;
        savedInteractable = bezel.interactable;
        savedClick = bezel.onClick;

        // A private highlight avoids changing the stock button's art or another screen's state.
        var highlight = Image("TALON selected", bezel.transform, Green, false);
        Stretch(highlight.rectTransform, 2f);
        var vanillaHighlight = owner.leftScreens.Count > 2 ? owner.leftScreens[2]?.highlight : null;
        if (vanillaHighlight != null)
        { highlight.color = vanillaHighlight.color; highlight.sprite = vanillaHighlight.sprite; highlight.type = vanillaHighlight.type; }
        highlight.enabled = false;
        screen.label = label;
        screen.highlight = highlight;
        screen.aircraftOnly = false;
        panel = Rect("TALON panel", root);
        panel.sizeDelta = new Vector2(Width, Height);
        panel.pivot = new Vector2(0, 1);
        panel.anchorMin = panel.anchorMax = new Vector2(0, 1);
        screen.displayPanel = panel.gameObject;
        ownClick = new Button.ButtonClickedEvent();
        ownClick.AddListener(Toggle);
        bezel.onClick = ownClick;
        screen.Setup(owner, "TAL");
        Build();
        bezel.enabled = bezel.interactable = true;
        screen.CloseScreen(-Vector3.right * Screen.width);
        lastFailure = "";
        log("Registered TAL purchase/flight page in left MFD slot " + (lease.Index + 1) + ".");
    }

    private void Toggle()
    {
        if (owner == null || screen == null || lease?.Owned != true || !DynamicMap.mapMaximized) return;
        if (screen.isActive)
        {
            screen.CloseScreen(-Vector3.right * Screen.width);
            if (owner.activeLeft == screen) owner.activeLeft = null;
        }
        else
        {
            plugin.MfdOpened();
            owner.HideAllLeftScreens();
            owner.activeLeft = screen;
            screen.ShowScreen(Vector3.zero);
            canvas!.enabled = true;
            root!.gameObject.SetActive(true);
            Canvas.ForceUpdateCanvases();
            Layout();
            Refresh();
            log("Panel opened: overlay=" + canvas.renderMode + " sort=" + canvas.sortingOrder +
                " screen=" + Screen.width + "x" + Screen.height + " panel=" + panel!.anchoredPosition +
                " scale=" + panel.localScale.x);
        }
    }

    internal void Close()
    {
        if (canvas != null) canvas.enabled = false;
        if (root != null) root.gameObject.SetActive(false);
        if (screen == null) return;
        screen.CloseScreen(-Vector3.right * Screen.width);
        if (owner != null && owner.activeLeft == screen) owner.activeLeft = null;
    }

    private void Layout()
    {
        if (root == null || panel == null || bezel == null) return;
        ((RectTransform)bezel.transform).GetWorldCorners(corners);
        Camera? camera = nativeCanvas != null && nativeCanvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? nativeCanvas.worldCamera : null;
        float leftEdge = RectTransformUtility.WorldToScreenPoint(camera, corners[0]).x;
        var placement = MfdPanelLayout.Calculate(Screen.width, Screen.height, leftEdge, Width, Height);
        panel.localScale = Vector3.one * placement.Scale;
        panel.anchoredPosition = new Vector2(placement.X, -placement.Y);
        if (nativeCanvas != null) appearance?.Layout(panel, nativeCanvas, leftEdge, Width, Height);
    }

    private void Build()
    {
        if (panel == null) throw new InvalidOperationException("MFD panel not initialized.");
        appearance!.Panel(panel!, Border, Dark);
        Text(panel!, "TALON", 18, 6, 424, 28, 30, Green, TextAlignmentOptions.Center);
        Text(panel!, "TACTICAL AIRBORNE LEADERSHIP\n& OPERATIONS NETWORK", 18, 34, 424, 34, 12, Green, TextAlignmentOptions.Center);
        purchaseTab = Button(panel, "REINFORCEMENTS", 18, 76, 256, 36, () => SetTab(false));
        wingTab = Button(panel, "ORDERS", 282, 76, 160, 36, () => SetTab(true));
        allocation = Text(panel, "", 18, 124, 424, 22, 13, Blue);
        capacity = Text(panel, "", 18, 148, 424, 20, 12, Muted);
        purchasePage = Rect("Purchase page", panel).gameObject;
        Stretch((RectTransform)purchasePage.transform);
        flightPage = Rect("Flight page", panel).gameObject;
        Stretch((RectTransform)flightPage.transform);
        BuildPurchase(purchasePage.transform);
        BuildFlight(flightPage.transform);
        var statusBox = Image("Server feedback", panel, Border, true);
        Place(statusBox.rectTransform, 18, 558, 424, 72);
        feedback = Text(statusBox.transform, "", 10, 8, 404, 56, 13, Muted);
        SetTab(false);
    }

    private void BuildPurchase(Transform parent)
    {
        Heading(parent, "AIRCRAFT", 182);
        aircraft = Selector(parent, 208, () => plugin.MfdAircraft(-1), () => plugin.MfdAircraft(1));
        Heading(parent, "MISSION LOADOUT", 254);
        preset = Selector(parent, 280, () => plugin.MfdPreset(-1), () => plugin.MfdPreset(1));
        stores = Text(parent, "", 22, 326, 416, 58, 13, Blue);
        Heading(parent, "LIVERY", 392);
        livery = Selector(parent, 416, () => plugin.MfdLivery(-1), () => plugin.MfdLivery(1));
        Button(parent, "REFRESH LIVERIES", 18, 460, 180, 24, plugin.MfdRefreshLiveries);
        Text(parent, "Installed Workshop / faction default", 204, 460, 238, 24, 10, Muted);
        gate = Text(parent, "", 18, 490, 424, 27, 11, Muted);
        cancel = Button(parent, "CANCEL WAITING", 18, 520, 152, 30, plugin.MfdCancel);
        purchase = Button(parent, "ORDER WINGMAN  >", 178, 520, 264, 30, plugin.MfdPurchase);
    }

    private void BuildFlight(Transform parent)
    {
        Heading(parent, "ACTIVE FLIGHT", 182);
        Text(parent, "CALLSIGN", 22, 206, 190, 18, 11, Muted);
        Text(parent, "STATUS", 220, 206, 218, 18, 11, Muted, TextAlignmentOptions.MidlineRight);
        roster = Text(parent, "", 22, 228, 190, 72, 14, Green);
        rosterStatus = Text(parent, "", 220, 228, 218, 72, 14, Blue, TextAlignmentOptions.MidlineRight);
        roster.enableAutoSizing = rosterStatus.enableAutoSizing = false;
        roster.enableWordWrapping = rosterStatus.enableWordWrapping = false;
        scope = Text(parent, "", 22, 308, 300, 28, 14, Blue);
        Button(parent, "SCOPE >", 334, 306, 108, 30, plugin.MfdScope);
        commandButtons.Add(Button(parent, "FORM UP", 18, 348, 208, 38, () => plugin.MfdCommand(TalonCommand.FormUp)));
        commandButtons.Add(Button(parent, "ATTACK MY TARGETS", 234, 348, 208, 38, () => plugin.MfdCommand(TalonCommand.AttackMyTargets)));
        commandButtons.Add(Button(parent, "HOLD HERE", 18, 396, 208, 38, () => plugin.MfdCommand(TalonCommand.HoldHere)));
        commandButtons.Add(Button(parent, "RETURN TO BASE", 234, 396, 208, 38, () => plugin.MfdCommand(TalonCommand.ReturnToBase)));
        commandButtons.Add(Button(parent, "FORMATION (WHOLE FLIGHT)", 18, 444, 208, 34,
            () => plugin.MfdCommand(TalonCommand.CycleFormation)));
        commandButtons.Add(Button(parent, "ALPHA STRIKE", 234, 444, 208, 34,
            () => plugin.MfdCommand(TalonCommand.FullStrike)));
        attackModeLabel = Button(parent, "MODE: DISTRIBUTED", 18, 486, 208, 32,
            plugin.CycleAttackMode).GetComponentInChildren<TMP_Text>();
        jam = Button(parent, "JAM TARGETS", 234, 486, 208, 32, () => plugin.MfdCommand(TalonCommand.Jam));
        jamLabel = jam.GetComponentInChildren<TMP_Text>();
        Button(parent, "JOYSTICK & KEYBOARD BINDINGS", 18, 526, 424, 24, plugin.MfdBindings);
    }

    private void Refresh()
    {
        Plugin.MfdState state = plugin.ReadMfdState();
        aircraft!.text = state.Aircraft;
        preset!.text = state.Preset;
        stores!.text = state.Stores;
        livery!.text = state.Livery;
        allocation!.text = state.Allocation;
        capacity!.text = state.Capacity;
        feedback!.text = state.Status;
        gate!.text = state.Gate;
        scope!.text = "SCOPE  " + state.Scope;
        roster!.text = state.Roster.Count == 0 ? "NO ACTIVE WINGMEN" : string.Join("\n", state.Roster);
        rosterStatus!.text = string.Join("\n", state.RosterStatuses);
        purchase!.interactable = state.CanPurchase;
        cancel!.interactable = state.CanCancel;
        cancel.gameObject.SetActive(state.CanCancel);
        jam!.gameObject.SetActive(state.CanJam || state.Jamming);
        jam.interactable = state.CanCommand;
        jamLabel!.text = state.Jamming ? "STOP JAMMING" : "JAM / MEDUSA PODS";
        attackModeLabel!.text = "MODE: " + state.AttackMode;
        foreach (var button in commandButtons) button.interactable = state.CanCommand;
    }

    private void SetTab(bool flight)
    {
        purchasePage!.SetActive(!flight);
        flightPage!.SetActive(flight);
        SetTabColor(purchaseTab!, !flight);
        SetTabColor(wingTab!, flight);
    }

    private void SetTabColor(Button button, bool active)
    {
        var colors = button.colors;
        colors.normalColor = active ? Green : Border;
        button.colors = colors;
    }

    private void ReadVanillaPalette()
    {
        var textColors = new List<MfdPaletteColor>();
        var imageColors = new List<MfdPaletteColor>();
        // Read the stock BDF/MAP/HUD graphics after native styling has resolved inheritance.
        // Theme.GetColors returns override data: alpha zero means KEEP the original alpha,
        // and RGB zero means KEEP the original colour. It is not a render-ready palette.
        var stockScreens = owner!.leftScreens;
        for (int i = 0; i < Math.Min(3, stockScreens.Count); i++)
        {
            var display = stockScreens[i]?.displayPanel;
            if (display == null) continue;
            foreach (var graphic in display.GetComponentsInChildren<Graphic>(true))
            {
                if (!graphic.enabled) continue;
                var c = graphic.color;
                var sample = new MfdPaletteColor(c.r, c.g, c.b, c.a);
                if (graphic is TMP_Text) textColors.Add(sample);
                else if (graphic is Image) imageColors.Add(sample);
            }
        }
        Color Pick(List<MfdPaletteColor> colors, Color reference)
        {
            var c = MfdPaletteColor.Pick(colors,
                new MfdPaletteColor(reference.r, reference.g, reference.b, reference.a));
            return new Color(c.R, c.G, c.B, c.A);
        }
        Green = Pick(textColors, Color.green);
        Blue = Pick(textColors, new Color(0f, .4f, 1f));
        Muted = Pick(textColors, Color.gray);
        Border = Pick(imageColors, Color.gray);
        Dark = Pick(imageColors, new Color(0f, 0f, 0f, .6f));
        log("Resolved native palette: green=" + Green + " blue=" + Blue + " text=" + Muted +
            " border=" + Border + " background=" + Dark);
    }
    private TMP_Text Selector(Transform parent, float y, Action previous, Action next)
    {
        Button(parent, "<", 18, y, 34, 36, previous);
        var field = Image("Selection", parent, Border, true);
        Place(field.rectTransform, 58, y, 344, 36);
        var label = Text(field.transform, "", 8, 3, 328, 30, 15, Blue, TextAlignmentOptions.Center);
        Button(parent, ">", 408, y, 34, 36, next);
        return label;
    }

    private void Heading(Transform parent, string label, float y)
    {
        Text(parent, label, 18, y, 424, 20, 12, Green);
    }

    private Button Button(Transform parent, string label, float x, float y, float w, float h, Action click)
    {
        var border = Image(label, parent, Border, true);
        Place(border.rectTransform, x, y, w, h);
        var button = border.gameObject.AddComponent<Button>();
        button.targetGraphic = border;
        border.color = Color.white; // neutral multiplier; ColorBlock supplies exact vanilla colours
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        var colors = button.colors;
        colors.normalColor = Border;
        colors.highlightedColor = Green;
        colors.selectedColor = Green;
        colors.pressedColor = Green;
        colors.disabledColor = Muted;
        button.colors = colors;
        Text(border.transform, label, 5, 2, w - 10, h - 4, 13, Green, TextAlignmentOptions.Center);
        button.onClick.AddListener(() => { click(); nextRefresh = 0f; });
        return button;
    }

    private TMP_Text Text(Transform parent, string value, float x, float y, float w, float h,
                          float size, Color color, TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft)
    {
        var rect = Rect("Text", parent);
        Place(rect, x, y, w, h);
        var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        appearance!.TextStyle(text, size >= 27f);
        text.text = value;
        text.color = color;
        text.alignment = align;
        text.enableWordWrapping = true;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.richText = false;
        text.raycastTarget = false;
        return text;
    }

    private static RectTransform Rect(string name, Transform parent)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        return rect;
    }

    private Image Image(string name, Transform parent, Color color, bool raycast)
    {
        var image = Rect(name, parent).gameObject.AddComponent<Image>();
        appearance?.Box(image);
        image.color = color;
        image.raycastTarget = raycast;
        return image;
    }

    private static void Place(RectTransform rect, float x, float y, float w, float h)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(w, h);
    }

    private static void Stretch(RectTransform rect, float inset = 0f)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(inset, inset);
        rect.offsetMax = new Vector2(-inset, -inset);
    }

    private void Release()
    {
        bool owned = lease?.Owned == true;
        bool vacant = owner != null && lease != null &&
            (lease.Index >= owner.leftScreens.Count || owner.leftScreens[lease.Index] == null);
        if (screen != null)
        {
            if (owner != null && owner.activeLeft == screen) owner.activeLeft = null;
            if (screen.displayPanel != null) screen.displayPanel.SetActive(false);
            if (screen.highlight != null) UnityEngine.Object.Destroy(screen.highlight.gameObject);
        }
        // Restore exactly the event object we replaced; keep a successor's listeners/label intact.
        if (bezel != null && ReferenceEquals(bezel.onClick, ownClick))
        {
            bezel.onClick = savedClick!;
            if (owned || vacant)
            {
                bezel.enabled = savedEnabled;
                bezel.interactable = savedInteractable;
                var label = bezel.GetComponentInChildren<TextMeshProUGUI>(true);
                if (label != null) label.text = savedLabel;
            }
        }
        lease?.Dispose();
        if (registration != null) UnityEngine.Object.Destroy(registration);
        registration = null; nativeCanvas = null; appearance = null;
        if (root != null) { root.gameObject.SetActive(false); UnityEngine.Object.Destroy(root.gameObject); }
        lease = null; screen = null; root = panel = null; owner = null; bezel = null; canvas = null;
        savedClick = ownClick = null;
        commandButtons.Clear();
        wasVisible = false;
    }

    public void Dispose() => Release();
}
