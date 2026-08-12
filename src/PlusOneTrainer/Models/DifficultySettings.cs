namespace PlusOneTrainer.Models;

public enum GrowthUnit
{
    None,
    Wave,
    Flag,
    SeedSelection,
    Level
}

public enum GrowthFormula
{
    Add,
    Percent,
    Multiply
}

public sealed record DifficultySettings(
    bool Enabled,
    double SpawnCountMultiplier,
    double SpawnSpeedMultiplier,
    double DurabilityMultiplier,
    GrowthUnit GrowthUnit,
    GrowthFormula GrowthFormula,
    double GrowthStep,
    int StartIndex,
    double MaximumMultiplier,
    bool ResetAtLevel)
{
    public static DifficultySettings Default { get; } =
        new(false, 1, 1, 1, GrowthUnit.None, GrowthFormula.Add, 1, 1, 100, true);

    public DifficultySettings Normalize()
    {
        static double Finite(double value, double fallback) => double.IsFinite(value) ? value : fallback;
        var normalizedFormula = GrowthFormula;
        var step = Math.Clamp(Finite(GrowthStep, normalizedFormula == GrowthFormula.Multiply ? 1 : 0), 0, 1000);
        if (normalizedFormula == GrowthFormula.Multiply)
            step = Math.Max(0.01, step);
        return this with
        {
            SpawnCountMultiplier = Math.Clamp(Finite(SpawnCountMultiplier, 1), 1, 10),
            SpawnSpeedMultiplier = Math.Clamp(Finite(SpawnSpeedMultiplier, 1), 0.25, 10),
            DurabilityMultiplier = Math.Clamp(Finite(DurabilityMultiplier, 1), 0.1, 1000),
            GrowthStep = step,
            StartIndex = Math.Clamp(StartIndex, 1, 9999),
            MaximumMultiplier = Math.Clamp(Finite(MaximumMultiplier, 100), 0.1, 100000)
        };
    }
}
