using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using NuclearOption.AddressableScripts;
using NuclearOption.Networking;
using UnityEngine;

namespace KellysTALONPublicUI;

[BepInPlugin(Guid, Name, LoaderVersion)]
public sealed partial class Plugin : BaseUnityPlugin
{
    public const string Guid = "kelly.nuclearoption.talon.publicui";
    public const string Name = "Kelly's TALON Public UI";
    public const string Version = "0.8.6-rc.3";
    public const string LoaderVersion = "0.8.6.3";

    private const int PurchaseOperation = 0;
    private const int CancelOperation = 1;
    private const int QuoteOperation = 2;
    private readonly PurchaseQuoteCache quote = new PurchaseQuoteCache();
    private FactionHQ? quoteHq;
    private bool purchaseProtocolMismatch;
    private const int WindowId = 846031;
    // Dedicated bit preserves map/chat/menu cursor ownership; never clear another surface's flag.
    private const CursorFlags MenuCursorFlag = (CursorFlags)(1 << 28);

    private readonly List<AircraftDefinition> aircraft = new List<AircraftDefinition>();
    private readonly List<TalonClientTransport.WingmanEntry> wingmen =
        new List<TalonClientTransport.WingmanEntry>();

    private ConfigEntry<bool> uiEnabled = null!;
    private ConfigEntry<KeyboardShortcut> menuKey = null!;
    private WingmanJoystickBindings? bindings;
    private WingmanJoystickAction? capture;
    private bool captureKeyboard, bindingsOnly;
    private bool flightSalvo;
    private float captureDeadline;
    private WingmanPurchaseDonateUi? donate;
    private TalonMfdUi? mfd;
    private Harmony? transportRegistration;
    private Rect window = new Rect(40f, 80f, 540f, 500f);
    private Vector2 windowScroll;
    private Vector2 aircraftScroll;
    private Vector2 presetScroll;
    private bool visible;
    private bool aircraftListVisible;
    private bool presetListVisible;
    private bool controlsVisible;
    private int selection;
    private int presetSelection;
    private readonly List<(LiveryKey key, string label)> liveryOptions = new List<(LiveryKey, string)>();
    private readonly List<(string id, string label)> liveries = new List<(string, string)> { ("", "Faction default") };
    private AircraftDefinition? liveryAircraft;
    private string liveryFaction = "";
    private int liverySelection;
    private bool liveryListVisible;
    private Vector2 liveryScroll;
    private int pending;
    private int cancellable;
    private float nextSnapshot;
    private float nextUiError;
    private int scope = PublicUiLogic.AllWingmen;
    private uint sessionId;
    private string status = "Waiting for a compatible Kelly's TALON server.";
    private string bindingStatus = "Choose KEY or BUTTON. Escape cancels capture; F7 closes the window.";

    private void Awake()
    {
        if (Application.isBatchMode)
        {
            Logger.LogInfo(Name + " is client-only and will remain inactive in batch mode.");
            return;
        }

        uiEnabled = Config.Bind("General", "Enable", true, "Show the Kelly's TALON client interface.");
        menuKey = Config.Bind("General", "ToggleMenu", new KeyboardShortcut(KeyCode.F7),
            "Open or close the Kelly's TALON purchase and command window.");
        bindings = new WingmanJoystickBindings(Config);
        TalonClientTransport.Bind(this);
        InstallTransportRegistration();
        donate = new WingmanPurchaseDonateUi(QueryDonateState, OpenFromDonate,
            text => Logger.LogInfo("[KellysTALONPublicUI] " + text));
        mfd = new TalonMfdUi(this, text => Logger.LogInfo("[TALON MFD] " + text));
        Logger.LogInfo(Name + " " + Version + " loaded; protocol " +
            TalonClientTransport.ProtocolVersion + ".");
    }

    private void Start()
    {
        if (Application.isBatchMode) return;
        NetworkManagerNuclearOption? manager = NetworkManagerNuclearOption.i;
        if (manager != null) TalonClientTransport.RegisterFor(manager);
    }

    private void Update()
    {
        if (Application.isBatchMode || uiEnabled == null) return;
        if (!uiEnabled.Value)
        {
            visible = false; CursorManager.SetFlag(MenuCursorFlag, false);
            mfd?.Tick(false); CancelCapture("interface disabled"); return;
        }
        bool opened = false;
        if (menuKey.Value.IsDown())
        {
            visible = !visible;
            aircraftListVisible = false;
            presetListVisible = false;
            if (visible) opened = true;
            else CancelCapture("window closed");
        }
        CursorManager.SetFlag(MenuCursorFlag, visible);
        // Optional scene UI must not prevent keyboard access to the standalone menu.
        try
        {
            if (opened) { mfd?.Close(); RefreshAircraft(); }
            HandleBindings();
            TalonClientTransport.PollLocalHost();
            if (Time.unscaledTime >= nextSnapshot && TalonClientTransport.Active?.Ready == true)
            { nextSnapshot = Time.unscaledTime + 1f; TalonClientTransport.Active.RequestFreshSnapshot(); }
            mfd?.Tick(true);
            donate?.Tick();
        }
        catch (Exception error)
        {
            if (Time.unscaledTime >= nextUiError)
            { nextUiError = Time.unscaledTime + 10f; Logger.LogWarning("Optional UI refresh: " + error); }
        }
    }

    private void OnGUI()
    {
        if (!visible || Application.isBatchMode || uiEnabled == null || !uiEnabled.Value) return;
        window.width = Mathf.Min(540f, Mathf.Max(430f, Screen.width - 20f));
        window.height = Mathf.Min(controlsVisible ? 720f : 570f, Mathf.Max(400f, Screen.height - 40f));
        window.x = Mathf.Clamp(window.x, 0f, Mathf.Max(0f, Screen.width - window.width));
        window.y = Mathf.Clamp(window.y, 0f, Mathf.Max(0f, Screen.height - window.height));
        window = GUI.Window(WindowId, window, DrawWindow, "KELLY'S TALON FLIGHT CONTROL");
    }

    private void DrawWindow(int _)
    {
        windowScroll = GUILayout.BeginScrollView(windowScroll);
        if (bindingsOnly)
        {
            GUILayout.Label("TALON / KEYBOARD & JOYSTICK BINDINGS");
            DrawBindings();
            if (GUILayout.Button("BACK TO PURCHASE & COMMANDS", GUILayout.Height(28f)))
            { CancelCapture("binding panel hidden"); bindingsOnly = false; windowScroll = Vector2.zero; }
            GUILayout.EndScrollView();
            GUI.DragWindow(new Rect(0f, 0f, window.width, 24f));
            return;
        }
        GUILayout.Label("TALON / REINFORCEMENTS");
        GUILayout.Label("Order before deployment or while flying. Up to three wingmen.");
        GUILayout.Space(6f);

        AircraftDefinition? selected = aircraft.Count == 0
            ? null
            : aircraft[Mathf.Clamp(selection, 0, aircraft.Count - 1)];
        string selectedText = selected == null
            ? "No compatible aircraft found"
            : DisplayName(selected) + "  |  airframe " + Money(PublicUiLogic.DisplayPrice(selected.value));
        if (GUILayout.Button(selectedText, GUILayout.Height(32f))) aircraftListVisible = !aircraftListVisible;
        if (aircraftListVisible)
        {
            aircraftScroll = GUILayout.BeginScrollView(aircraftScroll, GUILayout.Height(210f));
            for (int index = 0; index < aircraft.Count; index++)
            {
                AircraftDefinition option = aircraft[index];
                if (!GUILayout.Button(DisplayName(option) + "  |  airframe " +
                                      "airframe " + Money(PublicUiLogic.DisplayPrice(option.value)))) continue;
                selection = index;
                selected = option;
                presetSelection = 0;
                aircraftListVisible = false;
                presetListVisible = false;
            }
            GUILayout.EndScrollView();
        }

        IReadOnlyList<PublicLoadoutPreset> presets = selected == null
            ? Array.Empty<PublicLoadoutPreset>()
            : PublicUiLogic.PresetsFor(selected.jsonKey, selected.unitName, selected.name);
        PublicLoadoutPreset? selectedPreset = presets.Count == 0
            ? null
            : presets[Mathf.Clamp(presetSelection, 0, presets.Count - 1)];
        string presetText = selectedPreset.HasValue
            ? "LOADOUT: " + selectedPreset.Value.Label
            : "No verified loadout available";
        if (GUILayout.Button(presetText, GUILayout.Height(32f))) presetListVisible = !presetListVisible;
        if (selectedPreset.HasValue)
            GUILayout.Label(selectedPreset.Value.Summary);
        if (presetListVisible)
        {
            presetScroll = GUILayout.BeginScrollView(presetScroll, GUILayout.Height(132f));
            for (int index = 0; index < presets.Count; index++)
            {
                PublicLoadoutPreset option = presets[index];
                if (!GUILayout.Button(option.Label + "\n" + option.Summary, GUILayout.Height(46f))) continue;
                presetSelection = index;
                presetListVisible = false;
            }
            GUILayout.EndScrollView();
        }

        DrawLiveries(selected);
        GUILayout.Space(8f);
        float totalPrice = SelectedQuote(selected, selectedPreset?.Id);
        GUILayout.Label("AIRFRAME + WEAPONS: " + Money(totalPrice));
        if (!PublicUiLogic.Finite(totalPrice)) GUILayout.Label(quote.Reason);
        bool linkReady = TalonClientTransport.Active != null && TalonClientTransport.Active.Ready;
        GUI.enabled = linkReady && selected != null && selectedPreset.HasValue &&
                      wingmen.Count + pending < PublicUiLogic.MaximumWingmen &&
                      PublicUiLogic.Finite(totalPrice) && TryLocalPlayer(out Player buyer) &&
                      PublicUiLogic.Finite(buyer.Allocation) && buyer.Allocation >= totalPrice;
        if (GUILayout.Button("ORDER WINGMAN", GUILayout.Height(34f)) && selected != null && selectedPreset.HasValue)
            SendPurchase(PurchaseOperation, selected.jsonKey, selectedPreset.Value.Id);
        GUI.enabled = linkReady && cancellable > 0;
        if (cancellable > 0 && GUILayout.Button("CANCEL WAITING ORDER", GUILayout.Height(28f)))
            SendPurchase(CancelOperation, string.Empty, string.Empty);
        GUI.enabled = true;

        GUILayout.Space(8f);
        if (TryLocalPlayer(out Player player))
            GUILayout.Label("Available allocation: " + Money(player.Allocation));
        GUILayout.Label("Wingmen: " + wingmen.Count + " active + " + pending + " queued / " +
                        PublicUiLogic.MaximumWingmen);
        GUILayout.Label("Status: " + status);

        GUILayout.Space(6f);
        if (GUILayout.Button(controlsVisible ? "HIDE COMMANDS & BINDINGS" : "COMMANDS & BINDINGS",
                             GUILayout.Height(28f)))
        {
            controlsVisible = !controlsVisible;
            aircraftListVisible = false;
            presetListVisible = false;
            if (!controlsVisible) CancelCapture("binding panel hidden");
        }
        if (controlsVisible) DrawControls();
        GUILayout.FlexibleSpace();
        GUILayout.Label("F7 closes this window. Displayed prices are estimates; the server is authoritative.");
        GUILayout.EndScrollView();
        GUI.DragWindow(new Rect(0f, 0f, window.width, 24f));
    }

    private void DrawControls()
    {
        foreach (var member in wingmen)
            GUILayout.Label(PublicUiLogic.Callsign(sessionId, member.Ordinal) + "  |  " +
                PublicUiLogic.FlightStatusLabel(member.Status));
        GUILayout.Label("Command scope: " + ScopeLabel());
        if (GUILayout.Button("CYCLE SCOPE", GUILayout.Height(28f))) CycleScope();
        if (GUILayout.Button("CYCLE FORMATION (WHOLE FLIGHT)", GUILayout.Height(28f))) SendCommand(TalonCommand.CycleFormation);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("FORM UP", GUILayout.Height(28f))) SendCommand(TalonCommand.FormUp);
        if (GUILayout.Button("ATTACK MY TARGETS", GUILayout.Height(28f))) SendCommand(TalonCommand.AttackMyTargets);
        GUILayout.EndHorizontal();
        if (GUILayout.Button("ATTACK MODE: " + AttackModeLabel(), GUILayout.Height(28f))) CycleAttackMode();
        if (GUILayout.Button("ALPHA STRIKE — ALL COMPATIBLE MUNITIONS", GUILayout.Height(28f))) SendCommand(TalonCommand.FullStrike);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("HOLD HERE", GUILayout.Height(28f))) SendCommand(TalonCommand.HoldHere);
        if (GUILayout.Button("RETURN TO BASE", GUILayout.Height(28f))) SendCommand(TalonCommand.ReturnToBase);
        GUILayout.EndHorizontal();
        var jamState = ReadMfdState();
        if ((jamState.CanJam || jamState.Jamming) && GUILayout.Button(jamState.Jamming ? "STOP JAMMING" :
            "JAM SELECTED RADAR TARGETS / 60s", GUILayout.Height(28f)))
            SendCommand(TalonCommand.Jam);
        DrawBindings();
    }

    private void DrawBindings()
    {
        GUILayout.Label(bindingStatus);
        BindingRow(WingmanJoystickAction.CycleScope, "Cycle scope");
        BindingRow(WingmanJoystickAction.FormUp, "Form up");
        BindingRow(WingmanJoystickAction.AttackMyTargets, "Attack targets");
        BindingRow(WingmanJoystickAction.HoldHere, "Hold here");
        BindingRow(WingmanJoystickAction.ReturnToBase, "RTB");
        BindingRow(WingmanJoystickAction.CycleFormation, "Cycle formation");
        BindingRow(WingmanJoystickAction.Jam, "Jam / stop jamming");
        BindingRow(WingmanJoystickAction.FullStrike, "Alpha strike");
        BindingRow(WingmanJoystickAction.CycleAttackMode, "Toggle attack mode");
        GUILayout.Label("Bindings use raw Rewired button IDs and support high-numbered HOTAS buttons.");
    }

    private void BindingRow(WingmanJoystickAction action, string label)
    {
        GUILayout.Label(label + ": " + (bindings?.BindingLabel(action) ?? "UNBOUND"));
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("KEY")) BeginCapture(action, true);
        if (GUILayout.Button("BUTTON")) BeginCapture(action, false);
        if (GUILayout.Button("CLEAR KEY")) ClearBinding(action, true);
        if (GUILayout.Button("CLEAR BUTTON")) ClearBinding(action, false);
        GUILayout.EndHorizontal();
    }

    private void BeginCapture(WingmanJoystickAction action, bool keyboard)
    {
        capture = action; captureKeyboard = keyboard; captureDeadline = Time.unscaledTime + 30f;
        bindingStatus = "Press " + (keyboard ? "a key (optional Ctrl/Shift/Alt)" : "a joystick button") +
            " for " + ActionLabel(action) + ". Escape cancels.";
    }

    private void ClearBinding(WingmanJoystickAction action, bool keyboard)
    {
        CancelCapture("binding cleared");
        bool cleared = bindings != null && (keyboard ? bindings.ClearKeyboardBinding(action) : bindings.ClearJoystickBinding(action));
        bindingStatus = ActionLabel(action) + (keyboard ? " key" : " button") +
            (cleared ? " cleared." : " could not be saved; previous binding retained.");
    }

    private void HandleBindings()
    {
        if (bindings == null) return;
        if (capture.HasValue)
        {
            if (Input.GetKeyDown(KeyCode.Escape)) { CancelCapture("Escape"); return; }
            if (Time.unscaledTime >= captureDeadline) { CancelCapture("timed out"); return; }
            WingmanJoystickAction action = capture.Value;
            string label, reason;
            bool captured = captureKeyboard
                ? bindings.TryCaptureKeyboard(action, menuKey.Value.MainKey, out label, out reason)
                : bindings.TryCaptureNext(action, out label, out reason);
            if (captured)
            {
                capture = null;
                bindingStatus = ActionLabel(action) + " bound to " + label + ".";
                Report(bindingStatus);
            }
            else if (reason != "waiting")
                bindingStatus = "Binding " + ActionLabel(action) + ": " + PublicUiLogic.HumanReason(reason) + ".";
            return;
        }
        if (bindings.IsDown(WingmanJoystickAction.CycleScope)) { CycleScope(); return; }
        if (bindings.IsDown(WingmanJoystickAction.CycleFormation)) { SendCommand(TalonCommand.CycleFormation); return; }
        if (bindings.IsDown(WingmanJoystickAction.FormUp)) { SendCommand(TalonCommand.FormUp); return; }
        if (bindings.IsDown(WingmanJoystickAction.AttackMyTargets)) { SendCommand(TalonCommand.AttackMyTargets); return; }
        if (bindings.IsDown(WingmanJoystickAction.HoldHere)) { SendCommand(TalonCommand.HoldHere); return; }
        if (bindings.IsDown(WingmanJoystickAction.ReturnToBase)) { SendCommand(TalonCommand.ReturnToBase); return; }
        if (bindings.IsDown(WingmanJoystickAction.Jam)) { SendCommand(TalonCommand.Jam); return; }
        if (bindings.IsDown(WingmanJoystickAction.FullStrike)) { SendCommand(TalonCommand.FullStrike); return; }
        if (bindings.IsDown(WingmanJoystickAction.CycleAttackMode)) CycleAttackMode();
    }

    private void SendPurchase(int operation, string key, string presetId)
    {
        TalonClientTransport? link = TalonClientTransport.Active;
        string reason = "transport_unavailable";
        string livery = operation == PurchaseOperation ? liveries[Mathf.Clamp(liverySelection, 0, liveries.Count - 1)].id : "";
        if (link == null || !link.SendPurchase(operation, key, presetId, out reason, livery))
        {
            status = "Request rejected locally: " + PublicUiLogic.HumanReason(reason) + ".";
            return;
        }
        status = operation == CancelOperation ? "Cancellation sent to server." : "Purchase request sent to server.";
    }

    private void DrawLiveries(AircraftDefinition? selected)
    {
        string faction = TryLocalPlayer(out Player player) ? player.HQ?.faction?.factionName ?? "" : "";
        if (selected != liveryAircraft || faction != liveryFaction)
            RefreshLiveries(selected, faction);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("LIVERY: " + liveries[liverySelection].label, GUILayout.Height(30f)))
            liveryListVisible = !liveryListVisible;
        if (GUILayout.Button("REFRESH", GUILayout.Width(80f), GUILayout.Height(30f)))
            RefreshLiveries(selected, faction, true);
        GUILayout.EndHorizontal();
        if (!liveryListVisible) return;
        liveryScroll = GUILayout.BeginScrollView(liveryScroll, GUILayout.Height(132f));
        for (int index = 0; index < liveries.Count; index++)
            if (GUILayout.Button(liveries[index].label)) { liverySelection = index; liveryListVisible = false; }
        GUILayout.EndScrollView();
        GUILayout.Label("Subscribed, installed Workshop liveries for this aircraft/faction. Other players need the same livery to see it.");
    }

    private void RefreshLiveries(AircraftDefinition? selected, string faction, bool refreshCache = false)
    {
        string previous = selected == liveryAircraft && faction == liveryFaction ? liveries[liverySelection].id : "";
        liveryAircraft = selected;
        liveryFaction = faction;
        liveries.Clear();
        liveries.Add(("", "Faction default"));
        liverySelection = 0;
        if (selected == null) return;
        try
        {
            // Native enumeration validates aircraft compatibility and faction restrictions.
            if (refreshCache) ModLoadCache.HasSkinMetaData = false;
            LoadoutSelector.GetLiveryOptions(liveryOptions, selected, faction, true);
            var seen = new HashSet<ulong>();
            foreach (var option in liveryOptions)
            {
                if (option.key.Type != LiveryKey.KeyType.Workshop || !seen.Add(option.key.Id)) continue;
                uint state = Steamworks.SteamUGC.GetItemState(option.key.WorkshopId);
                if ((state & (uint)Steamworks.EItemState.k_EItemStateSubscribed) == 0) continue;
                liveries.Add(("workshop:" + option.key.Id.ToString(CultureInfo.InvariantCulture), option.label));
            }
            liverySelection = Math.Max(0, liveries.FindIndex(item => item.id == previous));
        }
        catch (Exception exception)
        {
            Logger.LogWarning("Livery catalogue unavailable: " + exception.GetType().Name);
        }
    }

    private void SendCommand(TalonCommand command)
    {
        if (command == TalonCommand.AttackMyTargets && flightSalvo) command = TalonCommand.FlightSalvo;
        TalonClientTransport? link = TalonClientTransport.Active;
        scope = PublicUiLogic.NormalizeScope(scope, wingmen.Count);
        uint selectedPid = scope == PublicUiLogic.AllWingmen ? 0u : wingmen[scope].Pid;
        if (command == TalonCommand.CycleFormation) selectedPid = 0u;
        string reason = "transport_unavailable";
        if (link == null || !link.SendQuick(command, selectedPid, out reason))
        {
            string failure = CommandLabel(command) + " rejected locally: " + PublicUiLogic.HumanReason(reason) + ".";
            status = failure;
            Report(failure);
            return;
        }
        string sent = CommandLabel(command) + " sent for " + ScopeLabel() + ".";
        status = sent;
        Report(sent);
    }

    private void CycleScope()
    {
        scope = PublicUiLogic.NextScope(scope, wingmen.Count);
        string value = "Command scope: " + ScopeLabel() + ".";
        status = value;
        Report(value);
    }

    internal string AttackModeLabel() => flightSalvo ? "FLIGHT SALVO" : "DISTRIBUTED";
    internal void CycleAttackMode()
    {
        flightSalvo = !flightSalvo;
        status = "ATTACK MODE: " + AttackModeLabel() + (flightSalvo
            ? " — one munition per aircraft per target." : " — one munition per target across the flight.");
        Report(status);
    }

    private string ScopeLabel()
    {
        int normalized = PublicUiLogic.NormalizeScope(scope, wingmen.Count);
        return normalized == PublicUiLogic.AllWingmen
            ? "ALL (" + wingmen.Count + ")"
            : PublicUiLogic.Callsign(sessionId, wingmen[normalized].Ordinal);
    }

    internal void ReceivePurchaseResult(int protocol, int operation, string key, string presetId, bool accepted,
                                        float charge, string reason)
    {
        if (protocol != TalonClientTransport.ProtocolVersion)
        {
            pending = 0;
            quote.Reset();
            purchaseProtocolMismatch = true;
            status = "Server protocol mismatch.";
            return;
        }
        if (operation == QuoteOperation)
        {
            quote.Receive(key, presetId, accepted, charge, reason, Time.unscaledTime);
            return;
        }
        if (accepted && operation == PurchaseOperation && reason == "queued")
            pending = Math.Min(PublicUiLogic.MaximumWingmen, pending + 1);
        else if (accepted && operation == PurchaseOperation && reason == "attached")
            pending = Math.Max(0, pending - 1);
        else if (accepted && operation == CancelOperation)
            pending = Math.Max(0, pending - 1);
        else if (!accepted && reason == "nothing_queued")
            pending = 0;

        status = operation == CancelOperation
            ? accepted ? "Server refunded " + Money(charge) + " (" + PublicUiLogic.HumanReason(reason) + ")."
                       : "Cancellation rejected: " + PublicUiLogic.HumanReason(reason) + "."
            : accepted && reason == "attached" ? "Purchased wingman is airborne and attached."
            : accepted ? "Order accepted at " + Money(charge) + ". Dispatching from a friendly base if deployed."
                       : "Purchase rejected: " + PublicUiLogic.HumanReason(reason) + ".";
        Report(status);
    }

    internal void ReceiveQuickResult(int protocol, int command, uint _, bool accepted, int affected, string reason)
    {
        string label = Enum.IsDefined(typeof(TalonCommand), command) ? CommandLabel((TalonCommand)command) : "COMMAND";
        status = protocol != TalonClientTransport.ProtocolVersion ? "Server protocol mismatch."
            : accepted ? command == (int)TalonCommand.CycleFormation ? "Formation: " + reason + "."
                : label + " accepted for " + affected + " aircraft."
            : label + " rejected: " + PublicUiLogic.HumanReason(reason) + ".";
        Report(status);
    }

    internal void ReceiveModeResult(int protocol, uint _, uint __, int ___, bool accepted, string reason)
    {
        if (protocol != TalonClientTransport.ProtocolVersion || !accepted)
            Logger.LogWarning("TALON mode response: " + PublicUiLogic.HumanReason(reason));
    }

    internal void ReceiveSnapshot(int protocol, uint receivedSession,
                                  IReadOnlyList<TalonClientTransport.WingmanEntry> received, string reason)
    {
        bool changedSession = sessionId != receivedSession;
        wingmen.Clear();
        if (protocol == TalonClientTransport.ProtocolVersion && receivedSession != 0)
        {
            sessionId = receivedSession;
            wingmen.AddRange(received);
            scope = PublicUiLogic.NormalizeScope(scope, wingmen.Count);
            if (changedSession)
                status = wingmen.Count == 0 ? "Connected; no active wingmen." : "Connected to " +
                    PublicUiLogic.Callsign(sessionId, 0) + " flight.";
        }
        else
        {
            sessionId = 0;
            scope = PublicUiLogic.AllWingmen;
            if (changedSession || reason != "session_not_ready")
                status = reason == "session_not_ready" ? "Connected; order reinforcements at any time."
                    : "Snapshot unavailable: " + PublicUiLogic.HumanReason(reason) + ".";
        }
    }

    internal void TransportStateChanged(bool connected)
    {
        purchaseProtocolMismatch = false;
        quote.Reset();
        if (!connected)
        {
            pending = 0;
            cancellable = 0;
            sessionId = 0;
            wingmen.Clear();
            scope = PublicUiLogic.AllWingmen;
        }
        status = connected ? "Connected to Kelly's TALON server." : "Waiting for a compatible Kelly's TALON server.";
    }

    internal void ReceiveQueueSnapshot(int count, int canCancel)
    {
        pending = Math.Max(0, Math.Min(PublicUiLogic.MaximumWingmen, count));
        cancellable = Math.Max(0, Math.Min(pending, canCancel));
    }

    private WingmanPurchaseDonateState QueryDonateState(Player player)
    {
        RefreshAircraft();
        float minimum = float.NaN; // A preset is required for a server-authoritative total.
        bool local = player != null && player.IsLocalPlayer;
        bool link = TalonClientTransport.Active != null && TalonClientTransport.Active.Ready;
        bool capacity = wingmen.Count + pending < PublicUiLogic.MaximumWingmen;
        string detail = !local ? "A local player is required." : !link ? "Waiting for Kelly's TALON server."
            : aircraft.Count == 0 ? "No compatible faction aircraft found." : !capacity ? "Wingman limit reached."
            : "Choose a preset in TALON for its airframe + weapons price.";
        return new WingmanPurchaseDonateState(local && link && aircraft.Count > 0 && capacity, minimum, detail);
    }

    private void OpenFromDonate(Player _)
    {
        visible = true;
        aircraftListVisible = false;
        presetListVisible = false;
        RefreshAircraft();
    }

    private void RefreshAircraft()
    {
        var candidates = new Dictionary<string, AircraftDefinition>(StringComparer.Ordinal);
        try
        {
            if (Encyclopedia.i?.aircraft != null)
                foreach (AircraftDefinition definition in Encyclopedia.i.aircraft) AddCandidate(candidates, definition);
            if (Encyclopedia.Lookup != null)
                foreach (UnitDefinition definition in Encyclopedia.Lookup.Values)
                    if (definition is AircraftDefinition aircraftDefinition) AddCandidate(candidates, aircraftDefinition);
        }
        catch (Exception exception)
        {
            Logger.LogWarning("Aircraft catalogue refresh failed: " + exception.GetType().Name);
        }
        string priorKey = aircraft.Count == 0 || selection < 0 || selection >= aircraft.Count
            ? string.Empty : aircraft[selection].jsonKey;
        aircraft.Clear();
        FactionHQ? hq = TryLocalPlayer(out Player player) ? player.HQ : null;
        foreach (AircraftDefinition definition in candidates.Values)
            if (ClientVisible(definition, hq)) aircraft.Add(definition);
        aircraft.Sort((left, right) => StringComparer.OrdinalIgnoreCase.Compare(DisplayName(left), DisplayName(right)));
        int matched = aircraft.FindIndex(item => item.jsonKey == priorKey);
        selection = Math.Max(0, matched);
        if (matched < 0) presetSelection = 0;
    }

    private static void AddCandidate(Dictionary<string, AircraftDefinition> values, AircraftDefinition? definition)
    {
        if (definition == null || string.IsNullOrWhiteSpace(definition.jsonKey) || values.ContainsKey(definition.jsonKey)) return;
        values.Add(definition.jsonKey, definition);
    }

    private static bool ClientVisible(AircraftDefinition definition, FactionHQ? hq)
    {
        if (definition == null || definition.unitPrefab == null || definition.aircraftParameters == null ||
            !PublicUiLogic.AllowedAircraft(definition.jsonKey, definition.unitName, definition.name) ||
            PublicUiLogic.PresetsFor(definition.jsonKey, definition.unitName, definition.name).Count == 0 ||
            !PublicUiLogic.Finite(definition.value) ||
            definition.value < 0f || hq?.faction == null) return false;
        try
        {
            int livery = definition.aircraftParameters.GetFirstLiveryForFaction(hq.faction);
            if (definition.aircraftParameters.liveries == null || livery < 0 ||
                livery >= definition.aircraftParameters.liveries.Count) return false;
            Aircraft? prefab = definition.unitPrefab.GetComponent<Aircraft>();
            if (prefab == null) return false;
            return true; // The server owns exact catalogue and live pilot admission.
        }
        catch { return false; }
    }

    private void InstallTransportRegistration()
    {
        try
        {
            var target = AccessTools.Method(typeof(NetworkManagerNuclearOption),
                nameof(NetworkManagerNuclearOption.RegisterPrefabs));
            var callback = AccessTools.Method(typeof(Plugin), nameof(RegisterTransportPrefab));
            if (target == null || callback == null) throw new MissingMethodException("RegisterPrefabs");
            transportRegistration = new Harmony(Guid + ".transport_registration");
            transportRegistration.Patch(target, postfix: new HarmonyMethod(callback));
        }
        catch (Exception exception)
        {
            Logger.LogError("TALON transport registration unavailable: " + exception);
        }
    }

    private static void RegisterTransportPrefab(NetworkManagerNuclearOption __instance) =>
        TalonClientTransport.RegisterFor(__instance);

    private void CancelCapture(string reason)
    {
        if (!capture.HasValue) return;
        capture = null;
        bindingStatus = "Binding cancelled (" + reason + ").";
    }

    private static bool TryLocalPlayer(out Player player)
    {
        player = null!;
        return GameManager.GetLocalPlayer(out player) && player != null && player.IsLocalPlayer;
    }

    private static string DisplayName(AircraftDefinition definition) =>
        string.IsNullOrWhiteSpace(definition.unitName) ? definition.jsonKey : definition.unitName;

    private static string Money(float value) => PublicUiLogic.Finite(value)
        ? "$" + value.ToString("0.##", CultureInfo.InvariantCulture) + "M" : "unknown";

    private static string CommandLabel(TalonCommand command) => command switch
    {
        TalonCommand.FormUp => "FORM UP",
        TalonCommand.AttackMyTargets => "ATTACK MY TARGETS",
        TalonCommand.HoldHere => "HOLD HERE",
        TalonCommand.ReturnToBase => "RETURN TO BASE",
        TalonCommand.CycleFormation => "CYCLE FORMATION",
        TalonCommand.Jam => "JAM",
        TalonCommand.FullStrike => "ALPHA STRIKE",
        TalonCommand.FlightSalvo => "FLIGHT SALVO",
        _ => "COMMAND"
    };

    private static string ActionLabel(WingmanJoystickAction action) => action switch
    {
        WingmanJoystickAction.CycleScope => "CYCLE SCOPE",
        WingmanJoystickAction.FormUp => "FORM UP",
        WingmanJoystickAction.AttackMyTargets => "ATTACK MY TARGETS",
        WingmanJoystickAction.HoldHere => "HOLD HERE",
        WingmanJoystickAction.ReturnToBase => "RETURN TO BASE",
        WingmanJoystickAction.CycleFormation => "CYCLE FORMATION",
        WingmanJoystickAction.Jam => "JAM / STOP JAMMING",
        WingmanJoystickAction.FullStrike => "ALPHA STRIKE",
        WingmanJoystickAction.CycleAttackMode => "TOGGLE ATTACK MODE",
        _ => "CONTROL"
    };

    private static void Report(string value)
    {
        try { AircraftActionsReport.i?.ReportText("[TALON] " + value, 5f); } catch { }
    }

    private void OnDestroy()
    {
        if (!Application.isBatchMode) CursorManager.SetFlag(MenuCursorFlag, false);
        mfd?.Dispose();
        mfd = null;
        donate?.Dispose();
        donate = null;
        transportRegistration?.UnpatchSelf();
        transportRegistration = null;
        TalonClientTransport.Shutdown();
    }
}
