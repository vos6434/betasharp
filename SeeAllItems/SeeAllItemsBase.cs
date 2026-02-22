using BetaSharp.Modding;

namespace SeeAllItems;

[ModSide(Side.Client)]
public class SeeAllItemsBase : ModBase
{
    public override string Name => "See All Items";
    public override string Description => "A small mod placeholder that will list all items.";
    public override string Author => "autogen";
    public override bool HasOptionsMenu => false;

    public override void Initialize(Side side)
    {
        Console.WriteLine("SeeAllItems: initialized");
    }

    public override void PostInitialize(Side side)
    {
        Console.WriteLine("SeeAllItems: post-initialize");
    }

    public override void Unload(Side side)
    {
        Console.WriteLine("SeeAllItems: unloading");
    }
}
