using BetaSharp;
using BetaSharp.Client.Guis;
using BetaSharp.Modding;

namespace ModMenu.Guis;

public class GuiModListScreen : GuiScreen
{
#if DEBUG
    private static readonly bool DebugFakeMods = false;
    private const int DebugFakeModCount = 10;
#endif
    private const int ButtonDoneId = 200;

    private const int OuterMargin = 4;
    private const int PanelGap = 10;
    private const int HeaderY = 14;
    private const int ListTop = 36;
    private const int FooterReservedHeight = 44;
    private const int MinListViewportHeight = 40;
    private const int MinListPanelWidth = 236;
    private const int MinDetailsPanelWidth = 140;
    private const int MinDetailsPanelHeight = 80;
    private const int MinRowHeight = 24;
    private const int MaxRowHeight = 40;
    private const int PreferredVisibleRows = 8;
    private const int MaxSlotWidth = 420;
    private const int DoneButtonBottomOffset = 38;
    private const string TinyLayoutMessage = "Window too small for mod menu. Increase size or lower GUI scale.";

    private readonly GuiScreen _parent;
    private readonly List<ModBase> _mods = [];
    private GuiModListSlot? _modListSlot;
    private int _selectedModIndex = -1;

    private bool _isTinyLayout;
    private int _doneButtonY;
    private int _listWidth;
    private int _listTop;
    private int _listBottom;
    private int _listRowHeight;
    private int _detailsLeft;
    private int _detailsTop;
    private int _detailsRight;
    private int _detailsBottom;

    private int _slotCachedWidth = -1;
    private int _slotCachedTop = -1;
    private int _slotCachedBottom = -1;
    private int _slotCachedRowHeight = -1;

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

#if DEBUG
        if (DebugFakeMods)
        {
            AddFakeModsForTesting(_mods, DebugFakeModCount);
        }
#endif

        _selectedModIndex = _mods.Count > 0 ? 0 : -1;
        _modListSlot = null;
        _slotCachedWidth = -1;
        _slotCachedTop = -1;
        _slotCachedBottom = -1;
        _slotCachedRowHeight = -1;

        int doneButtonY = Math.Max(4, Height - DoneButtonBottomOffset);
        _controlList.Add(new GuiButton(ButtonDoneId, Width / 2 - 100, doneButtonY, translations.TranslateKey("gui.done")));
    }

    protected override void ActionPerformed(GuiButton button)
    {
        if (!button.Enabled)
        {
            return;
        }

        if (button.Id == ButtonDoneId)
        {
            mc.displayGuiScreen(_parent);
            return;
        }

        if (_modListSlot is not null && !IsTinyLayout())
        {
            _modListSlot.ActionPerformed(button);
        }
    }

    public override void Render(int mouseX, int mouseY, float partialTicks)
    {
        ComputeLayout();
        UpdateDoneButton(_doneButtonY);

        if (_isTinyLayout)
        {
            DrawBackground(0);
            DrawCenteredString(FontRenderer, "Mod List", Width / 2, HeaderY, 0xFFFFFF);
            DrawCenteredString(
                FontRenderer,
                TinyLayoutMessage,
                Width / 2,
                ListTop - 2,
                0xC0C0C0);
            base.Render(mouseX, mouseY, partialTicks);
            return;
        }

        EnsureSlot();
        _modListSlot!.DrawScreen(mouseX, mouseY, partialTicks);

        DrawCenteredString(FontRenderer, "Mod List", Width / 2, HeaderY, 0xFFFFFF);
        if (_detailsRight > _detailsLeft && _detailsBottom > _detailsTop)
        {
            DrawRect(_detailsLeft, _detailsTop, _detailsRight, _detailsBottom, 0x90000000u);

            if (_selectedModIndex >= 0 && _selectedModIndex < _mods.Count)
            {
                DrawModDetails(_detailsLeft, _detailsTop, _detailsRight, _detailsBottom, _mods[_selectedModIndex]);
            }
            else
            {
                DrawCenteredString(
                    FontRenderer,
                    "No mods loaded.",
                    (_detailsLeft + _detailsRight) / 2,
                    _detailsTop + 12,
                    0xC0C0C0);
            }
        }

        base.Render(mouseX, mouseY, partialTicks);
    }

    private void UpdateDoneButton(int y)
    {
        int x = Width / 2 - 100;
        foreach (GuiButton button in _controlList)
        {
            if (button.Id != ButtonDoneId)
            {
                continue;
            }

            button.XPosition = x;
            button.YPosition = y;
            return;
        }
    }

    private void EnsureSlot()
    {
        if (_modListSlot is not null &&
            _slotCachedWidth == _listWidth &&
            _slotCachedTop == _listTop &&
            _slotCachedBottom == _listBottom &&
            _slotCachedRowHeight == _listRowHeight)
        {
            return;
        }

        _slotCachedWidth = _listWidth;
        _slotCachedTop = _listTop;
        _slotCachedBottom = _listBottom;
        _slotCachedRowHeight = _listRowHeight;

        _modListSlot = new GuiModListSlot(
            this,
            _mods,
            _listWidth,
            Height,
            _listTop,
            _listBottom,
            _listRowHeight);
    }

    private bool IsTinyLayout()
    {
        int contentBottom = Math.Max(ListTop + 1, Height - FooterReservedHeight);
        int availableHeight = contentBottom - ListTop;
        int contentWidth = Width - OuterMargin * 2;
        int minRequiredWidth = MinListPanelWidth + PanelGap + MinDetailsPanelWidth;
        int minRequiredHeight = Math.Max(MinListViewportHeight, MinDetailsPanelHeight);
        return contentWidth < minRequiredWidth || availableHeight < minRequiredHeight;
    }

    private void ComputeLayout()
    {
        _doneButtonY = Math.Max(4, Height - DoneButtonBottomOffset);
        int contentBottom = Math.Max(ListTop + 1, Height - FooterReservedHeight);
        int availableHeight = contentBottom - ListTop;

        _listTop = ListTop;
        _listBottom = contentBottom;
        _listRowHeight = ComputeRowHeight(availableHeight);

        _isTinyLayout = IsTinyLayout();
        if (_isTinyLayout)
        {
            _listWidth = MinListPanelWidth;
            _detailsLeft = 0;
            _detailsTop = 0;
            _detailsRight = 0;
            _detailsBottom = 0;
            return;
        }

        int contentWidth = Width - OuterMargin * 2;
        int maxListWidth = contentWidth - PanelGap - MinDetailsPanelWidth;
        int preferredListWidth = contentWidth * 38 / 100;
        _listWidth = Math.Clamp(preferredListWidth, MinListPanelWidth, Math.Min(MaxSlotWidth, maxListWidth));
        _detailsLeft = OuterMargin + _listWidth + PanelGap;
        _detailsTop = ListTop;
        _detailsRight = Width - OuterMargin;
        _detailsBottom = contentBottom;
    }

    private int ComputeRowHeight(int viewportHeight)
    {
        int desiredRows = Math.Clamp(_mods.Count, 3, PreferredVisibleRows);
        int rowHeight = viewportHeight / Math.Max(1, desiredRows);
        return Math.Clamp(rowHeight, MinRowHeight, MaxRowHeight);
    }

    private void DrawModDetails(int left, int top, int right, int bottom, ModBase mod)
    {
        int panelWidth = right - left;
        int panelHeight = bottom - top;
        if (panelWidth <= 20 || panelHeight <= 20)
        {
            return;
        }

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

    public ModBase GetModAt(int index) => _mods[index];

    public int GetModCount() => _mods.Count;

    public static string GetModVersion(ModBase mod)
    {
#if DEBUG
        if (mod is FakeMod fakeMod)
        {
            return fakeMod.Version;
        }
#endif

        Version? version = mod.GetType().Assembly.GetName().Version;
        return version is null ? "Unknown" : version.ToString();
    }

#if DEBUG
    private static void AddFakeModsForTesting(List<ModBase> mods, int count)
    {
        for (int i = 1; i <= count; i++)
        {
            string index = i.ToString("00");
            mods.Add(new FakeMod(
                name: $"Debug Mod {index}",
                version: $"0.{i}.0-debug",
                author: $"DebugAuthor{index}",
                description: $"Fake mod entry {index} for Mod Menu layout testing."));
        }
    }

    private sealed class FakeMod : ModBase
    {
        public override string Name { get; }
        public override string Description { get; }
        public override string Author { get; }
        public string Version { get; }

        public FakeMod(string name, string version, string author, string description)
        {
            Name = name;
            Version = version;
            Author = author;
            Description = description;
        }

        public override void Initialize(Side side) { }

        public override void PostInitialize(Side side) { }

        public override void Unload(Side side) { }
    }
#endif
}
