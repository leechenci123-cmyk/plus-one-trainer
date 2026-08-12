namespace PlusOneTrainer.Core;

public sealed class TrainerException : Exception
{
    public string ResourceKey { get; }

    public TrainerException(string resourceKey, string message) : base(message)
    {
        ResourceKey = resourceKey;
    }
}
