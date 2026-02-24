using System.Reflection;
using BetaSharp.Client.Input;
using BetaSharp.Client.Guis;
using BetaSharp.Modding;
using MonoMod.RuntimeDetour;

namespace SeeAllItems;

[ModSide(Side.Client)]
public class SeeAllItemsBase : ModBase
{
    private Hook? _guiScreenRenderHook;
    private Hook? _guiScreenMouseHook;
    private Hook? _guiScreenKeyHook;
        private Hook? _guiScreenHandleKeyboardHook;
        private Hook? _guiScreenHandleMouseHook;

    private static SeeAllItemsOverlay? OverlayInstance;
    private static bool OverlayVisible = true; // enabled by default
    private static bool _lastRDown = false;

    public override string Name => "See All Items";
    public override string Description => "A small mod that shows an item browser overlay.";
    public override string Author => "autogen";
    public override bool HasOptionsMenu => false;

    public override void Initialize(Side side)
    {
        Console.WriteLine("SeeAllItems: initialized");

        if (side == Side.Client || side == Side.Both)
        {
            var renderMethod = typeof(GuiScreen).GetMethod("Render", BindingFlags.Instance | BindingFlags.Public);
            if (renderMethod != null)
            {
                _guiScreenRenderHook = new Hook(renderMethod, (Action<Action<GuiScreen, int, int, float>, GuiScreen, int, int, float>)GuiScreen_Render);
            }

            var mouseMethod = typeof(GuiScreen).GetMethod("MouseClicked", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (mouseMethod != null)
            {
                _guiScreenMouseHook = new Hook(mouseMethod, (Action<Action<GuiScreen, int, int, int>, GuiScreen, int, int, int>)GuiScreen_MouseClicked);
            }

            var keyMethod = typeof(GuiScreen).GetMethod("KeyTyped", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (keyMethod != null)
            {
                _guiScreenKeyHook = new Hook(keyMethod, (Action<Action<GuiScreen, char, int>, GuiScreen, char, int>)GuiScreen_KeyTyped);
            }

            var handleKb = typeof(GuiScreen).GetMethod("HandleKeyboardInput", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (handleKb != null)
            {
                _guiScreenHandleKeyboardHook = new Hook(handleKb, (Action<Action<GuiScreen>, GuiScreen>)GuiScreen_HandleKeyboardInput);
            }

            var handleMouse = typeof(GuiScreen).GetMethod("HandleMouseInput", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (handleMouse != null)
            {
                _guiScreenHandleMouseHook = new Hook(handleMouse, (Action<Action<GuiScreen>, GuiScreen>)GuiScreen_HandleMouseInput);
            }
        }
    }

    public override void PostInitialize(Side side)
    {
        Console.WriteLine("SeeAllItems: post-initialize");
    }

    public override void Unload(Side side)
    {
        _guiScreenRenderHook?.Dispose(); _guiScreenRenderHook = null;
        _guiScreenMouseHook?.Dispose(); _guiScreenMouseHook = null;
        _guiScreenKeyHook?.Dispose(); _guiScreenKeyHook = null;
        _guiScreenHandleKeyboardHook?.Dispose(); _guiScreenHandleKeyboardHook = null;
        _guiScreenHandleMouseHook?.Dispose(); _guiScreenHandleMouseHook = null;
        OverlayInstance = null;
        Console.WriteLine("SeeAllItems: unloading");
    }

    private static void GuiScreen_Render(Action<GuiScreen, int, int, float> orig, GuiScreen screen, int mouseX, int mouseY, float pt)
    {
        // Call original first so overlay draws on top
        orig(screen, mouseX, mouseY, pt);

        Console.WriteLine($"SeeAllItemsBase.GuiScreen_Render: screen={screen?.GetType().Name}, OverlayVisible={OverlayVisible}");

        // Edge-detect R key so a single press toggles the overlay even when
        // individual screens override KeyTyped.
            try
            {
                bool rDown = Keyboard.isKeyDown(Keyboard.KEY_R);
                if (rDown && !_lastRDown)
                {
                    // avoid toggling while the overlay's search field is focused (typing)
                    if (OverlayInstance != null && OverlayInstance.IsTyping())
                    {
                        // ignore toggle while typing
                    }
                    else
                    {
                        OverlayVisible = !OverlayVisible;
                        Console.WriteLine($"SeeAllItemsBase: toggle -> OverlayVisible={OverlayVisible} (via render-key)");
                        if (OverlayVisible)
                        {
                            OverlayInstance ??= new SeeAllItemsOverlay();
                        }
                        else
                        {
                            // overlay hidden -> ensure repeat events are off
                            try { Keyboard.enableRepeatEvents(false); } catch { }
                        }
                    }
                }
                _lastRDown = rDown;
            }
        catch (Exception ex)
        {
            Console.WriteLine("SeeAllItems: render-key check threw: " + ex);
        }

        if (!OverlayVisible) return;

        // Only render the overlay when the active screen is a container (inventory)
        // This prevents the overlay from appearing in chat, main menu, pause menu, etc.
        if (!(screen is BetaSharp.Client.Guis.GuiContainer)) return;

        OverlayInstance ??= new SeeAllItemsOverlay();
        try
        {
            OverlayInstance.RenderOverlay(screen, mouseX, mouseY, pt);
        }
        catch (Exception ex)
        {
            Console.WriteLine("SeeAllItemsOverlay.RenderOverlay threw: " + ex);
        }
    }

    private static void GuiScreen_MouseClicked(Action<GuiScreen, int, int, int> orig, GuiScreen screen, int x, int y, int button)
    {
        if (OverlayVisible && OverlayInstance != null)
        {
            try
            {
                if (OverlayInstance.IsMouseOver(screen, x, y))
                {
                    if (OverlayInstance.HandleMouseClicked(screen, x, y, button))
                    {
                        return; // handled by overlay
                    }
                    // if not handled, fall through to original so the underlying GUI can process
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("SeeAllItems: MouseClicked hook error: " + ex);
            }
        }

        orig(screen, x, y, button);
    }

    private static void GuiScreen_HandleKeyboardInput(Action<GuiScreen> orig, GuiScreen screen)
    {
        // Each call corresponds to a keyboard event (caller loops Keyboard.next()). Read the event and toggle if 'R' pressed.
        try
        {
                if (Keyboard.getEventKeyState())
            {
                char c = Keyboard.getEventCharacter();
                int key = Keyboard.getEventKey();
                Console.WriteLine($"SeeAllItems.HandleKeyboardInput: eventChar={c} eventKey={key}");
                // Forward raw keyboard events to the overlay so it can receive focus
                // and text input even if the parent screen consumes KeyTyped.
                    if (OverlayVisible && OverlayInstance != null && screen is BetaSharp.Client.Guis.GuiContainer)
                {
                    try
                    {
                        if (OverlayInstance.HandleKeyTyped(screen, c, key))
                        {
                            return; // handled by overlay
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("SeeAllItems: HandleKeyboardInput -> HandleKeyTyped threw: " + ex);
                    }
                }
                // NOTE: toggling moved to render-edge detector to ensure single, reliable toggle across screens.
                // We intentionally do not toggle here to avoid duplicate toggles.
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("SeeAllItems: HandleKeyboardInput hook error: " + ex);
        }

        orig(screen);
    }

    private static void GuiScreen_HandleMouseInput(Action<GuiScreen> orig, GuiScreen screen)
    {
        // call original first (so clicks and other mouse events are processed)
        orig(screen);

        if (OverlayVisible && OverlayInstance != null)
        {
            try
            {
                int wheel = Mouse.getEventDWheel();
                if (wheel != 0)
                {
                    int x = Mouse.getEventX() * screen.Width / screen.mc.displayWidth;
                    int y = screen.Height - Mouse.getEventY() * screen.Height / screen.mc.displayHeight - 1;
                    try
                    {
                        if (OverlayInstance.HandleMouseScrolled(screen, x, y, wheel))
                        {
                            // consumed by overlay; nothing further to do
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("SeeAllItems: HandleMouseInput -> HandleMouseScrolled threw: " + ex);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("SeeAllItems: HandleMouseInput hook error: " + ex);
            }
        }
    }

    private static void GuiScreen_KeyTyped(Action<GuiScreen, char, int> orig, GuiScreen screen, char eventChar, int eventKey)
    {
        // Do not toggle here; render-time edge detection handles toggling to avoid duplicate events.
        if (OverlayVisible && OverlayInstance != null && screen is BetaSharp.Client.Guis.GuiContainer && OverlayInstance.HandleKeyTyped(screen, eventChar, eventKey))
        {
            return; // handled by overlay
        }

        orig(screen, eventChar, eventKey);
    }
}
