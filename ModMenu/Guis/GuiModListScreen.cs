using BetaSharp;
using BetaSharp.Client.Guis;
using BetaSharp.Modding;

namespace ModMenu.Guis;

public class GuiModListScreen : GuiScreen
{
    private const bool EnableFakeModsForTesting = false;
    private const int FakeModCount = 10;
    private const int ButtonDone = 200;
    private const int PanelGap = 10;

    private readonly GuiScreen _parent;
    private readonly List<IMod> _mods = [];
    private GuiModListSlot _modListSlot = null!;
    private int _selectedModIndex = -1;

    public GuiModListScreen(GuiScreen parent)
    {
        _parent = parent;
    }

    public override void InitGui()
    {
        TranslationStorage translations = TranslationStorage.Instance;

        _mods.Clear();
        if (Mods.ModRegistry is not null)
        {
            _mods.AddRange(Mods.ModRegistry);
        }

        if (EnableFakeModsForTesting)
        {
            AddFakeModsForTesting(_mods, FakeModCount);
        }

        _selectedModIndex = _mods.Count > 0 ? 0 : -1;
        _modListSlot = new GuiModListSlot(this, _mods);

        _controlList.Add(new GuiButton(ButtonDone, Width / 2 - 100, Height - 38, translations.TranslateKey("gui.done")));
    }

    protected override void ActionPerformed(GuiButton button)
    {
        if (!button.Enabled)
        {
            return;
        }

        if (button.Id == ButtonDone)
        {
            mc.displayGuiScreen(_parent);
            return;
        }

        _modListSlot.ActionPerformed(button);
    }

    public override void Render(int mouseX, int mouseY, float partialTicks)
    {
        _modListSlot.DrawScreen(mouseX, mouseY, partialTicks);

        DrawCenteredString(FontRenderer, "Mod List", Width / 2, 14, 0xFFFFFF);

        int detailsLeft = GuiModListSlot.PanelWidth + PanelGap;
        int detailsTop = 36;
        int detailsRight = Width - 2;
        int detailsBottom = Height - 60;

        DrawRect(detailsLeft, detailsTop, detailsRight, detailsBottom, 0x90000000u);

        if (_selectedModIndex >= 0 && _selectedModIndex < _mods.Count)
        {
            DrawModDetails(detailsLeft, detailsTop, detailsRight, detailsBottom, _mods[_selectedModIndex]);
        }
        else
        {
            DrawCenteredString(FontRenderer, "No mods loaded.", (detailsLeft + detailsRight) / 2, detailsTop + 12, 0xC0C0C0);
        }

        base.Render(mouseX, mouseY, partialTicks);
    }

    private void DrawModDetails(int left, int top, int right, int bottom, IMod mod)
    {
        int centerX = (left + right) / 2;
        int textX = left + 10;
        int textY = top + 12;

        DrawCenteredString(FontRenderer, mod.Name, centerX, textY, 0xFFFFFF);

        textY += 22;
        string versionLabel = "Version: ";
        string versionText = GetModVersion(mod);
        DrawString(FontRenderer, versionLabel, textX, textY, 0xC0C0C0);
        DrawString(FontRenderer, versionText, textX + FontRenderer.GetStringWidth(versionLabel), textY, 0xFFFFFF);

        textY += 12;
        string authorLabel = "Author: ";
        DrawString(FontRenderer, authorLabel, textX, textY, 0xC0C0C0);
        DrawString(FontRenderer, mod.Author, textX + FontRenderer.GetStringWidth(authorLabel), textY, 0xFFFFFF);

        textY += 18;
        DrawString(FontRenderer, "Description:", textX, textY, 0xC0C0C0);
        textY += 12;

        string description = string.IsNullOrWhiteSpace(mod.Description)
            ? "No description provided."
            : mod.Description;

        FontRenderer.DrawStringWrapped(description, textX, textY, right - left - 20, 0xE0E0E0);
    }

    public int GetSelectedModIndex() => _selectedModIndex;

    public void SetSelectedModIndex(int index)
    {
        if ((uint)index < (uint)_mods.Count)
        {
            _selectedModIndex = index;
        }
    }

    public bool IsSelectedMod(int index) => index == _selectedModIndex;

    public IMod GetModAt(int index) => _mods[index];

    public int GetModCount() => _mods.Count;

    public static string GetModVersion(IMod mod)
    {
        if (mod is FakeMod fakeMod)
        {
            return fakeMod.Version;
        }

        Version? version = mod.GetType().Assembly.GetName().Version;
        return version is null ? "Unknown" : version.ToString();
    }

    private static void AddFakeModsForTesting(List<IMod> mods, int count)
    {
        for (int i = 1; i <= count; i++)
        {
            string index = i.ToString("00");
            mods.Add(new FakeMod(
                name: $"Test Mod {index}",
                version: $"0.{i}.0-test",
                author: $"TestAuthor{index}",
                description: $"Fake mod entry {index} for UI layout testing."));
        }
    }

    private sealed class FakeMod : IMod
    {
        public string Name { get; }
        public string Description { get; }
        public string Author { get; }
        public string Version { get; }
        public Side Side => Side.Client;

        public FakeMod(string name, string version, string author, string description)
        {
            Name = name;
            Version = version;
            Author = author;
            Description = description;
        }

        public void Initialize() { }
        public void PostInitialize() { }
        public void Unload() { }
    }
}
