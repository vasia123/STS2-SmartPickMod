using Godot;
using MegaCrit.Sts2.Core.Logging;

namespace FirstMod.UI;

/// <summary>
/// A single tier row in the editor: colored label + flow grid of item icons.
/// Acts as a drop target for drag-and-drop.
/// </summary>
public partial class TierRow : HBoxContainer
{
    private static readonly Dictionary<string, Color> TierColors = new()
    {
        ["S"] = new Color("ff8000"),
        ["A"] = new Color("a335ee"),
        ["B"] = new Color("0070dd"),
        ["C"] = new Color("1eff00"),
        ["D"] = new Color("9d9d9d"),
        ["?"] = new Color("666666"),
    };

    public string Tier { get; }
    private HFlowContainer _flow = null!;
    private PanelContainer _flowBg = null!;
    private StyleBoxFlat _normalFlowStyle = null!;
    private StyleBoxFlat _highlightFlowStyle = null!;

    /// <summary>Called when an item is dropped into this row.</summary>
    public event Action<string, TierItemIcon.Type, string>? ItemDropped;

    public TierRow(string tier)
    {
        Tier = tier;
    }

    public void BuildUI()
    {
        AddThemeConstantOverride("separation", 4);
        SizeFlagsHorizontal = SizeFlags.ExpandFill;

        // Tier label (colored square)
        var labelBg = new PanelContainer();
        labelBg.CustomMinimumSize = new Vector2(60, 60);
        var style = new StyleBoxFlat();
        var color = TierColors.GetValueOrDefault(Tier, TierColors["?"]);
        style.BgColor = new Color(color, 0.3f);
        style.BorderWidthBottom = 2;
        style.BorderWidthTop = 2;
        style.BorderWidthLeft = 2;
        style.BorderWidthRight = 2;
        style.BorderColor = color;
        style.CornerRadiusBottomLeft = 6;
        style.CornerRadiusBottomRight = 6;
        style.CornerRadiusTopLeft = 6;
        style.CornerRadiusTopRight = 6;
        labelBg.AddThemeStyleboxOverride("panel", style);
        labelBg.SizeFlagsVertical = SizeFlags.ShrinkCenter;

        var label = new Label();
        label.Text = Tier;
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.VerticalAlignment = VerticalAlignment.Center;
        label.AddThemeFontSizeOverride("font_size", 28);
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0));
        label.AddThemeConstantOverride("outline_size", 4);
        labelBg.AddChild(label);
        AddChild(labelBg);

        // Flow container for item icons — also a drop target
        _flowBg = new TierDropTarget(this);
        _flowBg.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _flowBg.CustomMinimumSize = new Vector2(0, 44);

        _normalFlowStyle = MakeFlowStyle(false);
        _highlightFlowStyle = MakeFlowStyle(true);
        _flowBg.AddThemeStyleboxOverride("panel", _normalFlowStyle);

        _flow = new HFlowContainer();
        _flow.AddThemeConstantOverride("h_separation", 4);
        _flow.AddThemeConstantOverride("v_separation", 4);
        _flow.MouseFilter = MouseFilterEnum.Ignore;
        _flowBg.AddChild(_flow);
        AddChild(_flowBg);
    }

    public void HighlightDrop(bool on)
    {
        _flowBg?.AddThemeStyleboxOverride("panel", on ? _highlightFlowStyle : _normalFlowStyle);
    }

    public void AddItem(Control icon)
    {
        _flow.AddChild(icon);
        if (icon is TierItemIcon item)
            item.BuildUI();
    }

    public void ClearItems()
    {
        if (_flow == null) return;
        foreach (var child in _flow.GetChildren())
            child.QueueFree();
    }

    public void OnItemDropped(Godot.Collections.Dictionary data)
    {
        var itemId = data["item_id"].AsString();
        var itemType = (TierItemIcon.Type)data["item_type"].AsInt32();
        var character = data["character"].AsString();
        ItemDropped?.Invoke(itemId, itemType, character);
    }

    public int ItemCount => _flow?.GetChildCount() ?? 0;

    private static StyleBoxFlat MakeFlowStyle(bool highlight)
    {
        var s = new StyleBoxFlat();
        s.BgColor = highlight ? new Color(0.2f, 0.18f, 0.3f, 0.9f) : new Color(0.12f, 0.10f, 0.18f, 0.6f);
        s.CornerRadiusBottomLeft = 4;
        s.CornerRadiusBottomRight = 4;
        s.CornerRadiusTopLeft = 4;
        s.CornerRadiusTopRight = 4;
        s.BorderWidthBottom = highlight ? 2 : 0;
        s.BorderWidthTop = highlight ? 2 : 0;
        s.BorderWidthLeft = highlight ? 2 : 0;
        s.BorderWidthRight = highlight ? 2 : 0;
        s.BorderColor = new Color(0.8f, 0.7f, 0.3f);
        s.ContentMarginLeft = 4;
        s.ContentMarginRight = 4;
        s.ContentMarginTop = 6;
        s.ContentMarginBottom = 6;
        return s;
    }
}

/// <summary>
/// Drop target panel that accepts TierItemIcon drag data.
/// </summary>
public partial class TierDropTarget : PanelContainer
{
    private TierRow _row;

    public TierDropTarget(TierRow row)
    {
        _row = row;
    }

    /// <summary>All active drop targets, for clearing highlights globally.</summary>
    private static readonly List<TierDropTarget> _allTargets = new();
    private bool _isHighlighted;

    public override void _EnterTree() { _allTargets.Add(this); }
    public override void _ExitTree() { _allTargets.Remove(this); }

    private static void ClearAllHighlights()
    {
        foreach (var t in _allTargets)
        {
            if (t._isHighlighted)
            {
                t._isHighlighted = false;
                t._row.HighlightDrop(false);
            }
        }
    }

    public override bool _CanDropData(Vector2 atPosition, Variant data)
    {
        if (data.VariantType == Variant.Type.Dictionary)
        {
            var dict = data.AsGodotDictionary();
            if (dict.ContainsKey("item_id"))
            {
                if (!_isHighlighted)
                {
                    ClearAllHighlights();
                    _isHighlighted = true;
                    _row.HighlightDrop(true);
                }
                return true;
            }
        }
        return false;
    }

    public override void _DropData(Vector2 atPosition, Variant data)
    {
        _isHighlighted = false;
        _row.HighlightDrop(false);
        if (data.VariantType == Variant.Type.Dictionary)
        {
            _row.OnItemDropped(data.AsGodotDictionary());
        }
    }

    public override void _Notification(int what)
    {
        if (what == NotificationDragEnd)
        {
            _isHighlighted = false;
            _row.HighlightDrop(false);
        }
        // Remove highlight when mouse exits during drag
        if (what == NotificationMouseExit && _isHighlighted)
        {
            _isHighlighted = false;
            _row.HighlightDrop(false);
        }
    }
}
