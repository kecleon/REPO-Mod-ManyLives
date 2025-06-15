using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

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

    public void Awake()
    {
        Instance = this;
        Log = base.Logger;
        Log.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");

        var harmony = new Harmony("ManyLives");
        harmony.PatchAll();
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
            
            // Set health based on number of deaths
            float healthPercent = Plugin.TotalDeaths == 1 ? 0.5f : 0.25f;
            var currentHealth = traverse.Field("health").GetValue<int>();
            var maxHealth = traverse.Field("maxHealth").GetValue<int>();
            var targetHealth = Mathf.RoundToInt(maxHealth * healthPercent);
            
            Plugin.Log.LogInfo($"Setting health - Current: {currentHealth}, Max: {maxHealth}, Target: {targetHealth}");
            
            // First set health directly
            traverse.Field("health").SetValue(targetHealth);
            Plugin.Log.LogInfo($"Set health directly to {targetHealth}");
            
            // Then use Heal to handle multiplayer sync
            playerHealth.Heal(0, false);
            
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
    }
}

[HarmonyPatch(typeof(RunManager), "Update")]
public static class RunManager_Update_Patch
{
    private static void Postfix(RunManager __instance)
    {
        var allPlayersDead = Traverse.Create(__instance).Field("allPlayersDead").GetValue<bool>();
        var restarting = Traverse.Create(__instance).Field("restarting").GetValue<bool>();
        
        if (allPlayersDead && !restarting)
        {
            Plugin.TotalDeaths++;
            Plugin.Lives--;
            
            Plugin.Log.LogInfo($"All players dead - TotalDeaths: {Plugin.TotalDeaths}, Lives remaining: {Plugin.Lives}");
            
            if (Plugin.Lives <= 0)
            {
                Plugin.Log.LogInfo("No lives remaining - Transitioning to arena");
                __instance.ChangeLevel(false, true, RunManager.ChangeLevelType.Normal);
            }
            else
            {
                Plugin.Log.LogInfo($"Restarting level with {Plugin.Lives} lives remaining");
                __instance.ChangeLevel(false, false, RunManager.ChangeLevelType.Normal);
            }
        }
    }
} 