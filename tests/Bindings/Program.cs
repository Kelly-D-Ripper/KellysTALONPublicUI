using BepInEx.Configuration;
using KellysTALONPublicUI;
using Rewired;
using UnityEngine;

internal static class Program
{
    static int checks;
    static void Check(bool value,string message) { if (!value) throw new Exception(message); checks++; }
    static int Main()
    {
        try { Run(); Console.WriteLine($"PASS: {checks} production binding checks (input/config API doubles)"); return 0; }
        catch(Exception error) { Console.Error.WriteLine(error); return 1; }
    }
    static void Run()
    {
        var config=new ConfigFile(); var bindings=new WingmanJoystickBindings(config);
        Check(Enum.GetValues<WingmanJoystickAction>().Length==9,"JAM, Alpha Strike and attack mode are configurable");
        Input.Frame(KeyCode.J); GameManager.playerInput=null;
        Check(bindings.TryCaptureKeyboard(WingmanJoystickAction.Jam,KeyCode.F7,out _,out _),"keyboard capture works without Rewired/player");
        Check(bindings.IsDown(WingmanJoystickAction.Jam),"saved JAM key polls");
        Input.Frame(); Check(!bindings.IsDown(WingmanJoystickAction.Jam),"no repeat on subsequent frame");
        Input.Frame(KeyCode.J);
        Check(!bindings.TryCaptureKeyboard(WingmanJoystickAction.FormUp,KeyCode.F7,out _,out var reason)&&reason.StartsWith("key_already_bound"),"duplicate action shortcut rejected");
        Input.Frame(KeyCode.LeftControl); Check(!bindings.TryCaptureKeyboard(WingmanJoystickAction.FormUp,KeyCode.F7,out _,out reason)&&reason=="waiting","modifier alone waits");
        Input.Frame(KeyCode.K); Input.Held.Add(KeyCode.LeftControl);
        Check(bindings.TryCaptureKeyboard(WingmanJoystickAction.FormUp,KeyCode.F7,out _,out _),"modifier chord captures");
        Check(bindings.IsDown(WingmanJoystickAction.FormUp),"modifier chord fires");
        Input.Held.Clear(); Check(!bindings.IsDown(WingmanJoystickAction.FormUp),"missing modifier does not fire");
        Input.Frame(KeyCode.Mouse0,KeyCode.JoystickButton0); Check(!bindings.TryCaptureKeyboard(WingmanJoystickAction.HoldHere,KeyCode.F7,out _,out reason)&&reason=="waiting","mouse clicks and joystick aliases ignored");
        Input.Frame(KeyCode.J,KeyCode.K); Check(!bindings.TryCaptureKeyboard(WingmanJoystickAction.HoldHere,KeyCode.F7,out _,out reason)&&reason=="multiple_keys_pressed","ambiguous keys rejected");
        foreach(var key in new[]{KeyCode.F7,KeyCode.Escape})
        { Input.Frame(key); Check(!bindings.TryCaptureKeyboard(WingmanJoystickAction.HoldHere,KeyCode.F7,out _,out reason)&&reason=="reserved_menu_key","menu/cancel not captured"); }
        Input.Frame(KeyCode.K); config.FailSave=true;
        Check(!bindings.TryCaptureKeyboard(WingmanJoystickAction.Jam,KeyCode.F7,out _,out reason)&&reason=="save_failed","failed save reported");
        Check(bindings.KeyboardLabel(WingmanJoystickAction.Jam)=="J","previous key restored");
        Check(!bindings.ClearKeyboardBinding(WingmanJoystickAction.Jam),"failed clear reported");
        Check(bindings.KeyboardLabel(WingmanJoystickAction.Jam)=="J","failed clear retains key");
        config.FailSave=false;
        GameManager.playerInput=new(); var stick=new Joystick();
        stick.ButtonElementIdentifiers.Add(new(){id=87,name="Button 88"});
        GameManager.playerInput.controllers.Joysticks.Add(stick); Input.Frame();
        Check(!bindings.TryCaptureNext(WingmanJoystickAction.Jam,out _,out reason)&&reason=="waiting","held/no-edge joystick not captured");
        stick.Down.Add(87);
        Check(bindings.TryCaptureNext(WingmanJoystickAction.Jam,out _,out _),"high numbered HOTAS capture");
        Check(bindings.IsDown(WingmanJoystickAction.Jam),"high numbered HOTAS fires");
        Check(bindings.KeyboardLabel(WingmanJoystickAction.Jam)=="J","button bind preserves key");
        Check(!bindings.TryCaptureNext(WingmanJoystickAction.HoldHere,out _,out reason)&&reason.StartsWith("button_already_bound"),"duplicate button rejected");
        var reloaded=new WingmanJoystickBindings(config);
        Check(reloaded.HasJoystickBinding(WingmanJoystickAction.Jam)&&reloaded.KeyboardLabel(WingmanJoystickAction.Jam)=="J","saved config reload retains both");
        Check(reloaded.ClearKeyboardBinding(WingmanJoystickAction.Jam)&&reloaded.IsDown(WingmanJoystickAction.Jam),"clear key preserves working joystick");
        Check(reloaded.ClearJoystickBinding(WingmanJoystickAction.Jam)&&!reloaded.IsDown(WingmanJoystickAction.Jam),"clear button actually disables it");
        Check(reloaded.TryCaptureNext(WingmanJoystickAction.HoldHere,out _,out _),"cleared button reusable");
        stick.Down.Clear(); Check(!reloaded.IsDown(WingmanJoystickAction.HoldHere),"joystick edge not repeated");
        stick.Down.Add(87); stick.isConnected=false;
        Check(!reloaded.IsDown(WingmanJoystickAction.HoldHere),"unplugged joystick does not fire");
        stick.isConnected=true; stick.deviceInstanceGuid=Guid.NewGuid();
        Check(reloaded.IsDown(WingmanJoystickAction.HoldHere),"single matching hardware fallback after GUID change");
        var twin=new Joystick(); twin.ButtonElementIdentifiers.Add(new(){id=87}); twin.Down.Add(87);
        GameManager.playerInput.controllers.Joysticks.Add(twin);
        Check(!reloaded.IsDown(WingmanJoystickAction.HoldHere),"ambiguous identical hardware fails closed");
        Check(!reloaded.TryCaptureNext(WingmanJoystickAction.Jam,out _,out reason)&&reason=="multiple_buttons_pressed","simultaneous device presses not saved");
    }
}
