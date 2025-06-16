using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using Photon.Pun;
using ExitGames.Client.Photon;
using Photon.Realtime;
using System;
using System.Collections.Generic;

namespace ManyLives;

// Network manager to handle RPCs
public class NetworkManager : MonoBehaviourPunCallbacks
{
    public static NetworkManager Instance { get; private set; }

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);

            // Ensure we have a PhotonView and it's properly set up
            if (!photonView)
            {
                var view = gameObject.AddComponent<PhotonView>();
                view.ViewID = 999; // Use a consistent ViewID
                view.Synchronization = ViewSynchronization.Off; // We don't need transform sync
                view.ObservedComponents = new List<Component>(); // No observed components needed
                view.OwnershipTransfer = OwnershipOption.Fixed; // Fixed ownership
            }
        }
        else
        {
            Destroy(gameObject);
        }
    }

    public override void OnJoinedRoom()
    {
        base.OnJoinedRoom();

        // Ensure our PhotonView is properly registered
        if (photonView && !photonView.IsMine)
        {
            Plugin.Log.LogInfo("Taking ownership of NetworkManager PhotonView");
            photonView.TransferOwnership(PhotonNetwork.LocalPlayer);
        }

        // Reset lives when joining a room
        Plugin.Lives = Plugin.MaxLives;
        Plugin.TotalDeaths = 0;
        Plugin.hasSetHealth = false;
        Plugin.Log.LogInfo($"Joined room - Reset lives to {Plugin.Lives}");
    }

    [PunRPC]
    public void SyncLivesRPC(int lives, int deaths)
    {
        Plugin.Lives = lives;
        Plugin.TotalDeaths = deaths;
        Plugin.hasSetHealth = false; // Reset so health will be updated
        Plugin.Log.LogInfo($"Received sync - Lives: {lives}, Deaths: {deaths}");
    }

    [PunRPC]
    public void SetPlayerHealthRPC(int targetHealth)
    {
        try
        {
            var localPlayer = GameDirector.instance?.PlayerList?.Find(p => p.photonView.IsMine);
            if (localPlayer != null && localPlayer.playerHealth != null)
            {
                var traverse = Traverse.Create(localPlayer.playerHealth);
                traverse.Field("health").SetValue(targetHealth);
                Plugin.Log.LogInfo($"RPC set health to {targetHealth} for local player");
                localPlayer.playerHealth.Heal(0, true); // Trigger health sync
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"Error in SetPlayerHealthRPC: {e.Message}");
        }
    }

    public void SyncToAll(int lives, int deaths)
    {
        if (!SemiFunc.IsMultiplayer() || !PhotonNetwork.IsMasterClient) return;

        try
        {
            if (photonView && photonView.ViewID != 0)
            {
                Plugin.Log.LogInfo($"Syncing lives ({lives}) and deaths ({deaths}) with ViewID: {photonView.ViewID}");
                photonView.RPC("SyncLivesRPC", RpcTarget.All, lives, deaths);
            }
            else
            {
                Plugin.Log.LogError("PhotonView not properly initialized for sync!");
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"Error syncing lives and deaths: {e.Message}");
        }
    }

    public void SetHealthForAll(int targetHealth)
    {
        try
        {
            if (photonView && photonView.ViewID != 0)
            {
                Plugin.Log.LogInfo($"Setting health to {targetHealth} for all players");
                photonView.RPC("SetPlayerHealthRPC", RpcTarget.All, targetHealth);
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"Error setting health for all: {e.Message}");
        }
    }
}

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

    private GameObject networkManagerObj;

    public void Awake()
    {
        Instance = this;
        Log = base.Logger;
        Log.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");

        var harmony = new Harmony("ManyLives");
        harmony.PatchAll();
    }

    public void Start()
    {
        // Create network manager object after a frame to ensure proper initialization
        StartCoroutine(CreateNetworkManager());
    }

    private System.Collections.IEnumerator CreateNetworkManager()
    {
        yield return null; // Wait one frame

        if (networkManagerObj == null)
        {
            networkManagerObj = new GameObject("ManyLivesNetworkManager");
            networkManagerObj.AddComponent<NetworkManager>();
            DontDestroyOnLoad(networkManagerObj);
            Log.LogInfo("Created NetworkManager");
        }
    }

    public static bool ShouldHandleGameLogic()
    {
        // In single player, always handle logic
        // In multiplayer, only master client handles logic
        return !SemiFunc.IsMultiplayer() || PhotonNetwork.IsMasterClient;
    }

    public static void SyncLivesAndDeaths()
    {
        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.SyncToAll(Lives, TotalDeaths);
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
            // Only proceed if this is our player or single player
            if (SemiFunc.IsMultiplayer() && !__instance.photonView.IsMine) return;

            var playerHealth = __instance.playerHealth;
            if (playerHealth == null) return;
            
            var traverse = Traverse.Create(playerHealth);
            var healthSet = traverse.Field("healthSet").GetValue<bool>();
            if (!healthSet) return;
            
            // Set health based on number of deaths
            float healthPercent = Plugin.TotalDeaths == 1 ? 0.5f : 0.01f;
            var maxHealth = traverse.Field("maxHealth").GetValue<int>();
            var targetHealth = Mathf.Max(1, Mathf.RoundToInt(maxHealth * healthPercent));
            
            Plugin.Log.LogInfo($"Setting health - Max: {maxHealth}, Target: {targetHealth}");

            if (SemiFunc.IsMultiplayer())
            {
                // In multiplayer, use NetworkManager to sync health
                NetworkManager.Instance?.SetHealthForAll(targetHealth);
				playerHealth.Heal(0, true);
				playerHealth.HealOther(0, true);
				playerHealth.HealOtherRPC(0, true);
            }
            else
            {
                // In single player, set health directly
                traverse.Field("health").SetValue(targetHealth);
                playerHealth.Heal(0, true);
				playerHealth.HealOther(0, true);
				playerHealth.HealOtherRPC(0, true);
            }

            Plugin.hasSetHealth = true;
        }
    }
}

[HarmonyPatch(typeof(RunManager), "ChangeLevel")]
public static class RunManager_ChangeLevel_Patch
{
    private static void Postfix(RunManager __instance, bool _completedLevel, bool _levelFailed)
    {
        // Only proceed if we should handle game logic
        if (!Plugin.ShouldHandleGameLogic()) return;

        // Reset lives when entering lobby or when level failed
        if (_levelFailed || __instance.levelCurrent == __instance.levelLobby || __instance.levelCurrent == __instance.levelLobbyMenu)
        {
            Plugin.Log.LogInfo($"Going to {__instance.levelCurrent.name} - Resetting lives from {Plugin.Lives} to {Plugin.MaxLives}");
            Plugin.Lives = Plugin.MaxLives;
            Plugin.TotalDeaths = 0;
            Plugin.SyncLivesAndDeaths();
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
        // Only proceed if we should handle game logic
        if (!Plugin.ShouldHandleGameLogic()) return;

        var allPlayersDead = Traverse.Create(__instance).Field("allPlayersDead").GetValue<bool>();
        var restarting = Traverse.Create(__instance).Field("restarting").GetValue<bool>();
        
        if (allPlayersDead && !restarting)
        {
            Plugin.TotalDeaths++;
            Plugin.Lives--;
            
            Plugin.Log.LogInfo($"All players dead - TotalDeaths: {Plugin.TotalDeaths}, Lives remaining: {Plugin.Lives}");
            
            // Sync the updated values to all clients (will only sync if in multiplayer)
            Plugin.SyncLivesAndDeaths();
            
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