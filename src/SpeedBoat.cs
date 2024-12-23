using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace SpeedBoat;

public class SpeedBoat : ModSystem {
    private static readonly AssetLocation _sailboat = new("game", "boat-sailed-*");

    private static Settings? _settings;

    public ICoreAPI Api { get; private set; } = null!;
    public ILogger Logger => Mod.Logger;
    public string ModId => Mod.Info.ModID;

    private Harmony? _harmony;

    private FileWatcher? _fileWatcher;
    private IServerNetworkChannel? _serverChannel;

    public override void StartPre(ICoreAPI api) {
        Api = api;
        LoadSettingsFromDisk();
    }

    public override void StartClientSide(ICoreClientAPI capi) {
        capi.Network.RegisterChannel(Mod.Info.ModID)
            .RegisterMessageType<Settings>()
            .SetMessageHandler<Settings>(settings => {
                Mod.Logger.Event("Received settings from the server");
                _settings = settings;
            });
    }

    public override void StartServerSide(ICoreServerAPI sapi) {
        _serverChannel = sapi.Network.RegisterChannel(Mod.Info.ModID)
            .RegisterMessageType<Settings>()
            .SetMessageHandler<Settings>((_, _) => { });

        sapi.Event.PlayerJoin += SendSettingsToPlayer;
    }

    private void SendSettingsToPlayer(IServerPlayer player) {
        _serverChannel?.SendPacket(_settings, player);
    }

    public override void AssetsFinalize(ICoreAPI api) {
        _harmony = new Harmony(Mod.Info.ModID);
        _harmony.Patch(AccessTools.PropertyGetter(typeof(EntityBoat), "SpeedMultiplier"), postfix: (Delegate)Postfix);
    }

    public void LoadSettingsFromDisk() {
        _settings = Api.LoadModConfig<Settings>($"{ModId}.json");

        if (_settings == null) {
            _settings = new Settings();
            (_fileWatcher ??= new FileWatcher(this)).Queued = true;
            Api.StoreModConfig(_settings, $"{ModId}.json");
            Api.Event.RegisterCallback(_ => _fileWatcher.Queued = false, 100);
        }

        if (Api is ICoreServerAPI) {
            foreach (IServerPlayer player in Api.World.AllOnlinePlayers.Cast<IServerPlayer>()) {
                SendSettingsToPlayer(player);
            }
        }
    }

    public override void Dispose() {
        _harmony?.UnpatchAll(Mod.Info.ModID);

        if (Api is ICoreServerAPI sapi) {
            sapi.Event.PlayerJoin -= SendSettingsToPlayer;
        }

        _fileWatcher?.Dispose();
        _fileWatcher = null;

        _settings = null;
    }

    private static void Postfix(EntityBoat __instance, ref float __result) {
        __result = (WildcardUtil.Match(_sailboat, __instance.Code) ? _settings?.SailboatSpeedMultiplier : _settings?.RaftSpeedMultiplier) ?? 1f;
    }
}
