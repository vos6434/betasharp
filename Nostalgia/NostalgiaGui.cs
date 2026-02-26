using System.IO;
using System.Linq;
using BetaSharp.Client;
using BetaSharp.Client.Guis;
using BetaSharp.Client.Rendering.Core.Textures;
using BetaSharp.Client.Rendering.Core;
using BetaSharp.Inventorys;
using BetaSharp.Screens;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Nostalgia;

public class NostalgiaGui : GuiContainer
{
    private const int TextureCanvas = 256;
    private TextureHandle? _cachedHandle;
    private string? _cachedResourceName;
    private int _cachedImageWidth = 0;
    private int _cachedImageHeight = 0;

    public NostalgiaGui() : base(CreateScreenHandler())
    {
        _xSize = 176;
        _ySize = 166;
    }

    private static ScreenHandler CreateScreenHandler()
    {
        var inv = new InventoryBasic("Nostalgia", 9);
        var mc = Minecraft.INSTANCE;
        IInventory? playerInv = null;
        try
        {
            playerInv = mc?.player?.inventory;
        }
        catch { }

        if (playerInv == null)
        {
            // fallback to a temporary 36-slot inventory to avoid index issues during early startup
            playerInv = new InventoryBasic("player", 36);
        }

        return new GenericContainerScreenHandler(playerInv, inv);
    }

    protected override void DrawGuiContainerForegroundLayer()
    {
        DrawString(FontRenderer, "Terminal", 8, 6, 0x404040);
    }

    protected override void DrawGuiContainerBackgroundLayer(float partialTicks)
    {
        var mc = Minecraft.INSTANCE;
        if (mc == null) return;

        try
        {
            var asm = typeof(NostalgiaGui).Assembly;
            var guiRes = asm.GetManifestResourceNames()
                .FirstOrDefault(n => n.IndexOf(".assets.gui.", System.StringComparison.OrdinalIgnoreCase) >= 0 && n.EndsWith(".png", System.StringComparison.OrdinalIgnoreCase));
            // create a full 256x256 canvas so DrawTexturedModalRect texture coords (1/256) match
            Image<Rgba32> canvas = new Image<Rgba32>(TextureCanvas, TextureCanvas);

            if (guiRes != null)
            {
                // Load and resize the embedded GUI image once and cache the texture handle
                if (_cachedHandle == null || _cachedResourceName != guiRes)
                {
                    using var s = asm.GetManifestResourceStream(guiRes);
                    if (s != null)
                    {
                        using Image<Rgba32> img = Image.Load<Rgba32>(s);
                        // preserve the original image size — do not resize
                        _cachedImageWidth = img.Width;
                        _cachedImageHeight = img.Height;
                        // draw the original GUI image into the top-left of the 256x256 canvas
                        canvas.Mutate(ctx => ctx.DrawImage(img, new SixLabors.ImageSharp.Point(0, 0), 1f));

                        // create and cache the texture handle for the full 256x256 canvas
                        _cachedHandle = mc.textureManager.Load(canvas);
                        _cachedResourceName = guiRes;

                        try
                        {
                            var tex = _cachedHandle.Texture;
                            System.Console.WriteLine($"NostalgiaGui: cached handle created: src={tex?.Source}, texW={tex?.Width}, texH={tex?.Height}, imgW={_cachedImageWidth}, imgH={_cachedImageHeight}");
                        }
                        catch { }
                    }
                }
            }

            // Draw vanilla container background first so slot positions match exactly.
            mc.textureManager.BindTexture(mc.textureManager.GetTextureId("/gui/container.png"));

            int guiLeft = (Width - _xSize) / 2;
            int guiTop = (Height - _ySize) / 2;

            int inventoryRows = 1; // our container uses 9 slots -> 1 row
            DrawTexturedModalRect(guiLeft, guiTop, 0, 0, _xSize, inventoryRows * 18 + 17);
            DrawTexturedModalRect(guiLeft, guiTop + inventoryRows * 18 + 17, 0, 126, _xSize, 96);

            // If we have a cached mod texture, overlay it on top of the vanilla background.
            if (_cachedHandle != null)
            {
                mc.textureManager.BindTexture(_cachedHandle);
                try
                {
                    var tex = _cachedHandle.Texture;
                    System.Console.WriteLine($"NostalgiaGui: binding cached texture src={tex?.Source} size={tex?.Width}x{tex?.Height} img={_cachedImageWidth}x{_cachedImageHeight}");
                    if (tex != null && _cachedImageWidth > 0 && _cachedImageHeight > 0)
                    {
                        DrawTexturedModalRectUV(guiLeft, guiTop, 0, 0, _cachedImageWidth, _cachedImageHeight, tex.Width, tex.Height);
                    }
                }
                catch { }
            }

            // Log first slot coordinates for alignment checks
            try
            {
                if (InventorySlots?.slots != null && InventorySlots.slots.size() > 0)
                {
                    var first = (BetaSharp.Screens.Slots.Slot)InventorySlots.slots.get(0);
                    System.Console.WriteLine($"NostalgiaGui: gui={_xSize}x{_ySize}, first slot at x={first.xDisplayPosition}, y={first.yDisplayPosition}");
                }
            }
            catch { }

            
        }
        catch (System.Exception ex)
        {
            System.Console.WriteLine("NostalgiaGui draw error: " + ex);
        }
    }

    private void DrawTexturedModalRectUV(int x, int y, int u, int v, int width, int height, int texW, int texH)
    {
        float fu = 1.0f / texW;
        float fv = 1.0f / texH;
        var tess = Tessellator.instance;
        tess.startDrawingQuads();
        tess.addVertexWithUV(x + 0, y + height, _zLevel, (double)((u + 0) * fu), (double)((v + height) * fv));
        tess.addVertexWithUV(x + width, y + height, _zLevel, (double)((u + width) * fu), (double)((v + height) * fv));
        tess.addVertexWithUV(x + width, y + 0, _zLevel, (double)((u + width) * fu), (double)((v + 0) * fv));
        tess.addVertexWithUV(x + 0, y + 0, _zLevel, (double)((u + 0) * fu), (double)((v + 0) * fv));
        tess.draw();
    }
}
