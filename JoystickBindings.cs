using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using Rewired;
using UnityEngine;

namespace KellysTALONPublicUI;

internal enum WingmanJoystickAction
{
    CycleScope,
    FormUp,
    AttackMyTargets,
    HoldHere,
    ReturnToBase,
    CycleFormation,
    Jam,
    FullStrike,
    CycleAttackMode
}

// Raw Rewired bindings deliberately sit beside KeyboardShortcut rather than replacing it. Unity KeyCode only
// exposes the first twenty joystick buttons; Rewired element IDs preserve high-button-count flight controls.
internal sealed class WingmanJoystickBindings
{
    private const string Section = "Wingman Quick Controls";
    private readonly ConfigFile config;
    private readonly Dictionary<WingmanJoystickAction, Binding> bindings =
        new Dictionary<WingmanJoystickAction, Binding>();

    internal WingmanJoystickBindings(ConfigFile config)
    {
        this.config = config ?? throw new ArgumentNullException(nameof(config));
        Add(WingmanJoystickAction.CycleScope, "CycleScope", "Cycle command scope: all, wingman 1, wingman 2, wingman 3, all.");
        Add(WingmanJoystickAction.FormUp, "FormUp", "Order the selected wingmen to form up.");
        Add(WingmanJoystickAction.AttackMyTargets, "AttackMyTargets", "Order the selected wingmen to attack the player's target.");
        Add(WingmanJoystickAction.HoldHere, "HoldHere", "Order the selected wingmen to hold here.");
        Add(WingmanJoystickAction.ReturnToBase, "ReturnToBase", "Order the selected wingmen to return to base.");
        Add(WingmanJoystickAction.CycleFormation, "CycleFormation", "Cycle the entire flight's formation shape.");
        Add(WingmanJoystickAction.Jam, "Jam", "Toggle selected Medusas' fitted radar-jamming pods against selected targets.");
        Add(WingmanJoystickAction.FullStrike, "FullStrike", "Expend fitted compatible non-cannon munitions across selected hostile targets.");
        Add(WingmanJoystickAction.CycleAttackMode, "CycleAttackMode", "Toggle normal attack between distributed targets and one munition per aircraft per target.");
    }

    private static readonly KeyCode[] Keys = (KeyCode[])Enum.GetValues(typeof(KeyCode));
    private static readonly KeyCode[] Modifiers = { KeyCode.LeftControl, KeyCode.RightControl,
        KeyCode.LeftShift, KeyCode.RightShift, KeyCode.LeftAlt, KeyCode.RightAlt,
        KeyCode.LeftCommand, KeyCode.RightCommand, KeyCode.LeftWindows, KeyCode.RightWindows };

    internal bool TryCaptureKeyboard(WingmanJoystickAction action, KeyCode menuKey,
                                    out string label, out string reason)
    {
        label = "";
        reason = "waiting";
        if (!bindings.TryGetValue(action, out Binding? binding)) { reason = "unknown_action"; return false; }
        KeyCode main = KeyCode.None;
        foreach (KeyCode key in Keys)
        {
            // Ignore mouse clicks, Unity joystick aliases and modifier-only presses.
            if (key == KeyCode.None || (int)key >= (int)KeyCode.Mouse0 ||
                Array.IndexOf(Modifiers, key) >= 0 || !Input.GetKeyDown(key)) continue;
            if (main == key) continue; // Enum aliases are the same physical key.
            if (main != KeyCode.None) { reason = "multiple_keys_pressed"; return false; }
            main = key;
        }
        if (main == KeyCode.None) return false;
        if (main == KeyCode.Escape || main == menuKey) { reason = "reserved_menu_key"; return false; }
        var held = new List<KeyCode>();
        foreach (KeyCode modifier in Modifiers)
            if (Input.GetKey(modifier) && !held.Contains(modifier)) held.Add(modifier);
        var shortcut = new KeyboardShortcut(main, held.ToArray());
        foreach (var pair in bindings)
            if (pair.Key != action && pair.Value.keyboard.Value.Equals(shortcut))
            { reason = "key_already_bound_to_" + pair.Key; return false; }
        KeyboardShortcut previous = binding.keyboard.Value;
        try
        {
            binding.keyboard.Value = shortcut;
            config.Save();
            label = shortcut.ToString(); reason = ""; return true;
        }
        catch
        {
            try { binding.keyboard.Value = previous; config.Save(); } catch { }
            reason = "save_failed"; return false;
        }
    }

    internal bool ClearKeyboardBinding(WingmanJoystickAction action)
    {
        if (!bindings.TryGetValue(action, out Binding? binding)) return false;
        KeyboardShortcut previous = binding.keyboard.Value;
        try { binding.keyboard.Value = new KeyboardShortcut(KeyCode.None); config.Save(); return true; }
        catch { try { binding.keyboard.Value = previous; config.Save(); } catch { } return false; }
    }

    internal bool IsDown(WingmanJoystickAction action)
    {
        if (!bindings.TryGetValue(action, out Binding? binding))
            return false;
        try
        {
            if (binding.keyboard.Value.IsDown())
                return true;
        }
        catch
        {
        }
        return TryResolve(binding, out Joystick? joystick) && ButtonDown(joystick, binding.elementIdentifierId.Value);
    }

    // Call once per frame while a bind prompt is armed. GetButtonDownById reports a transition, so an already-held
    // button is not captured. If more than one new press is seen in the same frame, nothing is saved and the caller
    // can keep waiting.
    internal bool TryCaptureNext(WingmanJoystickAction action, out string label, out string reason)
    {
        label = "";
        if (!bindings.TryGetValue(action, out Binding? binding))
        {
            reason = "unknown_action";
            return false;
        }
        if (!TryGetJoysticks(out IList<Joystick>? joysticks))
        {
            reason = "rewired_unavailable";
            return false;
        }

        Joystick? capturedJoystick = null;
        ControllerElementIdentifier? capturedElement = null;
        try
        {
            foreach (Joystick joystick in joysticks!)
            {
                if (!Usable(joystick))
                    continue;
                IList<ControllerElementIdentifier>? elements = joystick.ButtonElementIdentifiers;
                if (elements == null)
                    continue;
                foreach (ControllerElementIdentifier element in elements)
                {
                    if (element == null || !ButtonDown(joystick, element.id))
                        continue;
                    if (capturedJoystick != null)
                    {
                        reason = "multiple_buttons_pressed";
                        return false;
                    }
                    capturedJoystick = joystick;
                    capturedElement = element;
                }
            }
        }
        catch
        {
            reason = "rewired_unavailable";
            return false;
        }

        if (capturedJoystick == null || capturedElement == null)
        {
            reason = "waiting";
            return false;
        }
        if (Conflicts(action, capturedJoystick, capturedElement.id, out WingmanJoystickAction existingAction))
        {
            reason = "button_already_bound_to_" + existingAction;
            return false;
        }

        string oldDeviceGuid = binding.deviceInstanceGuid.Value;
        string oldHardware = binding.hardwareIdentifier.Value;
        int oldElementId = binding.elementIdentifierId.Value;
        try
        {
            binding.deviceInstanceGuid.Value = capturedJoystick.deviceInstanceGuid.ToString("D");
            binding.hardwareIdentifier.Value = capturedJoystick.hardwareIdentifier ?? "";
            binding.elementIdentifierId.Value = capturedElement.id;
            config.Save();
            label = RawLabel(capturedJoystick, capturedElement);
            reason = "";
            return true;
        }
        catch
        {
            Restore(binding, oldDeviceGuid, oldHardware, oldElementId);
            label = "";
            reason = "save_failed";
            return false;
        }
    }

    internal bool ClearJoystickBinding(WingmanJoystickAction action)
    {
        if (!bindings.TryGetValue(action, out Binding? binding))
            return false;
        string oldDeviceGuid = binding.deviceInstanceGuid.Value;
        string oldHardware = binding.hardwareIdentifier.Value;
        int oldElementId = binding.elementIdentifierId.Value;
        try
        {
            binding.deviceInstanceGuid.Value = "";
            binding.hardwareIdentifier.Value = "";
            binding.elementIdentifierId.Value = -1;
            config.Save();
            return true;
        }
        catch
        {
            Restore(binding, oldDeviceGuid, oldHardware, oldElementId);
            return false;
        }
    }

    internal bool HasJoystickBinding(WingmanJoystickAction action) =>
        bindings.TryGetValue(action, out Binding? binding) && Configured(binding);

    internal string KeyboardLabel(WingmanJoystickAction action)
    {
        if (!bindings.TryGetValue(action, out Binding? binding))
            return "UNBOUND";
        try
        {
            KeyboardShortcut value = binding.keyboard.Value;
            return value.MainKey == KeyCode.None ? "UNBOUND" : value.ToString();
        }
        catch
        {
            return "UNBOUND";
        }
    }

    internal string JoystickLabel(WingmanJoystickAction action)
    {
        if (!bindings.TryGetValue(action, out Binding? binding) || !Configured(binding))
            return "UNBOUND";
        if (TryResolve(binding, out Joystick? joystick))
        {
            try
            {
                ControllerElementIdentifier? element = joystick!.GetElementIdentifierById(binding.elementIdentifierId.Value);
                if (element != null)
                    return RawLabel(joystick, element);
            }
            catch
            {
            }
        }
        string hardware = binding.hardwareIdentifier.Value;
        if (string.IsNullOrWhiteSpace(hardware))
            hardware = "JOYSTICK";
        return hardware.Trim() + " · BUTTON " + binding.elementIdentifierId.Value + " (OFFLINE)";
    }

    internal string BindingLabel(WingmanJoystickAction action)
    {
        string keyboard = KeyboardLabel(action), joystick = JoystickLabel(action);
        if (keyboard == "UNBOUND") return joystick;
        if (joystick == "UNBOUND") return keyboard;
        return keyboard + " / " + joystick;
    }

    private void Add(WingmanJoystickAction action, string key, string description)
    {
        bindings.Add(action, new Binding(
            config.Bind(Section, key + "Key", new KeyboardShortcut(KeyCode.None),
                        description + " Keyboard or Unity joystick-button fallback."),
            config.Bind(Section, key + "JoystickDeviceInstanceGuid", "",
                        "Raw Rewired joystick device instance GUID. Set through the Kelly's TALON bind UI."),
            config.Bind(Section, key + "JoystickHardwareIdentifier", "",
                        "Raw Rewired hardware identifier used only when the saved device instance GUID is unavailable."),
            config.Bind(Section, key + "JoystickElementIdentifierId", -1,
                        "Raw Rewired button element identifier. -1 disables the raw joystick binding.")));
    }

    private static bool Configured(Binding binding) => binding.elementIdentifierId.Value >= 0 &&
        (!string.IsNullOrWhiteSpace(binding.deviceInstanceGuid.Value) ||
         !string.IsNullOrWhiteSpace(binding.hardwareIdentifier.Value));

    private bool Conflicts(WingmanJoystickAction requested, Joystick joystick, int elementId,
                           out WingmanJoystickAction existingAction)
    {
        existingAction = default;
        Guid capturedGuid = joystick.deviceInstanceGuid;
        string capturedHardware = joystick.hardwareIdentifier?.Trim() ?? "";
        foreach (KeyValuePair<WingmanJoystickAction, Binding> pair in bindings)
        {
            if (pair.Key == requested || !Configured(pair.Value) ||
                pair.Value.elementIdentifierId.Value != elementId) continue;
            bool sameDevice = Guid.TryParse(pair.Value.deviceInstanceGuid.Value, out Guid savedGuid) &&
                              savedGuid != Guid.Empty
                ? savedGuid == capturedGuid
                : capturedHardware.Length > 0 && string.Equals(pair.Value.hardwareIdentifier.Value?.Trim(),
                    capturedHardware, StringComparison.OrdinalIgnoreCase);
            if (!sameDevice) continue;
            existingAction = pair.Key;
            return true;
        }
        return false;
    }

    private static bool TryResolve(Binding binding, out Joystick? result)
    {
        result = null;
        if (!Configured(binding) || !TryGetJoysticks(out IList<Joystick>? joysticks))
            return false;
        try
        {
            if (Guid.TryParse(binding.deviceInstanceGuid.Value, out Guid deviceGuid) && deviceGuid != Guid.Empty)
            {
                foreach (Joystick candidate in joysticks!)
                    if (Usable(candidate) && candidate.deviceInstanceGuid == deviceGuid)
                    {
                        if (result != null)
                        {
                            result = null;
                            return false;
                        }
                        result = candidate;
                    }
                if (result != null)
                    return true;
            }

            string hardware = binding.hardwareIdentifier.Value?.Trim() ?? "";
            if (hardware.Length == 0)
                return false;
            foreach (Joystick candidate in joysticks!)
                if (Usable(candidate) && string.Equals(candidate.hardwareIdentifier, hardware,
                                                        StringComparison.OrdinalIgnoreCase))
                {
                    if (result != null)
                    {
                        result = null;
                        return false;
                    }
                    result = candidate;
                }
            return result != null;
        }
        catch
        {
            result = null;
            return false;
        }
    }

    private static bool TryGetJoysticks(out IList<Joystick>? joysticks)
    {
        joysticks = null;
        try
        {
            Player? player = GameManager.playerInput;
            if (player == null || player.controllers == null)
                return false;
            joysticks = player.controllers.Joysticks;
            return joysticks != null;
        }
        catch
        {
            joysticks = null;
            return false;
        }
    }

    private static bool Usable(Joystick? joystick)
    {
        try { return joystick != null && joystick.enabled && joystick.isConnected; }
        catch { return false; }
    }

    private static bool ButtonDown(Joystick? joystick, int elementIdentifierId)
    {
        if (!Usable(joystick) || elementIdentifierId < 0)
            return false;
        try { return joystick!.GetButtonDownById(elementIdentifierId); }
        catch { return false; }
    }

    private static string RawLabel(Joystick joystick, ControllerElementIdentifier element)
    {
        string hardware = !string.IsNullOrWhiteSpace(joystick.hardwareIdentifier)
            ? joystick.hardwareIdentifier.Trim()
            : !string.IsNullOrWhiteSpace(joystick.name) ? joystick.name.Trim() : "JOYSTICK";
        string button = !string.IsNullOrWhiteSpace(element.name)
            ? element.name.Trim()
            : "BUTTON " + element.id;
        return hardware + " · " + button;
    }

    private void Restore(Binding binding, string deviceGuid, string hardware, int elementId)
    {
        try
        {
            binding.deviceInstanceGuid.Value = deviceGuid;
            binding.hardwareIdentifier.Value = hardware;
            binding.elementIdentifierId.Value = elementId;
            config.Save();
        }
        catch
        {
            // Configuration writes are best-effort here. The polling path still validates every value and
            // fails closed when no unique, assigned Rewired controller can be resolved.
        }
    }

    private sealed class Binding
    {
        internal readonly ConfigEntry<KeyboardShortcut> keyboard;
        internal readonly ConfigEntry<string> deviceInstanceGuid;
        internal readonly ConfigEntry<string> hardwareIdentifier;
        internal readonly ConfigEntry<int> elementIdentifierId;

        internal Binding(ConfigEntry<KeyboardShortcut> keyboard, ConfigEntry<string> deviceInstanceGuid,
                         ConfigEntry<string> hardwareIdentifier, ConfigEntry<int> elementIdentifierId)
        {
            this.keyboard = keyboard;
            this.deviceInstanceGuid = deviceInstanceGuid;
            this.hardwareIdentifier = hardwareIdentifier;
            this.elementIdentifierId = elementIdentifierId;
        }
    }
}
