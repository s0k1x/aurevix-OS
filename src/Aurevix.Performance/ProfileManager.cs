namespace Aurevix.Performance;

/// <summary>
/// Aplica perfiles (Equilibrado, Gaming, Productividad, Eficiencia).
/// Al cambiar de perfil primero deshace el anterior, de modo que siempre se puede volver atrás.
/// </summary>
public sealed class ProfileManager(TweakEngine engine, IReadOnlyList<TweakDefinition> catalog, IReadOnlyList<ProfileDefinition> profiles, IPowerPlanAccess? power)
{
    public IReadOnlyList<ProfileDefinition> Profiles => profiles;

    public ProfileDefinition? Find(string id) => profiles.FirstOrDefault(p => p.Id == id);

    /// <summary>Plan de energía que se usaría en este equipo (el primero de la lista que exista).</summary>
    public Guid? ResolvePowerPlan(ProfileDefinition p)
    {
        if (power is null) return null;
        try
        {
            var available = power.ListPlans().Select(x => x.Id).ToHashSet();
            return p.PreferredPowerPlans.FirstOrDefault(available.Contains) is var g && g != Guid.Empty ? g : null;
        }
        catch { return null; }
    }

    public IReadOnlyList<TweakDefinition> TweaksOf(ProfileDefinition p) =>
        p.Tweaks.Select(id => catalog.FirstOrDefault(t => t.Id == id)
            ?? throw new InvalidOperationException($"El perfil {p.Id} referencia un ajuste inexistente: {id}")).ToList();

    public ApplyResult Switch(string profileId)
    {
        var target = Find(profileId) ?? throw new ArgumentException($"Perfil desconocido: {profileId}");
        var previous = engine.ListBackups().FirstOrDefault(b => b.ProfileId is not null);
        if (previous is not null) engine.Revert(previous);
        return engine.Apply(TweaksOf(target), $"Perfil {target.Name}", ResolvePowerPlan(target), target.Id);
    }
}
