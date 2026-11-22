using System.Globalization;
using System.Text;

namespace PKForge.Domain;

/// <summary>
/// One kind of sprite PokéAPI's sprite repository (github.com/PokeAPI/sprites, browsable at
/// pokeapi.github.io/sprites) keeps for a Pokémon: where its folder is and its file extension.
/// Files are named by the Pokédex number (10001 and up are alternate forms), e.g. <c>shiny/25.png</c>.
/// </summary>
public sealed record PokeApiSpriteStyle(string Id, string Label, string Folder, string Extension, string Detail);

/// <summary>A sprite on the shelf: the Pokédex number, the name it was picked by, and its style's id.</summary>
public sealed record SpriteShelfItem(int Id, string Name, string Style = "default");

/// <summary>A Pokémon found by a search.</summary>
public sealed record SpriteSearchHit(int Id, string Name);

/// <summary>PokéAPI's sprite files: their addresses, the styles on offer, and the search over species names.</summary>
public static class PokeApiSprites
{
    /// <summary>Where the repository's Pokémon sprites are served from.</summary>
    public const string Root = "https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/";

    /// <summary>The most sprites the shelf keeps (the large 4x4 layout shows sixteen).</summary>
    public const int MaxShelf = 16;

    /// <summary>The highest number a search accepts: forms in the repository start at 10001.</summary>
    public const int MaxId = 99999;

    /// <summary>
    /// The styles offered, most useful first. Not every Pokémon has every style upstream (Gen 9 has
    /// no Showdown sprite for some, few have a female or back-shiny one): a missing file is said so,
    /// never replaced with another style. Dream World SVGs are left out: they cannot be drawn here.
    /// </summary>
    public static readonly IReadOnlyList<PokeApiSpriteStyle> Styles =
    [
        new("default", "Default front", "", ".png", "The classic front sprite."),
        new("shiny", "Shiny front", "shiny/", ".png", "Its shiny colours."),
        new("artwork", "Official artwork", "other/official-artwork/", ".png", "Large, clean artwork."),
        new("artwork-shiny", "Official artwork (shiny)", "other/official-artwork/shiny/", ".png", "The artwork in shiny colours."),
        new("home", "HOME render", "other/home/", ".png", "Pokémon HOME's 3D model."),
        new("home-shiny", "HOME render (shiny)", "other/home/shiny/", ".png", "HOME's shiny model."),
        new("showdown", "Showdown", "other/showdown/", ".gif", "Showdown's sprite (first frame)."),
        new("showdown-shiny", "Showdown (shiny)", "other/showdown/shiny/", ".gif", "Showdown's shiny sprite (first frame)."),
        new("back", "Back", "back/", ".png", "Seen from behind."),
        new("back-shiny", "Back (shiny)", "back/shiny/", ".png", "Seen from behind, shiny."),
        new("female", "Female front", "female/", ".png", "Only where the sexes differ."),
        new("shiny-female", "Female front (shiny)", "shiny/female/", ".png", "Only where the sexes differ."),
    ];

    public static PokeApiSpriteStyle StyleOf(string? id) =>
        Styles.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.Ordinal)) ?? Styles[0];

    public static string Url(int id, PokeApiSpriteStyle style) =>
        Root + style.Folder + id.ToString(CultureInfo.InvariantCulture) + style.Extension;

    public static bool IsValidId(int id) => id is >= 1 and <= MaxId;

    /// <summary>The species' name, or "#id" / "Form #id" when the name list has none (alternate forms).</summary>
    public static string NameOf(IReadOnlyList<string> names, int id) =>
        (uint)id < (uint)names.Count && !string.IsNullOrWhiteSpace(names[id]) ? names[id] : id > 10000 ? $"Form #{id}" : $"#{id}";

    /// <summary>
    /// Pokémon whose name starts with, or else contains, <paramref name="query"/> (case and accents
    /// ignored), names that start with it first. A number finds that Pokédex number directly, which is
    /// also how forms (10001 and up) are reached. <paramref name="names"/> is indexed by Pokédex number.
    /// </summary>
    public static IReadOnlyList<SpriteSearchHit> Search(IReadOnlyList<string> names, string? query, int limit = 30)
    {
        var q = Fold((query ?? "").Trim().TrimStart('#'));
        if (q.Length == 0) return [];
        if (int.TryParse(q, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
            return IsValidId(number) ? [new SpriteSearchHit(number, NameOf(names, number))] : [];
        var starts = new List<SpriteSearchHit>();
        var contains = new List<SpriteSearchHit>();
        for (var id = 1; id < names.Count; id++)
        {
            var name = names[id];
            if (string.IsNullOrWhiteSpace(name)) continue;
            var folded = Fold(name);
            if (folded.StartsWith(q, StringComparison.Ordinal)) starts.Add(new SpriteSearchHit(id, name));
            else if (folded.Contains(q, StringComparison.Ordinal)) contains.Add(new SpriteSearchHit(id, name));
        }
        return [.. starts.Concat(contains).Take(Math.Max(1, limit))];
    }

    /// <summary>Lower case, without accents or spaces and dashes: "Flabébé", "mr mime" and "Mr. Mime" all fold alike.</summary>
    private static string Fold(string text)
    {
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var folded = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(c)) folded.Append(char.ToLowerInvariant(c));
        }
        return folded.ToString();
    }
}

/// <summary>The shelf's rules: which sprites a layout shows, adding and removing. Pure: the files are the app's.</summary>
public static class SpriteShelf
{
    /// <summary>How many sprites a layout holds: one per cell (2x2: 4, 4x2: 8, 4x4: 16).</summary>
    public static int Capacity(HomeWidgetSizeHint size)
    {
        var (columns, rows) = HomeWidgetSizes.Cells(size);
        return columns * rows;
    }

    /// <summary>The sprites a layout shows: the first ones, in the order they were added.</summary>
    public static IReadOnlyList<SpriteShelfItem> Visible(IReadOnlyList<SpriteShelfItem>? shelf, HomeWidgetSizeHint size) =>
        shelf is null ? [] : [.. shelf.Take(Capacity(size))];

    public static bool Contains(IReadOnlyList<SpriteShelfItem>? shelf, int id, string style) =>
        shelf?.Any(i => i.Id == id && string.Equals(i.Style, style, StringComparison.Ordinal)) == true;

    public static bool IsFull(IReadOnlyList<SpriteShelfItem>? shelf) => (shelf?.Count ?? 0) >= PokeApiSprites.MaxShelf;

    /// <summary>The shelf with <paramref name="item"/> added at the end; unchanged when it is there already or the shelf is full.</summary>
    public static SpriteShelfItem[] Add(IReadOnlyList<SpriteShelfItem>? shelf, SpriteShelfItem item)
    {
        var current = shelf?.ToArray() ?? [];
        return Contains(current, item.Id, item.Style) || IsFull(current) ? current : [.. current, item];
    }

    public static SpriteShelfItem[] RemoveAt(IReadOnlyList<SpriteShelfItem>? shelf, int index)
    {
        var current = shelf?.ToArray() ?? [];
        return index < 0 || index >= current.Length ? current : [.. current.Where((_, i) => i != index)];
    }

    /// <summary>"Pikachu · Shiny front", for lists.</summary>
    public static string Label(SpriteShelfItem item) => $"{item.Name} · {PokeApiSprites.StyleOf(item.Style).Label}";

    /// <summary>What a screen reader hears of the widget.</summary>
    public static string Describe(IReadOnlyList<SpriteShelfItem> shown) =>
        shown.Count == 0 ? "Sprite shelf. No sprites yet. Tap to add some." : "Sprite shelf: " + string.Join(", ", shown.Select(i => i.Name)) + ".";
}
