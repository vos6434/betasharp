using BetaSharp.Entities;
using BetaSharp.Inventorys;
using BetaSharp.Items;
using BetaSharp.Screens.Slots;

namespace Nostalgia;

// Custom handler that mirrors GenericContainerScreenHandler layout but allows
// shifting the player inventory vertically to match custom GUI artwork.
public class NostalgiaScreenHandler : BetaSharp.Screens.ScreenHandler
{
    private IInventory inventory;
    private int rows;
    private int yOffset;

    public NostalgiaScreenHandler(IInventory playerInventory, IInventory inventory, int playerYOffset)
    {
        this.inventory = inventory;
        this.rows = inventory.size() / 9;
        this.yOffset = playerYOffset;
        int var3 = (rows - 4) * 18;

        for (int var4 = 0; var4 < rows; ++var4)
        {
            for (int var5 = 0; var5 < 9; ++var5)
            {
                addSlot(new Slot(inventory, var5 + var4 * 9, 8 + var5 * 18, 18 + var4 * 18));
            }
        }

        for (int var4 = 0; var4 < 3; ++var4)
        {
            for (int var5 = 0; var5 < 9; ++var5)
            {
                addSlot(new Slot(playerInventory, var5 + var4 * 9 + 9, 8 + var5 * 18, 103 + var4 * 18 + var3 + yOffset));
            }
        }

        for (int var4 = 0; var4 < 9; ++var4)
        {
            addSlot(new Slot(playerInventory, var4, 8 + var4 * 18, 161 + var3 + yOffset));
        }
    }

    public override bool canUse(BetaSharp.Entities.EntityPlayer player)
    {
        return inventory.canPlayerUse(player);
    }

    public override BetaSharp.Items.ItemStack quickMove(int slot)
    {
        BetaSharp.Items.ItemStack var2 = null;
        Slot var3 = (Slot)slots.get(slot);
        if (var3 != null && var3.hasStack())
        {
            BetaSharp.Items.ItemStack var4 = var3.getStack();
            var2 = var4.copy();
            if (slot < rows * 9)
            {
                insertItem(var4, rows * 9, slots.size(), true);
            }
            else
            {
                insertItem(var4, 0, rows * 9, false);
            }

            if (var4.count == 0)
            {
                var3.setStack(null);
            }
            else
            {
                var3.markDirty();
            }
        }

        return var2;
    }
}
