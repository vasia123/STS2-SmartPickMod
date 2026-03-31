using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.UI;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Relics;
namespace FirstMod.UI;

/// <summary>
/// In-game tier list editor with drag-and-drop.
/// Opened via F7 hotkey. Added directly to scene root.
/// </summary>
public partial class TierEditorScreen : Control
{
    private static readonly Color BgColor = new(0.08f, 0.06f, 0.12f, 0.95f);

    private enum Tab { Cards, Relics }
    private Tab _currentTab = Tab.Cards;
    private string _currentCharacter = "";

    // Built dynamically from ModelDb.AllCharacters
    private readonly List<(string poolTitle, string localizedName)> _characters = new();

    private VBoxContainer _rowsContainer = null!;
    private Control _cardPreview = null!;
    private Control? _previewHolder;
    private readonly Dictionary<string, TierRow> _tierRows = new();
    private TierRow _unassignedRow = null!;

    private static TierEditorScreen? _instance;

    public static void Toggle()
    {
        if (_instance != null && GodotObject.IsInstanceValid(_instance))
        {
            _instance.OnClose();
            return;
        }

        try
        {
            var screen = new TierEditorScreen();
            screen.Name = "SmartPickTierEditor";

            // Add to NGame BEFORE HoverTipsContainer so hover tips render on top of us
            var root = ((SceneTree)Engine.GetMainLoop()).Root;
            if (NGame.Instance != null && GodotObject.IsInstanceValid(NGame.Instance)
                && NGame.Instance.HoverTipsContainer is Node hoverContainer)
            {
                NGame.Instance.AddChild(screen);
                NGame.Instance.MoveChild(screen, hoverContainer.GetIndex());
            }
            else
            {
                root.AddChild(screen);
            }
            _instance = screen;
            // _Ready() may not be called by Godot on unregistered C# classes,
            // so we call BuildUI manually after adding to tree
            screen.BuildUI();
        }
        catch (Exception ex)
        {
            _instance = null;
            Log.Error($"[SmartPick] TierEditor Open: {ex.Message}\n{ex.StackTrace}");
        }
    }

    private HBoxContainer? _charFilter;

    public void BuildUI()
    {
        // Build character list from game data
        foreach (var ch in ModelDb.AllCharacters)
        {
            var poolTitle = ch.CardPool?.Title?.ToLowerInvariant() ?? ch.Id.Entry.ToLowerInvariant();
            string localName;
            try { localName = ch.Title.GetFormattedText(); }
            catch { localName = poolTitle; }
            _characters.Add((poolTitle, localName));
        }
        if (_characters.Count > 0)
            _currentCharacter = _characters[0].poolTitle;

        // Full-screen: use explicit size from viewport
        var viewportSize = GetViewport().GetVisibleRect().Size;
        Position = Vector2.Zero;
        Size = viewportSize;
        MouseFilter = MouseFilterEnum.Stop;

        // Background
        var bg = new ColorRect();
        bg.Color = BgColor;
        bg.Position = Vector2.Zero;
        bg.Size = viewportSize;
        bg.MouseFilter = MouseFilterEnum.Stop;
        AddChild(bg);

        // Main layout
        var margin = new MarginContainer();
        margin.Position = Vector2.Zero;
        margin.Size = viewportSize;
        margin.AddThemeConstantOverride("margin_left", 40);
        margin.AddThemeConstantOverride("margin_right", 40);
        margin.AddThemeConstantOverride("margin_top", 20);
        margin.AddThemeConstantOverride("margin_bottom", 20);
        AddChild(margin);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 10);
        margin.AddChild(vbox);

        // Header
        BuildHeader(vbox);

        // Scrollable tier rows
        var scroll = new ScrollContainer();
        scroll.SizeFlagsVertical = SizeFlags.ExpandFill;
        scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        vbox.AddChild(scroll);

        _rowsContainer = new VBoxContainer();
        _rowsContainer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _rowsContainer.AddThemeConstantOverride("separation", 0);
        scroll.AddChild(_rowsContainer);

        // Build tier rows with drop handlers
        foreach (var tier in TierData.TierLetters)
        {
            var row = new TierRow(tier);
            _tierRows[tier] = row;
            _rowsContainer.AddChild(row);
            row.BuildUI();
            row.ItemDropped += (itemId, itemType, character) => OnItemDroppedToTier(itemId, itemType, character, tier);
        }
        _unassignedRow = new TierRow("?");
        _rowsContainer.AddChild(_unassignedRow);
        _unassignedRow.BuildUI();
        _unassignedRow.ItemDropped += (itemId, itemType, character) => OnItemRemovedFromTier(itemId, itemType, character);

        // Card preview overlay (shown on hover, on top of everything)
        _cardPreview = new Control();
        _cardPreview.Name = "CardPreview";
        _cardPreview.MouseFilter = MouseFilterEnum.Ignore;
        _cardPreview.Visible = false;
        _cardPreview.ZIndex = 100;
        AddChild(_cardPreview);

        // Footer
        BuildFooter(vbox);

        // Populate
        PopulateItems();
    }

    private void BuildHeader(VBoxContainer parent)
    {
        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 20);
        parent.AddChild(header);

        // Title
        var title = new Label();
        title.Text = "SmartPick Tier Editor";
        title.AddThemeFontSizeOverride("font_size", 28);
        title.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.5f));
        header.AddChild(title);

        // Spacer
        var spacer = new Control();
        spacer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        header.AddChild(spacer);

        // Tab buttons
        var cardsBtn = CreateTabButton("Cards", Tab.Cards);
        var relicsBtn = CreateTabButton("Relics", Tab.Relics);
        header.AddChild(cardsBtn);
        header.AddChild(relicsBtn);

        // Character filter (below header, only for cards)
        _charFilter = new HBoxContainer();
        _charFilter.Name = "CharFilter";
        _charFilter.AddThemeConstantOverride("separation", 8);
        parent.AddChild(_charFilter);
        var charFilter = _charFilter;

        foreach (var (poolTitle, localName) in _characters)
        {
            var btn = new Button();
            btn.Text = localName;
            btn.ToggleMode = true;
            btn.ButtonPressed = poolTitle == _currentCharacter;
            btn.CustomMinimumSize = new Vector2(140, 36);
            btn.AddThemeFontSizeOverride("font_size", 16);
            var charId = poolTitle;
            btn.Pressed += () => OnCharacterSelected(charId);
            charFilter.AddChild(btn);
        }
    }

    private Button CreateTabButton(string text, Tab tab)
    {
        var btn = new Button();
        btn.Text = text;
        btn.ToggleMode = true;
        btn.ButtonPressed = _currentTab == tab;
        btn.CustomMinimumSize = new Vector2(100, 40);
        btn.AddThemeFontSizeOverride("font_size", 18);
        btn.Pressed += () => OnTabSelected(tab);
        return btn;
    }

    private void BuildFooter(VBoxContainer parent)
    {
        var footer = new HBoxContainer();
        footer.AddThemeConstantOverride("separation", 15);
        parent.AddChild(footer);

        var saveBtn = new Button();
        saveBtn.Text = "Save";
        saveBtn.CustomMinimumSize = new Vector2(120, 40);
        saveBtn.AddThemeFontSizeOverride("font_size", 18);
        saveBtn.Pressed += OnSave;
        footer.AddChild(saveBtn);

        var resetBtn = new Button();
        resetBtn.Text = "Reset to Default";
        resetBtn.CustomMinimumSize = new Vector2(180, 40);
        resetBtn.AddThemeFontSizeOverride("font_size", 18);
        resetBtn.Pressed += OnReset;
        footer.AddChild(resetBtn);

        var spacer = new Control();
        spacer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        footer.AddChild(spacer);

        var closeBtn = new Button();
        closeBtn.Text = "Close";
        closeBtn.CustomMinimumSize = new Vector2(120, 40);
        closeBtn.AddThemeFontSizeOverride("font_size", 18);
        closeBtn.Pressed += OnClose;
        footer.AddChild(closeBtn);
    }

    private void PopulateItems()
    {
        // Clear existing
        foreach (var row in _tierRows.Values)
            row.ClearItems();
        _unassignedRow.ClearItems();

        if (_currentTab == Tab.Cards)
            PopulateCards();
        else
            PopulateRelics();
    }

    private void PopulateCards()
    {
        try
        {
            var allCards = ModelDb.AllCards
                .Where(c => c.Pool?.Title?.ToLowerInvariant() == _currentCharacter)
                .OrderBy(c => c.Id.Entry)
                .ToList();

            foreach (var card in allCards)
            {
                var tiers = TierData.GetTiers(_currentCharacter, CardBadgeOverlay.NormalizeCardIdPublic(card.Id.Entry));
                var targetTier = tiers.BlendedTier;
                var icon = CreateCardIcon(card);
                if (icon == null) continue;

                if (_tierRows.TryGetValue(targetTier, out var row))
                    row.AddItem(icon);
                else
                    _unassignedRow.AddItem(icon);
            }
        }
        catch (Exception ex)
        {
            Log.Error($"[SmartPick] TierEditor PopulateCards: {ex.Message}\n{ex.StackTrace}");
        }
    }

    private void PopulateRelics()
    {
        try
        {
            var allRelics = ModelDb.AllRelics.OrderBy(r => r.Id.Entry).ToList();

            foreach (var relic in allRelics)
            {
                var relicName = RelicTierData.IdEntryToDisplayName(relic.Id.Entry);
                var tier = RelicTierData.GetTier(relicName);
                var icon = CreateRelicIcon(relic);
                if (icon == null) continue;

                if (tier != null && _tierRows.TryGetValue(tier.Tier, out var row))
                    row.AddItem(icon);
                else
                    _unassignedRow.AddItem(icon);
            }
        }
        catch (Exception ex)
        {
            Log.Error($"[SmartPick] TierEditor PopulateRelics: {ex.Message}\n{ex.StackTrace}");
        }
    }

    private Control? CreateCardIcon(CardModel card)
    {
        try
        {
            string displayName;
            try { displayName = card.Title; }
            catch { displayName = CardBadgeOverlay.NormalizeCardIdPublic(card.Id.Entry); }

            var icon = new TierItemIcon(displayName, null);
            icon.ItemId = card.Id.Entry;
            icon.ItemType = TierItemIcon.Type.Card;
            icon.Character = _currentCharacter;

            // Card hover tips are shown from the NCard preview, not from the icon
            icon.ItemHoverEntered += (source) => ShowCardPreview(card, source);
            icon.ItemHoverExited += () => HideCardPreview();
            // Keep card preview during drag
            icon.ItemDragStarted += (source) => ShowCardPreview(card, source);
            icon.ItemDragEnded += () => HideCardPreview();

            return icon;
        }
        catch (Exception ex)
        {
            Log.Error($"[SmartPick] CreateCardIcon: {ex.Message}");
            return null;
        }
    }

    private Control? CreateRelicIcon(RelicModel relic)
    {
        try
        {
            string displayName;
            try { displayName = relic.Title.GetFormattedText(); }
            catch { displayName = RelicTierData.IdEntryToDisplayName(relic.Id.Entry); }

            NRelic? nRelic = null;
            try { nRelic = NRelic.Create(relic, NRelic.IconSize.Small); }
            catch { }

            var icon = new TierItemIcon(displayName, nRelic);
            icon.ItemId = relic.Id.Entry;
            icon.ItemType = TierItemIcon.Type.Relic;

            // Game hover tips for relic details
            try
            {
                var tips = relic.HoverTips?.ToList();
                icon.HoverTips = tips;

            }
            catch (Exception ex)
            {
                Log.Error($"[SmartPick] CreateRelicIcon HoverTips failed for {relic.Id.Entry}: {ex.Message}");
            }

            return icon;
        }
        catch (Exception ex)
        {
            Log.Error($"[SmartPick] CreateRelicIcon: {ex.Message}");
            return null;
        }
    }

    private void ShowCardPreview(CardModel card, Control source)
    {
        foreach (var child in _cardPreview.GetChildren())
            child.QueueFree();

        try
        {
            // Try canonical first (like NCardLibraryGrid does), then mutable clone as fallback
            NCard? nCard = null;
            try
            {
                nCard = NCard.Create(card, ModelVisibility.Visible);
            }
            catch (Exception ex1)
            {
                Log.Error($"[SmartPick] ShowCardPreview canonical failed: {ex1.Message}");
                try
                {
                    var mutableCard = (CardModel)card.MutableClone();
                    nCard = NCard.Create(mutableCard, ModelVisibility.Visible);
                }
                catch (Exception ex2)
                {
                    Log.Error($"[SmartPick] ShowCardPreview clone failed: {ex2.Message}");
                }
            }

            if (nCard == null)
            {
                Log.Error("[SmartPick] ShowCardPreview: NCard.Create returned null");
                return;
            }

            try { nCard.UpdateVisuals(PileType.None, CardPreviewMode.Normal); }
            catch (Exception ex) { Log.Error($"[SmartPick] UpdateVisuals failed: {ex.Message}"); }

            nCard.MouseFilter = MouseFilterEnum.Ignore;
            // NCard from pool may have size 0 — use NGridCardHolder like the game does
            var holder = NGridCardHolder.Create(nCard);
            if (holder != null)
            {
                holder.MouseFilter = MouseFilterEnum.Ignore;
                _cardPreview.AddChild(holder);

                _previewHolder = holder;
            }
            else
            {
                _cardPreview.AddChild(nCard);
            }

            // Position near the cursor but keep on screen
            // NGridCardHolder origin is at card CENTER
            var mousePos = GetViewport().GetMousePosition();
            var viewportSize = GetViewport().GetVisibleRect().Size;
            var halfW = 150f;
            var halfH = 211f;

            // Target: card center
            var cx = mousePos.X + 30 + halfW;
            var cy = mousePos.Y;

            // Clamp so card edges stay on screen
            if (cx + halfW > viewportSize.X) cx = mousePos.X - 30 - halfW;
            if (cx - halfW < 0) cx = halfW;
            if (cy - halfH < 0) cy = halfH;
            if (cy + halfH > viewportSize.Y) cy = viewportSize.Y - halfH;

            _cardPreview.GlobalPosition = new Vector2(cx, cy);
            _cardPreview.Visible = true;

            // Show hover tips positioned to the right of the card
            try
            {
                var tips = card.HoverTips?.ToList();
                if (tips != null && tips.Count > 0 && _previewHolder != null)
                {
                    var tipSet = NHoverTipSet.CreateAndShow(_previewHolder, tips);
                    // Position tips to the right of the card manually
                    tipSet.GlobalPosition = new Vector2(cx + halfW + 10, cy - halfH);
                }
            }
            catch { }
        }
        catch (Exception ex)
        {
            Log.Error($"[SmartPick] ShowCardPreview: {ex.Message}\n{ex.StackTrace}");
        }
    }

    private void HideCardPreview()
    {
        foreach (var child in _cardPreview.GetChildren())
        {
            if (child is Control ctrl)
            {
                try { NHoverTipSet.Remove(ctrl); } catch { }
            }
            child.QueueFree();
        }
        _previewHolder = null;
        _cardPreview.Visible = false;
    }

    private void OnItemDroppedToTier(string itemId, TierItemIcon.Type itemType, string character, string tier)
    {
        if (itemType == TierItemIcon.Type.Card)
        {
            var cardName = CardBadgeOverlay.NormalizeCardIdPublic(itemId);
            TierData.SetCustomTier(character, cardName, tier);
        }
        else
        {
            var relicName = RelicTierData.IdEntryToDisplayName(itemId);
            RelicTierData.SetCustomTier(relicName, tier);
        }
        // Auto-save and refresh
        TierData.SaveCustomTiers();
        PopulateItems();
    }

    private void OnItemRemovedFromTier(string itemId, TierItemIcon.Type itemType, string character)
    {
        if (itemType == TierItemIcon.Type.Card)
        {
            var cardName = CardBadgeOverlay.NormalizeCardIdPublic(itemId);
            TierData.RemoveCustomTier(character, cardName);
        }
        else
        {
            var relicName = RelicTierData.IdEntryToDisplayName(itemId);
            RelicTierData.RemoveCustomTier(relicName);
        }
        TierData.SaveCustomTiers();
        PopulateItems();
    }

    private void OnTabSelected(Tab tab)
    {
        if (_currentTab == tab) return;
        _currentTab = tab;

        if (_charFilter != null)
            _charFilter.Visible = tab == Tab.Cards;

        PopulateItems();
    }

    private void OnCharacterSelected(string character)
    {
        if (_currentCharacter == character) return;
        _currentCharacter = character;
        PopulateItems();
    }

    private void OnSave()
    {
        TierData.SaveCustomTiers();
    }

    private void OnReset()
    {
        TierData.ResetCustomTiers();
        RelicTierData.ResetCustomTiers();
        TierData.SaveCustomTiers();
        PopulateItems();
    }

    private void OnClose()
    {
        _instance = null;
        QueueFree();
    }

}
