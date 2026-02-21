using BetaSharp.Client.Guis;
using BetaSharp.Client.Rendering.Core;
using BetaSharp.Modding;

namespace ModMenu.Guis;

public class GuiModListSlot : GuiSlot
{
    private const int ContentHalfWidth = 110;
    private const int TextPadding = 4;
    private const string Ellipsis = "...";

    private readonly GuiModListScreen _parent;
    private readonly List<ModBase> _mods;
    private readonly int _slotWidth;
    private readonly int _rowHeight;

    public GuiModListSlot(
        GuiModListScreen parent,
        List<ModBase> mods,
        int width,
        int screenHeight,
        int top,
        int bottom,
        int rowHeight)
        : base(parent.mc, width, screenHeight, top, bottom, rowHeight)
    {
        _parent = parent;
        _mods = mods;
        _slotWidth = width;
        _rowHeight = rowHeight;
    }

    public override int GetSize()
    {
        return _mods.Count;
    }

    protected override void ElementClicked(int index, bool doubleClick)
    {
        _parent.SetSelectedModIndex(index);
    }

    protected override bool isSelected(int slotIndex)
    {
        return _parent.IsSelectedMod(slotIndex);
    }

    protected override int GetContentHeight()
    {
        return GetSize() * _rowHeight;
    }

    protected override void DrawBackground()
    {
        _parent.DrawBackground(0);
    }

    protected override void DrawSlot(int index, int x, int y, int slotHeight, Tessellator tess)
    {
        ModBase mod = _mods[index];
        string description = string.IsNullOrWhiteSpace(mod.Description)
            ? "No description provided."
            : mod.Description;

        int contentLeft = _slotWidth / 2 - ContentHalfWidth;
        int contentRight = _slotWidth / 2 + ContentHalfWidth;
        int textX = contentLeft + TextPadding;
        int maxTextWidth = Math.Max(0, contentRight - textX - TextPadding);

        string titleText = FitToWidth(mod.Name, maxTextWidth);
        Gui.DrawString(_parent.FontRenderer, titleText, textX, y + 2, 0xFFFFFF);

        if (slotHeight >= 20)
        {
            string descriptionText = FitToWidth(description, maxTextWidth);
            int descriptionY = y + Math.Max(12, slotHeight / 2 + 2);
            Gui.DrawString(_parent.FontRenderer, descriptionText, textX, descriptionY, 0xC0C0C0);
        }
    }

    private string FitToWidth(string text, int maxWidth)
    {
        if (string.IsNullOrEmpty(text) || maxWidth <= 0)
        {
            return string.Empty;
        }

        if (_parent.FontRenderer.GetStringWidth(text) <= maxWidth)
        {
            return text;
        }

        int ellipsisWidth = _parent.FontRenderer.GetStringWidth(Ellipsis);
        if (ellipsisWidth > maxWidth)
        {
            return string.Empty;
        }

        int length = text.Length;
        while (length > 0 && _parent.FontRenderer.GetStringWidth(text[..length]) + ellipsisWidth > maxWidth)
        {
            length--;
        }

        if (length <= 0)
        {
            return Ellipsis;
        }

        return text[..length].TrimEnd() + Ellipsis;
    }
}
