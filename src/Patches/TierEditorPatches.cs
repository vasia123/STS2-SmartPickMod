using FirstMod.UI;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes;

namespace FirstMod.Patches;

/// <summary>
/// Polls for F7 key press every frame using a background timer.
/// Godot doesn't call virtual _Input/_Process on dynamically created C# nodes,
/// so we use a SceneTreeTimer + Input.IsKeyPressed polling approach.
/// </summary>
[HarmonyPatch(typeof(NGame), "_Ready")]
public static class TierEditorInstallPatch
{
    private static bool _polling;
    private static bool _f7WasPressed;

    [HarmonyPostfix]
    public static void AfterReady(NGame __instance)
    {
        if (_polling) return;
        _polling = true;
        StartPolling(__instance);
        Log.Info("[SmartPick] F7 hotkey polling started");
    }

    private static async void StartPolling(NGame game)
    {
        while (GodotObject.IsInstanceValid(game))
        {
            try
            {
                await game.ToSignal(game.GetTree().CreateTimer(0.1), "timeout");

                bool f7Pressed = Input.IsKeyPressed(Key.F7);
                if (f7Pressed && !_f7WasPressed)
                {
                    TierEditorScreen.Toggle();
                }
                _f7WasPressed = f7Pressed;
            }
            catch { break; }
        }
        _polling = false;
    }
}
