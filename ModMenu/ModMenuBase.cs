using System.Reflection;
using BetaSharp.Client.Guis;
using BetaSharp.Modding;
using ModMenu.Guis;
using MonoMod.RuntimeDetour;

namespace ModMenu;

[ModSide(Side.Client)]
public class ModMenuBase : ModBase
{
    public override string Name => "Mod Menu";
    public override string Description => "Adds a mod list menu to the game.";
    public override string Author => "vos6434";
    private const int ButtonMainMenuModMenuId = 2001;
    private const int ButtonIngameMenuModMenuId = 2002;
    private const int ButtonTexturePacksAndModsId = 3;
    private const int ButtonMainMenuOptionsId = 0;
    private const int ButtonQuitId = 4;
    private const int ButtonIngameMenuOptionsId = 0;
    private static readonly FieldInfo? ControlListField = typeof(GuiScreen)
        .GetField("_controlList", BindingFlags.Instance | BindingFlags.NonPublic);

    private Hook? _guiMainMenuInitGuiHook;
    private Hook? _guiMainMenuActionPerformedHook;
    private Hook? _guiIngameMenuInitGuiHook;
    private Hook? _guiIngameMenuActionPerformedHook;

    public override void Initialize(Side side)
    {
        Console.WriteLine("Initialize called for Mod Menu");

        MethodInfo? initGuiMethod = typeof(GuiMainMenu).GetMethod(
            nameof(GuiMainMenu.InitGui),
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            types: Type.EmptyTypes,
            modifiers: null);
        if (initGuiMethod is null)
        {
            Console.WriteLine("Failed to hook GuiMainMenu.InitGui: method was not found.");
        }
        else
        {
            _guiMainMenuInitGuiHook = new Hook(initGuiMethod, GuiMainMenu_InitGui);
        }

        MethodInfo? actionPerformedMethod = typeof(GuiMainMenu).GetMethod(
            "ActionPerformed",
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            types: [typeof(GuiButton)],
            modifiers: null);
        if (actionPerformedMethod is null)
        {
            Console.WriteLine("Failed to hook GuiMainMenu.ActionPerformed: method was not found.");
        }
        else
        {
            _guiMainMenuActionPerformedHook = new Hook(actionPerformedMethod, GuiMainMenu_ActionPerformed);
        }

        MethodInfo? ingameInitGuiMethod = typeof(GuiIngameMenu).GetMethod(
            nameof(GuiIngameMenu.InitGui),
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            types: Type.EmptyTypes,
            modifiers: null);
        if (ingameInitGuiMethod is null)
        {
            Console.WriteLine("Failed to hook GuiIngameMenu.InitGui: method was not found.");
        }
        else
        {
            _guiIngameMenuInitGuiHook = new Hook(ingameInitGuiMethod, GuiIngameMenu_InitGui);
        }

        MethodInfo? ingameActionPerformedMethod = typeof(GuiIngameMenu).GetMethod(
            "ActionPerformed",
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            types: [typeof(GuiButton)],
            modifiers: null);
        if (ingameActionPerformedMethod is null)
        {
            Console.WriteLine("Failed to hook GuiIngameMenu.ActionPerformed: method was not found.");
        }
        else
        {
            _guiIngameMenuActionPerformedHook = new Hook(ingameActionPerformedMethod, GuiIngameMenu_ActionPerformed);
        }
    }

    public override void PostInitialize(Side side)
    {
        Console.WriteLine($"PostInitialize called for Mod Menu. " +
                          $"Loaded mods: [{string.Join(", ", Mods.ModRegistry.Select(m => m.Name))}]");
    }

    public override void Unload(Side side)
    {
        Console.WriteLine("Mod Menu is unloading");
        _guiMainMenuInitGuiHook?.Dispose();
        _guiMainMenuInitGuiHook = null;
        _guiMainMenuActionPerformedHook?.Dispose();
        _guiMainMenuActionPerformedHook = null;
        _guiIngameMenuInitGuiHook?.Dispose();
        _guiIngameMenuInitGuiHook = null;
        _guiIngameMenuActionPerformedHook?.Dispose();
        _guiIngameMenuActionPerformedHook = null;
    }

    private static void GuiMainMenu_InitGui(Action<GuiMainMenu> orig, GuiMainMenu instance)
    {
        orig(instance);

        if (ControlListField?.GetValue(instance) is not System.Collections.Generic.List<GuiButton> controls)
        {
            Console.WriteLine("Failed to add Mod Menu button: _controlList field was not accessible.");
            return;
        }

        foreach (GuiButton existingButton in controls)
        {
            if (existingButton.Id == ButtonMainMenuModMenuId)
            {
                return;
            }
        }

        GuiButton? modsButton = null;
        foreach (GuiButton existingButton in controls)
        {
            if (existingButton.Id == ButtonTexturePacksAndModsId)
            {
                modsButton = existingButton;
                break;
            }
        }

        int buttonX;
        int buttonY;

        // rename texturepacks button from "Texture Packs and Mods" to just "Texturepacks".
        if (modsButton is not null)
        {
            modsButton.DisplayString = "Texturepacks";
            buttonX = modsButton.XPosition;
            buttonY = modsButton.YPosition + 24;
        }
        else
        {
            buttonX = instance.Width / 2 - 100;
            buttonY = instance.Height / 4 + 48 + 96;
        }

        controls.Add(new GuiButton(ButtonMainMenuModMenuId, buttonX, buttonY, "Mods"));

        int optionsRowY = buttonY + 24;
        foreach (GuiButton existingButton in controls)
        {
            if (existingButton.Id == ButtonMainMenuOptionsId || existingButton.Id == ButtonQuitId)
            {
                existingButton.YPosition = optionsRowY;
            }
        }
    }

    private static void GuiMainMenu_ActionPerformed(
        Action<GuiMainMenu, GuiButton> orig,
        GuiMainMenu instance,
        GuiButton button)
    {
        if (button.Id == ButtonMainMenuModMenuId)
        {
            instance.mc.displayGuiScreen(new GuiModListScreen(instance));
            return;
        }

        orig(instance, button);
    }

    private static void GuiIngameMenu_InitGui(Action<GuiIngameMenu> orig, GuiIngameMenu instance)
    {
        orig(instance);

        if (ControlListField?.GetValue(instance) is not System.Collections.Generic.List<GuiButton> controls)
        {
            Console.WriteLine("Failed to add Mods button to game menu: _controlList field was not accessible.");
            return;
        }

        foreach (GuiButton existingButton in controls)
        {
            if (existingButton.Id == ButtonIngameMenuModMenuId)
            {
                return;
            }
        }

        GuiButton? optionsButton = null;
        foreach (GuiButton existingButton in controls)
        {
            if (existingButton.Id == ButtonIngameMenuOptionsId)
            {
                optionsButton = existingButton;
                break;
            }
        }

        int buttonX;
        int buttonY;
        if (optionsButton is not null)
        {
            buttonX = optionsButton.XPosition;
            buttonY = optionsButton.YPosition - 24;
        }
        else
        {
            buttonX = instance.Width / 2 - 100;
            buttonY = instance.Height / 4 + 56;
        }

        controls.Add(new GuiButton(ButtonIngameMenuModMenuId, buttonX, buttonY, "Mods"));
    }

    private static void GuiIngameMenu_ActionPerformed(
        Action<GuiIngameMenu, GuiButton> orig,
        GuiIngameMenu instance,
        GuiButton button)
    {
        if (button.Id == ButtonIngameMenuModMenuId)
        {
            instance.mc.displayGuiScreen(new GuiModListScreen(instance));
            return;
        }

        orig(instance, button);
    }
}
