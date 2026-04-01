using FirstMod.UI;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Debug;

namespace FirstMod.Patches;

[HarmonyPatch(typeof(NGame), "_Ready")]
public static class TierEditorInstallPatch
{
    private static bool _polling;
    private static bool _f7WasPressed;
    private static CanvasLayer? _canvasLayer;
    private static Button? _openButton;
    private static bool _editorOpen;

    [HarmonyPostfix]
    public static void AfterReady(NGame __instance)
    {
        EnsurePolling(__instance);
    }

    public static void EnsurePolling(NGame game)
    {
        if (_polling) return;
        _polling = true;
        StartPolling(game);
        Log.Info("[SmartPick] F7 hotkey polling started");
    }

    public static void SetOpenButtonVisible(bool visible)
    {
        _editorOpen = !visible;
    }

    private static void TryCreateButton(NGame game)
    {
        if (_openButton != null && GodotObject.IsInstanceValid(_openButton))
            return;

        var releaseLabel = FindReleaseInfoLabel(game);
        if (releaseLabel == null) return;

        try
        {
            var layer = new CanvasLayer();
            layer.Name = "SmartPickButtonLayer";
            layer.Layer = 10;
            game.GetTree().Root.AddChild(layer);

            var btn = new Button();
            btn.Name = "SmartPickOpenBtn";
            btn.Text = "SmartPick Tiers (F7)";

            var styleNormal = new StyleBoxFlat();
            styleNormal.BgColor = new Color(0.12f, 0.1f, 0.18f, 0.8f);
            styleNormal.SetCornerRadiusAll(4);
            styleNormal.SetContentMarginAll(4);
            styleNormal.ContentMarginLeft = 8;
            styleNormal.ContentMarginRight = 8;
            btn.AddThemeStyleboxOverride("normal", styleNormal);

            var styleHover = new StyleBoxFlat();
            styleHover.BgColor = new Color(0.22f, 0.18f, 0.32f, 0.9f);
            styleHover.SetCornerRadiusAll(4);
            styleHover.SetContentMarginAll(4);
            styleHover.ContentMarginLeft = 8;
            styleHover.ContentMarginRight = 8;
            btn.AddThemeStyleboxOverride("hover", styleHover);

            var stylePressed = new StyleBoxFlat();
            stylePressed.BgColor = new Color(0.3f, 0.25f, 0.45f, 0.95f);
            stylePressed.SetCornerRadiusAll(4);
            stylePressed.SetContentMarginAll(4);
            stylePressed.ContentMarginLeft = 8;
            stylePressed.ContentMarginRight = 8;
            btn.AddThemeStyleboxOverride("pressed", stylePressed);

            btn.AddThemeFontSizeOverride("font_size", 14);
            btn.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.5f));
            btn.AddThemeColorOverride("font_hover_color", new Color(1f, 0.95f, 0.7f));

            btn.MouseFilter = Control.MouseFilterEnum.Stop;
            btn.Pressed += () => TierEditorScreen.Toggle();
            btn.Size = new Vector2(180, 28);

            layer.AddChild(btn);

            _canvasLayer = layer;
            _openButton = btn;
            RepositionButton(releaseLabel);
            Log.Info($"[SmartPick] Open button created at ({btn.Position.X}, {btn.Position.Y})");
        }
        catch (Exception ex)
        {
            Log.Error($"[SmartPick] CreateOpenButton: {ex.Message}");
        }
    }

    private static Control? FindReleaseInfoLabel(NGame game)
    {
        var managers = FindNodesOfType<NDebugInfoLabelManager>(game.GetTree().Root);
        NDebugInfoLabelManager? target = null;
        foreach (var mgr in managers)
        {
            if (mgr.isMainMenu) { target = mgr; break; }
        }
        target ??= managers.Count > 0 ? managers[0] : null;
        return target?.GetNodeOrNull("%ReleaseInfo") as Control;
    }

    private static void RepositionButton(Control releaseLabel)
    {
        if (_openButton == null) return;
        var anchorGlobal = releaseLabel.GlobalPosition;
        var anchorSize = releaseLabel.Size;
        float rightEdge = anchorGlobal.X + anchorSize.X;
        _openButton.Position = new Vector2(rightEdge - _openButton.Size.X, anchorGlobal.Y + anchorSize.Y + 20);
    }

    private static List<T> FindNodesOfType<T>(Node root) where T : Node
    {
        var result = new List<T>();
        var stack = new Stack<Node>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (node is T typed) result.Add(typed);
            foreach (var child in node.GetChildren())
                stack.Push(child);
        }
        return result;
    }

    private static async void StartPolling(NGame game)
    {
        while (GodotObject.IsInstanceValid(game))
        {
            try
            {
                await game.ToSignal(game.GetTree().CreateTimer(0.1), "timeout");

                TryCreateButton(game);

                // Reposition every tick — anchor moves between main menu and in-game
                if (_openButton != null && GodotObject.IsInstanceValid(_openButton))
                {
                    var releaseLabel = FindReleaseInfoLabel(game);
                    if (releaseLabel != null)
                        RepositionButton(releaseLabel);

                    // Force CanvasLayer visible every tick (something external resets it)
                    _canvasLayer!.Visible = true;

                    // Hide button during: editor open or screen transitions
                    bool transitionActive = game.Transition != null
                        && GodotObject.IsInstanceValid(game.Transition)
                        && game.Transition.InTransition;
                    _openButton.Visible = !_editorOpen && !transitionActive;
                }

                // F7 hotkey
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
