using FirstMod;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;

namespace FirstMod.Patches;

[HarmonyPatch]
public static class CombatExportPatches
{
    // Wrap every postfix in try/catch — exceptions from the legacy exporter MUST NOT propagate
    // back into the game's combat-entry flow (it causes black-screen on combat entry, see v0.104.0).

    [HarmonyPatch(typeof(Player), nameof(Player.PopulateCombatState))]
    [HarmonyPostfix]
    public static void AfterPopulateCombatState(Player __instance)
    {
        try { CombatExporter.RequestExportFrom(__instance); }
        catch (Exception ex) { Log.Error($"[SmartPick] PopulateCombatState patch: {ex.Message}"); }
    }

    [HarmonyPatch(typeof(CombatState), nameof(CombatState.AddCreature), new[] { typeof(Creature) })]
    [HarmonyPostfix]
    public static void AfterAddCreature(CombatState __instance)
    {
        try
        {
            CombatExporter.SetCombatState(__instance);
            CombatExporter.RequestExport();
        }
        catch (Exception ex) { Log.Error($"[SmartPick] AddCreature patch: {ex.Message}"); }
    }

    [HarmonyPatch(typeof(CombatState), nameof(CombatState.RemoveCreature), new[] { typeof(Creature), typeof(bool) })]
    [HarmonyPostfix]
    public static void AfterRemoveCreature(CombatState __instance)
    {
        try { CombatExporter.RequestExport(); }
        catch (Exception ex) { Log.Error($"[SmartPick] RemoveCreature patch: {ex.Message}"); }
    }

    [HarmonyPatch(typeof(CombatState), "set_CurrentSide")]
    [HarmonyPostfix]
    public static void AfterSideChanged(CombatState __instance)
    {
        try
        {
            CombatExporter.SetCombatState(__instance);
            CombatExporter.RequestExport();
        }
        catch (Exception ex) { Log.Error($"[SmartPick] SideChanged patch: {ex.Message}"); }
    }

    [HarmonyPatch(typeof(CombatState), "set_RoundNumber")]
    [HarmonyPostfix]
    public static void AfterRoundChanged(CombatState __instance)
    {
        try { CombatExporter.RequestExport(); }
        catch (Exception ex) { Log.Error($"[SmartPick] RoundChanged patch: {ex.Message}"); }
    }

    [HarmonyPatch(typeof(PlayerCombatState), "set_Energy")]
    [HarmonyPostfix]
    public static void AfterEnergyChanged(PlayerCombatState __instance)
    {
        try
        {
            var field = typeof(PlayerCombatState).GetField("_player",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (field?.GetValue(__instance) is Player player)
                CombatExporter.RequestExportFrom(player);
        }
        catch (Exception ex)
        {
            Log.Error($"[SmartPick] Energy patch: {ex.Message}");
        }
    }

    [HarmonyPatch(typeof(CardPile), nameof(CardPile.InvokeContentsChanged))]
    [HarmonyPostfix]
    public static void AfterCardPileChanged()
    {
        try { CombatExporter.RequestExport(); }
        catch (Exception ex) { Log.Error($"[SmartPick] CardPileChanged patch: {ex.Message}"); }
    }

    [HarmonyPatch(typeof(NMerchantInventory), nameof(NMerchantInventory.Open))]
    [HarmonyPostfix]
    public static void AfterMerchantOpen(NMerchantInventory __instance)
    {
        try
        {
            CombatExporter.SetMerchantRelics(__instance.Inventory);

            var cardTitles = MerchantInventoryOffers.ExtractStockedCardTitles(__instance.Inventory);
            // Expect ~7 cards (5 class + 2 colorless); allow wider band if layout changes.
            if (cardTitles.Count is >= 4 and <= 12)
            {
                RewardExporter.ExportRewardState(cardTitles, "merchant_cards");
                Log.Info($"[SmartPick] Merchant cards exported: {cardTitles.Count} — {string.Join(", ", cardTitles)}");
            }
            else if (cardTitles.Count > 0)
            {
                Log.Info($"[SmartPick] Merchant card export skipped ({cardTitles.Count} titles, expected 4–12): {string.Join(", ", cardTitles)}");
            }
        }
        catch (Exception ex)
        {
            Log.Error($"[SmartPick] Merchant open patch: {ex.Message}");
        }
    }

    [HarmonyPatch(typeof(NMerchantInventory), "Close")]
    [HarmonyPostfix]
    public static void AfterMerchantClose()
    {
        try { CombatExporter.ClearMerchant(); }
        catch (Exception ex) { Log.Error($"[SmartPick] Merchant close clear: {ex.Message}"); }
        try { RewardExporter.ClearRewardState(); }
        catch (Exception ex) { Log.Error($"[SmartPick] Merchant close clear reward: {ex.Message}"); }
    }
}
