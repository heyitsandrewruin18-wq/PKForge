using PKForge.Domain;
using Xunit;

namespace PKForge.Domain.Tests;

/// <summary>
/// The pure half of the sprite shelf widget: PokéAPI's file addresses, the search over species
/// names, the shelf's rules, and the new settings reading old files.
/// </summary>
public sealed class SpriteShelfTests
{
    private static readonly string[] Names = ["", "Bulbasaur", "Ivysaur", "Venusaur", "Mr. Mime", "Flabébé", "Pikachu"];

    [Theory]
    [InlineData("default", 25, "https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/25.png")]
    [InlineData("shiny", 25, "https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/shiny/25.png")]
    [InlineData("back", 6, "https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/back/6.png")]
    [InlineData("artwork", 150, "https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/other/official-artwork/150.png")]
    [InlineData("home-shiny", 1, "https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/other/home/shiny/1.png")]
    [InlineData("showdown", 25, "https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/other/showdown/25.gif")]
    [InlineData("default", 10001, "https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/10001.png")]
    public void A_style_and_a_number_make_the_repositorys_address(string style, int id, string expected) =>
        Assert.Equal(expected, PokeApiSprites.Url(id, PokeApiSprites.StyleOf(style)));

    [Fact]
    public void Every_style_has_its_own_id_and_an_unknown_one_means_the_default()
    {
        var ids = PokeApiSprites.Styles.Select(s => s.Id).ToArray();
        Assert.Equal(ids.Length, ids.Distinct().Count());
        Assert.Equal("default", PokeApiSprites.StyleOf("no-such-style").Id);
        Assert.Equal("default", PokeApiSprites.StyleOf(null).Id);
        // A style id ends up in a file name: it is never a path.
        Assert.All(ids, id => Assert.True(id.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'), id));
    }

    [Fact]
    public void A_name_finds_pokemon_whose_name_starts_with_it_before_those_that_contain_it()
    {
        string[] names = ["", "Raichu", "Pichu", "Chu"];
        var hits = PokeApiSprites.Search(names, "chu");
        Assert.Equal(new[] { 3, 1, 2 }, hits.Select(h => h.Id).ToArray());
        Assert.Equal("Chu", hits[0].Name);
    }

    [Theory]
    [InlineData("pika", 6)]
    [InlineData("PIKACHU", 6)]
    [InlineData("  pikachu  ", 6)]
    [InlineData("mr mime", 4)]
    [InlineData("mrmime", 4)]
    [InlineData("Mr. Mime", 4)]
    [InlineData("flabebe", 5)]
    [InlineData("FLABÉBÉ", 5)]
    public void Case_accents_spaces_and_dots_do_not_matter(string query, int expectedId)
    {
        var hit = Assert.Single(PokeApiSprites.Search(Names, query));
        Assert.Equal(expectedId, hit.Id);
    }

    [Fact]
    public void A_part_of_a_name_finds_every_pokemon_that_has_it()
    {
        var hits = PokeApiSprites.Search(Names, "saur");
        Assert.Equal(new[] { 1, 2, 3 }, hits.Select(h => h.Id).ToArray());
    }

    [Fact]
    public void A_number_finds_that_pokedex_number_and_forms_are_reached_the_same_way()
    {
        Assert.Equal(new SpriteSearchHit(6, "Pikachu"), Assert.Single(PokeApiSprites.Search(Names, "6")));
        Assert.Equal(new SpriteSearchHit(6, "Pikachu"), Assert.Single(PokeApiSprites.Search(Names, "#6")));
        // Past the name list: still a sprite upstream has; it is told by its number.
        Assert.Equal(new SpriteSearchHit(25, "#25"), Assert.Single(PokeApiSprites.Search(Names, "25")));
        Assert.Equal(new SpriteSearchHit(10001, "Form #10001"), Assert.Single(PokeApiSprites.Search(Names, "10001")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("0")]
    [InlineData("100000")]
    [InlineData("zzzz")]
    public void Nothing_to_find_is_an_empty_list_never_an_error(string? query) =>
        Assert.Empty(PokeApiSprites.Search(Names, query));

    [Fact]
    public void A_search_stops_at_its_limit()
    {
        var names = new[] { "" }.Concat(Enumerable.Range(1, 100).Select(i => $"Mon{i}")).ToArray();
        Assert.Equal(30, PokeApiSprites.Search(names, "mon").Count);
        Assert.Equal(5, PokeApiSprites.Search(names, "mon", 5).Count);
    }

    [Theory]
    [InlineData(HomeWidgetSizeHint.Small, 4)]
    [InlineData(HomeWidgetSizeHint.Wide, 8)]
    [InlineData(HomeWidgetSizeHint.Large, 16)]
    public void A_layout_holds_one_sprite_per_cell(HomeWidgetSizeHint size, int expected) =>
        Assert.Equal(expected, SpriteShelf.Capacity(size));

    [Fact]
    public void The_shelf_shows_the_first_sprites_that_fit_the_layout()
    {
        var shelf = Enumerable.Range(1, 10).Select(i => new SpriteShelfItem(i, $"Mon{i}")).ToArray();
        Assert.Equal(new[] { 1, 2, 3, 4 }, SpriteShelf.Visible(shelf, HomeWidgetSizeHint.Small).Select(i => i.Id).ToArray());
        Assert.Equal(8, SpriteShelf.Visible(shelf, HomeWidgetSizeHint.Wide).Count);
        Assert.Equal(10, SpriteShelf.Visible(shelf, HomeWidgetSizeHint.Large).Count);
        Assert.Empty(SpriteShelf.Visible(null, HomeWidgetSizeHint.Small));
    }

    [Fact]
    public void Adding_keeps_the_order_and_never_doubles_a_sprite()
    {
        var shelf = SpriteShelf.Add(null, new SpriteShelfItem(25, "Pikachu"));
        shelf = SpriteShelf.Add(shelf, new SpriteShelfItem(25, "Pikachu", "shiny"));
        shelf = SpriteShelf.Add(shelf, new SpriteShelfItem(25, "Pikachu"));
        Assert.Equal(new[] { "default", "shiny" }, shelf.Select(i => i.Style).ToArray());
        Assert.True(SpriteShelf.Contains(shelf, 25, "shiny"));
        Assert.False(SpriteShelf.Contains(shelf, 25, "artwork"));
        Assert.False(SpriteShelf.Contains(null, 25, "default"));
    }

    [Fact]
    public void A_full_shelf_takes_no_more()
    {
        SpriteShelfItem[] shelf = [];
        for (var i = 1; i <= PokeApiSprites.MaxShelf + 3; i++) shelf = SpriteShelf.Add(shelf, new SpriteShelfItem(i, $"Mon{i}"));
        Assert.Equal(PokeApiSprites.MaxShelf, shelf.Length);
        Assert.True(SpriteShelf.IsFull(shelf));
        Assert.False(SpriteShelf.IsFull(shelf[..^1]));
        Assert.Equal(PokeApiSprites.MaxShelf, SpriteShelf.Capacity(HomeWidgetSizeHint.Large));
    }

    [Fact]
    public void Removing_takes_one_off_and_ignores_a_bad_place()
    {
        var shelf = new[] { new SpriteShelfItem(1, "A"), new SpriteShelfItem(2, "B"), new SpriteShelfItem(3, "C") };
        Assert.Equal(new[] { 1, 3 }, SpriteShelf.RemoveAt(shelf, 1).Select(i => i.Id).ToArray());
        Assert.Equal(3, SpriteShelf.RemoveAt(shelf, -1).Length);
        Assert.Equal(3, SpriteShelf.RemoveAt(shelf, 3).Length);
        Assert.Empty(SpriteShelf.RemoveAt(null, 0));
        // The shelf it was given is not changed.
        Assert.Equal(3, shelf.Length);
    }

    [Fact]
    public void The_widget_says_what_is_on_it()
    {
        Assert.Contains("No sprites yet", SpriteShelf.Describe([]));
        Assert.Equal("Sprite shelf: Pikachu, Eevee.", SpriteShelf.Describe([new(25, "Pikachu"), new(133, "Eevee", "shiny")]));
        Assert.Equal("Pikachu · Shiny front", SpriteShelf.Label(new SpriteShelfItem(25, "Pikachu", "shiny")));
    }

    [Fact]
    public void The_shelf_has_its_own_slot_and_layout()
    {
        Assert.Equal(WidgetSlot.Sprites, HomeWidgetSettings.SlotOf(HomeWidgetKind.Sprites));
        var settings = new HomeWidgetSettings().WithLayout(WidgetSlot.Sprites, HomeWidgetSizeHint.Wide);
        Assert.Equal(HomeWidgetSizeHint.Wide, settings.LayoutOf(WidgetSlot.Sprites));
        Assert.Equal(HomeWidgetSizeHint.Wide, settings.SpritesLayout);
        // The other widgets, the living park above all (the old fallback), keep their own.
        Assert.Null(settings.LiveParkLayout);
        Assert.Null(settings.LayoutOf(WidgetSlot.LivePark));
        Assert.Null(settings.LayoutOf(WidgetSlot.Egg));
    }

    [Fact]
    public void The_shelf_survives_being_kept_and_read_back()
    {
        var shelf = new[] { new SpriteShelfItem(25, "Pikachu"), new SpriteShelfItem(6, "Charizard", "artwork-shiny") };
        var read = HomeWidgetSettings.FromJson((new HomeWidgetSettings(Sprites: shelf, SpritesLayout: HomeWidgetSizeHint.Large)).ToJson());
        Assert.Equal(shelf, read.Sprites);
        Assert.Equal(HomeWidgetSizeHint.Large, read.SpritesLayout);
    }

    [Fact]
    public void Settings_kept_before_the_shelf_existed_still_read()
    {
        var read = HomeWidgetSettings.FromJson("""{"EggSave":"abc","WalkerBigNumber":false}""");
        Assert.Equal("abc", read.EggSave);
        Assert.False(read.WalkerBigNumber);
        Assert.Null(read.Sprites);
        Assert.Null(read.SpritesLayout);
    }
}
