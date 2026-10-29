namespace AppTemplate.Presentation.Core.Common.Jobs;

public sealed class BackgroundScope
{
    public bool IsActive { get; private set; }

    internal void Enter() => IsActive = true;
}
