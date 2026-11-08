namespace AppTemplate.Application.Core.Common.Ports;

public interface ICurrentAccessToken
{
    DateTimeOffset? IssuedAt { get; }
}
