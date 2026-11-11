namespace Aurevix.ControlCenter.Services;

/// <summary>
/// AUREVIX Shell: estilos para los mods «Styler» de Windhawk (herramienta de código abierto, GPL-3.0, que inyecta
/// código en el Explorador de Windows). Aquí solo hay ajustes de aspecto: los objetivos (targets) son los
/// documentados en las guías oficiales de ramensoftware. Se pegan en «Avanzado» de cada mod.
/// </summary>
public static class ShellPresets
{
    public const string WindhawkWingetId = "RamenSoftware.Windhawk";

    public sealed record Mod(string Name, string Page, string Json);

    private const string Graphite = "#0E0E11";
    private const string Line = "#26262D";
    private const string Lime = "#8DE03C";

    public static IReadOnlyList<Mod> Mods { get; } =
    [
        new("Barra de tareas", "https://windhawk.net/mods/windows-11-taskbar-styler", $$"""
            {
              "theme": "DockLike",
              "styleConstants[0]": "DockBackground=<AcrylicBrush TintColor=\"{{Graphite}}\" TintOpacity=\"0.82\" FallbackColor=\"{{Graphite}}\" />",
              "controlStyles[0].target": "Grid#IconPanel@RunningIndicatorStates > Rectangle#RunningIndicator, Taskbar.TaskListLabeledButtonPanel@RunningIndicatorStates > Rectangle#RunningIndicator",
              "controlStyles[0].styles[0]": "Fill={{Lime}}",
              "controlStyles[0].styles[1]": "Width@ActiveRunningIndicator=18"
            }
            """),
        new("Menú Inicio", "https://windhawk.net/mods/windows-11-start-menu-styler", $$"""
            {
              "controlStyles[0].target": "Border#AcrylicBorder",
              "controlStyles[0].styles[0]": "Background:=<AcrylicBrush BackgroundSource=\"Backdrop\" TintColor=\"{{Graphite}}\" TintOpacity=\"0.86\" />",
              "controlStyles[0].styles[1]": "CornerRadius=18",
              "controlStyles[0].styles[2]": "BorderBrush={{Line}}",
              "controlStyles[0].styles[3]": "BorderThickness=1",
              "controlStyles[1].target": "Border#AcrylicOverlay",
              "controlStyles[1].styles[0]": "Visibility=Collapsed",
              "controlStyles[2].target": "Border#StartDropShadow, Border#RootGridDropShadow, Border#DropShadow",
              "controlStyles[2].styles[0]": "CornerRadius=18",
              "controlStyles[3].target": "Border#AppBorder, Border#AccentAppBorder",
              "controlStyles[3].styles[0]": "Background:=<AcrylicBrush BackgroundSource=\"Backdrop\" TintColor=\"{{Graphite}}\" TintOpacity=\"0.86\" />",
              "controlStyles[3].styles[1]": "CornerRadius=18",
              "controlStyles[3].styles[2]": "BorderBrush={{Line}}",
              "controlStyles[4].target": "Border#LayerBorder, Border#AccentLayerBorder",
              "controlStyles[4].styles[0]": "Visibility=Collapsed"
            }
            """),
    ];
}
