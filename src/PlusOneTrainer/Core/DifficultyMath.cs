using PlusOneTrainer.Models;

namespace PlusOneTrainer.Core;

public static class DifficultyMath
{
    public static double EffectiveDurability(DifficultySettings settings, int wave, int level)
    {
        settings = settings.Normalize();
        var index = settings.GrowthUnit switch
        {
            GrowthUnit.Wave => Math.Max(0, wave) + 1,
            GrowthUnit.Flag => Math.Max(0, wave) / 10 + 1,
            GrowthUnit.SeedSelection => Math.Max(0, wave) / 20 + 1,
            GrowthUnit.Level => Math.Max(1, level),
            _ => 1
        };
        // "Start at N" means N is the first interval receiving one growth step.
        var steps = settings.GrowthUnit == GrowthUnit.None ? 0 : Math.Max(0, index - settings.StartIndex + 1);
        var result = settings.GrowthFormula switch
        {
            GrowthFormula.Add => settings.DurabilityMultiplier + settings.GrowthStep * steps,
            GrowthFormula.Percent => settings.DurabilityMultiplier * Math.Pow(1 + settings.GrowthStep / 100.0, steps),
            GrowthFormula.Multiply => settings.DurabilityMultiplier * Math.Pow(settings.GrowthStep, steps),
            _ => settings.DurabilityMultiplier
        };
        return Math.Clamp(result, 0.1, settings.MaximumMultiplier);
    }

    public static int ScaleHealth(int value, double multiplier)
    {
        if (value <= 0)
            return value;
        return (int)Math.Clamp(Math.Round(value * multiplier), 1, int.MaxValue / 4.0);
    }
}
