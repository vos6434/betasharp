using BetaSharp.Modding;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SeeAllItems;

public class SeeAllItemsMod : ModBase
{
    private SeeAllItemsClient? _clientImpl;
    private SeeAllItemsServer? _serverImpl;

    public SeeAllItemsMod()
    {
        Name = "See All Items";
        Description = "Client+Server support for SeeAllItems overlay.";
        Author = "autogen";
        Version = "1.0.0";
        Logger = NullLogger.Instance;
    }

    // Backwards-compatible constructor used by the mod loader when it expects a parameterized ctor.
    public SeeAllItemsMod(string name, string author, string description, string version, ILogger logger)
        : base(name, author, description, version, logger)
    {
    }
    public override bool HasOptionsMenu => false;

    public override void Initialize(Side side)
    {
        System.Console.WriteLine($"SeeAllItemsMod: Initialize(side={side})");

        if (side == Side.Client || side == Side.Both)
        {
            _clientImpl = new SeeAllItemsClient();
            _clientImpl.InitializeClient();
        }

        if (side == Side.Server || side == Side.Both)
        {
            _serverImpl = new SeeAllItemsServer();
            _serverImpl.InitializeServer();
        }
    }

    public override void PostInitialize(Side side)
    {
        if (side == Side.Client || side == Side.Both)
        {
            _clientImpl?.PostInitializeClient();
        }
        if (side == Side.Server || side == Side.Both)
        {
            _serverImpl?.PostInitializeServer();
        }
    }

    public override void Unload(Side side)
    {
        if (side == Side.Client || side == Side.Both)
        {
            _clientImpl?.UnloadClient();
            _clientImpl = null;
        }
        if (side == Side.Server || side == Side.Both)
        {
            _serverImpl?.UnloadServer();
            _serverImpl = null;
        }
    }
}
