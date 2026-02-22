using System;
using System.Collections.Generic;
using System.Linq;
using BetaSharp.Client;
using BetaSharp.Client.Input;
using BetaSharp.Client.Guis;
using BetaSharp.Client.Rendering.Core;
using BetaSharp.Client.Rendering.Items;
using BetaSharp;
using Silk.NET.OpenGL.Legacy;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Drawing.Processing;
using BetaSharp.Items;
using BetaSharp.Modding;

namespace SeeAllItems;

internal class SeeAllItemsOverlay
{
    private readonly Minecraft mc = Minecraft.INSTANCE!;
    private readonly ItemRenderer itemRenderer = new ItemRenderer();
    private readonly List<ItemStack> allItems = new();
    private List<ItemStack> filtered = new();

    // cached reflection info to read the private cursor counter from GuiTextField
    private static System.Reflection.FieldInfo? guiTextFieldCursorCounter;

    private GuiTextField? searchField;
    private int columns = 4;
    private int cellSize = 20;
    private int padding = 6;
    private int page = 0;
    // runtime-generated custom button texture id (if created)
    private int customButtonTextureId = -1;
    // height of the loaded custom button texture (used to select v offsets)
    private int customButtonTextureHeight = 0;
    private int customButtonTextureWidth = 0;

    // Slot handles vertical rows; we'll draw multiple columns per row
    private ItemGridSlot? slot;

    public SeeAllItemsOverlay()
    {
        // collect items
        for (int i = 0; i < Item.ITEMS.Length; i++)
        {
            var it = Item.ITEMS[i];
            if (it == null) continue;
            allItems.Add(new ItemStack(i, 1));
        }

        filtered = new List<ItemStack>(allItems);
        Console.WriteLine($"SeeAllItemsOverlay: constructed, totalItems={allItems.Count}");

        // create or load a small gui texture (256x256) with two button state rows
        try
        {
            string outDir = System.IO.Path.Combine("mods", "betasharp", "SeeAllItems");
            Directory.CreateDirectory(outDir);
            string outPath = System.IO.Path.Combine(outDir, "seeallitems_buttons.png");

            // try to load embedded resource from this assembly first (packed into the mod DLL)
            try
            {
                var asm = typeof(SeeAllItemsOverlay).Assembly;
                var res = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("seeallitems_buttons.png", StringComparison.OrdinalIgnoreCase));
                if (res != null)
                {
                    using var s = asm.GetManifestResourceStream(res);
                    if (s != null)
                    {
                        var imgRes = Image.Load<Rgba32>(s);
                        customButtonTextureHeight = imgRes.Height;
                        customButtonTextureWidth = imgRes.Width;
                        customButtonTextureId = mc.textureManager.Load(imgRes);
                        Console.WriteLine($"SeeAllItemsOverlay: loaded button PNG from assembly resource '{res}', h={customButtonTextureHeight}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("SeeAllItemsOverlay: failed to load embedded resource: " + ex);
            }

            // if not loaded from assembly, only try the mod file path (no other fallbacks)
            if (customButtonTextureId < 0)
            {
                if (File.Exists(outPath))
                {
                    var img = Image.Load<Rgba32>(outPath);
                    customButtonTextureHeight = img.Height;
                    customButtonTextureWidth = img.Width;
                    customButtonTextureId = mc.textureManager.Load(img);
                    Console.WriteLine($"SeeAllItemsOverlay: loaded button PNG from file '{outPath}', w={customButtonTextureWidth}, h={customButtonTextureHeight}");
                }
                else
                {
                    Console.WriteLine("SeeAllItemsOverlay: no custom button PNG found; buttons will be drawn without texture.");
                    customButtonTextureId = -1;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("SeeAllItemsOverlay: failed to create/load custom button texture: " + ex);
            customButtonTextureId = -1;
        }
    }

    public void RenderOverlay(GuiScreen parent, int mouseX, int mouseY, float partialTicks)
    {
        Console.WriteLine($"SeeAllItemsOverlay.RenderOverlay start: page={page}, filtered={filtered.Count}, mouse=({mouseX},{mouseY})");
        int w = parent.Width;
        int h = parent.Height;

        // initialize search field if needed (creation moved to after panel dims)

        // right panel dimensions (full height, flush to top/bottom)
        int panelW = 140;
        int panelX = w - panelW - 10;
        int panelY = 0;
        int panelH = h;

        // draw semi-transparent panel background (remove dirt texture)
        DrawFilledRect(panelX, panelY, panelX + panelW, panelY + panelH, 0x40000000);

        // initialize search field to live inside the overlay panel (bottom inside panel)
        if (searchField == null || searchField.GetType() == null)
        {
            int sfW = Math.Max(100, panelW - 12);
            int sfX = panelX + 6;
            int sfY = panelY + panelH - 26;
            searchField = new GuiTextField(parent, parent.FontRenderer, sfX, sfY, sfW, 20, "");
        }

        // top nav
        int navY = panelY + 4;
        int btnW = 36;
        int btnH = 13; // increased by 1px
        DrawButton(parent, panelX + 6, navY, btnW, btnH, "Back", mouseX, mouseY);
        DrawButton(parent, panelX + panelW - 6 - btnW, navY, btnW, btnH, "Next", mouseX, mouseY);
        string pageText = $"{page + 1}/{Math.Max(1, (int)Math.Ceiling(filtered.Count / (double)(columns * RowsPerPanel(panelH))))}";        
        int pageTextY = navY + (btnH - 8) / 2; // font height 8
        Gui.DrawCenteredString(parent.FontRenderer, pageText, panelX + panelW / 2, pageTextY, 0xFFFFFF);

        // draw items manually into the panel grid (avoid GuiSlot centering logic)
        int rows = RowsPerPanel(panelH);
        int slotTop = panelY + 24;
        int perPage = Math.Max(1, rows * columns);
        int cellFull = cellSize + padding;

        // center the grid horizontally inside the panel and leave a small top margin
        int contentWidth = columns * cellSize + (columns - 1) * padding;
        int startX = panelX + Math.Max(6, (panelW - contentWidth) / 2);
        int startY = slotTop + 6;

        // inner panel background to make alignment clear (semi-transparent so underlying background shows)
        DrawFilledRect(panelX + 2, slotTop - 2, panelX + panelW - 2, panelY + panelH - 6, 0x80000000);

        // determine hovered stack (to draw highlight) using same hit-testing
        var hoveredStackForHighlight = GetHoveredItem(parent, mouseX, mouseY, panelX, panelY, panelW, panelH);
        int hoveredGlobalIndex = -1;
        if (hoveredStackForHighlight != null) hoveredGlobalIndex = filtered.IndexOf(hoveredStackForHighlight);

        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < columns; c++)
            {
                int indexInPage = r * columns + c;
                int globalIndex = page * perPage + indexInPage;
                if (globalIndex >= filtered.Count) break;
                var stack = filtered[globalIndex];
                int px = startX + c * cellFull;
                int py = startY + r * cellFull;

                // draw cell background and border so alignment is visible
                // cell border (semi-transparent)
                DrawFilledRect(px - 2, py - 2, px + cellSize + 2, py + cellSize + 2, 0x60202020);
                // cell background (more transparent)
                DrawFilledRect(px, py, px + cellSize, py + cellSize, 0x503A3A3A);

                // Ensure GL is in a known state before calling the shared item renderer
                try
                {
                    GLManager.GL.Enable(GLEnum.Texture2D);
                    GLManager.GL.Enable(GLEnum.Blend);
                    GLManager.GL.BlendFunc(GLEnum.SrcAlpha, GLEnum.OneMinusSrcAlpha);
                    GLManager.GL.Color4(1.0f, 1.0f, 1.0f, 1.0f);
                }
                catch { }

                itemRenderer.renderItemIntoGUI(mc.fontRenderer, mc.textureManager, stack, px, py);
                try { itemRenderer.renderItemOverlayIntoGUI(mc.fontRenderer, mc.textureManager, stack, px, py); } catch { }

                // draw hover outline if this is the hovered stack
                if (globalIndex == hoveredGlobalIndex)
                {
                    // simple 1px white outline (drawn as thin filled rects)
                    DrawFilledRect(px - 1, py - 1, px + cellSize + 1, py, 0x80FFFFFF);
                    DrawFilledRect(px - 1, py + cellSize, px + cellSize + 1, py + cellSize + 1, 0x80FFFFFF);
                    DrawFilledRect(px - 1, py, px, py + cellSize, 0x80FFFFFF);
                    DrawFilledRect(px + cellSize, py, px + cellSize + 1, py + cellSize, 0x80FFFFFF);
                }
            }
        }

        // handle mouse wheel for page navigation
        try
        {
            int wheel = Mouse.getEventDWheel();
            if (wheel != 0)
            {
                    int rowsLocal = RowsPerPanel(panelH);
                    int perPageWheel = Math.Max(1, rowsLocal * columns);
                int maxPages = Math.Max(1, (int)Math.Ceiling(filtered.Count / (double)perPage));
                int old = page;
                if (wheel > 0) page = Math.Max(0, page - 1);
                else page = Math.Min(maxPages - 1, page + 1);
                if (page != old) Console.WriteLine($"SeeAllItemsOverlay: wheel -> page {old} -> {page}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("SeeAllItemsOverlay: wheel read threw: " + ex);
        }

        // draw search field using the game's GuiTextField implementation so it matches vanilla
        if (searchField != null)
        {
            try { searchField.updateCursorCounter(); } catch { }

            int sfW = Math.Max(100, panelW - 12);
            int sfX = panelX + 6;
            int sfY = panelY + panelH - 26;
            int sfH = 20;

            // keep the GuiTextField's private position/size in sync with the overlay panel
            try
            {
                var t = typeof(GuiTextField);
                var fx = t.GetField("_xPos", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                var fy = t.GetField("_yPos", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                var fw = t.GetField("_width", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                var fh = t.GetField("_height", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                if (fx != null) fx.SetValue(searchField, sfX);
                if (fy != null) fy.SetValue(searchField, sfY);
                if (fw != null) fw.SetValue(searchField, sfW);
                if (fh != null) fh.SetValue(searchField, sfH);
            }
            catch { }

            try
            {
                // force a known GL state for GUI textured draws (vanilla GUI expectations)
                GLManager.GL.Disable(GLEnum.Lighting);
                GLManager.GL.Disable(GLEnum.DepthTest);
                GLManager.GL.Disable(GLEnum.CullFace);
                GLManager.GL.Enable(GLEnum.Texture2D);
                GLManager.GL.Enable(GLEnum.Blend);
                GLManager.GL.Enable(GLEnum.AlphaTest);
                GLManager.GL.BlendFunc(GLEnum.SrcAlpha, GLEnum.OneMinusSrcAlpha);
                GLManager.GL.Color4(1.0f, 1.0f, 1.0f, 1.0f);
            }
            catch { }

            try { searchField.DrawTextBox(); } catch (Exception ex) { Console.WriteLine("SeeAllItemsOverlay: DrawTextBox threw: " + ex); }

            try
            {
                // restore safe defaults used elsewhere in this overlay
                GLManager.GL.Color4(1.0f, 1.0f, 1.0f, 1.0f);
                GLManager.GL.Enable(GLEnum.Texture2D);
                GLManager.GL.Disable(GLEnum.Blend);
                GLManager.GL.Enable(GLEnum.AlphaTest);
            }
            catch { }
        }

        // if tooltip
        var hovered = GetHoveredItem(parent, mouseX, mouseY, panelX, panelY, panelW, panelH);
        if (hovered != null)
        {
            string itemName = ("" + TranslationStorage.Instance.TranslateNamedKey(hovered.getItemName())).Trim();
                if (itemName.Length > 0)
                {
                    int tipX = mouseX + 12;
                    int tipY = mouseY - 12;
                    int textWidth = parent.FontRenderer.GetStringWidth(itemName);
                    DrawGradientRect(tipX - 3, tipY - 3, tipX + textWidth + 3, tipY + 8 + 3, 0xC0000000, 0xC0000000);
                    parent.FontRenderer.DrawStringWithShadow(itemName, tipX, tipY, 0xFFFFFFFF);
                }
        }
    }

    private int RowsPerPanel(int panelH)
    {
        return Math.Max(1, (panelH - 32) / (cellSize + padding));
    }

    private ItemStack? GetHoveredItem(GuiScreen parent, int mouseX, int mouseY, int panelX, int panelY, int panelW, int panelH)
    {
        int w = parent.Width;
        int slotTop = panelY + 24;
        int contentWidth = columns * cellSize + (columns - 1) * padding;
        int startX = panelX + Math.Max(6, (panelW - contentWidth) / 2);
        int startY = slotTop + 6;
        int localX = mouseX - startX;
        int localY = mouseY - startY;
        int rows = RowsPerPanel(panelH);
        int cellFull = cellSize + padding;
        if (localX >= 0 && localY >= 0)
        {
            int col = localX / cellFull;
            int row = localY / cellFull;
            if (col >= 0 && col < columns && row >= 0 && row < rows)
            {
                int perPage = Math.Max(1, rows * columns);
                int indexInPage = row * columns + col;
                int globalIndex = page * perPage + indexInPage;
                if (globalIndex >= 0 && globalIndex < filtered.Count) return filtered[globalIndex];
            }
        }

        return null;
    }

    private void DrawPanelBackground(int x, int y, int w, int h)
    {
        Tessellator tess = Tessellator.instance;
        mc.textureManager.BindTexture(mc.textureManager.GetTextureId("/gui/background.png"));
        tess.startDrawingQuads();
        tess.setColorRGBA_I(0x404040, 255);
        tess.addVertexWithUV(x, y + h, 0.0, 0.0, (y + h) / 32.0);
        tess.addVertexWithUV(x + w, y + h, 0.0, w / 32.0, (y + h) / 32.0);
        tess.addVertexWithUV(x + w, y, 0.0, w / 32.0, y / 32.0);
        tess.addVertexWithUV(x, y, 0.0, 0.0, y / 32.0);
        tess.draw();
        // reset GL color/state so subsequent textured UI elements render normally
        GLManager.GL.Color4(1.0f, 1.0f, 1.0f, 1.0f);
        GLManager.GL.Enable(GLEnum.Texture2D);
        GLManager.GL.Disable(GLEnum.Blend);
    }

    private void DrawButton(GuiScreen parent, int x, int y, int w, int h, string text, int mouseX, int mouseY)
    {
        // draw using the game's button texture from /gui/gui.png to match vanilla
        try
        {
            if (customButtonTextureId >= 0)
            {
                GLManager.GL.BindTexture(GLEnum.Texture2D, (uint)customButtonTextureId);
            }
            else
            {
                GLManager.GL.BindTexture(GLEnum.Texture2D, (uint)mc.textureManager.GetTextureId("/gui/gui.png"));
            }
            GLManager.GL.Color4(1.0F, 1.0F, 1.0F, 1.0F);

            bool isHovered = mouseX >= x && mouseY >= y && mouseX < x + w && mouseY < y + h;
            int hoverState = (!isHovered) ? 1 : 2; // 1 = normal, 2 = hovered; disabled not used here

            // draw left and right halves to support variable width (same technique as GuiButton)
            int half = w / 2;
            // compute v offset based on which texture we're using. Our generated compact texture
            // places rows starting at 46 with step=(h+1) and normal/hover at 1*step/2*step.
            // Vanilla's /gui/gui.png uses rows at ~66 and 86 (20px step).
            int v;
            // Only use our custom texture (no vanilla fallback). If custom texture is large (>=200h) assume vanilla layout rows at 66/86,
            // otherwise treat as compact where top row=normal and second row=hover.
            if (customButtonTextureId >= 0 && customButtonTextureHeight > 0)
            {
                if (customButtonTextureHeight >= 200)
                {
                    int baseV = 66;
                    int hoverOffset = 20;
                    v = baseV + (isHovered ? hoverOffset : 0);
                }
                else
                {
                    int baseV = 0;
                    int step = h + 1;
                    v = baseV + (isHovered ? step : 0);
                }
            }
            else
            {
                // no custom texture loaded; skip textured drawing by setting v to 0
                v = 0;
            }

            // debug: print which texture/height and v being used (useful for runtime verification)
            try { Console.WriteLine($"SeeAllItemsOverlay: DrawButton textureId={customButtonTextureId}, texW={customButtonTextureWidth}, texH={customButtonTextureHeight}, v={v}, isHovered={isHovered}"); } catch { }

            // If we have a custom texture, compute UVs using its real dimensions
            if (customButtonTextureId >= 0 && customButtonTextureWidth > 0 && customButtonTextureHeight > 0)
            {
                // normalized UV scale
                float fU = 1.0f / customButtonTextureWidth;
                float fV = 1.0f / customButtonTextureHeight;

                Tessellator tess = Tessellator.instance;
                tess.startDrawingQuads();
                // left half
                tess.addVertexWithUV(x + 0, y + (h + 1), 0.0, (double)((0 + 0) * fU), (double)((v + (h + 1)) * fV));
                tess.addVertexWithUV(x + half, y + (h + 1), 0.0, (double)((0 + half) * fU), (double)((v + (h + 1)) * fV));
                tess.addVertexWithUV(x + half, y + 0, 0.0, (double)((0 + half) * fU), (double)((v + 0) * fV));
                tess.addVertexWithUV(x + 0, y + 0, 0.0, (double)((0 + 0) * fU), (double)((v + 0) * fV));
                // right half (u starts at texture width - 200 + (200 - half) == custom width - half if custom width==200; to support arbitrary widths we map using right-side UV using (customWidth - (w-half)) etc.)
                int uRight = Math.Max(0, customButtonTextureWidth - 200 + (200 - half));
                tess.addVertexWithUV(x + half, y + (h + 1), 0.0, (double)((uRight + 0) * fU), (double)((v + (h + 1)) * fV));
                tess.addVertexWithUV(x + w, y + (h + 1), 0.0, (double)((uRight + (w - half)) * fU), (double)((v + (h + 1)) * fV));
                tess.addVertexWithUV(x + w, y + 0, 0.0, (double)((uRight + (w - half)) * fU), (double)((v + 0) * fV));
                tess.addVertexWithUV(x + half, y + 0, 0.0, (double)((uRight + 0) * fU), (double)((v + 0) * fV));
                tess.draw();
                }
            else
            {
                // no custom texture: draw a simple filled rect as a non-textured button background
                DrawFilledRect(x, y, x + w, y + h, 0x80404040);
            }

            // draw label centered vertically with slight padding
            int textColor = isHovered ? 0xFFFFA0 : 0xE0E0E0;
            Gui.DrawCenteredString(parent.FontRenderer, text, x + w / 2, y + (h - 8) / 2 + 1, (uint)textColor);
                // draw a solid 1px black bottom border to ensure the button bottom edge is visible
                DrawFilledRect(x, y + h, x + w, y + h + 1, 0xFF000000);
        }
        catch { }
    }

    private void DrawFilledRect(int x1, int y1, int x2, int y2, uint color)
    {
        Tessellator tess = Tessellator.instance;
        GLManager.GL.Enable(GLEnum.Blend);
        GLManager.GL.Disable(GLEnum.Texture2D);
        GLManager.GL.BlendFunc(GLEnum.SrcAlpha, GLEnum.OneMinusSrcAlpha);
        float a = (color >> 24 & 255) / 255.0F;
        float r = (color >> 16 & 255) / 255.0F;
        float g = (color >> 8 & 255) / 255.0F;
        float b = (color & 255) / 255.0F;
        GLManager.GL.Color4(r, g, b, a);
        tess.startDrawingQuads();
        tess.addVertex(x1, y2, 0.0D);
        tess.addVertex(x2, y2, 0.0D);
        tess.addVertex(x2, y1, 0.0D);
        tess.addVertex(x1, y1, 0.0D);
        tess.draw();
        // restore color and texture state so subsequent textured UI draws are not tinted
        GLManager.GL.Color4(1.0f, 1.0f, 1.0f, 1.0f);
        GLManager.GL.Enable(GLEnum.Texture2D);
        GLManager.GL.Disable(GLEnum.Blend);
    }

    private void DrawGradientRect(int left, int top, int right, int bottom, uint topColor, uint bottomColor)
    {
        float a1 = (topColor >> 24 & 255) / 255.0F;
        float r1 = (topColor >> 16 & 255) / 255.0F;
        float g1 = (topColor >> 8 & 255) / 255.0F;
        float b1 = (topColor & 255) / 255.0F;

        float a2 = (bottomColor >> 24 & 255) / 255.0F;
        float r2 = (bottomColor >> 16 & 255) / 255.0F;
        float g2 = (bottomColor >> 8 & 255) / 255.0F;
        float b2 = (bottomColor & 255) / 255.0F;

        GLManager.GL.Disable(GLEnum.Texture2D);
        GLManager.GL.Enable(GLEnum.Blend);
        GLManager.GL.Disable(GLEnum.AlphaTest);
        GLManager.GL.BlendFunc(GLEnum.SrcAlpha, GLEnum.OneMinusSrcAlpha);
        GLManager.GL.ShadeModel(GLEnum.Smooth);

        Tessellator tess = Tessellator.instance;
        tess.startDrawingQuads();
        tess.setColorRGBA_F(r1, g1, b1, a1);
        tess.addVertex(left, bottom, 0.0D);
        tess.addVertex(right, bottom, 0.0D);
        tess.setColorRGBA_F(r2, g2, b2, a2);
        tess.addVertex(right, top, 0.0D);
        tess.addVertex(left, top, 0.0D);
        tess.draw();

        GLManager.GL.ShadeModel(GLEnum.Flat);
        // ensure color is reset for textured draws afterwards
        GLManager.GL.Color4(1.0f, 1.0f, 1.0f, 1.0f);
        GLManager.GL.Disable(GLEnum.Blend);
        GLManager.GL.Enable(GLEnum.AlphaTest);
        GLManager.GL.Enable(GLEnum.Texture2D);
    }

    public bool HandleMouseClicked(GuiScreen parent, int x, int y, int button)
    {
        // only forward to search field if click is over the overlay area or the field is already focused
        if (searchField != null && (searchField.IsFocused || IsMouseOver(parent, x, y)))
        {
            searchField.MouseClicked(x, y, button);
        }

        // check nav buttons
        int w = parent.Width;
        int h = parent.Height;
        int panelW = 140; int panelX = w - panelW - 10; int panelY = 0;
        int navY = panelY + 4; int btnW = 36;
        int btnH = 12;
        if (x >= panelX + 6 && x <= panelX + 6 + btnW && y >= navY && y <= navY + btnH)
        {
            // back
            page = Math.Max(0, page - 1);
            return true;
        }
        if (x >= panelX + panelW - 6 - btnW && x <= panelX + panelW - 6 && y >= navY && y <= navY + btnH)
        {
            page = page + 1;
            return true;
        }

        // check clicks on items in grid
        int panelH = h;
        int rows = RowsPerPanel(panelH);
        int slotTop = panelY + 24;
        int contentWidth = columns * cellSize + (columns - 1) * padding;
        int startX = panelX + Math.Max(6, (panelW - contentWidth) / 2);
        int startY = slotTop + 6;
        int localX = x - startX;
        int localY = y - startY;
        int cellFull = cellSize + padding;
        if (localX >= 0 && localY >= 0)
        {
            int col = localX / cellFull;
            int row = localY / cellFull;
            if (col >= 0 && col < columns && row >= 0 && row < rows)
            {
                int perPage = Math.Max(1, rows * columns);
                int indexInPage = row * columns + col;
                int globalIndex = page * perPage + indexInPage;
                if (globalIndex >= 0 && globalIndex < filtered.Count)
                {
                    if (button == 0) // left click
                    {
                        var stackInfo = filtered[globalIndex];
                        int amount = 1;
                        if (Keyboard.isKeyDown(Keyboard.KEY_LSHIFT) || Keyboard.isKeyDown(Keyboard.KEY_RSHIFT))
                        {
                            amount = new ItemStack(stackInfo.itemId, 1).getMaxCount();
                        }
                        Console.WriteLine($"SeeAllItemsOverlay: click itemId={stackInfo.itemId} amount={amount} at globalIndex={globalIndex}");
                        if (mc.player != null)
                        {
                            bool ok = mc.player.inventory.addItemStackToInventory(new ItemStack(stackInfo.itemId, amount));
                            Console.WriteLine($"SeeAllItemsOverlay: addItemStackToInventory returned {ok}");
                        }
                        return true;
                    }
                }
            }
        }

        // pass to slot (fallback) only if click is over the panel
        if (slot != null && IsMouseOver(parent, x, y) && slot.HandleMouseClicked(parent, x, y, button)) return true;

        return false;
    }

    public bool IsMouseOver(GuiScreen parent, int x, int y)
    {
        int w = parent.Width;
        int h = parent.Height;
        int panelW = 140;
        int panelX = w - panelW - 10;
        int panelY = 0;
        int panelH = h;

        // check search field focus as well
        if (searchField != null && searchField.IsFocused) return true;

        return x >= panelX && x <= panelX + panelW && y >= panelY && y <= panelY + panelH;
    }

    public bool IsTyping()
    {
        return searchField != null && searchField.IsFocused;
    }

    public bool HandleKeyTyped(GuiScreen parent, char eventChar, int eventKey)
    {
        if (searchField != null && searchField.IsFocused)
        {
            searchField.textboxKeyTyped(eventChar, eventKey);
            // update filter
            ApplyFilter(searchField.GetText());
            return true;
        }

        // focus search on '/' or if user types when overlay visible
        if (eventChar == '/')
        {
            searchField?.SetFocused(true);
            return true;
        }

        return false;
    }

    private void ApplyFilter(string q)
    {
        if (string.IsNullOrWhiteSpace(q)) filtered = new List<ItemStack>(allItems);
        else
        {
            var low = q.ToLowerInvariant();
            filtered = allItems.Where(s => (s.getItemName() ?? "").ToLowerInvariant().Contains(low) || s.itemId.ToString().Contains(low)).ToList();
        }
        // recreate slot data
        slot = null;
        page = 0;
    }

    private class ItemGridSlot : GuiSlot
    {
        private readonly SeeAllItemsOverlay parentOverlay;
        private readonly int columns;
        private int hoveredIndex = -1;

        public ItemGridSlot(Minecraft mc, int width, int height, int top, int bottom, int posZ, SeeAllItemsOverlay parent, int columns)
            : base(mc, width, height, top, bottom, posZ)
        {
            parentOverlay = parent;
            this.columns = columns;
            SetShowSelectionHighlight(false);
        }

        public override int GetSize()
        {
            int panelH = _bottom - _top;
            int rows = Math.Max(1, (panelH - 32) / (parentOverlay.cellSize + parentOverlay.padding));
            return rows;
        }

        protected override void ElementClicked(int index, bool doubleClick) { }

        protected override bool isSelected(int slotIndex) => false;

        protected override void DrawBackground() { }

        protected override void DrawSlot(int index, int x, int y, int height, Tessellator tess)
        {
            int startX = x + 6;
            int cell = parentOverlay.cellSize;
            int rows = GetSize();
            int perPage = Math.Max(1, rows * columns);
            int pageOffset = parentOverlay.page * perPage;
            for (int c = 0; c < columns; c++)
            {
                int itIndex = pageOffset + index * columns + c;
                if (itIndex >= parentOverlay.filtered.Count) break;
                var stack = parentOverlay.filtered[itIndex];
                int px = startX + c * (cell + parentOverlay.padding);
                int py = y + 2;
                parentOverlay.itemRenderer.renderItemIntoGUI(parentOverlay.mc.fontRenderer, parentOverlay.mc.textureManager, stack, px, py);
            }
        }

        public ItemStack? GetHoveredItem()
        {
            return null; // TODO: hit testing for tooltip
        }

        public bool HandleMouseClicked(GuiScreen parent, int x, int y, int button)
        {
            return false;
        }
    }
}
