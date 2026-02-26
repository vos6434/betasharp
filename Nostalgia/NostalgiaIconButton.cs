using BetaSharp.Client;
using BetaSharp.Client.Guis;
using BetaSharp.Client.Rendering.Core.Textures;
using BetaSharp.Client.Rendering.Core;
using Silk.NET.OpenGL.Legacy;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.IO;

namespace Nostalgia;

public class NostalgiaIconButton : GuiButton
{
    private readonly int _srcU;
    private readonly int _srcV;
    private static bool _debugLogged = false;

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
            var tex = texHandle.Texture;
            if (tex != null)
            {
                // One-time debug: sample the local terminal.png to confirm source pixels
                if (!_debugLogged)
                {
                    try
                    {
                        var path = Path.Combine("mods", "Nostalgia", "assets", "gui", "terminal.png");
                        if (File.Exists(path))
                        {
                            using var img = Image.Load<Rgba32>(path);
                            var px = img[_srcU, _srcV];
                            System.Console.WriteLine($"NostalgiaIconButton debug: id={Id} src=({_srcU},{_srcV}) pixel=R{px.R} G{px.G} B{px.B} A{px.A} imgSize={img.Width}x{img.Height} texSize={(tex != null ? tex.Width + "x" + tex.Height : "null")}");
                        }
                        else
                        {
                            System.Console.WriteLine($"NostalgiaIconButton debug: terminal.png not found at {Path.GetFullPath(path)}");
                        }
                    }
                    catch (System.Exception ex)
                    {
                        System.Console.WriteLine("NostalgiaIconButton debug error: " + ex);
                    }
                    _debugLogged = true;
                }
                // Ensure GL state is correct for textured quad drawing
                GLManager.GL.Enable(GLEnum.Texture2D);
                GLManager.GL.Color4(1.0F, 1.0F, 1.0F, 1.0F);
                GLManager.GL.Enable(GLEnum.Blend);
                GLManager.GL.BlendFunc(GLEnum.SrcAlpha, GLEnum.OneMinusSrcAlpha);

                float fu = 1.0f / tex.Width;
                float fv = 1.0f / tex.Height;
                var tess = Tessellator.instance;
                tess.startDrawingQuads();
                tess.addVertexWithUV(XPosition + 0, YPosition + _height, _zLevel, (double)((_srcU + 0) * fu), (double)((_srcV + _height) * fv));
                tess.addVertexWithUV(XPosition + _width, YPosition + _height, _zLevel, (double)((_srcU + _width) * fu), (double)((_srcV + _height) * fv));
                tess.addVertexWithUV(XPosition + _width, YPosition + 0, _zLevel, (double)((_srcU + _width) * fu), (double)((_srcV + 0) * fv));
                tess.addVertexWithUV(XPosition + 0, YPosition + 0, _zLevel, (double)((_srcU + 0) * fu), (double)((_srcV + 0) * fv));
                tess.draw();
            }

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
