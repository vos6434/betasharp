using System;
using System.IO;
using BetaSharp.Client.Textures;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Nostalgia;

public class NostalgiaDynamicTexture : DynamicTexture
{
    private readonly byte[] _imageBytes;

    public NostalgiaDynamicTexture(int iconIdx, byte[] imageBytes) : base(iconIdx)
    {
        _imageBytes = imageBytes;
        atlas = FXImage.Terrain;
    }

    public override void Setup(BetaSharp.Client.Minecraft mc)
    {
        try
        {
            using var ms = new MemoryStream(_imageBytes);
            using Image<Rgba32> img = Image.Load<Rgba32>(ms);
            var atlasHandle = mc.textureManager.GetTextureId("/terrain.png");
            var atlasTex = atlasHandle.Texture;
            if (atlasTex == null) return;
            int tileSize = atlasTex.Width / 16;
            using var resized = img.Clone(ctx => ctx.Resize(tileSize, tileSize));
            byte[] raw = new byte[tileSize * tileSize * 4];
            resized.CopyPixelDataTo(raw);
            pixels = raw;
            copyTo = sprite;
            replicate = 1;
            Console.WriteLine($"NostalgiaDynamicTexture.Setup: sprite={sprite}, tileSize={tileSize}, pixelsLength={pixels.Length}");
        }
        catch (Exception ex)
        {
            Console.WriteLine("NostalgiaDynamicTexture.Setup error: " + ex);
        }
    }
}
