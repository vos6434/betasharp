using System.Reflection;
using BetaSharp.Client.Guis;
using BetaSharp.Modding;
using ModMenu.Guis;
using MonoMod.RuntimeDetour;

namespace ModMenu;

[ModSide(Side.Client)]
public class ModMenuBase : ModBase
{
    private const string LogPrefix = "ModMenu:";
    public override string Name => "Mod Menu";
    public override string Description => "Adds a mod list menu to the game.";
    public override string Author => "vos6434";
    private const int MainMenuModsButtonId = 2001;
    private const int IngameMenuModsButtonId = 2002;
    private const int MainMenuTexturePacksButtonId = 3;
    private const int MainMenuOptionsButtonId = 0;
    private const int MainMenuQuitButtonId = 4;
    private const int IngameMenuOptionsButtonId = 0;
    private const string ModsButtonLabel = "Mods";
    private const string TexturePacksLabel = "Texturepacks";
    private static readonly FieldInfo? ControlListField = typeof(GuiScreen)
        .GetField("_controlList", BindingFlags.Instance | BindingFlags.NonPublic);

    private Hook? _guiMainMenuInitGuiHook;
    private Hook? _guiMainMenuActionPerformedHook;
    private Hook? _guiIngameMenuInitGuiHook;
    private Hook? _guiIngameMenuActionPerformedHook;

    public override void Initialize(Side side)
    {
        Console.WriteLine("Initialize called for Mod Menu");

        MethodInfo? mainMenuInitGuiMethod = GetInstanceMethod(
            typeof(GuiMainMenu),
            nameof(GuiMainMenu.InitGui),
            BindingFlags.Instance | BindingFlags.Public);
        if (mainMenuInitGuiMethod is null)
        {
            Console.WriteLine($"{LogPrefix} failed to hook GuiMainMenu.InitGui (method not found).");
        }
        else
        {
            _guiMainMenuInitGuiHook = new Hook(mainMenuInitGuiMethod, GuiMainMenu_InitGui);
        }

        MethodInfo? mainMenuActionPerformedMethod = GetInstanceMethod(
            typeof(GuiMainMenu),
            "ActionPerformed",
            BindingFlags.Instance | BindingFlags.NonPublic,
            typeof(GuiButton));
        if (mainMenuActionPerformedMethod is null)
        {
            Console.WriteLine($"{LogPrefix} failed to hook GuiMainMenu.ActionPerformed (method not found).");
        }
        else
        {
            _guiMainMenuActionPerformedHook = new Hook(mainMenuActionPerformedMethod, GuiMainMenu_ActionPerformed);
        }

        MethodInfo? ingameMenuInitGuiMethod = GetInstanceMethod(
            typeof(GuiIngameMenu),
            nameof(GuiIngameMenu.InitGui),
            BindingFlags.Instance | BindingFlags.Public);
        if (ingameMenuInitGuiMethod is null)
        {
            Console.WriteLine($"{LogPrefix} failed to hook GuiIngameMenu.InitGui (method not found).");
        }
        else
        {
            _guiIngameMenuInitGuiHook = new Hook(ingameMenuInitGuiMethod, GuiIngameMenu_InitGui);
        }

        MethodInfo? ingameMenuActionPerformedMethod = GetInstanceMethod(
            typeof(GuiIngameMenu),
            "ActionPerformed",
            BindingFlags.Instance | BindingFlags.NonPublic,
            typeof(GuiButton));
        if (ingameMenuActionPerformedMethod is null)
        {
            Console.WriteLine($"{LogPrefix} failed to hook GuiIngameMenu.ActionPerformed (method not found).");
        }
        else
        {
            _guiIngameMenuActionPerformedHook = new Hook(ingameMenuActionPerformedMethod, GuiIngameMenu_ActionPerformed);
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

        if (!TryGetControlList(instance, out List<GuiButton> controls))
        {
            return;
        }

        foreach (GuiButton existingButton in controls)
        {
            if (existingButton.Id == MainMenuModsButtonId)
            {
                return;
            }
        }

        GuiButton? modsButton = null;
        foreach (GuiButton existingButton in controls)
        {
            if (existingButton.Id == MainMenuTexturePacksButtonId)
            {
                modsButton = existingButton;
                break;
            }
        }

        int buttonX;
        int buttonY;

        // The existing menu entry currently points to texture packs.
        if (modsButton is not null)
        {
            modsButton.DisplayString = TexturePacksLabel;
            buttonX = modsButton.XPosition;
            buttonY = modsButton.YPosition + 24;
        }
        else
        {
            buttonX = instance.Width / 2 - 100;
            buttonY = instance.Height / 4 + 48 + 96;
        }

        controls.Add(new GuiButton(MainMenuModsButtonId, buttonX, buttonY, ModsButtonLabel));

        int optionsRowY = buttonY + 24;
        foreach (GuiButton existingButton in controls)
        {
            if (existingButton.Id == MainMenuOptionsButtonId || existingButton.Id == MainMenuQuitButtonId)
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
        if (button.Id == MainMenuModsButtonId)
        {
            instance.mc.displayGuiScreen(new GuiModListScreen(instance));
            return;
        }

        orig(instance, button);
    }

    private static void GuiIngameMenu_InitGui(Action<GuiIngameMenu> orig, GuiIngameMenu instance)
    {
        orig(instance);

        if (!TryGetControlList(instance, out List<GuiButton> controls))
        {
            return;
        }

        foreach (GuiButton existingButton in controls)
        {
            if (existingButton.Id == IngameMenuModsButtonId)
            {
                return;
            }
        }

        GuiButton? optionsButton = null;
        foreach (GuiButton existingButton in controls)
        {
            if (existingButton.Id == IngameMenuOptionsButtonId)
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

        controls.Add(new GuiButton(IngameMenuModsButtonId, buttonX, buttonY, ModsButtonLabel));
    }

    private static void GuiIngameMenu_ActionPerformed(
        Action<GuiIngameMenu, GuiButton> orig,
        GuiIngameMenu instance,
        GuiButton button)
    {
        if (button.Id == IngameMenuModsButtonId)
        {
            instance.mc.displayGuiScreen(new GuiModListScreen(instance));
            return;
        }

        orig(instance, button);
    }

    private static bool TryGetControlList(GuiScreen screen, out List<GuiButton> controls)
    {
        if (ControlListField?.GetValue(screen) is List<GuiButton> controlList)
        {
            controls = controlList;
            return true;
        }

        controls = [];
        Console.WriteLine($"{LogPrefix} failed to access {screen.GetType().Name}._controlList.");
        return false;
    }

    private static MethodInfo? GetInstanceMethod(
        Type targetType,
        string methodName,
        BindingFlags bindingFlags,
        params Type[] parameterTypes)
    {
        return targetType.GetMethod(
            methodName,
            bindingFlags,
            binder: null,
            types: parameterTypes,
            modifiers: null);
    }
}
