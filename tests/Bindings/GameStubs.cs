namespace UnityEngine
{
    public enum KeyCode { None=0, Escape=27, J=106, K=107, F7=288, RightShift=303, LeftShift=304,
        RightControl=305, LeftControl=306, RightAlt=307, LeftAlt=308, RightCommand=309, LeftCommand=310,
        LeftWindows=311, RightWindows=312, Mouse0=323, JoystickButton0=330 }
    public static class Input
    {
        public static readonly HashSet<KeyCode> Down = new(), Held = new();
        public static bool GetKeyDown(KeyCode key) => Down.Contains(key);
        public static bool GetKey(KeyCode key) => Held.Contains(key) || Down.Contains(key);
        public static void Frame(params KeyCode[] keys) { Down.Clear(); Held.Clear(); foreach (var key in keys) Down.Add(key); }
    }
}
namespace BepInEx.Configuration
{
    public class ConfigEntry<T> { public T Value; public ConfigEntry(T value) { Value=value; } }
    public class ConfigFile
    {
        public readonly Dictionary<string,object> Entries=new();
        public int Saves; public bool FailSave;
        public ConfigEntry<T> Bind<T>(string section,string key,T value,string description)
        { if (!Entries.ContainsKey(key)) Entries[key]=new ConfigEntry<T>(value); return (ConfigEntry<T>)Entries[key]; }
        public void Save() { if (FailSave) throw new IOException("save failure"); Saves++; }
    }
    public readonly struct KeyboardShortcut
    {
        public UnityEngine.KeyCode MainKey { get; }
        public UnityEngine.KeyCode[] Modifiers { get; }
        public KeyboardShortcut(UnityEngine.KeyCode key, params UnityEngine.KeyCode[] mods) { MainKey=key; Modifiers=mods; }
        public bool IsDown() => MainKey!=UnityEngine.KeyCode.None && UnityEngine.Input.GetKeyDown(MainKey) && Modifiers.All(UnityEngine.Input.GetKey);
        public override bool Equals(object? value) => value is KeyboardShortcut other && MainKey==other.MainKey && Modifiers.Order().SequenceEqual(other.Modifiers.Order());
        public override int GetHashCode() => (int)MainKey;
        public override string ToString() => string.Join(" + ",Modifiers.Append(MainKey));
    }
}
namespace Rewired
{
    public class ControllerElementIdentifier { public int id; public string name=""; }
    public class Joystick
    {
        public bool enabled=true,isConnected=true;
        public Guid deviceInstanceGuid=Guid.NewGuid(); public string hardwareIdentifier="HOTAS",name="Stick";
        public IList<ControllerElementIdentifier> ButtonElementIdentifiers=new List<ControllerElementIdentifier>();
        public HashSet<int> Down=new();
        public bool GetButtonDownById(int id)=>Down.Contains(id);
        public ControllerElementIdentifier? GetElementIdentifierById(int id)=>ButtonElementIdentifiers.FirstOrDefault(e=>e.id==id);
    }
    public class Controllers { public IList<Joystick> Joysticks=new List<Joystick>(); }
    public class Player { public Controllers controllers=new(); }
}
public static class GameManager { public static Rewired.Player? playerInput; }
