using System;
using System.Collections.Generic;
using NuclearOption.Networking;
using UnityEngine;

namespace KellysTALONPublicUI;

public sealed partial class Plugin
{
    internal sealed class MfdState
    {
        internal string Aircraft = "", Preset = "", Stores = "", Livery = "", Allocation = "",
            Capacity = "", Status = "", Scope = "", Gate = "", AttackMode = "";
        internal readonly List<string> Roster = new List<string>();
        internal readonly List<string> RosterStatuses = new List<string>();
        internal bool CanPurchase, CanCancel, CanCommand, CanJam, Jamming;
    }

    private float SelectedQuote(AircraftDefinition? selected, string? presetId)
    {
        if (purchaseProtocolMismatch || selected == null || string.IsNullOrEmpty(presetId) || !TryLocalPlayer(out Player player) ||
            player.HQ == null || TalonClientTransport.Active?.Ready != true)
        { quote.Reset(); return float.PositiveInfinity; }
        if (quoteHq != player.HQ) { quote.Reset(); quoteHq = player.HQ; }
        if (quote.RequestDue(selected.jsonKey, presetId!, Time.unscaledTime))
            TalonClientTransport.Active.SendPurchase(QuoteOperation, selected.jsonKey, presetId, out _);
        return quote.Price(Time.unscaledTime);
    }

    internal MfdState ReadMfdState()
    {
        RefreshAircraft();
        AircraftDefinition? selected = aircraft.Count == 0 ? null : aircraft[selection];
        var presets = selected == null ? Array.Empty<PublicLoadoutPreset>() :
            PublicUiLogic.PresetsFor(selected.jsonKey, selected.unitName, selected.name);
        presetSelection = Mathf.Clamp(presetSelection, 0, Math.Max(0, presets.Count - 1));
        bool local = TryLocalPlayer(out Player player);
        string faction = local ? player.HQ?.faction?.factionName ?? "" : "";
        if (selected != liveryAircraft || faction != liveryFaction) RefreshLiveries(selected, faction);
        bool ready = TalonClientTransport.Active?.Ready == true;
        float price = SelectedQuote(selected, presets.Count == 0 ? null : presets[presetSelection].Id);
        string gate = purchaseProtocolMismatch ? "Update both TALON server and UI: protocol mismatch." : !ready ? "Waiting for a compatible TALON server." : !local ? "Join a faction to purchase."
            : selected == null || presets.Count == 0 ? "No compatible aircraft available for your faction."
            : wingmen.Count + pending >= PublicUiLogic.MaximumWingmen ? "Flight at capacity. Await dispatch or release a wingman."
            : !PublicUiLogic.Finite(price) ? quote.Reason
            : !PublicUiLogic.Finite(player.Allocation) || player.Allocation < price ? "Insufficient allocation for airframe + weapons."
            : "Order anytime. A friendly base dispatches reinforcements while you fly.";
        var result = new MfdState
        {
            Aircraft = selected == null ? "NO AIRCRAFT AVAILABLE" : DisplayName(selected),
            Preset = presets.Count == 0 ? "NO VERIFIED LOADOUT" : presets[presetSelection].Label,
            Stores = presets.Count == 0 ? "Join a faction and check that the aircraft add-ons are installed." : presets[presetSelection].Summary,
            Livery = liveries[liverySelection].label,
            Allocation = "ALLOCATION  " + (local ? Money(player.Allocation) : "--") + "     TOTAL  " + Money(price),
            Capacity = wingmen.Count + " ACTIVE  +  " + pending + " QUEUED  /  " + PublicUiLogic.MaximumWingmen,
            Scope = ScopeLabel(), Status = status, Gate = gate, AttackMode = AttackModeLabel(),
            CanPurchase = ready && local && selected != null && presets.Count > 0 &&
                wingmen.Count + pending < PublicUiLogic.MaximumWingmen &&
                PublicUiLogic.Finite(price) && PublicUiLogic.Finite(player.Allocation) && player.Allocation >= price,
            CanCancel = ready && cancellable > 0, CanCommand = ready && wingmen.Count > 0
        };
        int normalizedScope = PublicUiLogic.NormalizeScope(scope, wingmen.Count);
        for (int index = 0; index < wingmen.Count; index++)
        {
            var entry = wingmen[index];
            result.Roster.Add(PublicUiLogic.Callsign(sessionId, entry.Ordinal));
            result.RosterStatuses.Add(PublicUiLogic.FlightStatusLabel(entry.Status));
            if (normalizedScope == PublicUiLogic.AllWingmen || normalizedScope == index)
            { result.CanJam |= ready && entry.CanJam; result.Jamming |= entry.Jamming; }
        }
        return result;
    }

    internal void MfdAircraft(int direction)
    {
        RefreshAircraft();
        selection = WrapSelection(selection, direction, aircraft.Count);
        presetSelection = 0;
    }

    internal void MfdPreset(int direction)
    {
        ReadMfdState();
        if (aircraft.Count == 0) return;
        var selected = aircraft[selection];
        presetSelection = WrapSelection(presetSelection, direction,
            PublicUiLogic.PresetsFor(selected.jsonKey, selected.unitName, selected.name).Count);
    }

    internal void MfdLivery(int direction)
    {
        ReadMfdState();
        liverySelection = WrapSelection(liverySelection, direction, liveries.Count);
    }

    internal void MfdRefreshLiveries()
    {
        ReadMfdState();
        RefreshLiveries(liveryAircraft, liveryFaction, true);
    }

    internal void MfdPurchase()
    {
        if (!ReadMfdState().CanPurchase) return;
        var selected = aircraft[selection];
        SendPurchase(PurchaseOperation, selected.jsonKey,
            PublicUiLogic.PresetsFor(selected.jsonKey, selected.unitName, selected.name)[presetSelection].Id);
    }

    internal void MfdCancel() { if (ReadMfdState().CanCancel) SendPurchase(CancelOperation, "", ""); }
    internal void MfdCommand(TalonCommand command) { if (ReadMfdState().CanCommand) SendCommand(command); }
    internal void MfdScope() => CycleScope();
    internal void MfdOpened() { visible = false; CursorManager.SetFlag(MenuCursorFlag, false); CancelCapture("MFD opened"); }
    internal void MfdBindings()
    {
        visible = true; controlsVisible = true; bindingsOnly = true; windowScroll = Vector2.zero;
        CancelCapture("binding window opened");
        CursorManager.SetFlag(MenuCursorFlag, true);
        // The overlay canvas otherwise stays above the standalone IMGUI controls.
        try { mfd?.Close(); }
        catch (Exception error) { Logger.LogWarning("MFD close while opening bindings: " + error.Message); }
    }
    private static int WrapSelection(int current, int direction, int count) =>
        count == 0 ? 0 : (current + direction % count + count) % count;
}
