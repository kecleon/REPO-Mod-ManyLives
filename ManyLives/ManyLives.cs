using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using Photon.Pun;

namespace ManyLives;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin
{
    internal static ManualLogSource Log;
    public static Plugin Instance { get; private set; }
    
    public static int MaxLives = 3;
    public static int Lives = 3;
    public static int TotalDeaths = 0;
    public static float HealthMultiplier = 1f;
    public static bool hasSetHealth = false;
    public static int deadPlayers = 0;

    public void Awake()
    {
        Instance = this;
        Log = base.Logger;
        Log.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");

        var harmony = new Harmony("ManyLives");
        harmony.PatchAll();
    }

    public static void ResetDeadPlayers()
    {
        deadPlayers = 0;
    }
}

[HarmonyPatch(typeof(PlayerAvatar), "PlayerDeathRPC")]
public static class PlayerAvatar_PlayerDeathRPC_Patch
{
    private static void Postfix(PlayerAvatar __instance)
    {
        Plugin.deadPlayers++;
        Plugin.Log.LogInfo($"Player died - Dead players: {Plugin.deadPlayers}, Total players: {PhotonNetwork.CurrentRoom?.PlayerCount ?? 1}");

        // Check if all players are dead
        if (Plugin.deadPlayers >= (PhotonNetwork.CurrentRoom?.PlayerCount ?? 1))
        {
            Plugin.TotalDeaths++;
            Plugin.Lives--;
            Plugin.Log.LogInfo($"All players dead - TotalDeaths: {Plugin.TotalDeaths}, Lives remaining: {Plugin.Lives}");

            // Only host handles level changes
            if (PhotonNetwork.IsMasterClient)
            {
                var runManager = GameObject.FindObjectOfType<RunManager>();
                if (runManager != null)
                {
                    if (Plugin.Lives <= 0)
                    {
                        Plugin.Log.LogInfo("No lives remaining - Transitioning to arena");
                        runManager.ChangeLevel(false, true, RunManager.ChangeLevelType.Normal);
                    }
                    else
                    {
                        Plugin.Log.LogInfo($"Restarting level with {Plugin.Lives} lives remaining");
                        runManager.ChangeLevel(false, false, RunManager.ChangeLevelType.Normal);
                    }
                }
            }
        }
    }
}

[HarmonyPatch(typeof(PlayerAvatar), "Update")]
public static class PlayerAvatar_Update_Patch
{
    private static void Postfix(PlayerAvatar __instance)
    {
        if (!Plugin.hasSetHealth && Plugin.TotalDeaths > 0)
        {
            var playerHealth = __instance.playerHealth;
            if (playerHealth == null) return;
            
            var traverse = Traverse.Create(playerHealth);
            var healthSet = traverse.Field("healthSet").GetValue<bool>();
            if (!healthSet) return;

            // Check if this is our local player
            var photonView = __instance.GetComponent<PhotonView>();
            if (photonView == null || !photonView.IsMine) return;
            
            // Set health based on number of deaths
            float healthPercent = Plugin.TotalDeaths == 1 ? 0.5f : 0.25f;
            var currentHealth = traverse.Field("health").GetValue<int>();
            var maxHealth = traverse.Field("maxHealth").GetValue<int>();
            var targetHealth = Mathf.RoundToInt(maxHealth * healthPercent);
            
            Plugin.Log.LogInfo($"Setting local player health - Current: {currentHealth}, Max: {maxHealth}, Target: {targetHealth}");
            
            // First set health directly
            traverse.Field("health").SetValue(targetHealth);
            
            // Then use Heal to handle multiplayer sync
            playerHealth.Heal(0, true); // Set to true to force network sync
            
            Plugin.Log.LogInfo($"Final health: {traverse.Field("health").GetValue<int>()}");
            Plugin.hasSetHealth = true;
        }
    }
}

[HarmonyPatch(typeof(RunManager), "ChangeLevel")]
public static class RunManager_ChangeLevel_Patch
{
    private static void Postfix(RunManager __instance, bool _completedLevel, bool _levelFailed)
    {
        // Only reset lives and deaths when going to a new level (not when restarting)
        if (_levelFailed)
        {
            Plugin.Log.LogInfo($"Going to arena - Resetting lives from {Plugin.Lives} to {Plugin.MaxLives}");
            Plugin.Lives = Plugin.MaxLives;
            Plugin.TotalDeaths = 0;
        }
        else
        {
            Plugin.Log.LogInfo($"Restarting level - Keeping lives at {Plugin.Lives} and deaths at {Plugin.TotalDeaths}");
            Plugin.hasSetHealth = false;
        }
        // Reset dead players count on any level change
        Plugin.ResetDeadPlayers();
    }
}

[HarmonyPatch(typeof(RunManager), "UpdateLevel")]
public static class RunManager_UpdateLevel_Patch
{
    private static void Postfix(RunManager __instance, string _levelName, int _levelsCompleted, bool _gameOver)
    {
        // This is called on all clients when the host changes level
        if (_gameOver)
        {
            Plugin.Lives = Plugin.MaxLives;
            Plugin.TotalDeaths = 0;
            Plugin.Log.LogInfo($"Game over - Resetting lives to {Plugin.MaxLives} and deaths to 0");
        }
        // Reset dead players count on any level update
        Plugin.ResetDeadPlayers();
    }
} 