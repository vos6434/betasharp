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
    private readonly int _srcWidth;
    private readonly int _srcHeight;
    private static bool _debugLogged = false;

    public NostalgiaIconButton(int id, int xPos, int yPos, int wid, int hei, int srcU, int srcV, int srcWidth, int srcHeight)
        : base(id, xPos, yPos, wid, hei, "")
    {
        _srcU = srcU;
        _srcV = srcV;
        _srcWidth = srcWidth;
        _srcHeight = srcHeight;
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
                // One-time debug: sample the in-memory cached canvas (preferred) and print texture/image sizes + UVs
                if (!_debugLogged)
                {
                    try
                    {
                        var img = NostalgiaBase.CachedGuiImage;
                        int imgWDbg = img != null ? img.Width : NostalgiaBase.CachedGuiImageWidth;
                        int imgHDbg = img != null ? img.Height : NostalgiaBase.CachedGuiImageHeight;
                        int texW = tex != null ? tex.Width : 0;
                        int texH = tex != null ? tex.Height : 0;

                        string resource = NostalgiaBase.CachedGuiResourceName ?? "<unknown>";

                        string sampleInfo = "<no-image>";
                        if (img != null)
                        {
                            int ox = Math.Max(0, _srcU);
                            int oy = Math.Max(0, _srcV);
                            int ex = Math.Min(img.Width, _srcU + _width);
                            int ey = Math.Min(img.Height, _srcV + _height);
                            int opaque = 0;
                            int nonRed = 0;
                            int firstX = -1, firstY = -1;
                            Rgba32 firstPx = default;

                            for (int yy = oy; yy < ey; yy++)
                            {
                                for (int xx = ox; xx < ex; xx++)
                                {
                                    var p = img[xx, yy];
                                    if (p.A > 0)
                                    {
                                        opaque++;
                                        // consider "red background" as R=255,G=0,B=0
                                        if (!(p.R == 255 && p.G == 0 && p.B == 0))
                                        {
                                            nonRed++;
                                            if (firstX == -1)
                                            {
                                                firstX = xx; firstY = yy; firstPx = p;
                                            }
                                        }
                                    }
                                }
                            }

                            if (opaque == 0)
                            {
                                sampleInfo = "scan: empty (all transparent)";
                            }
                            else
                            {
                                sampleInfo = $"scan: opaque={opaque} nonRed={nonRed}";
                                if (firstX != -1) sampleInfo += $" firstNonRed=({firstX},{firstY}) col=R{firstPx.R} G{firstPx.G} B{firstPx.B} A{firstPx.A}";
                            }
                        }

                        float fuDbg = imgWDbg > 0 ? 1.0f / imgWDbg : (texW > 0 ? 1.0f / texW : 0f);
                        float fvDbg = imgHDbg > 0 ? 1.0f / imgHDbg : (texH > 0 ? 1.0f / texH : 0f);
                        double u0 = (_srcU + 0) * fuDbg;
                        double v0 = (_srcV + 0) * fvDbg;
                        double u1 = (_srcU + _srcWidth) * fuDbg;
                        double v1 = (_srcV + _srcHeight) * fvDbg;

                        System.Console.WriteLine($"NostalgiaIconButton debug: id={Id} resource={resource} imgSize={imgWDbg}x{imgHDbg} texSize={texW}x{texH} src=({_srcU},{_srcV}) srcSize={_srcWidth}x{_srcHeight} drawSize={_width}x{_height} u0={u0:F6} v0={v0:F6} u1={u1:F6} v1={v1:F6} {sampleInfo}");
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

                // Use the GL texture size for UV scaling to match overlay drawing
            int imgW = tex.Width;
            int imgH = tex.Height;
            float fu = 1.0f / (float)imgW;
            float fv = 1.0f / (float)imgH;
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
