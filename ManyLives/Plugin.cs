using BepInEx;
using BepInEx.Logging;
using BepInEx.NET.Common;
using HarmonyLib;

namespace ManyLives;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BasePlugin
{
	internal static new ManualLogSource Log;
	
	public static int MaxLives = 1;
	public static int Lives = 1;

	public override void Load()
	{
		// Plugin startup logic
		Log = base.Log;
		Log.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");
	}
}

[HarmonyPatch(typeof(PlayerAvatar), "Awake")]
public static class PlayerAvatar_Awake_Patch
{
	private static void Postfix(PlayerAvatar __instance)
	{
		
	}
}

[HarmonyPatch(typeof(RunManager), "ChangeLevel")]
public static class RunManager_ChangeLevel_Patch
{
	private static void Postfix(RunManager __instance)
	{
		Plugin.Lives = Plugin.MaxLives;
	}
}