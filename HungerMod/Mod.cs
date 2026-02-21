using System.Reflection;
using BetaSharp.Blocks.Materials;
using BetaSharp.Client;
using BetaSharp.Client.Guis;
using BetaSharp.Client.Rendering.Core;
using BetaSharp.Client.Rendering.Items;
using BetaSharp.Entities;
using BetaSharp.Items;
using BetaSharp.Modding;
using BetaSharp.Worlds;
using MonoMod.RuntimeDetour;
using Silk.NET.OpenGL.Legacy;

namespace HungerMod;

[ModSide(Side.Client)]
public class Mod : IMod
{
    private const int DoubledMaxHealth = 40;
    private const int ExtraHudBoxCount = 3;
    private const int ExtraHudBoxSize = 17;
    private const int ExtraHudBoxSpacing = 2;
    private static readonly ItemRenderer HudItemRenderer = new();
    private static readonly List<int> RecentFoodItemIds = [];
    private static readonly FieldInfo? GuiIngameMcField = typeof(GuiIngame)
        .GetField("_mc", BindingFlags.Instance | BindingFlags.NonPublic);

    private Hook? _entityPlayerTickMovementHook;
    private Hook? _entityPlayerTeleportToTopHook;
    private Hook? _entityLivingHealHook;
    private Hook? _guiIngameRenderInventorySlotHook;
    private Hook? _itemFoodUseHook;

    public string Name => "Hunger Mod";
    public string Description => "Gives players 20 hearts (40 health).";
    public string Author => "vos6434";
    public Side Side => Side.Client;

    public void Initialize()
    {
        MethodInfo? playerTickMovementMethod = typeof(EntityPlayer).GetMethod(
            nameof(EntityPlayer.tickMovement),
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            types: Type.EmptyTypes,
            modifiers: null);
        if (playerTickMovementMethod is null)
        {
            Console.WriteLine("HungerMod: failed to hook EntityPlayer.tickMovement (method not found).");
        }
        else
        {
            _entityPlayerTickMovementHook = new Hook(playerTickMovementMethod, EntityPlayer_TickMovement);
        }

        MethodInfo? playerTeleportToTopMethod = typeof(EntityPlayer).GetMethod(
            nameof(EntityPlayer.teleportToTop),
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            types: Type.EmptyTypes,
            modifiers: null);
        if (playerTeleportToTopMethod is null)
        {
            Console.WriteLine("HungerMod: failed to hook EntityPlayer.teleportToTop (method not found).");
        }
        else
        {
            _entityPlayerTeleportToTopHook = new Hook(playerTeleportToTopMethod, EntityPlayer_TeleportToTop);
        }

        MethodInfo? livingHealMethod = typeof(EntityLiving).GetMethod(
            nameof(EntityLiving.heal),
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            types: [typeof(int)],
            modifiers: null);
        if (livingHealMethod is null)
        {
            Console.WriteLine("HungerMod: failed to hook EntityLiving.heal (method not found).");
        }
        else
        {
            _entityLivingHealHook = new Hook(livingHealMethod, EntityLiving_Heal);
        }

        MethodInfo? guiRenderInventorySlotMethod = typeof(GuiIngame).GetMethod(
            "renderInventorySlot",
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            types: [typeof(int), typeof(int), typeof(int), typeof(float)],
            modifiers: null);
        if (guiRenderInventorySlotMethod is null)
        {
            Console.WriteLine("HungerMod: failed to hook GuiIngame.renderInventorySlot (method not found).");
        }
        else
        {
            _guiIngameRenderInventorySlotHook = new Hook(guiRenderInventorySlotMethod, GuiIngame_RenderInventorySlot);
        }

        MethodInfo? itemFoodUseMethod = typeof(ItemFood).GetMethod(
            nameof(ItemFood.use),
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            types: [typeof(ItemStack), typeof(World), typeof(EntityPlayer)],
            modifiers: null);
        if (itemFoodUseMethod is null)
        {
            Console.WriteLine("HungerMod: failed to hook ItemFood.use (method not found).");
        }
        else
        {
            _itemFoodUseHook = new Hook(itemFoodUseMethod, ItemFood_Use);
        }

        Console.WriteLine("HungerMod: enabled 20-heart player health.");
    }

    public void PostInitialize()
    {
    }

    public void Unload()
    {
        _entityPlayerTickMovementHook?.Dispose();
        _entityPlayerTickMovementHook = null;
        _entityPlayerTeleportToTopHook?.Dispose();
        _entityPlayerTeleportToTopHook = null;
        _entityLivingHealHook?.Dispose();
        _entityLivingHealHook = null;
        _guiIngameRenderInventorySlotHook?.Dispose();
        _guiIngameRenderInventorySlotHook = null;
        _itemFoodUseHook?.Dispose();
        _itemFoodUseHook = null;
        RecentFoodItemIds.Clear();
    }

    private static void EntityPlayer_TickMovement(
        Action<EntityPlayer> orig,
        EntityPlayer instance)
    {
        ApplyDoubleHealthProfile(instance, fillToMax: false);
        orig(instance);

        if (instance.world.difficulty == 0 &&
            instance.maxHealth > 20 &&
            instance.health >= 20 &&
            instance.health < instance.maxHealth &&
            instance.age % (20 * 12) == 0)
        {
            instance.heal(1);
        }
    }

    private static void EntityPlayer_TeleportToTop(Action<EntityPlayer> orig, EntityPlayer instance)
    {
        orig(instance);
        ApplyDoubleHealthProfile(instance, fillToMax: true);
    }

    private static void EntityLiving_Heal(Action<EntityLiving, int> orig, EntityLiving instance, int amount)
    {
        if (instance is not EntityPlayer player)
        {
            orig(instance, amount);
            return;
        }

        ApplyDoubleHealthProfile(player, fillToMax: false);
        if (player.health <= 0)
        {
            return;
        }

        player.health += amount;
        if (player.health > player.maxHealth)
        {
            player.health = player.maxHealth;
        }

        player.hearts = player.maxHealth / 2;
    }

    private static void GuiIngame_RenderInventorySlot(
        Action<GuiIngame, int, int, int, float> orig,
        GuiIngame instance,
        int slotIndex,
        int x,
        int y,
        float partialTicks)
    {
        if (slotIndex == 0 &&
            GuiIngameMcField?.GetValue(instance) is Minecraft mc &&
            mc.player is not null &&
            mc.playerController.shouldDrawHUD() &&
            mc.player.maxHealth > 20)
        {
            RenderExtraHealthHud(instance, mc);
        }

        orig(instance, slotIndex, x, y, partialTicks);
    }

    private static void RenderExtraHealthHud(GuiIngame instance, Minecraft mc)
    {

        int extraMaxHealth = mc.player.maxHealth - 20;
        int extraHealth = Math.Clamp(mc.player.health - 20, 0, extraMaxHealth);
        int extraLastHealth = Math.Clamp(mc.player.lastHealth - 20, 0, extraMaxHealth);
        int extraHearts = (extraMaxHealth + 1) / 2;

        if (extraHearts <= 0)
        {
            return;
        }

        ScaledResolution scaled = new(mc.options, mc.displayWidth, mc.displayHeight);
        int scaledWidth = scaled.ScaledWidth;
        int scaledHeight = scaled.ScaledHeight;
        int armorValue = mc.player.getPlayerArmorValue();
        DrawExtraHudBoxes(mc, scaledWidth, scaledHeight, armorValue);

        bool heartBlink = mc.player.hearts / 3 % 2 == 1;
        if (mc.player.hearts < 10)
        {
            heartBlink = false;
        }

        int baseY = scaledHeight - 42;
        if (mc.player.isInFluid(Material.Water))
        {
            baseY -= 9;
        }

        GLManager.GL.Color4(1.0F, 1.0F, 1.0F, 1.0F);
        GLManager.GL.BindTexture(GLEnum.Texture2D, (uint)mc.textureManager.GetTextureId("/gui/icons.png"));

        for (int i = 0; i < extraHearts; i++)
        {
            int row = i / 10;
            int col = i % 10;
            int x = scaledWidth / 2 - 91 + col * 8;
            int y = baseY - row * 10;

            byte blinkIndex = heartBlink ? (byte)1 : (byte)0;
            instance.DrawTexturedModalRect(x, y, 16 + blinkIndex * 9, 0, 9, 9);

            if (heartBlink)
            {
                if (i * 2 + 1 < extraLastHealth)
                {
                    instance.DrawTexturedModalRect(x, y, 70, 0, 9, 9);
                }

                if (i * 2 + 1 == extraLastHealth)
                {
                    instance.DrawTexturedModalRect(x, y, 79, 0, 9, 9);
                }
            }

            if (i * 2 + 1 < extraHealth)
            {
                instance.DrawTexturedModalRect(x, y, 52, 0, 9, 9);
            }

            if (i * 2 + 1 == extraHealth)
            {
                instance.DrawTexturedModalRect(x, y, 61, 0, 9, 9);
            }
        }
    }

    private static void DrawExtraHudBoxes(Minecraft mc, int scaledWidth, int scaledHeight, int armorValue)
    {
        int hudRightX = scaledWidth / 2 + 91;
        int totalWidth = ExtraHudBoxCount * ExtraHudBoxSize + (ExtraHudBoxCount - 1) * ExtraHudBoxSpacing;
        int boxStartX = hudRightX - totalWidth;

        int hotbarTopY = scaledHeight - 22;
        int armorRowY = scaledHeight - 32;
        int boxY = armorValue > 0
            ? armorRowY - ExtraHudBoxSize - 1
            : hotbarTopY - ExtraHudBoxSize - 1;

        for (int i = 0; i < ExtraHudBoxCount; i++)
        {
            int x = boxStartX + i * (ExtraHudBoxSize + ExtraHudBoxSpacing);
            DrawHudBox(x, boxY);
        }

        for (int i = 0; i < RecentFoodItemIds.Count && i < ExtraHudBoxCount; i++)
        {
            int itemId = RecentFoodItemIds[i];
            if (itemId < 0 || itemId >= Item.ITEMS.Length || Item.ITEMS[itemId] is null)
            {
                continue;
            }

            int iconX = boxStartX + i * (ExtraHudBoxSize + ExtraHudBoxSpacing) + 1;
            int iconY = boxY + 1;
            ItemStack iconStack = new(itemId, 1);
            HudItemRenderer.renderItemIntoGUI(mc.fontRenderer, mc.textureManager, iconStack, iconX, iconY);
        }

        GLManager.GL.Enable(GLEnum.Texture2D);
        GLManager.GL.Color4(1.0F, 1.0F, 1.0F, 1.0F);
    }

    private static void DrawHudBox(int x, int y)
    {
        DrawSolidRect(x, y, x + ExtraHudBoxSize, y + ExtraHudBoxSize, 0x80000000);
    }

    private static void DrawSolidRect(int left, int top, int right, int bottom, uint color)
    {
        float a = (color >> 24 & 255) / 255.0F;
        float r = (color >> 16 & 255) / 255.0F;
        float g = (color >> 8 & 255) / 255.0F;
        float b = (color & 255) / 255.0F;

        Tessellator tess = Tessellator.instance;

        GLManager.GL.Enable(GLEnum.Blend);
        GLManager.GL.Disable(GLEnum.Texture2D);
        GLManager.GL.BlendFunc(GLEnum.SrcAlpha, GLEnum.OneMinusSrcAlpha);
        GLManager.GL.Color4(r, g, b, a);

        tess.startDrawingQuads();
        tess.addVertex(left, bottom, 0.0D);
        tess.addVertex(right, bottom, 0.0D);
        tess.addVertex(right, top, 0.0D);
        tess.addVertex(left, top, 0.0D);
        tess.draw();

        GLManager.GL.Enable(GLEnum.Texture2D);
        GLManager.GL.Disable(GLEnum.Blend);
        GLManager.GL.Color4(1.0F, 1.0F, 1.0F, 1.0F);
    }

    private static void ApplyDoubleHealthProfile(EntityPlayer player, bool fillToMax)
    {
        player.maxHealth = DoubledMaxHealth;
        if (fillToMax)
        {
            player.health = DoubledMaxHealth;
            player.lastHealth = DoubledMaxHealth;
        }
        else
        {
            if (player.health > DoubledMaxHealth)
            {
                player.health = DoubledMaxHealth;
            }

            if (player.lastHealth > DoubledMaxHealth)
            {
                player.lastHealth = DoubledMaxHealth;
            }
        }
    }

    private static ItemStack ItemFood_Use(
        Func<ItemFood, ItemStack, World, EntityPlayer, ItemStack> orig,
        ItemFood instance,
        ItemStack itemStack,
        World world,
        EntityPlayer entityPlayer)
    {
        int foodItemId = itemStack.itemId;
        int countBefore = itemStack.count;

        ItemStack result = orig(instance, itemStack, world, entityPlayer);
        int countAfter = result?.count ?? itemStack.count;

        if (countAfter < countBefore && !RecentFoodItemIds.Contains(foodItemId))
        {
            RecentFoodItemIds.Add(foodItemId);
            while (RecentFoodItemIds.Count > ExtraHudBoxCount)
            {
                RecentFoodItemIds.RemoveAt(0);
            }
        }

        return result;
    }
}
