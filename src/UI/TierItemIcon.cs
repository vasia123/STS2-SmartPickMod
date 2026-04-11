using Godot;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Relics;

namespace FirstMod.UI;

/// <summary>
/// A draggable item icon in the tier editor. Supports cards and relics.
/// Uses game's hover tip system for tooltips.
/// Implements Godot drag-and-drop via _GetDragData.
/// </summary>
public partial class TierItemIcon : PanelContainer
{
    public enum Type { Card, Relic }

    public string ItemId { get; set; } = "";
    public Type ItemType { get; set; }
    public string Character { get; set; } = "";

    /// <summary>Hover tips from the game model (CardModel.HoverTips or RelicModel.HoverTips).</summary>
    public IEnumerable<IHoverTip>? HoverTips { get; set; }

    /// <summary>Fired on mouse enter with the source control (for card preview).</summary>
    public event Action<Control>? ItemHoverEntered;
    /// <summary>Fired on mouse exit.</summary>
    public event Action? ItemHoverExited;
    /// <summary>Fired when drag starts (so parent can keep preview visible).</summary>
    public event Action<Control>? ItemDragStarted;
    /// <summary>Fired when drag ends.</summary>
    public event Action? ItemDragEnded;

    private bool _isDragging;

    /// <summary>Global flag: some item is being dragged, suppress hover on others.</summary>
    public static bool AnyDragging;

    private string _displayName;
    private NRelic? _relicIcon;

    private static readonly StyleBoxFlat NormalStyle = MakeStyle(false);
    private static readonly StyleBoxFlat HoverStyle = MakeStyle(true);

    public TierItemIcon(string displayName, NRelic? relicIcon)
    {
        _displayName = displayName;
        _relicIcon = relicIcon;
    }

    public void BuildUI()
    {
        CustomMinimumSize = new Vector2(160, 34);
        AddThemeStyleboxOverride("panel", NormalStyle);
        MouseFilter = MouseFilterEnum.Stop;

        var hbox = new HBoxContainer();
        hbox.AddThemeConstantOverride("separation", 5);
        hbox.MouseFilter = MouseFilterEnum.Ignore;

        // Relic icon (if available)
        if (_relicIcon != null)
        {
            var iconContainer = new Control();
            iconContainer.CustomMinimumSize = new Vector2(28, 28);
            iconContainer.Size = new Vector2(28, 28);
            iconContainer.ClipContents = true;
            iconContainer.MouseFilter = MouseFilterEnum.Ignore;
            DisableMouseRecursive(_relicIcon);
            iconContainer.AddChild(_relicIcon);
            hbox.AddChild(iconContainer);
        }

        var label = new Label();
        label.Text = _displayName;
        label.AddThemeFontSizeOverride("font_size", 12);
        label.AddThemeColorOverride("font_color", new Color(0.9f, 0.85f, 0.75f));
        label.MouseFilter = MouseFilterEnum.Ignore;
        label.ClipText = true;
        label.CustomMinimumSize = new Vector2(80, 0);
        label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        hbox.AddChild(label);

        AddChild(hbox);

        // Hover: highlight + game hover tips + card preview event
        MouseEntered += () =>
        {
            if (AnyDragging && !_isDragging) return; // skip hover on others during drag
            AddThemeStyleboxOverride("panel", HoverStyle);
            ShowGameHoverTips();
            ItemHoverEntered?.Invoke(this);
        };
        MouseExited += () =>
        {
            // Always remove highlight
            AddThemeStyleboxOverride("panel", NormalStyle);
            // But only hide tips/preview if not the dragged item
            if (!_isDragging)
            {
                HideGameHoverTips();
                ItemHoverExited?.Invoke();
            }
        };
    }

    private void ShowGameHoverTips()
    {
        if (HoverTips == null) return;
        // Cards show hover tips via TierEditorScreen (positioned next to the floating card preview).
        // Only relics show them from here (aligned to the right of the icon).
        if (ItemType == Type.Card) return;
        try
        {
            var tipSet = NHoverTipSet.CreateAndShow(this, HoverTips);
            tipSet.SetAlignment(this, MegaCrit.Sts2.Core.HoverTips.HoverTipAlignment.Right);
        }
        catch (Exception ex)
        {
            MegaCrit.Sts2.Core.Logging.Log.Error($"[SmartPick] ShowGameHoverTips: {ex.Message}\n{ex.StackTrace}");
        }
    }

    private void HideGameHoverTips()
    {
        try
        {
            NHoverTipSet.Remove(this);
        }
        catch { }
    }

    public override void _Notification(int what)
    {
        if (what == NotificationDragEnd && _isDragging)
        {
            _isDragging = false;
            AnyDragging = false;
            HideGameHoverTips();
            ItemDragEnded?.Invoke();
        }
    }

    public override Variant _GetDragData(Vector2 atPosition)
    {
        _isDragging = true;
        AnyDragging = true;
        HideGameHoverTips();
        ItemDragStarted?.Invoke(this);

        // Create a visual copy of this item as drag preview
        var preview = new PanelContainer();
        preview.CustomMinimumSize = new Vector2(160, 34);
        preview.AddThemeStyleboxOverride("panel", MakeStyle(true));
        preview.Modulate = new Color(1f, 1f, 1f, 0.85f);

        var hbox = new HBoxContainer();
        hbox.AddThemeConstantOverride("separation", 5);

        // Relic icon copy
        if (_relicIcon != null && ItemType == Type.Relic)
        {
            try
            {
                // Can't clone NRelic, use a colored rect as placeholder
                var iconRect = new ColorRect();
                iconRect.CustomMinimumSize = new Vector2(28, 28);
                iconRect.Color = new Color(0.4f, 0.35f, 0.5f);
                hbox.AddChild(iconRect);
            }
            catch { }
        }

        var label = new Label();
        label.Text = _displayName;
        label.AddThemeFontSizeOverride("font_size", 14);
        label.AddThemeColorOverride("font_color", new Color(1f, 0.9f, 0.6f));
        label.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f));
        label.AddThemeConstantOverride("outline_size", 4);
        label.ClipText = true;
        hbox.AddChild(label);

        preview.AddChild(hbox);
        SetDragPreview(preview);

        var data = new Godot.Collections.Dictionary
        {
            ["item_id"] = ItemId,
            ["item_type"] = (int)ItemType,
            ["character"] = Character,
            ["display_name"] = _displayName,
        };
        return data;
    }

    // Accept drops on top of items — forward to parent TierRow
    public override bool _CanDropData(Vector2 atPosition, Variant data)
    {
        var parent = GetParent()?.GetParent();
        if (parent is TierDropTarget dropTarget)
        {
            // Convert to TierDropTarget-relative position
            var globalPos = GlobalPosition + atPosition;
            var relativePos = globalPos - dropTarget.GlobalPosition;
            return dropTarget._CanDropData(relativePos, data);
        }
        return false;
    }

    public override void _DropData(Vector2 atPosition, Variant data)
    {
        var parent = GetParent()?.GetParent();
        if (parent is TierDropTarget dropTarget)
        {
            // Convert to TierDropTarget-relative position
            var globalPos = GlobalPosition + atPosition;
            var relativePos = globalPos - dropTarget.GlobalPosition;
            dropTarget._DropData(relativePos, data);
        }
    }

    private static void DisableMouseRecursive(Node node)
    {
        if (node is Control ctrl)
            ctrl.MouseFilter = MouseFilterEnum.Ignore;
        foreach (var child in node.GetChildren())
            DisableMouseRecursive(child);
    }

    private static StyleBoxFlat MakeStyle(bool hover)
    {
        var style = new StyleBoxFlat();
        style.BgColor = hover ? new Color(0.28f, 0.24f, 0.38f, 0.95f) : new Color(0.15f, 0.13f, 0.2f, 0.9f);
        style.CornerRadiusBottomLeft = 4;
        style.CornerRadiusBottomRight = 4;
        style.CornerRadiusTopLeft = 4;
        style.CornerRadiusTopRight = 4;
        style.BorderWidthBottom = hover ? 2 : 1;
        style.BorderWidthTop = hover ? 2 : 1;
        style.BorderWidthLeft = hover ? 2 : 1;
        style.BorderWidthRight = hover ? 2 : 1;
        style.BorderColor = hover ? new Color(0.7f, 0.6f, 0.3f) : new Color(0.25f, 0.22f, 0.3f);
        style.ContentMarginLeft = 6;
        style.ContentMarginRight = 6;
        style.ContentMarginTop = 3;
        style.ContentMarginBottom = 3;
        return style;
    }
}
