using BetaSharp.Modding;

namespace SeeAllItems;

public class SeeAllItemsMod : ModBase
{
    private SeeAllItemsClient? _clientImpl;
    private SeeAllItemsServer? _serverImpl;

    public override string Name => "See All Items";
    public override string Description => "Client+Server support for SeeAllItems overlay.";
    public override string Author => "autogen";
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
