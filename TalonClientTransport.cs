using System;
using System.Collections.Generic;
using Mirage;
using Mirage.RemoteCalls;
using Mirage.Serialization;
using NuclearOption.Networking;
using UnityEngine;

namespace KellysTALONPublicUI;

// Client-side implementation of the published TALON wire contract. It deliberately contains no
// server callbacks, economy logic, aircraft spawning, or AI authority.
internal sealed class TalonClientTransport : NetworkSceneSingleton<TalonClientTransport>
{
    internal const int ProtocolVersion = 15;
    internal const int PrefabHash = 0x4B540001;
    internal const int MaximumDefinitionKeyLength = 128;
    internal const int MaximumPresetIdLength = 64;
    private const int RpcCount = 8;

    private const int CmdSetMode = 0;
    private const int RpcModeChanged = 1;
    private const int CmdSnapshot = 2;
    private const int RpcSnapshot = 3;
    private const int CmdPurchase = 4;
    private const int RpcPurchase = 5;
    private const int CmdQuick = 6;
    private const int RpcQuick = 7;

    internal readonly struct WingmanEntry
    {
        internal WingmanEntry(uint pid, int ordinal, bool canJam = false, bool jamming = false, int status = 0)
        {
            Pid = pid;
            Ordinal = ordinal;
            CanJam = canJam;
            Jamming = jamming;
            Status = status;
        }

        internal uint Pid { get; }
        internal int Ordinal { get; }
        internal bool CanJam { get; }
        internal bool Jamming { get; }
        internal int Status { get; }
    }

    private static readonly HashSet<ClientObjectManager> Managers = new HashSet<ClientObjectManager>();
    private static GameObject? template;
    private static Plugin? ui;

    private readonly List<WingmanEntry> wingmen = new List<WingmanEntry>();
    private uint sessionId;

    private static TalonClientTransport? hostProxy;
    private static float nextHostProbe;
    private NetworkIdentity? hostIdentity;
    private NetworkManagerNuclearOption? hostManager;
    private Player? hostPlayer;
    private Func<int, byte[], bool>? hostSend;
    private readonly Queue<Tuple<int, byte[]>> hostReplies = new Queue<Tuple<int, byte[]>>();

    internal static TalonClientTransport? Active => hostProxy != null ? hostProxy : i;
    internal bool Ready => hostSend != null ? HostIsCurrent() : Identity != null && Identity.IsSpawned;

    private bool HostIsCurrent() => hostIdentity != null && hostIdentity.IsSpawned &&
        hostManager != null && hostManager == NetworkManagerNuclearOption.i &&
        hostManager.Server.Active && hostManager.Server.IsHost &&
        hostPlayer != null && GameManager.GetLocalPlayer(out Player current) && ReferenceEquals(current, hostPlayer);

    internal static void PollLocalHost()
    {
        if (hostProxy != null)
        {
            if (!hostProxy.HostIsCurrent()) ClearHost();
            else
            {
                // Defer replies until after SendPurchase/SendQuick returns, just like remote RPCs.
                while (hostProxy != null && hostProxy.hostReplies.Count > 0)
                {
                    var reply = hostProxy.hostReplies.Dequeue();
                    using var reader = NetworkReaderPool.GetReader(reply.Item2, hostProxy.hostManager!.Client.World);
                    switch (reply.Item1)
                    {
                        case RpcModeChanged: ReadModeResult(hostProxy, reader, null!, 0); break;
                        case RpcSnapshot: ReadSnapshot(hostProxy, reader, null!, 0); break;
                        case RpcPurchase: ReadPurchaseResult(hostProxy, reader, null!, 0); break;
                        case RpcQuick: ReadQuickResult(hostProxy, reader, null!, 0); break;
                    }
                }
                return;
            }
        }
        var manager = NetworkManagerNuclearOption.i;
        if (manager == null || !manager.Server.Active || !manager.Server.IsHost ||
            !GameManager.GetLocalPlayer(out Player player) || player == null || !player.IsLocalPlayer ||
            Time.unscaledTime < nextHostProbe) return;
        nextHostProbe = Time.unscaledTime + 0.5f;
        if (!BepInEx.Bootstrap.Chainloader.PluginInfos.TryGetValue("kelly.nuclearoption.talon", out var info) ||
            info.Instance == null) return;
        var connect = info.Instance.GetType().GetMethod("ConnectLocalTalonUi",
            new[] { typeof(int), typeof(Action<int, byte[]>) });
        if (connect == null) return; // Older servers remain supported for remote use.
        EnsureTemplate();
        var clone = UnityEngine.Object.Instantiate(template!);
        var proxy = clone.GetComponent<TalonClientTransport>();
        try
        {
            Action<int, byte[]> receive = (index, payload) =>
            {
                if (proxy != null && ReferenceEquals(hostProxy, proxy))
                    proxy.hostReplies.Enqueue(Tuple.Create(index, payload));
            };
            var binding = connect.Invoke(info.Instance, new object[] { ProtocolVersion, receive })
                as Tuple<NetworkIdentity, Func<int, byte[], bool>>;
            if (binding == null) { UnityEngine.Object.Destroy(clone); return; }
            proxy.hostIdentity = binding.Item1;
            proxy.hostSend = binding.Item2;
            proxy.hostManager = manager;
            proxy.hostPlayer = player;
            hostProxy = proxy;
            clone.SetActive(true);
            UnityEngine.Object.DontDestroyOnLoad(clone);
            ui?.TransportStateChanged(true);
            UnityEngine.Debug.Log("[KellysTALONPublicUI] event=local_host_connected protocol=" + ProtocolVersion);
            proxy.RequestFreshSnapshot();
        }
        catch (Exception error)
        {
            if (hostProxy == proxy) ClearHost();
            else UnityEngine.Object.Destroy(clone);
            UnityEngine.Debug.LogWarning("[KellysTALONPublicUI] local_host_connect_failed=" + error.GetType().Name);
        }
    }

    private static void ClearHost()
    {
        var proxy = hostProxy;
        hostProxy = null;
        if (proxy == null) return;
        proxy.hostSend = null;
        proxy.hostReplies.Clear();
        proxy.ClearSnapshot();
        if (i == proxy) i = null!;
        UnityEngine.Object.Destroy(proxy.gameObject);
        ui?.TransportStateChanged(false);
    }

    private bool SendRequest(int index, NetworkWriter writer)
    {
        if (!Ready) return false;
        if (hostSend != null) return hostSend(index, writer.ToArray());
        ServerRpcSender.Send(this, index, writer, Channel.Reliable, false);
        return true;
    }
    internal uint SessionId => sessionId;
    internal IReadOnlyList<WingmanEntry> Wingmen => wingmen;

    internal static void Bind(Plugin plugin) => ui = plugin;

    public override void Awake()
    {
        base.Awake();
        Identity.OnStartClient.AddListener(ClientStarted);
        Identity.OnStopClient.AddListener(ClientStopped);
    }

    public override int GetRpcCount() => RpcCount;

    protected override void RegisterRpc(RemoteCallCollection calls)
    {
        calls.Register(CmdSetMode, "KellysTALON.WingmanSessionTransport.CmdSetMode", false,
            RpcInvokeType.ServerRpc, this, IgnoreServerCall, default);
        calls.Register(RpcModeChanged, "KellysTALON.WingmanSessionTransport.RpcModeChanged", false,
            RpcInvokeType.ClientRpc, this, ReadModeResult, default);
        calls.Register(CmdSnapshot, "KellysTALON.WingmanSessionTransport.CmdRequestSnapshot", false,
            RpcInvokeType.ServerRpc, this, IgnoreServerCall, default);
        calls.Register(RpcSnapshot, "KellysTALON.WingmanSessionTransport.RpcSnapshot", false,
            RpcInvokeType.ClientRpc, this, ReadSnapshot, default);
        calls.Register(CmdPurchase, "KellysTALON.WingmanSessionTransport.CmdWingmanPurchase", false,
            RpcInvokeType.ServerRpc, this, IgnoreServerCall, default);
        calls.Register(RpcPurchase, "KellysTALON.WingmanSessionTransport.RpcWingmanPurchaseResult", false,
            RpcInvokeType.ClientRpc, this, ReadPurchaseResult, default);
        calls.Register(CmdQuick, "KellysTALON.WingmanSessionTransport.CmdQuickCommand", false,
            RpcInvokeType.ServerRpc, this, IgnoreServerCall, default);
        calls.Register(RpcQuick, "KellysTALON.WingmanSessionTransport.RpcQuickCommandResult", false,
            RpcInvokeType.ClientRpc, this, ReadQuickResult, default);
    }

    private static void IgnoreServerCall(NetworkBehaviour _, NetworkReader __, INetworkPlayer ___, int ____) { }

    private static void ReadModeResult(NetworkBehaviour _, NetworkReader reader, INetworkPlayer __, int ___)
    {
        int protocol = reader.ReadInt32();
        uint reportedSession = reader.ReadUInt32();
        uint pid = reader.ReadUInt32();
        int mode = reader.ReadInt32();
        bool accepted = reader.ReadBoolean();
        string reason = reader.ReadString();
        ui?.ReceiveModeResult(protocol, reportedSession, pid, mode, accepted, reason);
    }

    private static void ReadPurchaseResult(NetworkBehaviour _, NetworkReader reader, INetworkPlayer __, int ___)
    {
        int protocol = reader.ReadInt32();
        int operation = reader.ReadInt32();
        string key = reader.ReadString();
        string presetId = reader.ReadString();
        bool accepted = reader.ReadBoolean();
        float charge = reader.ReadSingle();
        string reason = reader.ReadString();
        ui?.ReceivePurchaseResult(protocol, operation, key, presetId, accepted, charge, reason);
    }

    private static void ReadQuickResult(NetworkBehaviour _, NetworkReader reader, INetworkPlayer __, int ___)
    {
        int protocol = reader.ReadInt32();
        int command = reader.ReadInt32();
        uint selectedPid = reader.ReadUInt32();
        bool accepted = reader.ReadBoolean();
        int affected = reader.ReadInt32();
        string reason = reader.ReadString();
        ui?.ReceiveQuickResult(protocol, command, selectedPid, accepted, affected, reason);
    }

    private static void ReadSnapshot(NetworkBehaviour behaviour, NetworkReader reader, INetworkPlayer _, int __)
    {
        TalonClientTransport link = (TalonClientTransport)behaviour;
        int protocol = reader.ReadInt32();
        if (protocol != ProtocolVersion)
        {
            link.ClearSnapshot();
            ui?.ReceiveQueueSnapshot(0, 0);
            ui?.ReceiveSnapshot(protocol, 0, Array.Empty<WingmanEntry>(), "protocol_mismatch");
            return;
        }
        uint reportedSession = reader.ReadUInt32();
        bool accepted = reader.ReadBoolean();
        string reason = reader.ReadString();
        reader.ReadUInt32(); // Home aircraft PID is not needed by this compact public control surface.
        int count = reader.ReadInt32();
        var received = new List<WingmanEntry>();
        bool validCount = count >= 0 && count <= 64;
        if (validCount)
        {
            for (int index = 0; index < count; index++)
            {
                uint pid = reader.ReadUInt32();
                int ordinal = reader.ReadInt32();
                reader.ReadInt32(); // group id
                reader.ReadInt32(); // group slot
                reader.ReadInt32(); // mode
                received.Add(new WingmanEntry(pid, ordinal, reader.ReadBoolean(), reader.ReadBoolean(), reader.ReadInt32()));
            }
        }
        if (validCount) ui?.ReceiveQueueSnapshot(reader.ReadInt32(), reader.ReadInt32());
        if (protocol != ProtocolVersion || !accepted || reportedSession == 0 || !validCount)
        {
            link.ClearSnapshot();
            ui?.ReceiveSnapshot(protocol, 0, Array.Empty<WingmanEntry>(),
                protocol != ProtocolVersion ? "protocol_mismatch" : validCount ? reason : "snapshot_count");
            return;
        }
        received.Sort((left, right) => left.Ordinal != right.Ordinal
            ? left.Ordinal.CompareTo(right.Ordinal)
            : left.Pid.CompareTo(right.Pid));
        if (received.Count > PublicUiLogic.MaximumWingmen)
            received.RemoveRange(PublicUiLogic.MaximumWingmen, received.Count - PublicUiLogic.MaximumWingmen);
        link.sessionId = reportedSession;
        link.wingmen.Clear();
        link.wingmen.AddRange(received);
        ui?.ReceiveSnapshot(protocol, reportedSession, received, "ok");
    }

    internal bool SendPurchase(int operation, string? definitionKey, string? requestedPresetId, out string reason, string livery = "")
    {
        string key = definitionKey ?? string.Empty;
        string presetId = requestedPresetId ?? string.Empty;
        if (livery == null || livery.Length > 29) { reason = "livery_invalid"; return false; }
        if (key.Length > MaximumDefinitionKeyLength)
        {
            reason = "definition_key_length";
            return false;
        }
        if (presetId.Length > MaximumPresetIdLength)
        {
            reason = "preset_id_length";
            return false;
        }
        if (!Ready)
        {
            reason = "transport_unavailable";
            return false;
        }
        using var writer = NetworkWriterPool.GetWriter();
        writer.WriteInt32(ProtocolVersion);
        writer.WriteInt32(operation);
        writer.WriteString(key);
        writer.WriteString(presetId);
        writer.WriteString(livery);
        bool sent = SendRequest(CmdPurchase, writer);
        reason = sent ? "sent" : "transport_unavailable";
        return sent;
    }

    internal bool SendQuick(TalonCommand command, uint selectedPid, out string reason)
    {
        if (sessionId == 0 || !Ready)
        {
            reason = "session_not_ready";
            return false;
        }
        var targets = new List<uint>();
        if (command == TalonCommand.AttackMyTargets || command == TalonCommand.FullStrike || command == TalonCommand.FlightSalvo)
        {
            if (GameManager.GetLocalPlayer(out Player local) && local?.Aircraft?.weaponManager != null)
                foreach (Unit target in local.Aircraft.weaponManager.GetTargetList())
                    if (target != null && !targets.Contains(target.persistentID.Id)) targets.Add(target.persistentID.Id);
            if (targets.Count > 64) { reason = "too_many_targets"; return false; }
            if (targets.Count == 0) { reason = "no_hostile_target"; return false; }
        }
        using var writer = NetworkWriterPool.GetWriter();
        writer.WriteInt32(ProtocolVersion);
        writer.WriteUInt32(sessionId);
        writer.WriteInt32((int)command);
        writer.WriteUInt32(selectedPid);
        writer.WriteInt32(targets.Count);
        foreach (uint target in targets) writer.WriteUInt32(target);
        bool sent = SendRequest(CmdQuick, writer);
        reason = sent ? "sent" : "transport_unavailable";
        return sent;
    }

    internal void RequestFreshSnapshot()
    {
        if (!Ready) return;
        using var writer = NetworkWriterPool.GetWriter();
        writer.WriteInt32(ProtocolVersion);
        writer.WriteUInt32(0);
        SendRequest(CmdSnapshot, writer);
    }

    private void ClientStarted()
    {
        ClearSnapshot();
        ui?.TransportStateChanged(true);
        if (!IsServer) RequestFreshSnapshot();
    }

    private void ClientStopped()
    {
        ClearSnapshot();
        ui?.TransportStateChanged(false);
    }

    private void ClearSnapshot()
    {
        sessionId = 0;
        wingmen.Clear();
    }

    internal static void RegisterFor(NetworkManagerNuclearOption manager)
    {
        if (manager == null) return;
        EnsureTemplate();
        ClientObjectManager client = manager.ClientObjectManager;
        if (Managers.Contains(client)) client.UnregisterSpawnHandler(PrefabHash);
        client.RegisterSpawnHandler(PrefabHash, Spawn, Unspawn);
        Managers.Add(client);
    }

    private static void EnsureTemplate()
    {
        if (template != null) return;
        template = new GameObject("KellysTALONPublicUI.Transport");
        template.SetActive(false);
        NetworkIdentity identity = template.AddComponent<NetworkIdentity>();
        template.AddComponent<TalonClientTransport>();
        identity.PrefabHash = PrefabHash;
        UnityEngine.Object.DontDestroyOnLoad(template);
    }

    private static NetworkIdentity Spawn(SpawnMessage _)
    {
        EnsureTemplate();
        GameObject clone = UnityEngine.Object.Instantiate(template!);
        clone.SetActive(true);
        return clone.GetComponent<NetworkIdentity>();
    }

    private static void Unspawn(NetworkIdentity identity)
    {
        TalonClientTransport? link = identity.GetComponent<TalonClientTransport>();
        link?.ClearSnapshot();
        UnityEngine.Object.Destroy(identity.gameObject);
    }

    internal static void Shutdown()
    {
        ClearHost();
        nextHostProbe = 0;
        foreach (ClientObjectManager manager in Managers)
            if (manager != null) manager.UnregisterSpawnHandler(PrefabHash);
        Managers.Clear();
        if (template != null) UnityEngine.Object.Destroy(template);
        template = null;
        ui = null;
    }
}
