using System;
using BetaSharp.Modding;
using System.Linq;
using System.IO;
using System.Reflection;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using BetaSharp.Client;
using BetaSharp.Client.Rendering.Core.Textures;

namespace Nostalgia;

[ModSide(Side.Client)]
public class NostalgiaBase : ModBase
{
    private bool _registered = false;
    // Centralized cached GUI texture (shared across GUI instances)
    public static TextureHandle? CachedGuiHandle;
    public static string? CachedGuiResourceName;
    public static SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>? CachedGuiImage;
    public static int CachedGuiImageWidth = 0;
    public static int CachedGuiImageHeight = 0;
    private static bool _guiLoaded = false;
    public override bool HasOptionsMenu => false;

    public NostalgiaBase()
    {
        Name = "Nostalgia";
        Description = "A small nostalgia-themed mod";
        Author = "ModAuthor";
        Version = "1.0.0";
        Logger = Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
    }

    // Backwards-compatible constructor used by the mod loader when it expects a parameterized ctor.
    public NostalgiaBase(string name, string author, string description, string version, Microsoft.Extensions.Logging.ILogger logger)
        : base(name, author, description, version, logger)
    {
    }

    public override void Initialize(Side side)
    {
        Console.WriteLine("Initialize called for Nostalgia mod");

        // Extract embedded block textures (if any) into the mod folder and attempt to register them
        try
        {
            var asm = typeof(NostalgiaBase).Assembly;
            try
            {
                var resList = asm.GetManifestResourceNames().Where(n => n.IndexOf(".assets.", StringComparison.OrdinalIgnoreCase) >= 0).ToList();
                Console.WriteLine("Nostalgia: embedded resources containing '.assets.':");
                foreach (var r in resList) Console.WriteLine("  " + r);
            }
            catch { }
            var resNames = asm.GetManifestResourceNames()
                .Where(n => n.EndsWith(".png", StringComparison.OrdinalIgnoreCase) && (
                    n.IndexOf(".assets.blocks.", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf(".assets\\blocks\\", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf(".assets.gui.", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf(".assets\\gui\\", System.StringComparison.OrdinalIgnoreCase) >= 0))
                .ToList();

            if (resNames.Count > 0)
            {
                string outDir = Path.Combine("mods", "Nostalgia", "assets", "blocks");
                Directory.CreateDirectory(outDir);

                foreach (var res in resNames)
                {
                    try
                    {
                        var parts = res.Split('.');
                        string fileName = parts.Last();
                        var outPath = Path.Combine(outDir, fileName);

                        using (var s = asm.GetManifestResourceStream(res))
                        {
                            if (s == null) continue;
                            using (var fs = File.Create(outPath))
                            {
                                s.CopyTo(fs);
                            }
                        }

                        Console.WriteLine($"Extracted embedded block texture: {fileName} -> {outPath}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Failed to extract embedded block resource " + res + ": " + ex);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error while extracting embedded assets: " + ex);
        }

        // Try to find the first embedded block texture and prepare registration (deferred if needed)
        try
        {
            var asm = typeof(NostalgiaBase).Assembly;
            var blockRes = asm.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith(".png", StringComparison.OrdinalIgnoreCase) && (n.Contains(".assets.blocks.") || n.Contains(".assets\\blocks\\")));

            Console.WriteLine($"Nostalgia: found embedded block resource? {(blockRes != null ? "yes" : "no")}");
            if (blockRes != null)
            {
                using var s = asm.GetManifestResourceStream(blockRes);
                if (s != null)
                {
                    using var ms = new MemoryStream();
                    s.CopyTo(ms);
                    byte[] imageBytes = ms.ToArray();

                    // Helper to perform the actual registration (id selection, texture upload, block constructor)
                    void DoRegister()
                    {
                        if (_registered) return;
                        var mc = BetaSharp.Client.Minecraft.INSTANCE;
                        if (mc == null || mc.textureManager == null)
                        {
                            Console.WriteLine("Nostalgia: cannot register yet, Minecraft or textureManager not ready.");
                            return;
                        }

                        int nextTex = -1;
                        try
                        {
                            for (int candidate = 0; candidate < 256; candidate++)
                            {
                                bool used = false;
                                foreach (var b in BetaSharp.Blocks.Block.Blocks)
                                {
                                    if (b != null && b.textureId == candidate) { used = true; break; }
                                }

                                if (used) continue;

                                foreach (var it in BetaSharp.Items.Item.ITEMS)
                                {
                                    if (it != null && it.getTextureId(0) == candidate) { used = true; break; }
                                }

                                if (!used)
                                {
                                    nextTex = candidate;
                                    break;
                                }
                            }

                            if (nextTex == -1)
                            {
                                nextTex = Enumerable.Max(BetaSharp.Blocks.Block.Blocks.Where(b => b != null).Select(b => b.textureId)) + 1;
                            }
                        }
                        catch { nextTex = 200; }

                        var dyn = new NostalgiaDynamicTexture(nextTex, imageBytes);
                        mc.textureManager.AddDynamicTexture(dyn);

                        // pick a free block id but avoid 0 and avoid low indices — start at 100
                        int freeId = -1;
                        for (int i = 100; i < BetaSharp.Blocks.Block.Blocks.Length; i++)
                        {
                            if (BetaSharp.Blocks.Block.Blocks[i] == null)
                            {
                                freeId = i;
                                break;
                            }
                        }

                        if (freeId == -1)
                        {
                            Console.WriteLine("Nostalgia: no free block id available (searched starting at 100).");
                            return;
                        }

                        // create a custom NostalgiaBlock so we can open a GUI on right-click
                        try
                        {
                            var stone = BetaSharp.Blocks.Materials.Material.Stone;
                            var blockInstance = new NostalgiaBlock(freeId, nextTex, stone);
                            blockInstance.setBlockName("terminal");
                            Console.WriteLine($"Nostalgia: registered block id {freeId} with texture {nextTex}");
                            try
                            {
                                var internalName = blockInstance.getBlockName() ?? "<null>";
                                Console.WriteLine($"Nostalgia debug: block internal name = {internalName}, id = {freeId}, texture = {nextTex}");
                            }
                            catch { }
                            // Create an ItemBlock wrapper so the block can be given as an item (/give)
                            try
                            {
                                if (freeId >= 0 && freeId < BetaSharp.Items.Item.ITEMS.Length)
                                {
                                    if (BetaSharp.Items.Item.ITEMS[freeId] == null)
                                    {
                                        var itemWrap = new BetaSharp.Items.ItemBlock(freeId - 256);
                                        // set a readable item name
                                        var setItemName = typeof(BetaSharp.Items.Item).GetMethod("setItemName", BindingFlags.Instance | BindingFlags.Public);
                                        setItemName?.Invoke(itemWrap, new object[] { "terminal" });
                                        Console.WriteLine($"Nostalgia: created ItemBlock wrapper at id {freeId}");
                                    }
                                    else
                                    {
                                        Console.WriteLine($"Nostalgia: Item wrapper already exists at id {freeId}");
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine("Nostalgia: failed to create ItemBlock wrapper: " + ex);
                            }

                            _registered = true;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("Nostalgia: failed to register block instance: " + ex);
                        }
                    }

                    var mcNow = BetaSharp.Client.Minecraft.INSTANCE;
                    if (mcNow != null && mcNow.textureManager != null)
                    {
                        DoRegister();
                        try { EnsureGuiTextureLoaded(mcNow); } catch { }
                    }
                    else
                    {
                        Console.WriteLine("Nostalgia: textureManager is null, deferring registration until after texture initialization.");
                        // ensure we only add one deferred action
                        if (!_registered)
                        {
                            BetaSharp.Modding.Mods.PostTextureInitActions.Add(() =>
                            {
                                try
                                {
                                    DoRegister();
                                    try { EnsureGuiTextureLoaded(BetaSharp.Client.Minecraft.INSTANCE); } catch { }
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine("Nostalgia (deferred) registration error: " + ex);
                                }
                            });
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error while registering custom block: " + ex);
        }
    }

    public static void EnsureGuiTextureLoaded(Minecraft mc)
    {
        if (_guiLoaded) return;
        if (mc == null || mc.textureManager == null) return;

        try
        {
            var asm = typeof(NostalgiaBase).Assembly;
            var guiRes = asm.GetManifestResourceNames()
                .FirstOrDefault(n => n.IndexOf(".assets.gui.", System.StringComparison.OrdinalIgnoreCase) >= 0 && n.EndsWith(".png", System.StringComparison.OrdinalIgnoreCase));

            Stream? resourceStream = null;
            string? diskFallbackPath = null;

            if (guiRes != null)
            {
                resourceStream = asm.GetManifestResourceStream(guiRes);
            }

            // If embedded resource not present, try to find the file on disk under likely mod/workspace paths.
            if (resourceStream == null)
            {
                string baseDir = AppContext.BaseDirectory;
                var dir = new DirectoryInfo(baseDir);

                while (dir != null)
                {
                    try
                    {
                        // Fast check common candidate locations first
                        var candidates = new[] {
                            Path.Combine(dir.FullName, "mods", "Nostalgia", "assets", "gui", "terminal.png"),
                            Path.Combine(dir.FullName, "Nostalgia", "assets", "gui", "terminal.png"),
                            Path.Combine(dir.FullName, "mods", "betasharp", "Nostalgia", "assets", "gui", "terminal.png")
                        };

                        foreach (var candidate in candidates)
                        {
                            if (File.Exists(candidate))
                            {
                                resourceStream = File.OpenRead(candidate);
                                diskFallbackPath = candidate;
                                Console.WriteLine($"Nostalgia: loaded GUI texture from disk fallback: {candidate}");
                                break;
                            }
                        }

                        if (resourceStream != null) break;

                        // If not found, try a limited recursive search for terminal.png under this directory.
                        // Avoid searching inside large build folders by skipping paths containing /bin/ or /obj/.
                        if (!dir.FullName.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar) &&
                            !dir.FullName.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
                        {
                            try
                            {
                                var matches = Directory.EnumerateFiles(dir.FullName, "terminal.png", SearchOption.AllDirectories);
                                foreach (var m in matches)
                                {
                                    // prefer matches that look like an assets/gui path
                                    if (m.IndexOf(Path.Combine("assets", "gui"), StringComparison.OrdinalIgnoreCase) >= 0 &&
                                        m.IndexOf("Nostalgia", StringComparison.OrdinalIgnoreCase) >= 0)
                                    {
                                        resourceStream = File.OpenRead(m);
                                        diskFallbackPath = m;
                                        Console.WriteLine($"Nostalgia: loaded GUI texture from disk (recursive): {m}");
                                        break;
                                    }
                                }

                                if (resourceStream != null) break;

                                // if none matched the preferred pattern, take the first found
                                var first = matches.FirstOrDefault();
                                if (first != null)
                                {
                                    resourceStream = File.OpenRead(first);
                                    diskFallbackPath = first;
                                    Console.WriteLine($"Nostalgia: loaded GUI texture from disk (first match): {first}");
                                    break;
                                }
                            }
                            catch { }
                        }
                    }
                    catch { }

                    dir = dir.Parent;
                }
            }

            if (resourceStream == null) { _guiLoaded = true; return; }

            using Image<Rgba32> img = Image.Load<Rgba32>(resourceStream);
            CachedGuiImageWidth = img.Width;
            CachedGuiImageHeight = img.Height;

            const int TextureCanvas = 256;
            Image<Rgba32> canvas = new Image<Rgba32>(TextureCanvas, TextureCanvas);
            canvas.Mutate(ctx => ctx.DrawImage(img, new SixLabors.ImageSharp.Point(0, 0), 1f));

            // Keep an in-memory copy for debug sampling and verification
            try
            {
                CachedGuiImage = canvas.Clone();
            }
            catch { CachedGuiImage = null; }

            CachedGuiHandle = mc.textureManager.Load(canvas);
            CachedGuiResourceName = diskFallbackPath ?? guiRes;
            _guiLoaded = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine("Nostalgia: failed to load cached GUI texture: " + ex);
            _guiLoaded = true;
        }
    }

    public override void PostInitialize(Side side)
    {
        Console.WriteLine("PostInitialize called for Nostalgia mod");
        // Registration handled in Initialize (immediate or deferred). Nothing to do here.
    }

    public override void Unload(Side side)
    {
        Console.WriteLine("Nostalgia mod is unloading");
        try
        {
            var mc = BetaSharp.Client.Minecraft.INSTANCE;
            if (mc != null && mc.textureManager != null && CachedGuiHandle != null)
            {
                mc.textureManager.Delete(CachedGuiHandle);
            }
        }
        catch { }
    }
}
