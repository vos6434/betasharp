using System.Reflection;
using System.Text.Json;
using BetaSharp.Blocks.Materials;
using BetaSharp.Client;
using BetaSharp.Client.Entities;
using BetaSharp.Client.Guis;
using BetaSharp.Client.Network;
using BetaSharp.Client.Rendering.Core;
using BetaSharp.Client.Rendering.Items;
using BetaSharp.Entities;
using BetaSharp.Items;
using BetaSharp.Modding;
using BetaSharp.NBT;
using BetaSharp.Worlds;
using MonoMod.RuntimeDetour;
using Silk.NET.OpenGL.Legacy;

namespace HungerMod;

[ModSide(Side.Both)]
public class HungerModBase : ModBase
{
    private const int BaseMaxHealth = 20;
    private const int AbsoluteMaxHealth = 40;
    private const int TicksPerSecond = 20;
    private const int LowTimeThresholdTicks = 30 * TicksPerSecond;
    private const int LowTimeBlinkPeriodTicks = 8;
    private const int DefaultExtraHudBoxCount = 3;
    private const int DefaultExtraHudBoxSize = 16;
    private const int DefaultHudItemIconSize = 16;
    private const int DefaultExtraHudBoxSpacing = 2;
    private const string ConfigFileName = "HungerMod.json";
    private const string FoodsNbtKey = "HungerModFoods";
    private const string RegenNbtKey = "HungerModRegen";
    private const string FoodItemIdNbtKey = "ItemId";
    private const string FoodTicksNbtKey = "Ticks";

    private static readonly JsonSerializerOptions ConfigJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    // Client-only reflection/rendering fields — initialize when running on client.
    private static ItemRenderer? HudItemRenderer = null;
    private static FieldInfo? GuiIngameMcField = null;
    private static FieldInfo? ClientPlayerMcField = null;
    private static int ExtraHudBoxCount = DefaultExtraHudBoxCount;
    private static int ExtraHudBoxSize = DefaultExtraHudBoxSize;
    private static int HudItemIconSize = DefaultHudItemIconSize;
    private static int ExtraHudBoxSpacing = DefaultExtraHudBoxSpacing;
    private static Dictionary<int, FoodDefinition> FoodDefinitions = CreateDefaultFoodDefinitions();
    private static readonly Dictionary<EntityPlayer, PlayerFoodState> PlayerFoodStates = [];

    private Hook? _entityPlayerTickMovementHook;
    private Hook? _entityPlayerTeleportToTopHook;
    private Hook? _entityLivingHealHook;
    private Hook? _entityPlayerReadNbtHook;
    private Hook? _entityPlayerWriteNbtHook;
    private Hook? _guiIngameRenderInventorySlotHook;
    private Hook? _guiIngameRenderGameOverlayHook;
    private Hook? _itemFoodUseHook;
    private Hook? _itemSoupUseHook;
    private Hook? _clientPlayerSendChatMessageHook;
    private Hook? _entityClientPlayerMPSendChatMessageHook;
    private Hook? _playerControllerMPSendUseItemHook;

    public override string Name => "Hunger Mod";
    public override string Description => "Valheim-style timed food buffs for health and regeneration.";
    public override string Author => "vos6434";

    public override void Initialize(Side side)
    {
        LoadConfig();

        // If running on client or a combined client+server process, initialize client-only renderer/fieldinfo and hooks.
        if (side == Side.Client || side == Side.Both)
        {
            HudItemRenderer = new ItemRenderer();
            GuiIngameMcField = typeof(GuiIngame).GetField("_mc", BindingFlags.Instance | BindingFlags.NonPublic);
            ClientPlayerMcField = typeof(ClientPlayerEntity).GetField("mc", BindingFlags.Instance | BindingFlags.NonPublic);
        }

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

        MethodInfo? playerReadNbtMethod = typeof(EntityPlayer).GetMethod(
            nameof(EntityPlayer.readNbt),
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            types: [typeof(NBTTagCompound)],
            modifiers: null);
        if (playerReadNbtMethod is null)
        {
            Console.WriteLine("HungerMod: failed to hook EntityPlayer.readNbt (method not found).");
        }
        else
        {
            _entityPlayerReadNbtHook = new Hook(playerReadNbtMethod, EntityPlayer_ReadNbt);
        }

        MethodInfo? playerWriteNbtMethod = typeof(EntityPlayer).GetMethod(
            nameof(EntityPlayer.writeNbt),
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            types: [typeof(NBTTagCompound)],
            modifiers: null);
        if (playerWriteNbtMethod is null)
        {
            Console.WriteLine("HungerMod: failed to hook EntityPlayer.writeNbt (method not found).");
        }
        else
        {
            _entityPlayerWriteNbtHook = new Hook(playerWriteNbtMethod, EntityPlayer_WriteNbt);
        }

        // Only hook GUI-related methods on the client side (or if both sides are loaded into this process).
        if (side == Side.Client || side == Side.Both)
        {
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

            MethodInfo? guiRenderGameOverlayMethod = typeof(GuiIngame).GetMethod(
                nameof(GuiIngame.renderGameOverlay),
                BindingFlags.Instance | BindingFlags.Public,
                binder: null,
                types: [typeof(float), typeof(bool), typeof(int), typeof(int)],
                modifiers: null);
            if (guiRenderGameOverlayMethod is null)
            {
                Console.WriteLine("HungerMod: failed to hook GuiIngame.renderGameOverlay (method not found).");
            }
            else
            {
                _guiIngameRenderGameOverlayHook = new Hook(guiRenderGameOverlayMethod, GuiIngame_RenderGameOverlay);
            }

            MethodInfo? clientPlayerSendChatMessageMethod = typeof(ClientPlayerEntity).GetMethod(
                nameof(ClientPlayerEntity.sendChatMessage),
                BindingFlags.Instance | BindingFlags.Public,
                binder: null,
                types: [typeof(string)],
                modifiers: null);
            if (clientPlayerSendChatMessageMethod is null)
            {
                Console.WriteLine("HungerMod: failed to hook ClientPlayerEntity.sendChatMessage (method not found).");
            }
            else
            {
                _clientPlayerSendChatMessageHook = new Hook(
                    clientPlayerSendChatMessageMethod,
                    ClientPlayerEntity_SendChatMessage);
            }

            MethodInfo? entityClientPlayerMPSendChatMessageMethod = typeof(EntityClientPlayerMP).GetMethod(
                nameof(EntityClientPlayerMP.sendChatMessage),
                BindingFlags.Instance | BindingFlags.Public,
                binder: null,
                types: [typeof(string)],
                modifiers: null);
            if (entityClientPlayerMPSendChatMessageMethod is null)
            {
                Console.WriteLine("HungerMod: failed to hook EntityClientPlayerMP.sendChatMessage (method not found).");
            }
            else
            {
                _entityClientPlayerMPSendChatMessageHook = new Hook(
                    entityClientPlayerMPSendChatMessageMethod,
                    EntityClientPlayerMP_SendChatMessage);
            }

            // Hook PlayerControllerMP.sendUseItem to prevent sending the use packet when
            // the player already has an active food buff (client-side guard).
            try
            {
                Type? pcType = null;
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    pcType = asm.GetType("BetaSharp.Client.Input.PlayerControllerMP");
                    if (pcType != null) break;
                }

                if (pcType != null)
                {
                    MethodInfo? sendUseItemMethod = pcType.GetMethod(
                        "sendUseItem",
                        BindingFlags.Instance | BindingFlags.Public,
                        binder: null,
                        types: new Type[] { typeof(EntityPlayer), typeof(World), typeof(ItemStack) },
                        modifiers: null);

                    if (sendUseItemMethod != null)
                    {
                        _playerControllerMPSendUseItemHook = new Hook(sendUseItemMethod, PlayerControllerMP_SendUseItem);
                    }
                }
            }
            catch { }
        }

        // Locate the game's runtime types for ItemFood/ItemStack/World/EntityPlayer so hooks
        // work regardless of assembly load contexts (client vs server types may differ).
        Type? gameItemFoodType = null;
        Type? gameItemStackType = null;
        Type? gameWorldType = null;
        Type? gameEntityPlayerType = null;
        foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            gameItemFoodType = asm.GetType("BetaSharp.Items.ItemFood");
            if (gameItemFoodType != null)
            {
                gameItemStackType = asm.GetType("BetaSharp.Items.ItemStack");
                gameWorldType = asm.GetType("BetaSharp.Worlds.World");
                gameEntityPlayerType = asm.GetType("BetaSharp.Entities.EntityPlayer");
                break;
            }
        }

        if (gameItemFoodType == null || gameItemStackType == null || gameWorldType == null || gameEntityPlayerType == null)
        {
            Console.WriteLine("HungerMod: failed to locate game types for ItemFood hook.");
        }
        else
        {
            MethodInfo? itemFoodUseMethod = gameItemFoodType.GetMethod(
                "use",
                BindingFlags.Instance | BindingFlags.Public,
                binder: null,
                types: new Type[] { gameItemStackType, gameWorldType, gameEntityPlayerType },
                modifiers: null);

            if (itemFoodUseMethod is null)
            {
                Console.WriteLine("HungerMod: failed to hook ItemFood.use (method not found).");
            }
            else
            {
                _itemFoodUseHook = new Hook(itemFoodUseMethod, ItemFood_Use);
            }
        }

        if (gameItemFoodType != null && gameItemStackType != null && gameWorldType != null && gameEntityPlayerType != null)
        {
            Type? gameItemSoupType = null;
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                gameItemSoupType = asm.GetType("BetaSharp.Items.ItemSoup");
                if (gameItemSoupType != null)
                {
                    break;
                }
            }

            if (gameItemSoupType == null)
            {
                Console.WriteLine("HungerMod: failed to locate ItemSoup type for hook.");
            }
            else
            {
                MethodInfo? itemSoupUseMethod = gameItemSoupType.GetMethod(
                    "use",
                    BindingFlags.Instance | BindingFlags.Public,
                    binder: null,
                    types: new Type[] { gameItemStackType, gameWorldType, gameEntityPlayerType },
                    modifiers: null);

                if (itemSoupUseMethod is null)
                {
                    Console.WriteLine("HungerMod: failed to hook ItemSoup.use (method not found).");
                }
                else
                {
                    _itemSoupUseHook = new Hook(itemSoupUseMethod, ItemSoup_Use);
                }
            }
        }

        Console.WriteLine("HungerMod: enabled timed food effects.");
    }

    public override void PostInitialize(Side side)
    {
    }

    public override void Unload(Side side)
    {
        _entityPlayerTickMovementHook?.Dispose();
        _entityPlayerTickMovementHook = null;
        _entityPlayerTeleportToTopHook?.Dispose();
        _entityPlayerTeleportToTopHook = null;
        _entityLivingHealHook?.Dispose();
        _entityLivingHealHook = null;
        _entityPlayerReadNbtHook?.Dispose();
        _entityPlayerReadNbtHook = null;
        _entityPlayerWriteNbtHook?.Dispose();
        _entityPlayerWriteNbtHook = null;
        _guiIngameRenderInventorySlotHook?.Dispose();
        _guiIngameRenderInventorySlotHook = null;
        _guiIngameRenderGameOverlayHook?.Dispose();
        _guiIngameRenderGameOverlayHook = null;
        _itemFoodUseHook?.Dispose();
        _itemFoodUseHook = null;
        _itemSoupUseHook?.Dispose();
        _itemSoupUseHook = null;
        _clientPlayerSendChatMessageHook?.Dispose();
        _clientPlayerSendChatMessageHook = null;
        _entityClientPlayerMPSendChatMessageHook?.Dispose();
        _entityClientPlayerMPSendChatMessageHook = null;
        _playerControllerMPSendUseItemHook?.Dispose();
        _playerControllerMPSendUseItemHook = null;
        PlayerFoodStates.Clear();
    }

    private static void EntityPlayer_TickMovement(
        Action<EntityPlayer> orig,
        EntityPlayer player)
    {
        PlayerFoodState state = GetOrCreatePlayerFoodState(player);
        RemoveExpiredEffects(state);
        ApplyFoodHealthProfile(player, state);

        orig(player);
        TickFoodEffects(player, state);
    }

    private static void EntityPlayer_TeleportToTop(Action<EntityPlayer> orig, EntityPlayer player)
    {
        orig(player);
        ApplyFoodHealthProfile(player, GetOrCreatePlayerFoodState(player));
    }

    private static void EntityLiving_Heal(Action<EntityLiving, int> orig, EntityLiving instance, int amount)
    {
        if (instance is not EntityPlayer player)
        {
            orig(instance, amount);
            return;
        }

        ApplyFoodHealthProfile(player, GetOrCreatePlayerFoodState(player));
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

    private static void EntityPlayer_ReadNbt(
        Action<EntityPlayer, NBTTagCompound> orig,
        EntityPlayer player,
        NBTTagCompound nbt)
    {
        orig(player, nbt);

        PlayerFoodState state = GetOrCreatePlayerFoodState(player);
        ReadFoodStateFromNbt(state, nbt);
        ApplyFoodHealthProfile(player, state);
    }

    private static void EntityPlayer_WriteNbt(
        Action<EntityPlayer, NBTTagCompound> orig,
        EntityPlayer player,
        NBTTagCompound nbt)
    {
        orig(player, nbt);
        WriteFoodStateToNbt(GetOrCreatePlayerFoodState(player), nbt);
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
            mc.playerController.shouldDrawHUD())
        {
            RenderFoodHud(instance, mc);
        }

        orig(instance, slotIndex, x, y, partialTicks);
    }

    private static void GuiIngame_RenderGameOverlay(
        Action<GuiIngame, float, bool, int, int> orig,
        GuiIngame instance,
        float partialTicks,
        bool unusedFlag,
        int unusedA,
        int unusedB)
    {
        orig(instance, partialTicks, unusedFlag, unusedA, unusedB);

        if (GuiIngameMcField?.GetValue(instance) is not Minecraft mc ||
            mc.player is null ||
            !mc.options.ShowDebugInfo)
        {
            return;
        }

        RenderFoodDebugOverlay(mc);
    }

    private static void RenderFoodHud(GuiIngame instance, Minecraft mc)
    {
        ScaledResolution scaled = new(mc.options, mc.displayWidth, mc.displayHeight);
        int scaledWidth = scaled.ScaledWidth;
        int scaledHeight = scaled.ScaledHeight;
        int armorValue = mc.player.getPlayerArmorValue();
        DrawFoodHudBoxes(mc, scaledWidth, scaledHeight, armorValue);

        int extraMaxHealth = mc.player.maxHealth - BaseMaxHealth;
        int extraHealth = Math.Clamp(mc.player.health - 20, 0, extraMaxHealth);
        int extraLastHealth = Math.Clamp(mc.player.lastHealth - 20, 0, extraMaxHealth);
        int extraHearts = (extraMaxHealth + 1) / 2;

        if (extraHearts <= 0)
        {
            return;
        }

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

    private static void DrawFoodHudBoxes(Minecraft mc, int scaledWidth, int scaledHeight, int armorValue)
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

        PlayerFoodState state = GetOrCreatePlayerFoodState(mc.player);
        int iconOffset = (int)Math.Floor((ExtraHudBoxSize - HudItemIconSize) / 2.0D);
        for (int i = 0; i < state.ActiveFoods.Count && i < ExtraHudBoxCount; i++)
        {
            ActiveFoodEffect activeFood = state.ActiveFoods[i];
            int itemId = activeFood.ItemId;
            if (itemId < 0 || itemId >= Item.ITEMS.Length || Item.ITEMS[itemId] is null)
            {
                continue;
            }

            int boxX = boxStartX + i * (ExtraHudBoxSize + ExtraHudBoxSpacing);
            int iconX = boxX + iconOffset;
            int iconY = boxY + iconOffset;
            bool isLowTime = activeFood.RemainingTicks < LowTimeThresholdTicks;
            bool shouldDrawIcon = !isLowTime || (mc.player.age / LowTimeBlinkPeriodTicks) % 2 == 0;

            if (shouldDrawIcon)
            {
                ItemStack iconStack = new(itemId, 1);
                HudItemRenderer.renderItemIntoGUI(mc.fontRenderer, mc.textureManager, iconStack, iconX, iconY);
            }

            if (!isLowTime)
            {
                DrawFoodTimeOverlay(mc, FormatRemainingTime(activeFood.RemainingTicks), iconX, iconY);
            }
        }

        GLManager.GL.Enable(GLEnum.Texture2D);
        GLManager.GL.Color4(1.0F, 1.0F, 1.0F, 1.0F);
    }

    private static void RenderFoodDebugOverlay(Minecraft mc)
    {
        int meshY = mc.internalServer != null ? 120 : 104;
        int startY = meshY + 16;

        Gui.DrawString(mc.fontRenderer, "HungerMod Foods:", 2, startY, 0xE0E0E0);

        PlayerFoodState state = GetOrCreatePlayerFoodState(mc.player);
        for (int i = 0; i < ExtraHudBoxCount; i++)
        {
            string lineText;
            if (i < state.ActiveFoods.Count)
            {
                ActiveFoodEffect activeFood = state.ActiveFoods[i];
                string itemName = GetItemDisplayName(activeFood.ItemId);
                string remaining = FormatRemainingTimeMinutesSeconds(activeFood.RemainingTicks);
                lineText = $"Box {i + 1}: {itemName} ({remaining})";
            }
            else
            {
                lineText = $"Box {i + 1}: (empty)";
            }

            Gui.DrawString(mc.fontRenderer, lineText, 2, startY + 10 + i * 10, 0xE0E0E0);
        }
    }

    private static void DrawFoodTimeOverlay(Minecraft mc, string text, int iconX, int iconY)
    {
        int textX = iconX + 17 - mc.fontRenderer.GetStringWidth(text);
        int textY = iconY + 9;

        GLManager.GL.Disable(GLEnum.Lighting);
        GLManager.GL.Disable(GLEnum.DepthTest);
        mc.fontRenderer.DrawStringWithShadow(text, textX, textY, 0xFFFFFF);
        GLManager.GL.Enable(GLEnum.Lighting);
        GLManager.GL.Enable(GLEnum.DepthTest);
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

    private static ItemStack ItemFood_Use(
        Func<ItemFood, ItemStack, World, EntityPlayer, ItemStack> orig,
        ItemFood instance,
        ItemStack itemStack,
        World world,
        EntityPlayer entityPlayer)
    {
        if (!FoodDefinitions.TryGetValue(itemStack.itemId, out FoodDefinition definition))
        {
            return orig(instance, itemStack, world, entityPlayer);
        }

        if (ShouldBlockFoodConsume(entityPlayer, itemStack.itemId))
        {
            return itemStack;
        }

        // (debug logging removed)
        // Call original to ensure normal consumption behavior (including server packet)
        int prevHealth = entityPlayer.health;
        int prevLastHealth = entityPlayer.lastHealth;
        int prevHearts = entityPlayer.hearts;

        ItemStack resultStack = orig(instance, itemStack, world, entityPlayer);

        // (debug logging removed)
        // Revert the original heal so our timed food system controls health changes.
        entityPlayer.health = prevHealth;
        entityPlayer.lastHealth = prevLastHealth;
        entityPlayer.hearts = prevHearts;

        // Apply our timed food effects only on the server (authoritative).
        try
        {
            if (world == null || !world.isRemote)
            {
                ConsumeFoodAndApplyEffects(entityPlayer, resultStack, definition);
            }
        }
        catch { }

        // Do NOT manually modify the inventory here. The original `use` call
        // and server-side logic are authoritative for item consumption and
        // will synchronize the slot to the client. Return the stack returned
        // by the original method.
        return resultStack;
    }

    private static ItemStack ItemSoup_Use(
        Func<ItemSoup, ItemStack, World, EntityPlayer, ItemStack> orig,
        ItemSoup instance,
        ItemStack itemStack,
        World world,
        EntityPlayer entityPlayer)
    {
        if (FoodDefinitions.ContainsKey(itemStack.itemId) &&
            ShouldBlockFoodConsume(entityPlayer, itemStack.itemId))
        {
            return itemStack;
        }

        return orig(instance, itemStack, world, entityPlayer);
    }

    private static void ClientPlayerEntity_SendChatMessage(
        Action<ClientPlayerEntity, string> orig,
        ClientPlayerEntity player,
        string message)
    {
        if (TryHandleClearHungerCommand(player, message))
        {
            return;
        }

        orig(player, message);
    }

    private static void EntityClientPlayerMP_SendChatMessage(
        Action<EntityClientPlayerMP, string> orig,
        EntityClientPlayerMP player,
        string message)
    {
        if (TryHandleClearHungerCommand(player, message))
        {
            return;
        }

        orig(player, message);
    }

    private static bool TryHandleClearHungerCommand(ClientPlayerEntity player, string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        string[] parts = message.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || !parts[0].Equals("/clearhunger", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        ClearFoodEffects(player);
        AddPlayerChatMessage(player, "HungerMod: cleared active food effects.");
        return true;
    }

    private static bool PlayerControllerMP_SendUseItem(
        Func<object, EntityPlayer, World, ItemStack, bool> orig,
        object instance,
        EntityPlayer player,
        World world,
        ItemStack itemStack)
    {
        try
        {
            if (itemStack != null && FoodDefinitions.ContainsKey(itemStack.itemId) && ShouldBlockFoodConsume(player, itemStack.itemId))
            {
                return false;
            }
        }
        catch { }

        // Fallback to original
        return orig(instance, player, world, itemStack);
    }

    private static void ClearFoodEffects(EntityPlayer player)
    {
        PlayerFoodState state = GetOrCreatePlayerFoodState(player);
        state.ActiveFoods.Clear();
        state.RegenAccumulator = 0.0F;
        ApplyFoodHealthProfile(player, state);
    }

    private static void AddPlayerChatMessage(ClientPlayerEntity player, string message)
    {
        if (ClientPlayerMcField?.GetValue(player) is Minecraft mc)
        {
            mc.ingameGUI.addChatMessage(message);
        }
    }

    private static void ConsumeFoodAndApplyEffects(EntityPlayer player, ItemStack stack, FoodDefinition definition)
    {
        PlayerFoodState state = GetOrCreatePlayerFoodState(player);
        int previousMaxHealth = CalculateMaxHealth(state);
        state.ActiveFoods.Add(new ActiveFoodEffect(stack.itemId, definition.DurationSeconds * TicksPerSecond));
        state.RegenAccumulator = 0.0F;

        ApplyFoodHealthProfile(player, state);

        int maxHealthGain = Math.Max(0, player.maxHealth - previousMaxHealth);
        if (maxHealthGain > 0 && player.health > 0)
        {
            player.health = Math.Min(player.maxHealth, player.health + maxHealthGain);
            player.lastHealth = Math.Min(player.maxHealth, player.lastHealth + maxHealthGain);
            player.hearts = player.maxHealth / 2;
        }
    }

    private static bool ShouldBlockFoodConsume(EntityPlayer player, int foodItemId)
    {
        PlayerFoodState state = GetOrCreatePlayerFoodState(player);
        RemoveExpiredEffects(state);

        if (ContainsActiveFood(state, foodItemId))
        {
            return true;
        }

        return state.ActiveFoods.Count >= ExtraHudBoxCount;
    }

    private static bool ContainsActiveFood(PlayerFoodState state, int foodItemId)
    {
        for (int i = 0; i < state.ActiveFoods.Count; i++)
        {
            if (state.ActiveFoods[i].ItemId == foodItemId)
            {
                return true;
            }
        }

        return false;
    }

    private static void TickFoodEffects(EntityPlayer player, PlayerFoodState state)
    {
        if (player.health <= 0)
        {
            state.ActiveFoods.Clear();
            state.RegenAccumulator = 0.0F;
            ApplyFoodHealthProfile(player, state);
            return;
        }

        for (int i = 0; i < state.ActiveFoods.Count; i++)
        {
            state.ActiveFoods[i].RemainingTicks--;
        }

        RemoveExpiredEffects(state);
        ApplyFoodHealthProfile(player, state);

        float totalRegenPerSecond = GetTotalRegenPerSecond(state);
        if (totalRegenPerSecond <= 0.0F || player.health >= player.maxHealth)
        {
            state.RegenAccumulator = 0.0F;
            return;
        }

        state.RegenAccumulator += totalRegenPerSecond / TicksPerSecond;
        while (state.RegenAccumulator >= 1.0F && player.health < player.maxHealth)
        {
            player.heal(1);
            state.RegenAccumulator -= 1.0F;
        }

        if (player.health >= player.maxHealth)
        {
            state.RegenAccumulator = 0.0F;
        }
    }

    private static float GetTotalRegenPerSecond(PlayerFoodState state)
    {
        float total = 0.0F;
        for (int i = 0; i < state.ActiveFoods.Count; i++)
        {
            int itemId = state.ActiveFoods[i].ItemId;
            if (FoodDefinitions.TryGetValue(itemId, out FoodDefinition definition))
            {
                total += definition.RegenPerSecond;
            }
        }

        return total;
    }

    private static void RemoveExpiredEffects(PlayerFoodState state)
    {
        for (int i = state.ActiveFoods.Count - 1; i >= 0; i--)
        {
            if (state.ActiveFoods[i].RemainingTicks <= 0)
            {
                state.ActiveFoods.RemoveAt(i);
            }
        }
    }

    private static void ApplyFoodHealthProfile(EntityPlayer player, PlayerFoodState state)
    {
        player.maxHealth = CalculateMaxHealth(state);
        if (player.health > player.maxHealth)
        {
            player.health = player.maxHealth;
        }

        if (player.lastHealth > player.maxHealth)
        {
            player.lastHealth = player.maxHealth;
        }

        if (player.hearts > player.maxHealth)
        {
            player.hearts = player.maxHealth;
        }
    }

    private static int CalculateMaxHealth(PlayerFoodState state)
    {
        int bonusHealth = 0;
        for (int i = 0; i < state.ActiveFoods.Count; i++)
        {
            int itemId = state.ActiveFoods[i].ItemId;
            if (FoodDefinitions.TryGetValue(itemId, out FoodDefinition definition))
            {
                bonusHealth += definition.BonusHealth;
            }
        }

        int computedMaxHealth = BaseMaxHealth + bonusHealth;
        if (computedMaxHealth > AbsoluteMaxHealth)
        {
            computedMaxHealth = AbsoluteMaxHealth;
        }

        if (computedMaxHealth < BaseMaxHealth)
        {
            computedMaxHealth = BaseMaxHealth;
        }

        return computedMaxHealth;
    }

    private static PlayerFoodState GetOrCreatePlayerFoodState(EntityPlayer player)
    {
        if (!PlayerFoodStates.TryGetValue(player, out PlayerFoodState? state))
        {
            state = new PlayerFoodState();
            PlayerFoodStates[player] = state;
        }

        return state;
    }

    private static void WriteFoodStateToNbt(PlayerFoodState state, NBTTagCompound nbt)
    {
        RemoveExpiredEffects(state);

        NBTTagList foodsTagList = new();
        for (int i = 0; i < state.ActiveFoods.Count; i++)
        {
            ActiveFoodEffect activeFood = state.ActiveFoods[i];
            NBTTagCompound foodTag = new();
            foodTag.SetInteger(FoodItemIdNbtKey, activeFood.ItemId);
            foodTag.SetInteger(FoodTicksNbtKey, activeFood.RemainingTicks);
            foodsTagList.SetTag(foodTag);
        }

        nbt.SetTag(FoodsNbtKey, foodsTagList);
        nbt.SetFloat(RegenNbtKey, state.RegenAccumulator);
    }

    private static void ReadFoodStateFromNbt(PlayerFoodState state, NBTTagCompound nbt)
    {
        state.ActiveFoods.Clear();
        state.RegenAccumulator = Math.Max(0.0F, nbt.GetFloat(RegenNbtKey));

        if (!nbt.HasKey(FoodsNbtKey))
        {
            return;
        }

        NBTTagList foodsTagList = nbt.GetTagList(FoodsNbtKey);
        for (int i = 0; i < foodsTagList.TagCount(); i++)
        {
            if (foodsTagList.TagAt(i) is not NBTTagCompound foodTag)
            {
                continue;
            }

            int itemId = foodTag.GetInteger(FoodItemIdNbtKey);
            int remainingTicks = foodTag.GetInteger(FoodTicksNbtKey);
            if (remainingTicks <= 0 || !FoodDefinitions.ContainsKey(itemId) || ContainsActiveFood(state, itemId))
            {
                continue;
            }

            if (state.ActiveFoods.Count >= ExtraHudBoxCount)
            {
                break;
            }

            state.ActiveFoods.Add(new ActiveFoodEffect(itemId, remainingTicks));
        }
    }

    private static string FormatRemainingTime(int remainingTicks)
    {
        int remainingSeconds = Math.Max(0, (remainingTicks + TicksPerSecond - 1) / TicksPerSecond);
        int remainingMinutes = (remainingSeconds + 59) / 60;
        return remainingMinutes.ToString();
    }

    private static string FormatRemainingTimeMinutesSeconds(int remainingTicks)
    {
        int totalSeconds = Math.Max(0, (remainingTicks + TicksPerSecond - 1) / TicksPerSecond);
        int minutes = totalSeconds / 60;
        int seconds = totalSeconds % 60;
        return $"{minutes}:{seconds:D2}";
    }

    private static string GetItemDisplayName(int itemId)
    {
        if (itemId < 0 || itemId >= Item.ITEMS.Length || Item.ITEMS[itemId] is not Item item)
        {
            return $"Unknown ({itemId})";
        }

        return item.getStatName();
    }

    private static Dictionary<int, FoodDefinition> CreateDefaultFoodDefinitions()
    {
        return new Dictionary<int, FoodDefinition>
        {
            // FoodDefinition(BonusHealth, RegenPerSecond, DurationSeconds).
            [Item.Apple.id] = new FoodDefinition(2, 0.5F, 120),
            [Item.MushroomStew.id] = new FoodDefinition(6, 1.5F, 240),
            [Item.Bread.id] = new FoodDefinition(4, 1.0F, 180),
            [Item.RawPorkchop.id] = new FoodDefinition(4, 0.75F, 120),
            [Item.CookedPorkchop.id] = new FoodDefinition(8, 1.5F, 300),
            [Item.GoldenApple.id] = new FoodDefinition(20, 4.0F, 600),
            [Item.RawFish.id] = new FoodDefinition(2, 0.5F, 120),
            [Item.CookedFish.id] = new FoodDefinition(6, 1.25F, 240),
            [Item.Cookie.id] = new FoodDefinition(2, 0.4F, 90)
        };
    }

    private void LoadConfig()
    {
        string configPath = Path.Combine(Mods.ConfigFolder, ConfigFileName);
        HungerModConfig config = CreateDefaultConfig();
        bool shouldWrite = false;

        if (File.Exists(configPath))
        {
            try
            {
                string json = File.ReadAllText(configPath);
                HungerModConfig? loaded = JsonSerializer.Deserialize<HungerModConfig>(json, ConfigJsonOptions);
                if (loaded is null)
                {
                    Console.WriteLine($"HungerMod: config '{configPath}' is empty or invalid. Using defaults.");
                    shouldWrite = true;
                }
                else
                {
                    config = loaded;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"HungerMod: failed to read config '{configPath}': {ex.Message}. Using defaults.");
                shouldWrite = true;
            }
        }
        else
        {
            shouldWrite = true;
        }

        bool normalized = ApplyConfig(config);
        if (shouldWrite || normalized)
        {
            WriteConfig(configPath, config);
        }
    }

    private static HungerModConfig CreateDefaultConfig()
    {
        HungerModConfig config = new()
        {
            ExtraHudBoxCount = DefaultExtraHudBoxCount,
            ExtraHudBoxSize = DefaultExtraHudBoxSize,
            HudItemIconSize = DefaultHudItemIconSize,
            ExtraHudBoxSpacing = DefaultExtraHudBoxSpacing,
            FoodDefinitions = []
        };

        foreach ((int itemId, FoodDefinition definition) in CreateDefaultFoodDefinitions().OrderBy(pair => pair.Key))
        {
            config.FoodDefinitions.Add(new FoodDefinitionConfig
            {
                ItemId = itemId,
                BonusHealth = definition.BonusHealth,
                RegenPerSecond = definition.RegenPerSecond,
                DurationSeconds = definition.DurationSeconds
            });
        }

        return config;
    }

    private static bool ApplyConfig(HungerModConfig config)
    {
        bool normalized = false;

        int hudCount = Math.Max(1, config.ExtraHudBoxCount);
        if (hudCount != config.ExtraHudBoxCount)
        {
            config.ExtraHudBoxCount = hudCount;
            normalized = true;
        }

        int hudBoxSize = Math.Max(8, config.ExtraHudBoxSize);
        if (hudBoxSize != config.ExtraHudBoxSize)
        {
            config.ExtraHudBoxSize = hudBoxSize;
            normalized = true;
        }

        int iconSize = Math.Clamp(config.HudItemIconSize, 1, hudBoxSize);
        if (iconSize != config.HudItemIconSize)
        {
            config.HudItemIconSize = iconSize;
            normalized = true;
        }

        int boxSpacing = Math.Max(0, config.ExtraHudBoxSpacing);
        if (boxSpacing != config.ExtraHudBoxSpacing)
        {
            config.ExtraHudBoxSpacing = boxSpacing;
            normalized = true;
        }

        Dictionary<int, FoodDefinition> foods = [];
        List<FoodDefinitionConfig>? foodConfigs = config.FoodDefinitions;
        if (foodConfigs is not null)
        {
            foreach (FoodDefinitionConfig foodConfig in foodConfigs)
            {
                if (foodConfig.ItemId < 0 || foodConfig.ItemId >= Item.ITEMS.Length || Item.ITEMS[foodConfig.ItemId] is null)
                {
                    normalized = true;
                    continue;
                }

                int bonusHealth = Math.Max(0, foodConfig.BonusHealth);
                float regenPerSecond = Math.Max(0.0F, foodConfig.RegenPerSecond);
                int durationSeconds = Math.Max(1, foodConfig.DurationSeconds);

                if (bonusHealth != foodConfig.BonusHealth ||
                    regenPerSecond != foodConfig.RegenPerSecond ||
                    durationSeconds != foodConfig.DurationSeconds)
                {
                    foodConfig.BonusHealth = bonusHealth;
                    foodConfig.RegenPerSecond = regenPerSecond;
                    foodConfig.DurationSeconds = durationSeconds;
                    normalized = true;
                }

                foods[foodConfig.ItemId] = new FoodDefinition(bonusHealth, regenPerSecond, durationSeconds);
            }
        }

        if (foods.Count == 0)
        {
            foods = CreateDefaultFoodDefinitions();
            config.FoodDefinitions = CreateDefaultConfig().FoodDefinitions;
            normalized = true;
        }

        ExtraHudBoxCount = hudCount;
        ExtraHudBoxSize = hudBoxSize;
        HudItemIconSize = iconSize;
        ExtraHudBoxSpacing = boxSpacing;
        FoodDefinitions = foods;

        return normalized;
    }

    private static void WriteConfig(string configPath, HungerModConfig config)
    {
        try
        {
            string json = JsonSerializer.Serialize(config, ConfigJsonOptions);
            File.WriteAllText(configPath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"HungerMod: failed to write config '{configPath}': {ex.Message}");
        }
    }

    private sealed class PlayerFoodState
    {
        public readonly List<ActiveFoodEffect> ActiveFoods = [];
        public float RegenAccumulator;
    }

    private sealed class ActiveFoodEffect(int itemId, int remainingTicks)
    {
        public int ItemId = itemId;
        public int RemainingTicks = remainingTicks;
    }

    private sealed class HungerModConfig
    {
        public int ExtraHudBoxCount { get; set; } = DefaultExtraHudBoxCount;
        public int ExtraHudBoxSize { get; set; } = DefaultExtraHudBoxSize;
        public int HudItemIconSize { get; set; } = DefaultHudItemIconSize;
        public int ExtraHudBoxSpacing { get; set; } = DefaultExtraHudBoxSpacing;
        public List<FoodDefinitionConfig> FoodDefinitions { get; set; } = [];
    }

    private sealed class FoodDefinitionConfig
    {
        public int ItemId { get; set; }
        public int BonusHealth { get; set; }
        public float RegenPerSecond { get; set; }
        public int DurationSeconds { get; set; }
    }

    private readonly record struct FoodDefinition(int BonusHealth, float RegenPerSecond, int DurationSeconds);
}
