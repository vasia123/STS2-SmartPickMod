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
    private Control? _placeholder;
    private int _placeholderIndex = -1;

    /// <summary>Called when an item is dropped into this row. Args: itemId, itemType, character, insertIndex.</summary>
    public event Action<string, TierItemIcon.Type, string, int>? ItemDropped;

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
        if (!on) RemovePlaceholder();
    }

    /// <summary>Show a placeholder gap at the insertion position, shifting other items.</summary>
    public void UpdatePlaceholder(Vector2 globalDropPos)
    {
        if (_flow == null) return;

        int newIndex = CalculateInsertIndex(globalDropPos);
        if (newIndex < 0) newIndex = _flow.GetChildCount();

        // Account for placeholder already being in the flow
        if (_placeholder != null && _placeholder.GetParent() == _flow)
        {
            int currentIdx = _placeholder.GetIndex();
            if (currentIdx == newIndex || currentIdx == newIndex - 1)
                return; // already in the right spot
            _flow.RemoveChild(_placeholder);
        }

        if (_placeholder == null)
        {
            _placeholder = new PanelContainer();
            _placeholder.CustomMinimumSize = new Vector2(120, 34);
            var style = new StyleBoxFlat();
            style.BgColor = new Color(1f, 0.85f, 0.3f, 0.15f);
            style.CornerRadiusBottomLeft = 4;
            style.CornerRadiusBottomRight = 4;
            style.CornerRadiusTopLeft = 4;
            style.CornerRadiusTopRight = 4;
            style.BorderWidthBottom = 2;
            style.BorderWidthTop = 2;
            style.BorderWidthLeft = 2;
            style.BorderWidthRight = 2;
            style.BorderColor = new Color(1f, 0.85f, 0.3f, 0.6f);
            ((PanelContainer)_placeholder).AddThemeStyleboxOverride("panel", style);
            _placeholder.MouseFilter = MouseFilterEnum.Ignore;
        }

        // Clamp index
        var childCount = _flow.GetChildCount();
        if (newIndex > childCount) newIndex = childCount;

        _flow.AddChild(_placeholder);
        _flow.MoveChild(_placeholder, newIndex);
        _placeholderIndex = newIndex;

        // Calculate real insert index (excluding placeholder)
        int realIndex = 0;
        for (int i = 0; i < _flow.GetChildCount(); i++)
        {
            if (_flow.GetChild(i) == _placeholder)
            {
                _lastRealInsertIndex = realIndex;
                break;
            }
            realIndex++;
        }
    }

    public void RemovePlaceholder()
    {
        if (_placeholder != null && _placeholder.GetParent() == _flow)
            _flow.RemoveChild(_placeholder);
        _placeholderIndex = -1;
    }

    private int _lastRealInsertIndex = -1;

    /// <summary>Get the logical insert index (excluding the placeholder itself).</summary>
    public int GetPlaceholderInsertIndex()
    {
        return _lastRealInsertIndex;
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

    public void OnItemDropped(Godot.Collections.Dictionary data, Vector2 globalDropPosition)
    {
        var itemId = data["item_id"].AsString();
        var itemType = (TierItemIcon.Type)data["item_type"].AsInt32();
        var character = data["character"].AsString();

        int insertAt = GetPlaceholderInsertIndex();
        RemovePlaceholder();
        ItemDropped?.Invoke(itemId, itemType, character, insertAt);
    }

    private int CalculateInsertIndex(Vector2 globalPos)
    {
        if (_flow == null) return -1;

        // Find the closest item considering both rows (Y) and columns (X)
        int bestIndex = _flow.GetChildCount();

        for (int i = 0; i < _flow.GetChildCount(); i++)
        {
            var child = _flow.GetChild<Control>(i);
            if (child == _placeholder) continue;

            var childCenter = child.GlobalPosition + child.Size / 2;

            // If cursor is on the same row (Y within child height)
            if (globalPos.Y >= child.GlobalPosition.Y && globalPos.Y <= child.GlobalPosition.Y + child.Size.Y)
            {
                // Insert before this child if cursor is left of its center
                if (globalPos.X < childCenter.X)
                    return i;
            }
            // If cursor is above this child's row — insert before it
            else if (globalPos.Y < child.GlobalPosition.Y)
            {
                return i;
            }
        }

        return bestIndex; // append at end
    }

    public int ItemCount => _flow?.GetChildCount() ?? 0;

    public IEnumerable<Node> GetFlowChildren()
    {
        if (_flow == null) yield break;
        foreach (var child in _flow.GetChildren())
            yield return child;
    }

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
                _row.UpdatePlaceholder(GlobalPosition + atPosition);
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
            _row.OnItemDropped(data.AsGodotDictionary(), GlobalPosition + atPosition);
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
