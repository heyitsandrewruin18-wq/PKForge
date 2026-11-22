using System.Net;
using PKForge.Domain;
using SkiaSharp;

namespace PKForge.App.Services;

/// <summary>What fetching a sprite from PokéAPI's repository came to.</summary>
public enum PokeApiFetch
{
    /// <summary>The sprite is on the device.</summary>
    Ready,
    /// <summary>The repository has no such file (not every Pokémon has every style).</summary>
    Missing,
    /// <summary>No network, a timeout, or something that was not a picture.</summary>
    Failed,
}

/// <summary>
/// PokéAPI's sprites (github.com/PokeAPI/sprites), fetched once when the player adds one to the
/// sprite shelf and kept as PNGs in the widgets' own folder, so the widget paints itself with PKForge
/// closed and offline. Only that one file is requested, from raw.githubusercontent.com; nothing is
/// sent but the request, and nothing here touches a save.
/// </summary>
public static class PokeApiSpriteService
{
    /// <summary>A sprite is a few KB; artwork a few hundred. Anything bigger is not one.</summary>
    private const int MaxBytes = 3 * 1024 * 1024;
    private const int MaxPixels = 4096 * 4096;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(12);
    private static readonly HttpClient Http = Create();

    private static HttpClient Create()
    {
        var http = new HttpClient { Timeout = Timeout };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("PKForge-sprite-shelf");
        return http;
    }

    /// <summary>Where a sprite is kept on the device (the widgets' folder, which a widget update can read cold).</summary>
    public static string FileOf(int id, string style) =>
        Path.Combine(HomeWidgetStore.Folder, $"pokeapi-{PokeApiSprites.StyleOf(style).Id}-{id}.png");

    public static bool IsKept(int id, string style) => File.Exists(FileOf(id, style));

    /// <summary>The kept sprite, or null when it is not on the device (or is unreadable). The caller disposes it.</summary>
    public static SKBitmap? LoadKept(int id, string style)
    {
        try { return HomeWidgetStore.LoadSprite(FileOf(id, style)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>Makes sure the sprite is on the device: kept already, or downloaded now.</summary>
    public static async Task<PokeApiFetch> EnsureAsync(int id, string styleId, CancellationToken cancellation = default)
    {
        if (!PokeApiSprites.IsValidId(id)) return PokeApiFetch.Missing;
        if (IsKept(id, styleId)) return PokeApiFetch.Ready;
        var style = PokeApiSprites.StyleOf(styleId);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(Timeout);
            using var response = await Http.GetAsync(PokeApiSprites.Url(id, style), HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            // The repository answers 404 for a style a Pokémon does not have.
            if (response.StatusCode == HttpStatusCode.NotFound) return PokeApiFetch.Missing;
            if (!response.IsSuccessStatusCode) return PokeApiFetch.Failed;
            if (response.Content.Headers.ContentLength > MaxBytes) return PokeApiFetch.Failed;
            await using var input = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            using var buffer = new MemoryStream();
            var chunk = new byte[16 * 1024];
            int read;
            while ((read = await input.ReadAsync(chunk, timeout.Token).ConfigureAwait(false)) > 0)
            {
                if (buffer.Length + read > MaxBytes) return PokeApiFetch.Failed;
                buffer.Write(chunk, 0, read);
            }
            // Decoded before it is kept: a page of HTML or a broken file is never stored as a sprite. A GIF
            // (Showdown's) decodes to its first frame; every kept sprite is a PNG.
            var bytes = buffer.ToArray();
            using var data = SKData.CreateCopy(bytes);
            using var codec = SKCodec.Create(data);
            if (codec is null || codec.Info.Width < 1 || codec.Info.Height < 1 || (long)codec.Info.Width * codec.Info.Height > MaxPixels)
                return PokeApiFetch.Failed;
            using var bitmap = SKBitmap.Decode(bytes);
            if (bitmap is null) return PokeApiFetch.Failed;
            HomeWidgetStore.SaveSprite(FileOf(id, style.Id), bitmap);
            return PokeApiFetch.Ready;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException or UnauthorizedAccessException)
        {
            AppLog.Warn("widgets", $"PokéAPI sprite {styleId}/{id} not fetched: {ex.Message}");
            return PokeApiFetch.Failed;
        }
    }

    /// <summary>Lets go of a kept sprite no shelf entry uses any more.</summary>
    public static void Forget(SpriteShelfItem item, IReadOnlyList<SpriteShelfItem> remaining)
    {
        if (SpriteShelf.Contains(remaining, item.Id, item.Style)) return;
        try
        {
            lock (HomeWidgetStore.FileLock)
            {
                var path = FileOf(item.Id, item.Style);
                if (File.Exists(path)) File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
