using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using PS5PKGTool.Core.Models;
using PS5PKGTool.Core.Services;

namespace PS5PKGTool.Gui.Services;

/// <summary>
/// Loads and caches decoded game icon artwork as Avalonia bitmaps for the library grid and details
/// pane. Decoding runs off the UI thread (via <see cref="Ps5DetailsLoader"/>/<see cref="Ps5ImageCodec"/>),
/// results are cached so scrolling never re-decodes the same icon twice, and any failure (missing
/// artwork, corrupt image, unreadable package) degrades to "no icon" rather than throwing.
/// </summary>
public sealed class GameArtworkService
{
    /// <summary>Bounds the cache so a large library scan cannot grow it without limit.</summary>
    private const int MaxCacheEntries = 512;

    private readonly Dictionary<string, Bitmap?> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<string> _lruOrder = new();
    private readonly HashSet<string> _inFlight = [];
    private readonly object _gate = new();

    /// <summary>
    /// Returns the cached icon bitmap for <paramref name="game"/> if already decoded (or known to
    /// have none), or null while it is still loading/not yet requested. Call <see cref="RequestAsync"/>
    /// to kick off a load; <paramref name="onLoaded"/> is invoked on the UI thread once decoding
    /// completes.
    /// </summary>
    public Bitmap? TryGetCached(Ps5GameInfo game)
    {
        string key = KeyOf(game);
        lock (_gate)
        {
            if (_cache.TryGetValue(key, out Bitmap? bitmap))
            {
                Touch(key);
                return bitmap;
            }
            return null;
        }
    }

    /// <summary>
    /// Ensures the icon for <paramref name="game"/> is loading (or loaded). Safe to call repeatedly
    /// (e.g. on every scroll-into-view) -- a load already cached or in flight is a no-op.
    /// </summary>
    public void RequestAsync(Ps5GameInfo game, Action<Bitmap?> onLoaded)
    {
        string key = KeyOf(game);
        lock (_gate)
        {
            if (_cache.ContainsKey(key)) return;
            if (!_inFlight.Add(key)) return;
        }

        _ = Task.Run(() =>
        {
            Bitmap? bitmap = null;
            try
            {
                bitmap = DecodeIcon(game);
            }
            catch (Exception)
            {
                // Any decode/IO failure degrades to "no icon" -- never surfaced to the caller.
                bitmap = null;
            }
            finally
            {
                lock (_gate)
                {
                    _inFlight.Remove(key);
                    StoreLocked(key, bitmap);
                }
            }

            Dispatcher.UIThread.Post(() => onLoaded(bitmap));
        });
    }

    /// <summary>Decodes just the icon (skips backgrounds/trophies/etc.) for the fastest grid thumbnail.</summary>
    private static Bitmap? DecodeIcon(Ps5GameInfo game)
    {
        Ps5ImageData? icon = null;
        var progress = new Progress<Ps5Artwork>(art => icon ??= art.Icon);

        // Reuse the full loader (it already knows how to pull icon0.png from loose dumps, readable
        // packages and raw PKG entries alike); the icon is reported first via IProgress, so the
        // heavier trophy/UDS/executable work that follows does not block us from having it -- but we
        // still await the whole load since there is no cheaper "icon-only" entry point today.
        var loader = new Ps5DetailsLoader();
        try
        {
            loader.LoadAsync(game, CancellationToken.None, progress).GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            // Fall through with whatever the progress callback already captured (possibly null).
        }

        return icon is null ? null : ToBitmap(icon);
    }

    private static Bitmap? ToBitmap(Ps5ImageData image)
    {
        if (image.IsEmpty) return null;
        try
        {
            if (!image.IsRgba)
            {
                using var stream = new MemoryStream(image.Bytes, writable: false);
                return new Bitmap(stream);
            }

            if (image.Width <= 0 || image.Height <= 0) return null;
            var size = new PixelSize(image.Width, image.Height);
            var writeable = new WriteableBitmap(size, new Vector(96, 96), PixelFormat.Rgba8888, AlphaFormat.Unpremul);
            using (ILockedFramebuffer buffer = writeable.Lock())
            {
                int rowBytes = image.Width * 4;
                if (buffer.RowBytes == rowBytes)
                {
                    System.Runtime.InteropServices.Marshal.Copy(image.Bytes, 0, buffer.Address, image.Bytes.Length);
                }
                else
                {
                    // Stride differs from the tightly-packed source; copy row by row.
                    for (int y = 0; y < image.Height; y++)
                    {
                        System.Runtime.InteropServices.Marshal.Copy(
                            image.Bytes, y * rowBytes, buffer.Address + y * buffer.RowBytes, rowBytes);
                    }
                }
            }
            return writeable;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void StoreLocked(string key, Bitmap? bitmap)
    {
        if (!_cache.ContainsKey(key))
        {
            if (_cache.Count >= MaxCacheEntries && _lruOrder.First is { } oldest)
            {
                _cache.Remove(oldest.Value);
                _lruOrder.RemoveFirst();
            }
            _lruOrder.AddLast(key);
        }
        _cache[key] = bitmap;
    }

    private void Touch(string key)
    {
        LinkedListNode<string>? node = _lruOrder.Find(key);
        if (node is null) return;
        _lruOrder.Remove(node);
        _lruOrder.AddLast(node);
    }

    private static string KeyOf(Ps5GameInfo game) =>
        $"{game.RootPath}|{game.ContainerInnerFileName}";
}
