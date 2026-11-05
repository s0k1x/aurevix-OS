using System.Windows;
using System.Windows.Controls;
using Aurevix.ControlCenter.Services;
using Aurevix.Performance;

namespace Aurevix.ControlCenter.Views.Pages;

/// <summary>Perfiles: Equilibrado, Gaming, Productividad y Eficiencia. Siempre se puede volver atrás.</summary>
public sealed class ProfilesPage : UserControl
{
    public ProfilesPage() => Build();

    private void Build()
    {
        var (root, body) = Ui.Page("Perfiles", "Cada perfil es un conjunto de ajustes documentados. Al cambiar de perfil se deshace el anterior.");
        var active = AppServices.State.ActiveProfile;

        foreach (var p in AppServices.Profiles)
        {
            var plan = AppServices.ProfileManager.ResolvePowerPlan(p);
            var planText = plan is null ? "Plan de energía: se mantiene el actual (el preferido no existe en este equipo)."
                : plan == PowerPlans.HighPerformance ? "Plan de energía: Alto rendimiento."
                : plan == PowerPlans.PowerSaver ? "Plan de energía: Economizador."
                : "Plan de energía: Equilibrado.";
            var tweaks = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
            foreach (var t in AppServices.ProfileManager.TweaksOf(p)) tweaks.Children.Add(Ui.Muted("• " + t.Title));

            var title = new StackPanel { Orientation = Orientation.Horizontal };
            title.Children.Add(Ui.H2(p.Name));
            if (p.Id == active) { var pill = Ui.Pill("Activo", Ui.Brush("Accent")); pill.Margin = new Thickness(10, 0, 0, 6); title.Children.Add(pill); }

            var apply = Ui.Button(p.Id == active ? "Volver a aplicar" : "Aplicar perfil", () => Apply(p), primary: p.Id != active);
            body.Children.Add(Ui.Card(title, Ui.Text(p.Description), Ui.Muted(planText), tweaks, Ui.Row(apply)));
        }

        if (active is not null)
            body.Children.Add(Ui.Card(Ui.H2("Volver a la configuración anterior"),
                Ui.Muted("Deshace el perfil activo y restaura exactamente los valores que había antes de aplicarlo."),
                Ui.Row(Ui.Button("Deshacer perfil activo", Undo))));

        Content = root;
    }

    private void Apply(ProfileDefinition p)
    {
        var preview = AppServices.Tweaks.Preview(AppServices.ProfileManager.TweaksOf(p));
        var lines = string.Join("\n", preview.Select(c => $"• {c.Title}\n    {c.Location}: {c.CurrentValue ?? "(sin definir)"} → {c.NewValue}"));
        var msg = $"Se aplicará el perfil «{p.Name}».\n\nCambios:\n{lines}\n\nSe guarda una copia de seguridad y puedes deshacerlo en cualquier momento. ¿Continuar?";
        if (MessageBox.Show(msg, "Confirmar perfil", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        var r = AppServices.ProfileManager.Switch(p.Id);
        if (r.Success)
        {
            AppServices.State.PreviousProfile = AppServices.State.ActiveProfile;
            AppServices.State.ActiveProfile = p.Id;
            AppServices.SaveState();
        }
        MessageBox.Show(string.Join("\n", r.Messages), r.Success ? "Perfil aplicado" : "No se pudo aplicar", MessageBoxButton.OK,
            r.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
        Build();
    }

    private void Undo()
    {
        var last = AppServices.Tweaks.ListBackups().FirstOrDefault(b => b.ProfileId is not null);
        if (last is null) return;
        if (MessageBox.Show("¿Deshacer el perfil activo y restaurar la configuración anterior?", "Deshacer", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        AppServices.Tweaks.Revert(last);
        AppServices.State.ActiveProfile = null;
        AppServices.SaveState();
        Build();
    }
}
