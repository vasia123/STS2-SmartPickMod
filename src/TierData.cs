using System.Reflection;
using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Logging;

namespace FirstMod;

/// <summary>
/// Loads embedded tier-list JSON files and provides per-card tier lookups.
/// Two sources: Mobalytics and slaythespire-2.com wiki.
/// Supports custom tier overrides saved by the user.
/// </summary>
public static class TierData
{
    // character (lowercase) → card name (lowercase) → tier letter
    private static Dictionary<string, Dictionary<string, string>> _mobaIndex = new();
    private static Dictionary<string, Dictionary<string, string>> _wikiIndex = new();
    private static Dictionary<string, Dictionary<string, string>> _customIndex = new();

    public static readonly Dictionary<string, int> TierScore = new()
    {
        ["S"] = 92, ["A"] = 78, ["B"] = 64, ["C"] = 50, ["D"] = 36, ["F"] = 22,
    };

    public static readonly string[] TierLetters = ["S", "A", "B", "C", "D"];

    private static bool _initialized;
    private static string? _customTiersPath;

    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        try
        {
            _mobaIndex = LoadEmbeddedTierList("mobalytics_cards.json");
            Log.Info($"[SmartPick] TierData: loaded Mobalytics ({_mobaIndex.Count} characters)");
        }
        catch (Exception ex) { Log.Error($"[SmartPick] TierData mobalytics load: {ex.Message}"); }

        try
        {
            _wikiIndex = LoadEmbeddedTierList("slaythespire2_com_cards.json");
            Log.Info($"[SmartPick] TierData: loaded Wiki ({_wikiIndex.Count} characters)");
        }
        catch (Exception ex) { Log.Error($"[SmartPick] TierData wiki load: {ex.Message}"); }

        LoadCustomTiers();
    }

    public record TierResult(string? MobaTier, string? WikiTier, string BlendedTier, int BlendedScore);

    public static TierResult GetTiers(string character, string cardName)
    {
        var charKey = NormalizeCharacter(character);
        var cardKey = NormalizeCardName(cardName);

        // Custom tier takes priority over everything
        var customTier = LookupTier(_customIndex, charKey, cardKey);
        if (customTier != null)
        {
            var customScore = TierScore.GetValueOrDefault(customTier, 50);
            return new TierResult(null, null, customTier, customScore);
        }

        var mobaTier = LookupTier(_mobaIndex, charKey, cardKey);
        var wikiTier = LookupTier(_wikiIndex, charKey, cardKey);

        // Fallback: if character unknown, search all characters
        if (mobaTier == null && wikiTier == null && (string.IsNullOrEmpty(charKey) || charKey == "unknown"))
        {
            foreach (var ch in _mobaIndex.Keys)
            {
                mobaTier = LookupTier(_mobaIndex, ch, cardKey);
                if (mobaTier != null) break;
            }
            foreach (var ch in _wikiIndex.Keys)
            {
                wikiTier = LookupTier(_wikiIndex, ch, cardKey);
                if (wikiTier != null) break;
            }
        }

        int score;
        if (mobaTier != null && wikiTier != null)
            score = (TierScore.GetValueOrDefault(mobaTier, 50) + TierScore.GetValueOrDefault(wikiTier, 50)) / 2;
        else if (mobaTier != null)
            score = TierScore.GetValueOrDefault(mobaTier, 50);
        else if (wikiTier != null)
            score = TierScore.GetValueOrDefault(wikiTier, 50);
        else
            score = -1;

        var blended = ScoreToTier(score);

        return new TierResult(mobaTier, wikiTier, blended, score);
    }

    public static string ScoreToTier(int score) => score switch
    {
        >= 85 => "S",
        >= 71 => "A",
        >= 57 => "B",
        >= 43 => "C",
        >= 29 => "D",
        >= 0 => "F",
        _ => "?",
    };

    /// <summary>Set a custom tier for a card (used by the tier editor UI).</summary>
    public static void SetCustomTier(string character, string cardName, string tier)
    {
        var charKey = NormalizeCharacter(character);
        var cardKey = NormalizeCardName(cardName);
        if (!_customIndex.ContainsKey(charKey))
            _customIndex[charKey] = new(StringComparer.OrdinalIgnoreCase);
        _customIndex[charKey][cardKey] = tier;
    }

    /// <summary>Remove a custom tier override for a card.</summary>
    public static void RemoveCustomTier(string character, string cardName)
    {
        var charKey = NormalizeCharacter(character);
        var cardKey = NormalizeCardName(cardName);
        if (_customIndex.TryGetValue(charKey, out var cards))
            cards.Remove(cardKey);
    }

    /// <summary>Get all custom tiers (for saving).</summary>
    public static Dictionary<string, Dictionary<string, string>> GetCustomIndex() => _customIndex;

    /// <summary>Reset all custom tiers to empty.</summary>
    public static void ResetCustomTiers()
    {
        _customIndex.Clear();
    }

    public static void SaveCustomTiers()
    {
        try
        {
            _customTiersPath ??= ProjectSettings.GlobalizePath("user://smartpick_custom_tiers.json");
            var data = new Dictionary<string, object>
            {
                ["cards"] = _customIndex,
                ["relics"] = RelicTierData.GetCustomIndex()
            };
            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_customTiersPath, json);
            Log.Info($"[SmartPick] Custom tiers saved to {_customTiersPath}");
        }
        catch (Exception ex) { Log.Error($"[SmartPick] SaveCustomTiers: {ex.Message}"); }
    }

    public static void LoadCustomTiers()
    {
        try
        {
            _customTiersPath ??= ProjectSettings.GlobalizePath("user://smartpick_custom_tiers.json");
            if (!File.Exists(_customTiersPath)) return;

            var json = File.ReadAllText(_customTiersPath);
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("cards", out var cardsEl))
            {
                _customIndex = ParseTierIndex(cardsEl);
                Log.Info($"[SmartPick] Custom card tiers loaded ({_customIndex.Count} characters)");
            }

            if (doc.RootElement.TryGetProperty("relics", out var relicsEl))
            {
                RelicTierData.LoadCustomFromJson(relicsEl);
            }
        }
        catch (Exception ex) { Log.Error($"[SmartPick] LoadCustomTiers: {ex.Message}"); }
    }

    private static Dictionary<string, Dictionary<string, string>> ParseTierIndex(JsonElement el)
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var charProp in el.EnumerateObject())
        {
            var cardMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var tierProp in charProp.Value.EnumerateObject())
            {
                cardMap[tierProp.Name] = tierProp.Value.GetString() ?? "";
            }
            result[charProp.Name] = cardMap;
        }
        return result;
    }

    private static string? LookupTier(Dictionary<string, Dictionary<string, string>> index, string charKey, string cardKey)
    {
        if (!index.TryGetValue(charKey, out var cards)) return null;
        if (cards.TryGetValue(cardKey, out var tier)) return tier;
        // Try without spaces/symbols for fuzzy match
        var collapsed = CollapseKey(cardKey);
        foreach (var (k, v) in cards)
        {
            if (CollapseKey(k) == collapsed) return v;
        }
        return null;
    }

    private static string NormalizeCardName(string name)
    {
        if (string.IsNullOrEmpty(name)) return "";
        var s = name.TrimEnd('+').Trim();
        return s.ToLowerInvariant();
    }

    private static string NormalizeCharacter(string character)
    {
        if (string.IsNullOrEmpty(character)) return "";
        var s = character;
        // Handle "CHARACTER.SILENT (18436160)" format
        var parenIdx = s.IndexOf('(');
        if (parenIdx > 0) s = s.Substring(0, parenIdx).Trim();
        // Handle "CHARACTER.SILENT" format
        var dotIdx = s.LastIndexOf('.');
        if (dotIdx >= 0) s = s.Substring(dotIdx + 1);
        // Handle "IroncladPlayer" format
        s = s.Replace("Player", "").Trim();
        return s.ToLowerInvariant();
    }

    private static string CollapseKey(string s)
    {
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (var c in s)
        {
            if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }

    private static Dictionary<string, Dictionary<string, string>> LoadEmbeddedTierList(string resourceName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException($"Embedded resource '{resourceName}' not found");

        using var doc = JsonDocument.Parse(stream);
        var root = doc.RootElement;
        var characters = root.GetProperty("characters");

        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var charProp in characters.EnumerateObject())
        {
            var charName = charProp.Name.ToLowerInvariant();
            var cardMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var tierProp in charProp.Value.EnumerateObject())
            {
                var tierLetter = tierProp.Name; // S, A, B, C, D, F
                foreach (var card in tierProp.Value.EnumerateArray())
                {
                    var cardName = card.GetString();
                    if (!string.IsNullOrEmpty(cardName))
                        cardMap[cardName.ToLowerInvariant()] = tierLetter;
                }
            }

            result[charName] = cardMap;
        }

        return result;
    }
}
