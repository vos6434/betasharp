using BetaSharp.Client;
using BetaSharp.Client.Guis;
using BetaSharp.Client.Rendering.Core.Textures;
using BetaSharp.Client.Rendering.Core;

namespace Nostalgia;

public class NostalgiaIconButton : GuiButton
{
    private readonly int _srcU;
    private readonly int _srcV;

    public NostalgiaIconButton(int id, int xPos, int yPos, int wid, int hei, int srcU, int srcV)
        : base(id, xPos, yPos, wid, hei, "")
    {
        _srcU = srcU;
        _srcV = srcV;
        Size(wid, hei);
    }

    public override void DrawButton(Minecraft mc, int mouseX, int mouseY)
    {
        if (!Visible) return;

        var texHandle = NostalgiaBase.CachedGuiHandle;
        if (texHandle != null && mc != null && mc.textureManager != null)
        {
            mc.textureManager.BindTexture(texHandle);
            bool isHovered = mouseX >= XPosition && mouseY >= YPosition && mouseX < XPosition + _width && mouseY < YPosition + _height;
            DrawTexturedModalRect(XPosition, YPosition, _srcU, _srcV, _width, _height);

            if (isHovered)
            {
                // subtle hover highlight to indicate interactivity
                DrawGradientRect(XPosition, YPosition, XPosition + _width, YPosition + _height, 0x40FFFFFF, 0x40FFFFFF);
            }
        }
        else
        {
            base.DrawButton(mc, mouseX, mouseY);
        }
    }
}
