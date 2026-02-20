using System.Reflection;
using BetaSharp.Client.Guis;
using BetaSharp.Modding;
using ModMenu.Guis;
using MonoMod.RuntimeDetour;

namespace ModMenu;

[ModSide(Side.Client)]
public class Mod : IMod
{
    public string Name => "Mod Menu";
    public string Description => "Adds a mod menu to the main menu. Im making a very long description as a test because i dont know what hapens wen the description is very long. Will it overflow? Will it wrap? Will it crash? Who knows! Maybe all of the above! Maybe none of the above! The suspense is killing me!";
    public string Author => "vos6434";
    public Side Side => Side.Client;
    private const int ButtonModMenu = 5;
    private const int ButtonTexturePacksAndMods = 3;
    private const int ButtonOptions = 0;
    private const int ButtonQuit = 4;
    private static readonly FieldInfo? ControlListField = typeof(GuiScreen)
        .GetField("_controlList", BindingFlags.Instance | BindingFlags.NonPublic);

    private Hook? _guiMainMenuInitGuiHook;
    private Hook? _guiMainMenuActionPerformedHook;

    public void Initialize()
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
    }

    public void PostInitialize()
    {
        Console.WriteLine($"PostInitialize called for Mod Menu. " +
                          $"Loaded mods: [{string.Join(", ", Mods.ModRegistry.Select(m => m.Name))}]");
    }

    public void Unload()
    {
        Console.WriteLine("Mod Menu is unloading");
        _guiMainMenuInitGuiHook?.Dispose();
        _guiMainMenuInitGuiHook = null;
        _guiMainMenuActionPerformedHook?.Dispose();
        _guiMainMenuActionPerformedHook = null;
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
            if (existingButton.Id == ButtonModMenu)
            {
                return;
            }
        }

        GuiButton? modsButton = null;
        foreach (GuiButton existingButton in controls)
        {
            if (existingButton.Id == ButtonTexturePacksAndMods)
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

        controls.Add(new GuiButton(ButtonModMenu, buttonX, buttonY, "Mods"));

        int optionsRowY = buttonY + 24;
        foreach (GuiButton existingButton in controls)
        {
            if (existingButton.Id == ButtonOptions || existingButton.Id == ButtonQuit)
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
        if (button.Id == ButtonModMenu)
        {
            instance.mc.displayGuiScreen(new GuiModListScreen(instance));
            return;
        }

        orig(instance, button);
    }
}
