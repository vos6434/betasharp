using System.Reflection;
using BetaSharp.Blocks.Materials;
using BetaSharp.Client;
using BetaSharp.Client.Guis;
using BetaSharp.Client.Rendering.Core;
using BetaSharp.Entities;
using BetaSharp.Modding;
using MonoMod.RuntimeDetour;
using Silk.NET.OpenGL.Legacy;

namespace HungerMod;

[ModSide(Side.Client)]
public class Mod : IMod
{
    private const int DoubledMaxHealth = 40;
    private static readonly FieldInfo? GuiIngameMcField = typeof(GuiIngame)
        .GetField("_mc", BindingFlags.Instance | BindingFlags.NonPublic);

    private Hook? _entityPlayerTickMovementHook;
    private Hook? _entityPlayerTeleportToTopHook;
    private Hook? _entityLivingHealHook;
    private Hook? _guiIngameRenderGameOverlayHook;

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

        MethodInfo? guiRenderOverlayMethod = typeof(GuiIngame).GetMethod(
            nameof(GuiIngame.renderGameOverlay),
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            types: [typeof(float), typeof(bool), typeof(int), typeof(int)],
            modifiers: null);
        if (guiRenderOverlayMethod is null)
        {
            Console.WriteLine("HungerMod: failed to hook GuiIngame.renderGameOverlay (method not found).");
        }
        else
        {
            _guiIngameRenderGameOverlayHook = new Hook(guiRenderOverlayMethod, GuiIngame_RenderGameOverlay);
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
        _guiIngameRenderGameOverlayHook?.Dispose();
        _guiIngameRenderGameOverlayHook = null;
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

    private static void GuiIngame_RenderGameOverlay(
        Action<GuiIngame, float, bool, int, int> orig,
        GuiIngame instance,
        float partialTicks,
        bool unusedFlag,
        int unusedA,
        int unusedB)
    {
        orig(instance, partialTicks, unusedFlag, unusedA, unusedB);

        if (GuiIngameMcField?.GetValue(instance) is not Minecraft mc || mc.player is null)
        {
            return;
        }

        if (!mc.playerController.shouldDrawHUD() || mc.player.maxHealth <= 20)
        {
            return;
        }

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
}
