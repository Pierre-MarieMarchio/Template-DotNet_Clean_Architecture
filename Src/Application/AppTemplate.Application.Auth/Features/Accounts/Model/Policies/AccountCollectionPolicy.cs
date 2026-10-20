using AppTemplate.Application.Core.Common.Collections;
using AppTemplate.Application.Core.Common.Policies;

namespace AppTemplate.Application.Auth.Features.Accounts.Model.Policies;

public sealed class AccountCollectionPolicy : ICollectionPolicy
{
    public const string EmailField = "email";

    public const int MaxEmailPrefixLength = 256;

    public static readonly AccountCollectionPolicy Instance = new();

    public IReadOnlyList<SortableField> SortableFields { get; } = [SortableField.OffsetOnly(EmailField)];

    public string DefaultSort => "email:asc";

    public int MaxSortTerms => 1;

    public int MaxPageSize => 100;

    public int DefaultPageSize => 20;
}
