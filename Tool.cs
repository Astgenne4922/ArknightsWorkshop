namespace ArknightsWorkshop;

public abstract class Tool
{
    public abstract string Name { get; }

    public abstract Task Run(CancellationToken cancel);
}
