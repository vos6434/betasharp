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
        // Use 5 rows of 9 slots (45-slot container) and compute height to match background pieces
        int inventoryRows = 5;
        // height = top (inventoryRows*18 + 17) + bottom (96)
        _ySize = inventoryRows * 18 + 17 + 96;
    }

    public override void InitGui()
    {
        base.InitGui();
        // Place two 10x10 icon buttons exactly at the red rectangle region in the
        // terminal.png overlay. The red rectangle spans 170,5 -> 190,15 and contains
        // two 10x10 icons at 170,5 and 180,5 respectively.
        int guiLeft = (Width - _xSize) / 2;
        int guiTop = (Height - _ySize) / 2;

        int iconW = 10;
        int iconH = 10;

        // Source coordinates within the cached GUI texture (pixels)
        // Top-right icons in terminal.png (10x10 each). Shifted left 1px per request.
        int srcU0 = 216;
        int srcV0 = 0;
        int srcU1 = 226;
        int srcV1 = 0;

        // Screen positions: place the buttons over the red-square area in the GUI crop
        // Red square target (in GUI pixels): top-left = (170,5) and (180,5)
        int x0 = guiLeft + 170;
        int y0 = guiTop + 5;
        int x1 = guiLeft + 180;
        int y1 = guiTop + 5;

        _controlList.Add(new NostalgiaIconButton(1000, x0, y0, iconW, iconH, srcU0, srcV0, 10, 10));
        _controlList.Add(new NostalgiaIconButton(1001, x1, y1, iconW, iconH, srcU1, srcV1, 10, 10));
    }

    protected override void ActionPerformed(BetaSharp.Client.Guis.GuiButton button)
    {
        if (button == null) return;
        if (button.Id == 1000)
        {
            System.Console.WriteLine("NostalgiaGui: top-right left button clicked (id=1000)");
        }
        else if (button.Id == 1001)
        {
            System.Console.WriteLine("NostalgiaGui: top-right right button clicked (id=1001)");
        }
        else
        {
            base.ActionPerformed(button);
        }
    }

    private static ScreenHandler CreateScreenHandler()
    {
        // 5 rows * 9 slots = 45 slots total
        var inv = new InventoryBasic("Nostalgia", 45);
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

        // Shift player inventory vertically to align the hotbar with the custom GUI artwork.
        // Decrease this value to move the player inventory higher on-screen.
        const int playerYOffset = -1;
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

            // Compute inventoryRows from the GUI height so the vanilla background pieces match our container.
            int inventoryRows = (_ySize - 17 - 96) / 18;
            if (inventoryRows < 1) inventoryRows = 1;
            DrawTexturedModalRect(guiLeft, guiTop, 0, 0, _xSize, inventoryRows * 18 + 17);
            DrawTexturedModalRect(guiLeft, guiTop + inventoryRows * 18 + 17, 0, 126, _xSize, 96);

            // If we have a cached mod texture, overlay it on top of the vanilla background.
            if (NostalgiaBase.CachedGuiHandle != null)
            {
                mc.textureManager.BindTexture(NostalgiaBase.CachedGuiHandle);
                var tex = NostalgiaBase.CachedGuiHandle.Texture;
                if (tex != null && NostalgiaBase.CachedGuiImageWidth > 0 && NostalgiaBase.CachedGuiImageHeight > 0)
                {
                    // Only draw the GUI crop (width=_xSize, height=_ySize) from the cached texture
                    DrawTexturedModalRectUV(guiLeft, guiTop, 0, 0, _xSize, _ySize, tex.Width, tex.Height);
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
