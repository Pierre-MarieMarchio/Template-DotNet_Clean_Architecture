namespace AppTemplate.Application.Core.Common.Results;

public interface IFailable<TSelf>
    where TSelf : IFailable<TSelf>
{
    static abstract TSelf Failure(Error failure);
}
