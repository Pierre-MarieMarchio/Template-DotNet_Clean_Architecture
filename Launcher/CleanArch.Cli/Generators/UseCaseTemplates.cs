namespace CleanArch.Cli.Generators;

internal static class UseCaseTemplates
{
    internal const string Validator = """
        using FluentValidation;

        namespace %P%.Application.Features.%F%.UseCases.%K%.%U%;

        public sealed class %In%Validator : AbstractValidator<%In%>
        {
            public %In%Validator()
            {
        %Rules%
            }
        }
        """;

    internal const string UseCase = """
        using %P%.Application.Core.Common.Results;
        using %Concurrency%;
        using %Dtos%;
        using Mediator;

        namespace %P%.Application.Features.%F%.UseCases.%K%.%U%;

        %Message%

        public sealed class %In%Handler : I%Kind%Handler<%In%, %Out%>
        {
            public ValueTask<%Out%> Handle(%In% %in%, CancellationToken cancellationToken) =>
                throw new NotImplementedException(
                    %Unwritten%);
        }
        """;

    internal const string Dto = """
        namespace %P%.Application.Features.%F%.Model.Dtos;

        public sealed record %D%;
        """;

    internal const string DtoWithId = """
        namespace %P%.Application.Features.%F%.Model.Dtos;

        public sealed record %D%(Guid Id);
        """;

    internal const string ValidatorTests = """
        using %P%.Application.Features.%F%.UseCases.%K%.%U%;
        using Shouldly;
        using Xunit;

        namespace %P%.Application.UnitTests.Features.%F%.UseCases.%K%.%U%;

        public sealed class %In%ValidatorTests
        {
            private readonly %In%Validator _validator = new();

            private static CancellationToken TestToken => TestContext.Current.CancellationToken;

            [Fact]
            public async Task AWellFormed%Kind%_PassesValidation() =>
                (await _validator.ValidateAsync(AWellFormed%Kind%(), TestToken))
                    .IsValid.ShouldBeTrue();
        %Refusals%
            internal static %In% AWellFormed%Kind%() =>
                new(%Values%);
        }
        """;

    internal const string HandlerTests = """
        using %P%.Application.Core.Common.Results;
        using %P%.Application.Features.%F%.UseCases.%K%.%U%;
        using %P%.Application.UnitTests.TestDoubles;
        using %Dtos%;
        using Shouldly;
        using Xunit;

        namespace %P%.Application.UnitTests.Features.%F%.UseCases.%K%.%U%;

        public sealed class %U%Tests
        {
            private static CancellationToken TestToken => TestContext.Current.CancellationToken;

            [Fact]
            public async Task AMalformed%Kind%_NeverReachesTheHandler()
            {
                var malformed = %In%ValidatorTests.AWellFormed%Kind%() with { %Malformed% };

                var result = await UseCase().Handle(malformed, TestToken);

                result.IsFailure.ShouldBeTrue();
                result.Error!.Type.ShouldBe(ErrorType.Validation);
            }

            private static ValidatedHandler<%In%, %Out%> UseCase() =>
                new(new %In%Handler().Handle, new %In%Validator());
        }
        """;
}
