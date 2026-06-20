using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace CurrencyProfitScanner;

internal static class CurrencyUi
{
    public static readonly Vector4 Accent = new(0.20f, 0.56f, 0.96f, 1f);
    public static readonly Vector4 Blue = Accent;
    public static readonly Vector4 Gold = new(1.00f, 0.76f, 0.24f, 1f);
    public static readonly Vector4 Dimmed = new(0.58f, 0.56f, 0.55f, 1f);
    public static readonly Vector4 Green = new(0.36f, 0.82f, 0.45f, 1f);
    public static readonly Vector4 Red = new(0.96f, 0.42f, 0.42f, 1f);
    public static readonly Vector4 Panel = new(0.130f, 0.130f, 0.130f, 1f);

    private const int ThemeColors = 29;
    private const int ThemeVars = 9;

    public static void PushTheme()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 9f);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 7f);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 5f);
        ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, 6f);
        ImGui.PushStyleVar(ImGuiStyleVar.GrabRounding, 4f);
        ImGui.PushStyleVar(ImGuiStyleVar.TabRounding, 6f);
        ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarRounding, 8f);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(8f, 5f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(12f, 10f));

        Col(ImGuiCol.Text, new(0.960f, 0.960f, 0.960f, 1f));
        Col(ImGuiCol.TextDisabled, new(0.550f, 0.550f, 0.550f, 1f));
        Col(ImGuiCol.WindowBg, new(0.082f, 0.082f, 0.082f, 0.94f));
        Col(ImGuiCol.ChildBg, new(0.120f, 0.120f, 0.120f, 0.45f));
        Col(ImGuiCol.PopupBg, new(0.100f, 0.100f, 0.100f, 0.96f));
        Col(ImGuiCol.Border, new(0.200f, 0.560f, 0.960f, 0.22f));
        Col(ImGuiCol.FrameBg, new(0.160f, 0.160f, 0.160f, 1f));
        Col(ImGuiCol.FrameBgHovered, new(0.205f, 0.213f, 0.230f, 1f));
        Col(ImGuiCol.FrameBgActive, new(0.255f, 0.270f, 0.300f, 1f));
        Col(ImGuiCol.TitleBg, new(0.100f, 0.100f, 0.100f, 1f));
        Col(ImGuiCol.TitleBgActive, new(0.090f, 0.170f, 0.280f, 1f));
        Col(ImGuiCol.TitleBgCollapsed, new(0.100f, 0.100f, 0.100f, 0.75f));
        Col(ImGuiCol.Button, new(0.200f, 0.200f, 0.200f, 1f));
        Col(ImGuiCol.ButtonHovered, new(0.180f, 0.390f, 0.640f, 1f));
        Col(ImGuiCol.ButtonActive, new(0.200f, 0.560f, 0.960f, 1f));
        Col(ImGuiCol.Header, new(0.180f, 0.190f, 0.205f, 1f));
        Col(ImGuiCol.HeaderHovered, new(0.170f, 0.370f, 0.610f, 1f));
        Col(ImGuiCol.HeaderActive, new(0.200f, 0.500f, 0.860f, 1f));
        Col(ImGuiCol.CheckMark, new(0.380f, 0.680f, 1f, 1f));
        Col(ImGuiCol.SliderGrab, new(0.230f, 0.500f, 0.840f, 1f));
        Col(ImGuiCol.SliderGrabActive, new(0.360f, 0.660f, 1f, 1f));
        Col(ImGuiCol.Separator, new(0.240f, 0.240f, 0.240f, 1f));
        Col(ImGuiCol.SeparatorHovered, new(0.200f, 0.560f, 0.960f, 0.70f));
        Col(ImGuiCol.Tab, new(0.130f, 0.130f, 0.130f, 1f));
        Col(ImGuiCol.TabHovered, new(0.180f, 0.390f, 0.640f, 1f));
        Col(ImGuiCol.TabActive, new(0.130f, 0.260f, 0.420f, 1f));
        Col(ImGuiCol.ScrollbarBg, new(0.080f, 0.080f, 0.080f, 0.60f));
        Col(ImGuiCol.ScrollbarGrab, new(0.240f, 0.240f, 0.240f, 1f));
        Col(ImGuiCol.ScrollbarGrabHovered, new(0.180f, 0.390f, 0.640f, 1f));
    }

    public static void PopTheme()
    {
        ImGui.PopStyleColor(ThemeColors);
        ImGui.PopStyleVar(ThemeVars);
    }

    public static void Section(string label)
    {
        ImGui.TextColored(Blue, label.ToUpperInvariant());
        ImGui.Separator();
    }

    public static void Pill(string label, Vector4 color)
    {
        var pad = new Vector2(8f, 3f);
        var pos = ImGui.GetCursorScreenPos();
        var size = ImGui.CalcTextSize(label) + pad * 2f;
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(pos, pos + size, ImGui.GetColorU32(color with { W = 0.16f }), 999f);
        draw.AddRect(pos, pos + size, ImGui.GetColorU32(color with { W = 0.56f }), 999f);
        ImGui.SetCursorScreenPos(pos + pad);
        ImGui.TextColored(color, label);
        ImGui.SetCursorScreenPos(new Vector2(pos.X + size.X, pos.Y));
        ImGui.Dummy(size);
    }

    private static void Col(ImGuiCol idx, Vector4 c) => ImGui.PushStyleColor(idx, c);
}
