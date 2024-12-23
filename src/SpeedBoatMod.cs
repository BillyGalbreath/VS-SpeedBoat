using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace SpeedBoat;

public class SpeedBoatMod : ModSystem {
    private static readonly AssetLocation _sailboat = new("game", "boat-sailed-*");

    private static Settings? _activeSettings;

    public ICoreAPI Api { get; private set; } = null!;
    public ILogger Logger => Mod.Logger;
    public string ModId => Mod.Info.ModID;

    private Settings? _clientSettings;
    private Settings? _serverSettings;

    private Harmony? _harmony;
    private FileWatcher? _fileWatcher;
    private IServerNetworkChannel? _serverChannel;

    public override void StartPre(ICoreAPI api) {
        Api = api;
    }

    public override void Start(ICoreAPI api) {
        _harmony = new Harmony(Mod.Info.ModID);
        _harmony.Patch(AccessTools.PropertyGetter(typeof(EntityBoat), "SpeedMultiplier"), postfix: (Delegate)PostSpeedMultiplier);
        _harmony.Patch(AccessTools.Method(typeof(EntityBoat), "SeatsToMotion"), postfix: (Delegate)PostSeatsToMotion);
        _harmony.Patch(AccessTools.Method(typeof(EntityBoat), "OnRenderFrame"), postfix: (Delegate)PostOnRenderFrame);
    }

    public override void StartClientSide(ICoreClientAPI capi) {
        capi.Network.RegisterChannel(Mod.Info.ModID)
            .RegisterMessageType<Settings>()
            .SetMessageHandler<Settings>(ReceiveSettingsFromServer);

        LoadClientSettingsFromDisk();
    }

    public override void StartServerSide(ICoreServerAPI sapi) {
        _serverChannel = sapi.Network.RegisterChannel(Mod.Info.ModID)
            .RegisterMessageType<Settings>()
            .SetMessageHandler<Settings>((_, _) => { });

        LoadServerSettingsFromDisk();

        sapi.Event.PlayerJoin += SendSettingsToPlayer;
    }

    private void ReceiveSettingsFromServer(Settings? settings) {
        _clientSettings = settings;
        _activeSettings = _clientSettings;
    }

    private void SendSettingsToPlayer(IServerPlayer player) {
        _serverChannel?.SendPacket(_serverSettings, player);
    }

    private void LoadClientSettingsFromDisk() {
        LoadSettingsFromDisk(out _clientSettings);
        _activeSettings = _clientSettings;
    }

    public void LoadServerSettingsFromDisk() {
        LoadSettingsFromDisk(out _serverSettings);
        _activeSettings = _serverSettings;
    }

    private void LoadSettingsFromDisk(out Settings? settings) {
        GamePaths.EnsurePathExists(GamePaths.ModConfig);

        settings = Api.LoadModConfig<Settings>($"{ModId}.json");

        _fileWatcher ??= new FileWatcher(this);

        if (settings == null) {
            settings = new Settings();

            _fileWatcher.Queued = true;
            Api.StoreModConfig(settings, $"{ModId}.json");
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

        _clientSettings = null;
        _serverSettings = null;

        _activeSettings = null;
    }

    private static float GetSpeedMultiplier(EntityBoat boat) {
        return (WildcardUtil.Match(_sailboat, boat.Code)
            ? _activeSettings?.SailboatSpeedMultiplier
            : _activeSettings?.RaftSpeedMultiplier) ?? 1f;
    }

    private static void PostSpeedMultiplier(EntityBoat __instance, ref float __result) {
        __result = GetSpeedMultiplier(__instance);
    }

    private static void PostSeatsToMotion(EntityBoat __instance, ref Vec2d __result) {
        __result.Y /= GetSpeedMultiplier(__instance);
    }

    private static void PostOnRenderFrame(EntityBoat __instance) {
        if (__instance.Properties.Client.Renderer is EntityShapeRenderer renderer) {
            renderer.zangle = __instance.Swimming ? __instance.mountAngle.Z + (float)(-__instance.ForwardSpeed * 1.3f) / GetSpeedMultiplier(__instance) : 0.0f;
        }
    }
}
