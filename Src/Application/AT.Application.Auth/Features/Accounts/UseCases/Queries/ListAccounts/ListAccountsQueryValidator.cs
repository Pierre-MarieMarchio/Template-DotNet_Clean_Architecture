using AppTemplate.Application.Auth.Features.Accounts.Model.Policies;
using FluentValidation;

namespace AppTemplate.Application.Auth.Features.Accounts.UseCases.Queries.ListAccounts;

public sealed class ListAccountsQueryValidator : AbstractValidator<ListAccountsQuery>
{
    public ListAccountsQueryValidator() =>
        RuleFor(x => x.Email).MaximumLength(AccountCollectionPolicy.MaxEmailPrefixLength);
}
