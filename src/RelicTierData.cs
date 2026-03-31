using System.Reflection;
using System.Text.Json;
using MegaCrit.Sts2.Core.Logging;

namespace FirstMod;

/// <summary>
/// Loads embedded relic tier-list JSON and provides per-relic tier lookups.
/// Single source (Mobalytics), global tiers (not per-character).
/// </summary>
public static class RelicTierData
{
    // relic name (normalized) → tier letter
    private static Dictionary<string, string> _index = new();
    private static Dictionary<string, string> _customIndex = new();

    private static bool _initialized;

    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        try
        {
            _index = LoadEmbeddedTierList("mobalytics_relics.json");
            Log.Info($"[SmartPick] RelicTierData: loaded {_index.Count} relics");
        }
        catch (Exception ex) { Log.Error($"[SmartPick] RelicTierData load: {ex.Message}"); }
    }

    public record RelicTierResult(string Tier, int Score);

    public static RelicTierResult? GetTier(string relicName)
    {
        if (string.IsNullOrEmpty(relicName)) return null;

        var key = NormalizeName(relicName);

        // Custom tier takes priority
        if (_customIndex.TryGetValue(key, out var customTier))
            return new RelicTierResult(customTier, TierData.TierScore.GetValueOrDefault(customTier, 50));

        if (_index.TryGetValue(key, out var tier))
            return new RelicTierResult(tier, TierData.TierScore.GetValueOrDefault(tier, 50));

        // Fuzzy match
        var collapsed = CollapseKey(key);
        foreach (var (k, v) in _customIndex)
        {
            if (CollapseKey(k) == collapsed)
                return new RelicTierResult(v, TierData.TierScore.GetValueOrDefault(v, 50));
        }
        foreach (var (k, v) in _index)
        {
            if (CollapseKey(k) == collapsed)
                return new RelicTierResult(v, TierData.TierScore.GetValueOrDefault(v, 50));
        }

        return null;
    }

    public static void SetCustomTier(string relicName, string tier)
    {
        _customIndex[NormalizeName(relicName)] = tier;
    }

    public static void RemoveCustomTier(string relicName)
    {
        _customIndex.Remove(NormalizeName(relicName));
    }

    public static Dictionary<string, string> GetCustomIndex() => _customIndex;

    public static void ResetCustomTiers()
    {
        _customIndex.Clear();
    }

    public static void LoadCustomFromJson(System.Text.Json.JsonElement el)
    {
        _customIndex.Clear();
        foreach (var prop in el.EnumerateObject())
        {
            _customIndex[prop.Name] = prop.Value.GetString() ?? "";
        }
        Log.Info($"[SmartPick] Custom relic tiers loaded ({_customIndex.Count} relics)");
    }

    private static string NormalizeName(string name)
    {
        return name.Trim().ToLowerInvariant();
    }

    /// <summary>
    /// Convert relic ID entry (e.g. "bag_of_preparation") to display name ("Bag Of Preparation")
    /// for tier lookup against the JSON which uses display names.
    /// </summary>
    public static string IdEntryToDisplayName(string entry)
    {
        var words = entry.Split('_', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < words.Length; i++)
        {
            if (words[i].Length > 0)
                words[i] = char.ToUpper(words[i][0]) + words[i].Substring(1).ToLower();
        }
        return string.Join(" ", words);
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

    private static Dictionary<string, string> LoadEmbeddedTierList(string resourceName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException($"Embedded resource '{resourceName}' not found");

        using var doc = JsonDocument.Parse(stream);
        var tiers = doc.RootElement.GetProperty("tiers");

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var tierProp in tiers.EnumerateObject())
        {
            var tierLetter = tierProp.Name; // S, A, B, C, D
            foreach (var relic in tierProp.Value.EnumerateArray())
            {
                var relicName = relic.GetString();
                if (!string.IsNullOrEmpty(relicName))
                    result[relicName.ToLowerInvariant()] = tierLetter;
            }
        }

        return result;
    }
}
