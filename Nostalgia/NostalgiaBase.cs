using System;
using BetaSharp.Modding;

namespace Nostalgia;

[ModSide(Side.Client)]
public class NostalgiaBase : ModBase
{
    public override bool HasOptionsMenu => false;

    public override void Initialize(Side side)
    {
        Console.WriteLine("Initialize called for Nostalgia mod");
    }

    public override void PostInitialize(Side side)
    {
        Console.WriteLine("PostInitialize called for Nostalgia mod");
    }

    public override void Unload(Side side)
    {
        Console.WriteLine("Nostalgia mod is unloading");
    }
}
