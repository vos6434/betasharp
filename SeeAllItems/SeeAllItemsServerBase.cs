using System.Reflection;
using MonoMod.RuntimeDetour;
using System;

namespace SeeAllItems;

public class SeeAllItemsServer
{
    private Hook? _clickHook;

    public void InitializeServer()
    {
        try { Console.WriteLine("SeeAllItemsServer: Initialize"); } catch { }

        var method = typeof(BetaSharp.Server.Network.ServerPlayNetworkHandler).GetMethod("onClickSlot", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (method != null)
        {
            _clickHook = new Hook(method, (Action<Action<object, BetaSharp.Network.Packets.C2SPlay.ClickSlotC2SPacket>, object, BetaSharp.Network.Packets.C2SPlay.ClickSlotC2SPacket>)Server_OnClickSlot);
            try { Console.WriteLine("SeeAllItemsServer: click hook installed"); } catch { }
        }
        else
        {
            try { Console.WriteLine("SeeAllItemsServer: onClickSlot method not found on ServerPlayNetworkHandler"); } catch { }
        }
    }

    public void PostInitializeServer()
    {
        try { Console.WriteLine("SeeAllItemsServer: PostInitialize"); } catch { }
    }

    public void UnloadServer()
    {
        _clickHook?.Dispose(); _clickHook = null;
    }

    private static void Server_OnClickSlot(Action<object, BetaSharp.Network.Packets.C2SPlay.ClickSlotC2SPacket> orig, object handlerObj, BetaSharp.Network.Packets.C2SPlay.ClickSlotC2SPacket packet)
    {
        try
        {
            try { Console.WriteLine($"SeeAllItemsServer: onClickSlot received syncId={packet?.syncId} stack={(packet?.stack==null?"null":packet.stack.itemId+"x"+packet.stack.count)}"); } catch { }

            if (packet.syncId == 127 && packet.stack != null)
            {
                var handler = handlerObj as BetaSharp.Server.Network.ServerPlayNetworkHandler;
                var player = handler?.GetType().GetField("player", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.GetValue(handler) as BetaSharp.Entities.ServerPlayerEntity;
                if (player != null && handler != null)
                {
                    var stack = packet.stack.copy();
                    bool ok = player.inventory.addItemStackToInventory(stack);
                    if (ok)
                    {
                        try { handler.sendPacket(new BetaSharp.Network.Packets.Play.ChatMessagePacket($"§aGave {stack.count}x {stack.itemId} to {player.name}")); } catch { }
                        try { Console.WriteLine($"SeeAllItemsServer: granted {stack.count}x{stack.itemId} to {player.name}"); } catch { }
                        try
                        {
                            var list = new java.util.ArrayList();
                            for (int i = 0; i < player.currentScreenHandler.slots.size(); i++) list.add(((BetaSharp.Screens.Slots.Slot)player.currentScreenHandler.slots.get(i)).getStack());
                            player.onContentsUpdate(player.currentScreenHandler, list);
                        }
                        catch { }
                    }
                    else
                    {
                        try { handler.sendPacket(new BetaSharp.Network.Packets.Play.ChatMessagePacket("§cYour inventory is full.")); } catch { }
                    }
                    return;
                }
            }
        }
        catch (System.Exception ex)
        {
            try { Console.WriteLine("SeeAllItemsServer: click hook error: " + ex); } catch { }
        }

        // not handled — call original
        orig(handlerObj, packet);
    }
}
