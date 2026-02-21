using BetaSharp.Client.Guis;
using BetaSharp.Client.Rendering.Core;
using BetaSharp.Modding;

namespace ModMenu.Guis;

public class GuiModListSlot : GuiSlot
{
    public const int PanelWidth = 244;
    private const string Ellipsis = "...";

    private readonly GuiModListScreen _parent;
    private readonly List<ModBase> _mods;

    public GuiModListSlot(GuiModListScreen parent, List<ModBase> mods)
        : base(parent.mc, PanelWidth, parent.Height, 36, parent.Height - 60, 36)
    {
        _parent = parent;
        _mods = mods;
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
        return GetSize() * 36;
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
        int textX = x + 2;
        int textRight = PanelWidth / 2 + 110 - 2;
        int maxTextWidth = Math.Max(0, textRight - textX);

        string titleText = FitToWidth(mod.Name, maxTextWidth);
        string descriptionText = FitToWidth(description, maxTextWidth);

        Gui.DrawString(_parent.FontRenderer, titleText, textX, y + 1, 0xFFFFFF);
        Gui.DrawString(_parent.FontRenderer, descriptionText, textX, y + 12, 0xC0C0C0);
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
