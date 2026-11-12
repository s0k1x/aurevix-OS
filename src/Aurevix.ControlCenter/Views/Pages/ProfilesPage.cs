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

        body.Children.Add(AutoCard());

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

    /// <summary>Tarjeta «Perfiles automáticos»: interruptor general, tres reglas y estado actual.</summary>
    private UIElement AutoCard()
    {
        var s = AppServices.Settings;
        var status = Ui.Muted("Ahora: " + AutoProfiles.Status);
        var rules = new StackPanel { Margin = new Thickness(0, 6, 0, 0), IsEnabled = s.AutoProfiles };

        var master = new CheckBox { Content = "Cambiar de perfil automáticamente", Style = Ui.Res<Style>("Switch"), IsChecked = s.AutoProfiles };
        master.Checked += (_, _) => { s.AutoProfiles = true; AppServices.SaveSettings(); rules.IsEnabled = true; };
        master.Unchecked += (_, _) => { s.AutoProfiles = false; AppServices.SaveSettings(); rules.IsEnabled = false; };

        rules.Children.Add(Rule("Juego", "Al abrir un juego, se activa el perfil Gaming.", s.AutoProfileGaming, v => s.AutoProfileGaming = v));
        rules.Children.Add(Rule("Batería", "Si el portátil va sin enchufar, se activa el perfil Eficiencia para que dure más.", s.AutoProfileBattery, v => s.AutoProfileBattery = v));
        rules.Children.Add(Rule("Trabajo", "Al abrir Word, Excel, Outlook, Teams o Visual Studio, se activa el perfil Productividad.", s.AutoProfileWork, v => s.AutoProfileWork = v));

        // El estado se refresca cuando el modo automático cambia de perfil.
        Action<string> onChanged = _ => Dispatcher.BeginInvoke(() => status.Text = "Ahora: " + AutoProfiles.Status);
        AutoProfiles.Changed += onChanged;
        Unloaded += (_, _) => AutoProfiles.Changed -= onChanged;

        return Ui.Card(Header("Perfiles automáticos", master),
            Ui.Muted("AUREVIX elige el perfil por ti según lo que estés haciendo. Espera unos segundos antes de cambiar y, cuando termina, vuelve al perfil que tenías elegido."),
            rules, status);
    }

    private static UIElement Rule(string title, string help, bool value, Action<bool> set)
    {
        var cb = new CheckBox { Content = title, IsChecked = value, Margin = new Thickness(0, 4, 0, 0) };
        cb.Checked += (_, _) => { set(true); AppServices.SaveSettings(); };
        cb.Unchecked += (_, _) => { set(false); AppServices.SaveSettings(); };
        var p = new StackPanel();
        p.Children.Add(cb);
        var m = Ui.Muted(help); m.Margin = new Thickness(22, 0, 0, 2);
        p.Children.Add(m);
        return p;
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

    /// <summary>Título de tarjeta con su interruptor a la derecha.</summary>
    private static Grid Header(string title, CheckBox sw)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var h = Ui.H2(title);
        sw.HorizontalAlignment = HorizontalAlignment.Right;
        sw.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(sw, 1);
        g.Children.Add(h);
        g.Children.Add(sw);
        return g;
    }

}
