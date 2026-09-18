using System;
using System.Reflection;
using HarmonyLib;
using NuclearOption.MissionEditorScripts.Buttons;
using NuclearOption.Networking;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KellysTALONPublicUI;

internal readonly struct WingmanPurchaseDonateState
{
    internal WingmanPurchaseDonateState(bool available, float displayPrice, string detail)
    {
        Available = available;
        DisplayPrice = displayPrice;
        Detail = detail ?? string.Empty;
    }

    internal bool Available { get; }
    internal float DisplayPrice { get; }
    internal string Detail { get; }
}

// Owns only the native Donate > Vehicles entry. Purchase state, selector presentation, and
// networking remain in the purchase controller and are reached through the two narrow delegates.
internal sealed class WingmanPurchaseDonateUi : IDisposable
{
    internal delegate WingmanPurchaseDonateState QueryState(Player player);
    internal delegate void OpenSelector(Player player);

    private const string HarmonyId = Plugin.Guid + ".donate_entry";
    private const string AirliftHarmonyId = "kelly.nuclearoption.airlift.publicui";
    private const string RowName = "KellysTALON_PurchaseWingmen";
    private const float RefreshIntervalSeconds = .25f;

    private static readonly FieldInfo? ConvoyPrefabField =
        AccessTools.Field(typeof(ContributeToFaction), "convoySelectPrefab");
    private static readonly FieldInfo? ConvoyBackgroundField =
        AccessTools.Field(typeof(ContributeToFaction), "convoySelectBackground");
    private static readonly FieldInfo? HoverTextField =
        AccessTools.Field(typeof(ContributeToFaction), "hoverText");
    private static readonly FieldInfo? LocalPlayerField =
        AccessTools.Field(typeof(ContributeToFaction), "localPlayer");
    private static readonly FieldInfo? CurrentFundsField =
        AccessTools.Field(typeof(ContributeToFaction), "currentFunds");
    private static readonly FieldInfo? OptionButtonField =
        AccessTools.Field(typeof(ConvoyPurchaseOption), "button");
    private static readonly FieldInfo? OptionHoverField =
        AccessTools.Field(typeof(ConvoyPurchaseOption), "buttonHoverText");
    private static readonly FieldInfo? OptionTextField =
        AccessTools.Field(typeof(ConvoyPurchaseOption), "text");
    private static readonly FieldInfo? OptionIconField =
        AccessTools.Field(typeof(ConvoyPurchaseOption), "icon");
    private static readonly FieldInfo? OptionUnavailableField =
        AccessTools.Field(typeof(ConvoyPurchaseOption), "unavailable");
    private static readonly FieldInfo? OptionLocalPlayerField =
        AccessTools.Field(typeof(ConvoyPurchaseOption), "localPlayer");
    private static readonly FieldInfo? OptionCostField =
        AccessTools.Field(typeof(ConvoyPurchaseOption), "cost");
    private static readonly FieldInfo? OptionCanSpawnField =
        AccessTools.Field(typeof(ConvoyPurchaseOption), "canSpawn");
    private static readonly FieldInfo? OptionWasInteractableField =
        AccessTools.Field(typeof(ConvoyPurchaseOption), "wasInteractable");
    private static readonly FieldInfo? OptionCompositionField =
        AccessTools.Field(typeof(ConvoyPurchaseOption), "composition");

    private static WingmanPurchaseDonateUi? active;

    private readonly QueryState queryState;
    private readonly OpenSelector openSelector;
    private readonly Action<string> log;
    private Harmony? harmony;
    private bool adapterFailureLogged;
    private bool rowReadyLogged;
    private float nextScan;
    private bool disposed;

    internal WingmanPurchaseDonateUi(QueryState queryState, OpenSelector openSelector,
                                     Action<string> log)
    {
        this.queryState = queryState ?? throw new ArgumentNullException(nameof(queryState));
        this.openSelector = openSelector ?? throw new ArgumentNullException(nameof(openSelector));
        this.log = log ?? (_ => { });

        // The public purchase surface is client UI only. In particular, do not even install its
        // Harmony patch on a dedicated server.
        if (Application.isBatchMode) return;

        try
        {
            ValidateApi();
            harmony = new Harmony(HarmonyId);
            var postfix = new HarmonyMethod(typeof(WingmanPurchaseDonateUi),
                                            nameof(RefreshVehicleListPostfix))
            {
                // AIRLIFT owns a separate row and currently resets/reflows this panel. Running
                // after its postfix lets both entries coexist without competing reset baselines.
                after = new[] { AirliftHarmonyId }
            };
            harmony.Patch(AccessTools.Method(typeof(ContributeToFaction),
                                             nameof(ContributeToFaction.RefreshVehicleList)),
                          postfix: postfix);
            active = this;
        }
        catch (Exception exception)
        {
            harmony?.UnpatchSelf();
            harmony = null;
            if (ReferenceEquals(active, this)) active = null;
            this.log("state=wingman_donate_ui_disabled reason=api_or_patch_failure exception="
                     + exception.GetType().Name);
        }
    }

    private static void ValidateApi()
    {
        if (ConvoyPrefabField == null || ConvoyBackgroundField == null
            || HoverTextField == null || LocalPlayerField == null || CurrentFundsField == null
            || OptionButtonField == null || OptionHoverField == null || OptionTextField == null
            || OptionIconField == null || OptionUnavailableField == null
            || OptionLocalPlayerField == null || OptionCostField == null
            || OptionCanSpawnField == null || OptionWasInteractableField == null
            || OptionCompositionField == null)
            throw new MissingFieldException(
                "Nuclear Option Donate/convoy UI fields changed.");
    }

    private static void RefreshVehicleListPostfix(ContributeToFaction __instance)
    {
        WingmanPurchaseDonateUi? ui = active;
        if (ui == null || ui.disposed || Application.isBatchMode || __instance == null) return;
        try
        {
            ui.AddOrRefreshRow(__instance);
        }
        catch (Exception exception)
        {
            ui.log("state=wingman_donate_row_rejected reason=refresh_failure exception="
                   + exception.GetType().Name);
        }
    }

    internal void Tick()
    {
        if (disposed || Application.isBatchMode || Time.unscaledTime < nextScan) return;
        nextScan = Time.unscaledTime + RefreshIntervalSeconds;
        try
        {
            ContributeToFaction[] menus = UnityEngine.Object.FindObjectsOfType<ContributeToFaction>();
            for (int index = 0; index < menus.Length; ++index)
            {
                ContributeToFaction menu = menus[index];
                if (menu == null || !menu.gameObject.activeInHierarchy) continue;
                AddOrRefreshRow(menu);
            }
        }
        catch (Exception exception)
        {
            if (adapterFailureLogged) return;
            adapterFailureLogged = true;
            log("state=wingman_donate_row_rejected reason=runtime_scan exception=" +
                exception.GetType().Name);
        }
    }

    private void AddOrRefreshRow(ContributeToFaction menu)
    {
        Player? player = LocalPlayerField!.GetValue(menu) as Player;
        GameObject? prefab = ConvoyPrefabField!.GetValue(menu) as GameObject;
        Transform? background = ConvoyBackgroundField!.GetValue(menu) as Transform;
        HoverText? hoverText = HoverTextField!.GetValue(menu) as HoverText;
        if (player == null || prefab == null || background == null) return;

        PurchaseWingmenMarker? marker = null;
        PurchaseWingmenMarker[] existing =
            background.GetComponentsInChildren<PurchaseWingmenMarker>(true);
        for (int index = 0; index < existing.Length; index++)
        {
            PurchaseWingmenMarker candidate = existing[index];
            if (candidate == null) continue;
            if (marker == null)
            {
                marker = candidate;
                continue;
            }

            // Hide extras synchronously so this frame's layout measurement cannot count them,
            // then let Unity destroy them at its safe end-of-frame point.
            candidate.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(candidate.gameObject);
        }

        bool created = marker == null;
        if (created)
        {
            GameObject clone = UnityEngine.Object.Instantiate(prefab, background);
            clone.name = RowName;
            marker = clone.AddComponent<PurchaseWingmenMarker>();
        }
        else if (!marker!.gameObject.activeSelf)
        {
            marker.gameObject.SetActive(true);
        }

        ConvoyPurchaseOption? option = marker!.GetComponent<ConvoyPurchaseOption>();
        if (option == null)
        {
            marker.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(marker.gameObject);
            return;
        }

        if (hoverText != null) option.SetButtonHoverText(hoverText);
        Button? button = OptionButtonField!.GetValue(option) as Button;
        TMP_Text? text = OptionTextField!.GetValue(option) as TMP_Text;
        Image? icon = OptionIconField!.GetValue(option) as Image;
        GameObject? unavailable = OptionUnavailableField!.GetValue(option) as GameObject;
        ShowHoverText? optionHover = OptionHoverField!.GetValue(option) as ShowHoverText;
        if (button == null || text == null)
        {
            marker.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(marker.gameObject);
            return;
        }

        if (created && icon != null && icon.sprite == null)
            CopyNativeIcon(background, option, icon);

        OptionLocalPlayerField!.SetValue(option, player);
        OptionCompositionField!.SetValue(option,
            "\nUp to three Kelly's TALON aircraft\nNative hangar, taxi, and takeoff");
        if (unavailable != null) unavailable.SetActive(false);

        button.onClick = new Button.ButtonClickedEvent();
        button.onClick.AddListener(() => Open(player));

        marker.Owner = this;
        marker.Player = player;
        marker.Option = option;
        marker.Button = button;
        marker.Label = text;
        marker.Hover = optionHover;
        marker.NextRefresh = 0f;

        // The clone is a visual host, not a native convoy purchase. Disabling this component
        // prevents its Update loop from overwriting TALON's availability and click state.
        option.enabled = false;
        marker.transform.SetAsLastSibling();
        RefreshMarker(marker);
        EnsureDonateLayout(menu, background);
        if (!rowReadyLogged)
        {
            rowReadyLogged = true;
            log("state=wingman_donate_row_ready row=" + RowName);
        }
    }

    private static void CopyNativeIcon(Transform background, ConvoyPurchaseOption ownOption,
                                       Image destination)
    {
        ConvoyPurchaseOption[] options =
            background.GetComponentsInChildren<ConvoyPurchaseOption>(true);
        for (int index = 0; index < options.Length; index++)
        {
            ConvoyPurchaseOption candidate = options[index];
            if (candidate == null || ReferenceEquals(candidate, ownOption)) continue;
            Image? source = OptionIconField!.GetValue(candidate) as Image;
            if (source == null || source.sprite == null) continue;
            destination.sprite = source.sprite;
            return;
        }
    }

    private void RefreshMarker(PurchaseWingmenMarker marker)
    {
        if (disposed || marker == null || marker.Player == null || marker.Option == null
            || marker.Button == null || marker.Label == null)
            return;

        WingmanPurchaseDonateState state;
        try
        {
            state = queryState(marker.Player);
            adapterFailureLogged = false;
        }
        catch (Exception exception)
        {
            state = new WingmanPurchaseDonateState(false, float.NaN,
                "Wingman purchasing is temporarily unavailable.");
            if (!adapterFailureLogged)
            {
                adapterFailureLogged = true;
                log("state=wingman_donate_state_rejected reason=adapter_failure exception="
                    + exception.GetType().Name);
            }
        }

        bool priceKnown = !float.IsNaN(state.DisplayPrice)
                          && !float.IsInfinity(state.DisplayPrice)
                          && state.DisplayPrice >= 0f;
        marker.Label.text = priceKnown
            ? "Purchase Wingmen (from " + UnitConverter.ValueReading(state.DisplayPrice) + ")"
            : "Purchase Wingmen";
        marker.Button.interactable = state.Available;
        OptionCostField!.SetValue(marker.Option, priceKnown ? state.DisplayPrice : 0f);
        OptionCanSpawnField!.SetValue(marker.Option, state.Available);
        OptionWasInteractableField!.SetValue(marker.Option, state.Available);

        if (marker.Hover != null)
        {
            string detail = string.IsNullOrWhiteSpace(state.Detail)
                ? "Open Kelly's TALON aircraft selector. The server resolves the aircraft, "
                  + "price, availability, and spawn request."
                : state.Detail;
            marker.Hover.SetText(detail);
        }
    }

    private void Open(Player player)
    {
        if (disposed || player == null) return;
        try
        {
            openSelector(player);
        }
        catch (Exception exception)
        {
            log("state=wingman_purchase_selector_rejected reason=adapter_failure exception="
                + exception.GetType().Name);
        }
    }

    private static void EnsureDonateLayout(ContributeToFaction menu, Transform optionContainer)
    {
        RectTransform? root = menu.transform as RectTransform;
        TMP_Text? funds = CurrentFundsField!.GetValue(menu) as TMP_Text;
        if (root == null || funds == null) return;

        DonateLayoutGuard guard = menu.GetComponent<DonateLayoutGuard>();
        if (guard == null) guard = menu.gameObject.AddComponent<DonateLayoutGuard>();

        Canvas.ForceUpdateCanvases();
        float lowestRow = float.PositiveInfinity;
        ConvoyPurchaseOption[] options =
            optionContainer.GetComponentsInChildren<ConvoyPurchaseOption>(true);
        for (int index = 0; index < options.Length; index++)
        {
            ConvoyPurchaseOption option = options[index];
            if (option == null || !option.gameObject.activeInHierarchy
                || !(option.transform is RectTransform rect))
                continue;
            lowestRow = Mathf.Min(lowestRow, WorldBottom(rect));
        }
        if (float.IsPositiveInfinity(lowestRow)) return;

        float requiredGrowth = Mathf.Max(0f,
            WorldTop(funds.rectTransform) - lowestRow + 14f);
        if (requiredGrowth < 1f) return;

        // If the same geometry reports the same residual overlap after our last adjustment,
        // another identical resize cannot improve it and would only make the panel drift.
        if (Mathf.Abs(root.rect.height - guard.LastAppliedHeight) < .5f
            && Mathf.Abs(requiredGrowth - guard.LastMeasuredOverlap) < .5f)
            return;

        // Grow only by the overlap measured in the current, fully laid-out hierarchy. There is no
        // saved baseline to reset, so repeated refreshes and AIRLIFT's independent row cannot
        // accumulate competing size deltas. The screen bound makes the adjustment finite.
        float maximumHeight = Mathf.Max(root.rect.height, Screen.height - 32f);
        float growth = Mathf.Min(requiredGrowth,
            Mathf.Max(0f, maximumHeight - root.rect.height));
        if (growth < 1f) return;

        float oldTop = WorldTop(root);
        Vector2 size = root.sizeDelta;
        size.y += growth;
        root.sizeDelta = size;
        Canvas.ForceUpdateCanvases();
        Vector3 position = root.position;
        position.y += oldTop - WorldTop(root);
        root.position = position;
        guard.LastAppliedHeight = root.rect.height;
        guard.LastMeasuredOverlap = requiredGrowth;
    }

    private static float WorldTop(RectTransform rect)
    {
        var corners = new Vector3[4];
        rect.GetWorldCorners(corners);
        float result = corners[0].y;
        for (int index = 1; index < corners.Length; index++)
            result = Mathf.Max(result, corners[index].y);
        return result;
    }

    private static float WorldBottom(RectTransform rect)
    {
        var corners = new Vector3[4];
        rect.GetWorldCorners(corners);
        float result = corners[0].y;
        for (int index = 1; index < corners.Length; index++)
            result = Mathf.Min(result, corners[index].y);
        return result;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (ReferenceEquals(active, this)) active = null;
        harmony?.UnpatchSelf();
        harmony = null;

        PurchaseWingmenMarker[] markers =
            Resources.FindObjectsOfTypeAll<PurchaseWingmenMarker>();
        for (int index = 0; index < markers.Length; index++)
        {
            PurchaseWingmenMarker marker = markers[index];
            if (marker == null || !ReferenceEquals(marker.Owner, this)) continue;
            marker.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(marker.gameObject);
        }
    }

    private sealed class PurchaseWingmenMarker : MonoBehaviour
    {
        internal WingmanPurchaseDonateUi? Owner;
        internal Player? Player;
        internal ConvoyPurchaseOption? Option;
        internal Button? Button;
        internal TMP_Text? Label;
        internal ShowHoverText? Hover;
        internal float NextRefresh;

        private void Update()
        {
            if (Owner == null || Time.unscaledTime < NextRefresh) return;
            NextRefresh = Time.unscaledTime + RefreshIntervalSeconds;
            Owner.RefreshMarker(this);
        }
    }

    private sealed class DonateLayoutGuard : MonoBehaviour
    {
        internal float LastAppliedHeight = float.NaN;
        internal float LastMeasuredOverlap = float.NaN;
    }
}
