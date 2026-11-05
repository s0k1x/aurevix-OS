using System.Windows;
using System.Windows.Controls;
using Aurevix.ControlCenter.Services;
using Aurevix.Performance;

namespace Aurevix.ControlCenter.Views.Pages;

/// <summary>Optimización: catálogo propio agrupado, con descripción, beneficio, inconvenientes, aplicar y deshacer.</summary>
public sealed class OptimizationPage : UserControl
{
    private static readonly Dictionary<TweakCategory, string> CategoryNames = new()
    {
        [TweakCategory.Startup] = "Inicio y aplicaciones en segundo plano",
        [TweakCategory.Power] = "Energía y planes de rendimiento",
        [TweakCategory.Gaming] = "Juegos y latencia",
        [TweakCategory.Storage] = "Almacenamiento y limpieza segura",
        [TweakCategory.Network] = "Red y conectividad",
        [TweakCategory.Interface] = "Interfaz y efectos visuales",
        [TweakCategory.Privacy] = "Privacidad (opciones oficiales de Windows)",
        [TweakCategory.Maintenance] = "Mantenimiento y actualizaciones",
    };

    public OptimizationPage() => Build();

    private void Build()
    {
        var (root, body) = Ui.Page("Optimización",
            "Catálogo propio y revisado. Ningún ajuste desactiva Defender, Firewall, Windows Update, SmartScreen ni UAC.");

        foreach (var group in AppServices.Catalog.GroupBy(t => t.Category).OrderBy(g => g.Key))
        {
            body.Children.Add(new TextBlock { Text = CategoryNames[group.Key], Style = Ui.Res<Style>("H2"), Margin = new Thickness(0, 12, 0, 8) });
            foreach (var t in group) body.Children.Add(TweakCard(t));
        }
        Content = root;
    }

    private Border TweakCard(TweakDefinition t)
    {
        var status = AppServices.Tweaks.GetStatus(t);
        var compat = AppServices.Tweaks.CompatibilityProblem(t);
        var stateText = status.State switch
        {
            TweakState.Applied => "Aplicado",
            TweakState.Partial => "Parcial",
            TweakState.NotApplied => "No aplicado",
            _ => "Desconocido"
        };
        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(Ui.Pill(stateText, status.State == TweakState.Applied ? Ui.Brush("Ok") : Ui.Brush("Unavailable")));
        header.Children.Add(Ui.Pill($"Riesgo {RiskText(t.Risk)}", t.Risk == RiskLevel.Low ? Ui.Brush("Ok") : Ui.Brush("Review")));
        header.Children.Add(Ui.Text(t.Title, 15, FontWeights.SemiBold));

        var apply = Ui.Button("Aplicar", () => Apply(t), primary: true);
        apply.IsEnabled = compat is null && status.State != TweakState.Applied;
        var undoBackup = AppServices.Tweaks.ListBackups().FirstOrDefault(b => b.ProfileId is null && b.TweakIds.SequenceEqual([t.Id]));
        var undo = Ui.Button("Deshacer", () => Undo(undoBackup!));
        undo.IsEnabled = undoBackup is not null && status.State == TweakState.Applied;

        var card = Ui.Card(header,
            Spaced(Ui.Text(t.Description)),
            Ui.Muted("Efecto esperado: " + t.ExpectedEffect),
            Ui.Muted("Posibles inconvenientes: " + t.PossibleImpact),
            Ui.Muted("Reversión: " + t.Reversal + (t.RequiresSignOut ? " Requiere cerrar sesión." : "")),
            Ui.Muted("Valor actual: " + status.CurrentValueSummary),
            Ui.Row(apply, undo));
        if (compat is not null) ((StackPanel)card.Child).Children.Add(Ui.Muted("⚠ " + compat));
        return card;
    }

    private static TextBlock Spaced(TextBlock tb) { tb.Margin = new Thickness(0, 8, 0, 4); return tb; }

    private static string RiskText(RiskLevel r) => r switch { RiskLevel.Low => "bajo", RiskLevel.Medium => "medio", _ => "alto" };

    private void Apply(TweakDefinition t)
    {
        var preview = AppServices.Tweaks.Preview([t]);
        var lines = string.Join("\n", preview.Select(c => $"{c.Location}: {c.CurrentValue ?? "(sin definir)"} → {c.NewValue}"));
        if (MessageBox.Show($"{t.Title}\n\n{lines}\n\n¿Aplicar este ajuste? Podrás deshacerlo.", "Confirmar ajuste",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        var r = AppServices.Tweaks.Apply([t], t.Title);
        if (!r.Success) MessageBox.Show(string.Join("\n", r.Messages), "No se pudo aplicar", MessageBoxButton.OK, MessageBoxImage.Warning);
        Build();
    }

    private void Undo(ChangeBackup b)
    {
        AppServices.Tweaks.Revert(b);
        Build();
    }
}
