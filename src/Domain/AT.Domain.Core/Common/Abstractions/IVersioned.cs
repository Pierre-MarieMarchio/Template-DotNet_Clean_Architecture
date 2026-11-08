namespace AppTemplate.Domain.Core.Common.Abstractions;

public interface IVersioned
{
    uint Version { get; }

    void SetVersion(uint version);
}
