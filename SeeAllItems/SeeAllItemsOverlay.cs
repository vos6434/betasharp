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
using BetaSharp.Items;
using BetaSharp.Modding;

namespace SeeAllItems;

internal class SeeAllItemsOverlay
{
    private readonly Minecraft mc = Minecraft.INSTANCE!;
    private readonly ItemRenderer itemRenderer = new ItemRenderer();
    private readonly List<ItemStack> allItems = new();
    private List<ItemStack> filtered = new();

    private GuiTextField? searchField;
    private int columns = 4;
    private int cellSize = 20;
    private int padding = 6;
    private int page = 0;

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
    }

    public void RenderOverlay(GuiScreen parent, int mouseX, int mouseY, float partialTicks)
    {
        Console.WriteLine($"SeeAllItemsOverlay.RenderOverlay start: page={page}, filtered={filtered.Count}, mouse=({mouseX},{mouseY})");
        int w = parent.Width;
        int h = parent.Height;

        // initialize search field if needed
        if (searchField == null || searchField.GetType() == null)
        {
            int sfW = Math.Max(100, w - 40);
            searchField = new GuiTextField(parent, parent.FontRenderer, 20, h - 26, sfW, 20, "");
        }

        // right panel dimensions
        int panelW = 140;
        int panelX = w - panelW - 10;
        int panelY = 30;
        int panelH = h - 80;

        // draw background panel
        DrawPanelBackground(panelX, panelY, panelW, panelH);

        // top nav
        int navY = panelY + 4;
        int btnW = 36;
        DrawButton(panelX + 6, navY, btnW, 14, "Back");
        DrawButton(panelX + panelW - 6 - btnW, navY, btnW, 14, "Next");
        string pageText = $"{page + 1}/{Math.Max(1, (int)Math.Ceiling(filtered.Count / (double)(columns * RowsPerPanel(panelH))))}";
        Gui.DrawString(parent.FontRenderer, pageText, panelX + panelW / 2 - parent.FontRenderer.GetStringWidth(pageText) / 2, navY + 2, 0xFFFFFF);

        // draw items manually into the panel grid (avoid GuiSlot centering logic)
        int rows = RowsPerPanel(panelH);
        int slotTop = panelY + 24;
        int perPage = Math.Max(1, rows * columns);
        int cellFull = cellSize + padding;

        // center the grid horizontally inside the panel and leave a small top margin
        int contentWidth = columns * cellSize + (columns - 1) * padding;
        int startX = panelX + Math.Max(6, (panelW - contentWidth) / 2);
        int startY = slotTop + 6;

        // inner panel background to make alignment clear
        DrawFilledRect(panelX + 2, slotTop - 2, panelX + panelW - 2, panelY + panelH - 6, 0xFF2A2A2A);

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
                DrawFilledRect(px - 2, py - 2, px + cellSize + 2, py + cellSize + 2, 0xFF202020);
                DrawFilledRect(px, py, px + cellSize, py + cellSize, 0xFF3A3A3A);

                itemRenderer.renderItemIntoGUI(mc.fontRenderer, mc.textureManager, stack, px, py);
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

        // draw search field
        searchField.DrawTextBox();

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
    }

    private void DrawButton(int x, int y, int w, int h, string text)
    {
        DrawFilledRect(x, y, x + w, y + h, 0xFF202020);
        Gui.DrawString(mc.fontRenderer, text, x + 4, y + 2, 0xFFFFFF);
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
        GLManager.GL.Enable(GLEnum.Texture2D);
        GLManager.GL.Disable(GLEnum.Blend);
    }

    private void DrawGradientRect(int right, int bottom, int left, int top, uint topColor, uint bottomColor)
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
        int panelW = 140; int panelX = w - panelW - 10; int panelY = 30;
        int navY = panelY + 4; int btnW = 36;
        if (x >= panelX + 6 && x <= panelX + 6 + btnW && y >= navY && y <= navY + 14)
        {
            // back
            page = Math.Max(0, page - 1);
            return true;
        }
        if (x >= panelX + panelW - 6 - btnW && x <= panelX + panelW - 6 && y >= navY && y <= navY + 14)
        {
            page = page + 1;
            return true;
        }

        // check clicks on items in grid
        int panelH = h - 80;
        int rows = RowsPerPanel(panelH);
        int slotTop = panelY + 24;
        int startX = panelX + 6;
        int localX = x - startX;
        int localY = y - slotTop;
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
        int panelY = 30;
        int panelH = h - 80;

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
