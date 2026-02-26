using BetaSharp.Blocks;
using BetaSharp.Blocks.Materials;
using BetaSharp.Entities;
using BetaSharp.Worlds;

namespace Nostalgia;

public class NostalgiaBlock : Block
{
    public NostalgiaBlock(int id, int textureId, Material material) : base(id, textureId, material)
    {
    }

    public override bool onUse(World world, int x, int y, int z, EntityPlayer player)
    {
        try
        {
            if (world.isRemote)
            {
                var mc = BetaSharp.Client.Minecraft.INSTANCE;
                if (mc != null)
                {
                    mc.displayGuiScreen(new NostalgiaGui());
                }

                return true;
            }
        }
        catch { }

        return true;
    }
}
