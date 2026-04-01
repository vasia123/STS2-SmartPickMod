using FirstMod;
using FirstMod.Patches;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes;

[ModInitializer("Initialize")]
public class ModEntry
{
    public static void Initialize()
    {
        TierData.Initialize();
        RelicTierData.Initialize();
        var harmony = new Harmony("smartpick.patch");
        try
        {
            harmony.PatchAll();
            Log.Info("[SmartPick] Harmony patches initialized.");
        }
        catch (Exception ex)
        {
            Log.Error($"[SmartPick] PatchAll FAILED: {ex.Message}\n{ex.StackTrace}");
        }

        // If NGame._Ready() already ran before our patch was applied, start F7 polling now
        if (NGame.Instance != null && GodotObject.IsInstanceValid(NGame.Instance))
        {
            TierEditorInstallPatch.EnsurePolling(NGame.Instance);
        }
    }
}