using BetaSharp.Client.Rendering;
using BetaSharp.Modding;
using MonoMod.RuntimeDetour;

namespace ExampleMod;

[ModSide(Side.Client)]
public class Mod : IMod
{
    public string Name => "Example Mod";
    public string Description => "Surely does something really cool";
    public string Author => "ExampleAuthor123";
    public Side Side => Side.Client;
    private Hook _textRendererHook = null!;

    public void Initialize()
    {
        Console.WriteLine("Initialize called for Example Mod");
        _textRendererHook = new(typeof(TextRenderer).GetMethod("RenderString")!, TextRenderer_RenderString);
    }

    public void PostInitialize()
    {
        Console.WriteLine($"PostInitialize called for Example Mod. " +
                          $"Loaded mods: [{string.Join(", ", Mods.ModRegistry.Select(m => m.Name))}]");
    }

    public void Unload()
    {
        Console.WriteLine("Example Mod is unloading");
        _textRendererHook.Dispose();
    }

    private static void TextRenderer_RenderString(
        Action<TextRenderer, string, int, int, uint, bool> orig,
        TextRenderer instance,
        string text,
        int x,
        int y,
        uint color,
        bool darken)
    {
        orig(instance, text, x, y, 0xFFFF0000, darken);
    }
}
