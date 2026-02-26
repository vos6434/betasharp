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

    public NostalgiaGui() : base(CreateScreenHandler())
    {
        _xSize = 194;
        // slightly reduce height so the GUI sits a bit higher on-screen
        _ySize = 202;
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

        // Shift player inventory down by 10 pixels to align the hotbar with the custom GUI artwork.
        const int playerYOffset = 71;
        return new NostalgiaScreenHandler(playerInv, inv, playerYOffset);
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
            // Ensure the mod's centralized cached GUI texture is loaded (NostalgiaBase will no-op if already loaded)
            try { NostalgiaBase.EnsureGuiTextureLoaded(mc); } catch { }

            // Draw vanilla container background first so slot positions match exactly.
            mc.textureManager.BindTexture(mc.textureManager.GetTextureId("/gui/container.png"));

            int guiLeft = (Width - _xSize) / 2;
            int guiTop = (Height - _ySize) / 2;

            int inventoryRows = 1; // our container uses 9 slots -> 1 row
            DrawTexturedModalRect(guiLeft, guiTop, 0, 0, _xSize, inventoryRows * 18 + 17);
            DrawTexturedModalRect(guiLeft, guiTop + inventoryRows * 18 + 17, 0, 126, _xSize, 96);

            // If we have a cached mod texture, overlay it on top of the vanilla background.
            if (NostalgiaBase.CachedGuiHandle != null)
            {
                mc.textureManager.BindTexture(NostalgiaBase.CachedGuiHandle);
                var tex = NostalgiaBase.CachedGuiHandle.Texture;
                if (tex != null && NostalgiaBase.CachedGuiImageWidth > 0 && NostalgiaBase.CachedGuiImageHeight > 0)
                {
                    DrawTexturedModalRectUV(guiLeft, guiTop, 0, 0, NostalgiaBase.CachedGuiImageWidth, NostalgiaBase.CachedGuiImageHeight, tex.Width, tex.Height);
                }
            }
            // (no diagnostic logs)

            
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
