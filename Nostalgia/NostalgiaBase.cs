using System;
using BetaSharp.Modding;

namespace Nostalgia;

[ModSide(Side.Client)]
public class NostalgiaBase : ModBase
{
    public override bool HasOptionsMenu => false;

    public NostalgiaBase()
    {
        Name = "Nostalgia";
        Description = "A small nostalgia-themed mod";
        Author = "ModAuthor";
        Version = "1.0.0";
        Logger = Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
    }

    // Backwards-compatible constructor used by the mod loader when it expects a parameterized ctor.
    public NostalgiaBase(string name, string author, string description, string version, Microsoft.Extensions.Logging.ILogger logger)
        : base(name, author, description, version, logger)
    {
    }

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
