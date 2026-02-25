using System.Reflection;
using System.Text.Json;
using BetaSharp.Blocks.Materials;
using BetaSharp.Client;
using BetaSharp.Client.Entities;
using BetaSharp.Client.Guis;
using BetaSharp.Client.Input;
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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

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

    // Removed unused method PlayerHasActiveFood
    private static int HudItemIconSize = DefaultHudItemIconSize;
    private static int ExtraHudBoxSpacing = DefaultExtraHudBoxSpacing;
    private static Dictionary<int, FoodDefinition> FoodDefinitions = CreateDefaultFoodDefinitions();
    private static readonly Dictionary<EntityPlayer, PlayerFoodState> PlayerFoodStates = new Dictionary<EntityPlayer, PlayerFoodState>();

    private static ItemRenderer? HudItemRenderer;
    private static FieldInfo? GuiIngameMcField;
    private static FieldInfo? ClientPlayerMcField;
    // Cached reflection info for runtime GuiMods hook (performance)
    private static FieldInfo? GuiModsOptionsField;
    private static FieldInfo? GuiModsModListField;
    private static FieldInfo? ModListSelectedIndexField;
    private static int ExtraHudBoxCount = DefaultExtraHudBoxCount;
    private static int ExtraHudBoxSize = DefaultExtraHudBoxSize;

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
    private Hook? _guiModsRenderHook;

    public HungerModBase()
    {
        Name = "Hunger Mod";
        Description = "Valheim-style timed food buffs for health and regeneration.";
        Author = "vos6434";
        Version = "1.0.0";
        Logger = NullLogger.Instance;
    }

    // Backwards-compatible constructor used by the mod loader when it expects a parameterized ctor.
    public HungerModBase(string name, string author, string description, string version, ILogger logger)
        : base(name, author, description, version, logger)
    {
    }

    public override bool HasOptionsMenu => true;

    public override void OpenOptionsMenu()
    {
        var parent = Minecraft.INSTANCE?.currentScreen;
        Minecraft.INSTANCE?.displayGuiScreen(new HungerModOptionsGui(parent));
    }

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
            types: new Type[] { typeof(int) },
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
            types: new Type[] { typeof(NBTTagCompound) },
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
            types: new Type[] { typeof(NBTTagCompound) },
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
                types: new Type[] { typeof(int), typeof(int), typeof(int), typeof(float) },
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
                types: new Type[] { typeof(float), typeof(bool), typeof(int), typeof(int) },
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
                types: new Type[] { typeof(string) },
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

            // Hook GuiMods.Render to enable/disable the Mod Options button for mods that expose options.
            try
            {
                MethodInfo? guiModsRenderMethod = typeof(GuiMods).GetMethod(
                    nameof(GuiMods.Render),
                    BindingFlags.Instance | BindingFlags.Public,
                    binder: null,
                    types: new Type[] { typeof(int), typeof(int), typeof(float) },
                    modifiers: null);

                if (guiModsRenderMethod != null)
                {
                    _guiModsRenderHook = new Hook(guiModsRenderMethod, (Action<Action<GuiMods, int, int, float>, GuiMods, int, int, float>)GuiMods_Render);
                    // Cache FieldInfo for GuiMods/_optionsButton and GuiMods/_modList (and ModList.SelectedIndex) once.
                    try
                    {
                        GuiModsOptionsField = typeof(GuiMods).GetField("_optionsButton", BindingFlags.Instance | BindingFlags.NonPublic);
                        GuiModsModListField = typeof(GuiMods).GetField("_modList", BindingFlags.Instance | BindingFlags.NonPublic);
                        if (GuiModsModListField != null)
                        {
                            Type modListType = GuiModsModListField.FieldType;
                            ModListSelectedIndexField = modListType.GetField("SelectedIndex", BindingFlags.Instance | BindingFlags.Public);
                        }
                    }
                    catch { }
                }
            }
            catch { }

            MethodInfo? entityClientPlayerMPSendChatMessageMethod = typeof(EntityClientPlayerMP).GetMethod(
                nameof(EntityClientPlayerMP.sendChatMessage),
                BindingFlags.Instance | BindingFlags.Public,
                binder: null,
                types: new Type[] { typeof(string) },
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
        _guiModsRenderHook?.Dispose();
        _guiModsRenderHook = null;
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
        mc.textureManager.BindTexture(mc.textureManager.GetTextureId("/gui/icons.png"));

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
        if (parts.Length == 0) return false;

        // /clearhunger - clears active food effects
        if (parts[0].Equals("/clearhunger", StringComparison.OrdinalIgnoreCase))
        {
            ClearFoodEffects(player);
            AddPlayerChatMessage(player, "HungerMod: cleared active food effects.");
            return true;
        }

        // /hungeroptions - open the Hunger Mod options screen (client-side)
        if (parts[0].Equals("/hungeroptions", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                Minecraft? mc = Minecraft.INSTANCE;
                mc?.displayGuiScreen(new HungerModOptionsGui(mc?.currentScreen));
            }
            catch { }

            return true;
        }

        return false;
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

    private static void GuiMods_Render(Action<GuiMods, int, int, float> orig, GuiMods instance, int mouseX, int mouseY, float partialTicks)
    {
        try
        {
            // Lazy-initialize cached FieldInfo if not already cached
            if (GuiModsOptionsField == null || GuiModsModListField == null || ModListSelectedIndexField == null)
            {
                try
                {
                    GuiModsOptionsField ??= typeof(GuiMods).GetField("_optionsButton", BindingFlags.Instance | BindingFlags.NonPublic);
                    GuiModsModListField ??= typeof(GuiMods).GetField("_modList", BindingFlags.Instance | BindingFlags.NonPublic);
                    if (GuiModsModListField != null && ModListSelectedIndexField == null)
                    {
                        Type modListType = GuiModsModListField.FieldType;
                        ModListSelectedIndexField = modListType.GetField("SelectedIndex", BindingFlags.Instance | BindingFlags.Public);
                    }
                }
                catch { }
            }

            if (GuiModsOptionsField != null && GuiModsModListField != null)
            {
                GuiButton? optionsBtn = GuiModsOptionsField.GetValue(instance) as GuiButton;
                object? modListObj = GuiModsModListField.GetValue(instance);
                int sel = -1;
                if (modListObj != null && ModListSelectedIndexField != null)
                {
                    sel = (int)(ModListSelectedIndexField.GetValue(modListObj) ?? -1);
                }

                if (optionsBtn != null)
                {
                    bool enabled = sel != -1 && sel >= 0 && sel < Mods.ModRegistry.Count && Mods.ModRegistry[sel].HasOptionsMenu;
                    optionsBtn.Enabled = enabled;
                }
            }
        }
        catch { }

        orig(instance, mouseX, mouseY, partialTicks);
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
    private class HungerModOptionsGui : GuiScreen
    {
        private const int BtnResetAll = 0;
        private const int BtnDone = 1;
        private const int BtnResetExtraCount = 10;
        private const int BtnResetBoxSize = 11;
        private const int BtnResetIconSize = 12;
        private const int BtnResetBoxSpacing = 13;

        private readonly GuiScreen? _parentScreen;
        private GuiTextField? _txtExtraCount;
        private GuiTextField? _txtBoxSize;
        private GuiTextField? _txtIconSize;
        private GuiTextField? _txtBoxSpacing;
            private GuiSlot? _generalSlot;
            private static FieldInfo? GuiTextFieldYField;
        private string _screenTitle = "Hunger Mod Options";
        private int _optionsStartY = 0;
        private int _optionsSpacing = 30;

        public HungerModOptionsGui(GuiScreen? parent)
        {
            _parentScreen = parent;
        }

        public override void UpdateScreen()
        {
            _txtExtraCount?.updateCursorCounter();
            _txtBoxSize?.updateCursorCounter();
            _txtIconSize?.updateCursorCounter();
            _txtBoxSpacing?.updateCursorCounter();
        }

        public override void InitGui()
        {
            Keyboard.enableRepeatEvents(true);
            int centerX = Width / 2;
            int centerY = Height / 4;

            _screenTitle = "Hunger Mod Options";

            // Layout parameters for two-column layout
            int fieldHeight = 20;
            int numFields = 4;
            int desiredSpacing = _optionsSpacing;
            int minTop = 40;
            int bottomMargin = 80; // leave room for bottom buttons

            // available space between margins
            int availableBetween = Height - minTop - bottomMargin;
            const int buttonsReserve = 64;
            int maxSpacing = (availableBetween - buttonsReserve - numFields * fieldHeight) / Math.Max(1, numFields - 1);
            if (maxSpacing < 0) maxSpacing = 2;
            int spacing = Math.Min(desiredSpacing, maxSpacing);

            int totalFieldsHeight = numFields * fieldHeight + (numFields - 1) * spacing;

            int startY = centerY - totalFieldsHeight / 2;
            int maxStartY = Height - bottomMargin - totalFieldsHeight;
            if (maxStartY < minTop) startY = minTop; else startY = Math.Clamp(startY, minTop, maxStartY);

            _optionsStartY = startY;
            _optionsSpacing = spacing;

            // Two-column positions
            int labelX = 20;
            int controlX = Width - 220; // controls column left
            int fieldWidth = 120;
            int resetWidth = 48;
            int resetX = controlX + fieldWidth + 8;

            // Create text fields (Y will be updated by the GuiSlot during render)
            _txtExtraCount = new GuiTextField(this, FontRenderer, controlX, 0, fieldWidth, fieldHeight, ExtraHudBoxCount.ToString()) { IsFocused = false };
            _txtBoxSize = new GuiTextField(this, FontRenderer, controlX, 0, fieldWidth, fieldHeight, ExtraHudBoxSize.ToString());
            _txtIconSize = new GuiTextField(this, FontRenderer, controlX, 0, fieldWidth, fieldHeight, HudItemIconSize.ToString());
            _txtBoxSpacing = new GuiTextField(this, FontRenderer, controlX, 0, fieldWidth, fieldHeight, ExtraHudBoxSpacing.ToString());

            // Cache private _yPos field on GuiTextField to move fields when the slot scrolls
            GuiTextFieldYField ??= typeof(GuiTextField).GetField("_yPos", BindingFlags.Instance | BindingFlags.NonPublic);

            // Create a GuiSlot to host the general option rows so we get native scrolling
            // Add a small top padding so the first input has the same gap as the bottom
            int slotTop = _optionsStartY + 4;
            // Add bottom padding so the last input doesn't touch the slot edge.
            // Make the scroll window taller by allowing the slot to extend closer to the bottom
            // while keeping a safe margin for the action buttons.
            int computedSlotBottom = _optionsStartY + totalFieldsHeight + 20;
            // Add an extra per-row gap so input boxes have visible separation
            int extraRowGap = 6;
            // Keep row height consistent with layout but include the extra per-row gap
            int slotRowHeight = fieldHeight + spacing + extraRowGap;
            // Default expanded bottom including gaps
            int expandedBottom = computedSlotBottom + extraRowGap * Math.Max(0, numFields - 1);
            // Reserve space for bottom buttons and a small margin (64px)
            int maxAllowedBottom = Height - 64;
            // Force the slot to extend closer to the bottom of the screen so the visible
            // scroll area is taller even when content is short. Keep a safe margin
            // to avoid overlapping the bottom action buttons.
            int slotBottom = maxAllowedBottom;
            // Ensure slotBottom is always below slotTop by at least one row
            if (slotBottom <= slotTop) slotBottom = slotTop + slotRowHeight;
            _generalSlot = new GeneralOptionsSlot(Minecraft.INSTANCE, Width, Height, slotTop, slotBottom, slotRowHeight, this);

            // Move the slot's scrollbar to the right of the reset buttons by tweaking the slot's internal width
            try
            {
                FieldInfo? slotWidthField = typeof(GuiSlot).GetField("_width", BindingFlags.Instance | BindingFlags.NonPublic);
                FieldInfo? slotRightField = typeof(GuiSlot).GetField("_right", BindingFlags.Instance | BindingFlags.NonPublic);
                if (slotWidthField != null && slotRightField != null)
                {
                    int desiredScrollX = resetX + resetWidth + 8; // a few pixels right of reset buttons
                    int requiredWidth = 2 * (desiredScrollX - 124);
                    // Clamp the computed width so we don't expand the slot outside the screen bounds
                    int newWidth = Math.Clamp(requiredWidth, 200, Width);
                    slotWidthField.SetValue(_generalSlot, newWidth);
                    slotRightField.SetValue(_generalSlot, newWidth);
                }
            }
            catch { }

            _controlList.Clear();
            // Per-field reset buttons (align with slot rows)
            // Slot draws rows at _top + 4; use the same offset so buttons align with rows
            // Use the slot row height (posZ) as the vertical increment so gaps match exactly
            int resetBaseY = slotTop + 4;
            // Compute exact slot row Y positions and align the reset buttons to the text box borders
            int resetY0 = resetBaseY - 1; // textbox outer border is at y - 1
            int resetY1 = resetBaseY + slotRowHeight * 1 - 1;
            int resetY2 = resetBaseY + slotRowHeight * 2 - 1;
            int resetY3 = resetBaseY + slotRowHeight * 3 - 1;
            _controlList.Add(new GuiButton(BtnResetExtraCount, resetX, resetY0, resetWidth, fieldHeight, "Reset"));
            _controlList.Add(new GuiButton(BtnResetBoxSize, resetX, resetY1, resetWidth, fieldHeight, "Reset"));
            _controlList.Add(new GuiButton(BtnResetIconSize, resetX, resetY2, resetWidth, fieldHeight, "Reset"));
            _controlList.Add(new GuiButton(BtnResetBoxSpacing, resetX, resetY3, resetWidth, fieldHeight, "Reset"));

            // Bottom actions: Reset All and Done
            int btnW = 150;
            // Place action buttons at the bottom of the screen for consistent UI
            int bottomY = Height - 40;
            _controlList.Add(new GuiButton(BtnResetAll, centerX - btnW - 6, bottomY, btnW, 20, "Reset All"));
            _controlList.Add(new GuiButton(BtnDone, centerX + 6, bottomY, btnW, 20, "Done"));
        }

        protected override void ActionPerformed(GuiButton button)
        {
            if (!button.Enabled) return;
            switch (button.Id)
            {
                case BtnResetAll:
                    // Reset all fields to defaults
                    _txtExtraCount?.SetText(DefaultExtraHudBoxCount.ToString());
                    _txtBoxSize?.SetText(DefaultExtraHudBoxSize.ToString());
                    _txtIconSize?.SetText(DefaultHudItemIconSize.ToString());
                    _txtBoxSpacing?.SetText(DefaultExtraHudBoxSpacing.ToString());
                    break;
                case BtnDone:
                    {
                        int extraCount = ExtraHudBoxCount;
                        int boxSize = ExtraHudBoxSize;
                        int iconSize = HudItemIconSize;
                        int boxSpacing = ExtraHudBoxSpacing;

                        if (_txtExtraCount != null && int.TryParse(_txtExtraCount.GetText(), out int v1)) extraCount = Math.Max(1, v1);
                        if (_txtBoxSize != null && int.TryParse(_txtBoxSize.GetText(), out int v2)) boxSize = Math.Max(8, v2);
                        if (_txtIconSize != null && int.TryParse(_txtIconSize.GetText(), out int v3)) iconSize = Math.Clamp(v3, 1, boxSize);
                        if (_txtBoxSpacing != null && int.TryParse(_txtBoxSpacing.GetText(), out int v4)) boxSpacing = Math.Max(0, v4);

                        HungerModConfig config = new HungerModConfig
                        {
                            ExtraHudBoxCount = extraCount,
                            ExtraHudBoxSize = boxSize,
                            HudItemIconSize = iconSize,
                            ExtraHudBoxSpacing = boxSpacing,
                            FoodDefinitions = new List<FoodDefinitionConfig>()
                        };

                        foreach (var kv in FoodDefinitions.OrderBy(k => k.Key))
                        {
                            var def = kv.Value;
                            config.FoodDefinitions.Add(new FoodDefinitionConfig
                            {
                                ItemId = kv.Key,
                                BonusHealth = def.BonusHealth,
                                RegenPerSecond = def.RegenPerSecond,
                                DurationSeconds = def.DurationSeconds
                            });
                        }

                        bool normalized = ApplyConfig(config);
                        string configPath = Path.Combine(Mods.ConfigFolder, ConfigFileName);
                        WriteConfig(configPath, config);
                        mc.displayGuiScreen(_parentScreen);
                        break;
                    }
                case BtnResetExtraCount:
                    _txtExtraCount?.SetText(DefaultExtraHudBoxCount.ToString());
                    break;
                case BtnResetBoxSize:
                    _txtBoxSize?.SetText(DefaultExtraHudBoxSize.ToString());
                    break;
                case BtnResetIconSize:
                    _txtIconSize?.SetText(DefaultHudItemIconSize.ToString());
                    break;
                case BtnResetBoxSpacing:
                    _txtBoxSpacing?.SetText(DefaultExtraHudBoxSpacing.ToString());
                    break;
            }
        }

        protected override void KeyTyped(char eventChar, int eventKey)
        {
            if (_txtExtraCount != null && _txtExtraCount.IsFocused) _txtExtraCount.textboxKeyTyped(eventChar, eventKey);
            else if (_txtBoxSize != null && _txtBoxSize.IsFocused) _txtBoxSize.textboxKeyTyped(eventChar, eventKey);
            else if (_txtIconSize != null && _txtIconSize.IsFocused) _txtIconSize.textboxKeyTyped(eventChar, eventKey);
            else if (_txtBoxSpacing != null && _txtBoxSpacing.IsFocused) _txtBoxSpacing.textboxKeyTyped(eventChar, eventKey);

            if (eventChar == 13)
            {
                // Trigger Done
                GuiButton? done = _controlList.FirstOrDefault(b => b.Id == BtnDone);
                if (done != null) ActionPerformed(done);
            }
        }

        protected override void MouseClicked(int x, int y, int button)
        {
            base.MouseClicked(x, y, button);
            _txtExtraCount?.MouseClicked(x, y, button);
            _txtBoxSize?.MouseClicked(x, y, button);
            _txtIconSize?.MouseClicked(x, y, button);
            _txtBoxSpacing?.MouseClicked(x, y, button);
        }

        public override void Render(int mouseX, int mouseY, float partialTicks)
        {
            DrawDefaultBackground();
            DrawCenteredString(FontRenderer, _screenTitle, Width / 2, 20, 0xFFFFFF);

            // Render the general options inside the slot so native scrolling is used.
            _generalSlot?.DrawScreen(mouseX, mouseY, partialTicks);

            // Draw text boxes after the slot has positioned them
            _txtExtraCount?.DrawTextBox();
            _txtBoxSize?.DrawTextBox();
            _txtIconSize?.DrawTextBox();
            _txtBoxSpacing?.DrawTextBox();

            // Snap per-field Reset buttons to the exact textbox positions (read via reflection)
            try
            {
                FieldInfo? xField = typeof(GuiTextField).GetField("_xPos", BindingFlags.Instance | BindingFlags.NonPublic);
                if (GuiTextFieldYField != null && xField != null)
                {
                    int controlX = Width - 220;
                    int fieldWidth = 120;
                    int btnOffset = 8; // pixels to the right of the textbox

                    void SnapButton(int btnId, GuiTextField? txt)
                    {
                        if (txt == null) return;
                        object? xv = xField.GetValue(txt);
                        object? yv = GuiTextFieldYField.GetValue(txt);
                        if (xv is int txtX && yv is int txtY)
                        {
                            GuiButton? btn = _controlList.FirstOrDefault(b => b.Id == btnId);
                            if (btn != null)
                            {
                                btn.XPosition = controlX + fieldWidth + btnOffset; // align to right of textbox
                                btn.YPosition = txtY - 1; // align top with textbox outer border
                            }
                        }
                    }

                    SnapButton(BtnResetExtraCount, _txtExtraCount);
                    SnapButton(BtnResetBoxSize, _txtBoxSize);
                    SnapButton(BtnResetIconSize, _txtIconSize);
                    SnapButton(BtnResetBoxSpacing, _txtBoxSpacing);
                }
            }
            catch { }

            base.Render(mouseX, mouseY, partialTicks);
        }

        private void RenderGeneralRow(int rowIndex, int x, int y)
        {
            int centerX = Width / 2;
            int fieldHeight = 20;
            int spacing = _optionsSpacing;

            int labelX = centerX - 220;
            int controlX = Width - 220;
            int labelVOffset = (fieldHeight - 8) / 2;

            string label = rowIndex switch
            {
                0 => "Extra HUD Box Count:",
                1 => "Extra HUD Box Size:",
                2 => "HUD Item Icon Size:",
                3 => "Extra HUD Box Spacing:",
                _ => ""
            };

            DrawString(FontRenderer, label, labelX, y + labelVOffset, 0xA0A0A0);

            // Move the corresponding text field into place by updating its private _yPos
            try
            {
                switch (rowIndex)
                {
                    case 0:
                        GuiTextFieldYField?.SetValue(_txtExtraCount, y);
                        break;
                    case 1:
                        GuiTextFieldYField?.SetValue(_txtBoxSize, y);
                        break;
                    case 2:
                        GuiTextFieldYField?.SetValue(_txtIconSize, y);
                        break;
                    case 3:
                        GuiTextFieldYField?.SetValue(_txtBoxSpacing, y);
                        break;
                }
            }
            catch { }
        }

        private void SetFieldFocused(int index)
        {
            _txtExtraCount?.SetFocused(false);
            _txtBoxSize?.SetFocused(false);
            _txtIconSize?.SetFocused(false);
            _txtBoxSpacing?.SetFocused(false);

            switch (index)
            {
                case 0: _txtExtraCount?.SetFocused(true); break;
                case 1: _txtBoxSize?.SetFocused(true); break;
                case 2: _txtIconSize?.SetFocused(true); break;
                case 3: _txtBoxSpacing?.SetFocused(true); break;
            }
        }

        private class GeneralOptionsSlot : GuiSlot
        {
            private readonly HungerModOptionsGui _parent;

            public GeneralOptionsSlot(Minecraft mc, int width, int height, int top, int bottom, int posZ, HungerModOptionsGui parent)
                : base(mc, width, height, top, bottom, posZ)
            {
                _parent = parent;
                SetShowSelectionHighlight(false);
            }

            public override int GetSize() => 4;

            protected override void ElementClicked(int index, bool doubleClick)
            {
                _parent.SetFieldFocused(index);
            }

            protected override bool IsSelected(int slotIndex) => false;

            protected override void DrawBackground() { }

            protected override void DrawSlot(int index, int x, int y, int height, Tessellator tess)
            {
                _parent.RenderGeneralRow(index, x, y);
            }
        }

        public override void SelectNextField()
        {
            if (_txtExtraCount != null && _txtExtraCount.IsFocused)
            {
                _txtExtraCount.SetFocused(false);
                _txtBoxSize?.SetFocused(true);
            }
            else if (_txtBoxSize != null && _txtBoxSize.IsFocused)
            {
                _txtBoxSize.SetFocused(false);
                _txtIconSize?.SetFocused(true);
            }
            else if (_txtIconSize != null && _txtIconSize.IsFocused)
            {
                _txtIconSize.SetFocused(false);
                _txtBoxSpacing?.SetFocused(true);
            }
            else
            {
                _txtExtraCount?.SetFocused(true);
                _txtBoxSize?.SetFocused(false);
                _txtIconSize?.SetFocused(false);
                _txtBoxSpacing?.SetFocused(false);
            }
        }
    }
}
