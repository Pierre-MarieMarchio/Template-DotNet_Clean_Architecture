namespace CleanArch.Cli.Generators.Shared;

internal static class FeatureTemplates
{
    internal const string Aggregate = """
        using %P%.Domain.Core.Common.Abstractions; //?IOwnedAggregate
        using %P%.Domain.Core.Common.Exceptions; //?DomainException
        using %P%.Domain.Core.Common.Primitives;

        namespace %P%.Domain.Features.%F%.Model.Entities;

        public sealed class %A% : AuditableAggregateRoot<Guid>%Implements%
        {
        %if fields%
        %FConstants%
        %else%
            public const int NameMaxLength = 200;

            public const int DescriptionMaxLength = 2_000;
        %end%
        %if anontext%

            public const string AnonymisedText = "[deleted account]";
        %end%

            private %A%(
                Guid id,
        %if owner%
                UserId ownerId,
        %end%
        %if fields%
        %FCtorParams%
        %else%
                string name,
                string description) : base(id)
        %end%
            {
        %if owner%
                ArgumentNullException.ThrowIfNull(ownerId);

                OwnerId = ownerId;
        %end%
        %if fields%
        %FAssign%
        %else%
                Name = name;
                Description = description;
        %end%
            }

        %if owner%
            public UserId OwnerId { get; }

        %end%
        %if fields%
        %FProperties%
        %else%
        %if update|anon%
            public string Name { get; private set; }

            public string Description { get; private set; }
        %else%
            public string Name { get; }

            public string Description { get; }
        %end%
        %end%

        %if create%
            public static %A% Create(
        %if owner%
                UserId ownerId,
        %end%
        %if fields%
        %FCreateParams%
        %else%
                string name,
                string description) =>
        %end%
                new(
                    Guid.CreateVersion7(),
        %if owner%
                    ownerId,
        %end%
        %if fields%
        %FCreateArgs%
        %else%
                    RequireName(name),
                    RequireDescription(description));
        %end%

        %end%
            public static %A% Rehydrate(
                Guid id,
        %if owner%
                UserId ownerId,
        %end%
        %if fields%
        %FRehydrateParams%
        %else%
                string name,
                string description)
        %end%
            {
                if (id == Guid.Empty)
                {
                    throw new DomainException("A stored %L% must have an id.");
                }

                return new(
                    id,
        %if owner%
                    ownerId,
        %end%
        %if fields%
        %FRehydrateArgs%
        %else%
                    name,
                    description);
        %end%
            }

        %if update%
        %if fields%
            public void Update(%FUpdateParams%)
            {
        %FUpdateBody%
            }
        %else%
            public void Update(string name, string description)
            {
                string validName = RequireName(name);
                string validDescription = RequireDescription(description);

                Name = validName;
                Description = validDescription;
            }
        %end%

        %end%
        %if anon%
            public void Anonymise()
            {
        %if fields%
        %FAnonymise%
        %else%
                Name = AnonymisedText;
                Description = string.Empty;
        %end%
            }

        %end%
        %if content&fields%
        %FRequire%
        %end%
        %if content&!fields%
            private static string RequireName(string name)
            {
                string trimmed = name?.Trim() ?? string.Empty;

                if (trimmed.Length == 0)
                {
                    throw new DomainException("%An% %L% name is required.");
                }

                return trimmed.Length <= NameMaxLength
                    ? trimmed
                    : throw new DomainException($"%An% %L% name cannot exceed {NameMaxLength} characters.");
            }

            private static string RequireDescription(string description)
            {
                if (description is null)
                {
                    throw new DomainException("%An% %L% description is required; it may be empty.");
                }

                return description.Length <= DescriptionMaxLength
                    ? description
                    : throw new DomainException($"%An% %L% description cannot exceed {DescriptionMaxLength} characters.");
            }
        %end%
        }
        """;

    internal const string RepositoryPort = """
        using %P%.Domain.Core.Common.Primitives; //?UserId
        using %P%.Domain.Features.%F%.Model.Entities;

        namespace %P%.Domain.Features.%F%.Repositories;

        public interface I%A%Repository
        {
        %if change%
        %if owner%
            Task<%A%?> GetAsync(UserId ownerId, Guid id, CancellationToken cancellationToken);
        %else%
            Task<%A%?> GetAsync(Guid id, CancellationToken cancellationToken);
        %end%

        %end%
        %if owner%
            Task<IReadOnlyList<%A%>> GetForOwnerAsync(
                UserId ownerId,
                CancellationToken cancellationToken);

        %end%
        %if create%
            void Add(%A% aggregate);

        %end%
        %if delete|owner%
            void Remove(%A% aggregate);
        %end%
        }
        """;

    internal const string ContentDto = """
        namespace %P%.Application.Features.%F%.Model.Dtos;

        %if fields%
        public sealed record %A%ContentDto(
        %FParamList%);
        %else%
        public sealed record %A%ContentDto(string Name, string Description);
        %end%
        """;

    internal const string Dto = """
        namespace %P%.Application.Features.%F%.Model.Dtos;

        public sealed record %A%Dto(
            Guid Id,
        %if fields%
        %FDtoParams%
        %else%
            string Name,
            string Description,
        %end%
            DateTimeOffset CreatedAt,
            DateTimeOffset? LastModifiedAt);
        """;

    internal const string SummaryDto = """
        namespace %P%.Application.Features.%F%.Model.Dtos;

        public sealed record %A%SummaryDto(
            Guid Id,
        %if fields%
        %FSummaryParams%
        %else%
            string Name,
        %end%
            DateTimeOffset CreatedAt,
            DateTimeOffset? LastModifiedAt);
        """;

    internal const string ListQuery = """
        using %P%.Application.Core.Common.Collections;

        namespace %ListInputNamespace%;

        public sealed record %ListInput%(
            string? Paging,
            int? Page,
            int? PageSize,
            string? Cursor,
            string? Sort) : ICollectionQuery
        {
            public static %ListInput% Offset(int? page, int? pageSize) => new(null, page, pageSize, null, null);
        }
        """;

    internal const string Errors = """
        using %P%.Application.Core.Common.Results;

        namespace %P%.Application.Features.%F%.Model.Errors;

        public static class %A%Errors
        {
            public static Error %A%NotFound(Guid %L%Id) => Error.NotFound(
                "%L%.notFound",
                $"No %L% with id '{%L%Id}' was found.");
        }
        """;

    internal const string CollectionPolicy = """
        using %P%.Application.Core.Common.Collections;
        using %P%.Application.Core.Common.Policies;

        namespace %P%.Application.Features.%F%.Model.Policies;

        public sealed class %A%CollectionPolicy : ICollectionPolicy
        {
        %if fields%
        %FPolicyFields%
        %else%
            public const string NameField = "name";
        %end%

            public const string CreatedAtField = "createdAt";

            public static readonly %A%CollectionPolicy Instance = new();

            public IReadOnlyList<SortableField> SortableFields { get; } =
            [
        %if fields%
        %FSortables%
        %else%
                SortableField.Keyset(NameField),
        %end%
                SortableField.Keyset(CreatedAtField),
            ];

            public string DefaultSort => "createdAt:desc";

            public int MaxSortTerms => 2;

            public int MaxPageSize => 100;

            public int DefaultPageSize => 20;
        }
        """;

    internal const string InputPolicy = """
        namespace %P%.Application.Features.%F%.Model.Policies;

        public static class %A%InputPolicy
        {
        %if fields%
        %FPolicyConstants%
        %else%
            public const int NameMaxLength = 200;

            public const int DescriptionMaxLength = 2_000;
        %end%
        }
        """;

    internal const string Filter = """
        namespace %P%.Application.Features.%F%.Ports.%A%Queries;

        public sealed record %A%Filter
        {
            public static readonly %A%Filter None = new();
        }
        """;

    internal const string QueriesPort = """
        using %P%.Application.Core.Common.Collections; //?FeaturePageRequest
        using %P%.Application.Core.Common.Concurrency; //?Versioned
        using %P%.Application.Core.Common.Results; //?PagedResult
        using %P%.Application.Features.%F%.Model.Dtos; //?%A%SummaryDto|%A%Dto
        using %P%.Domain.Core.Common.Primitives; //?UserId

        namespace %P%.Application.Features.%F%.Ports.%A%Queries;

        public interface I%A%Queries
        {
        %if list%
            Task<PagedResult<%A%SummaryDto>> %ListQuery%(
        %if owner%
                UserId ownerId,
        %end%
                FeaturePageRequest<%A%Filter> request,
                CancellationToken cancellationToken = default);

        %end%
        %if single%
            Task<%ReadDto%?> GetAsync(
        %if owner%
                UserId ownerId,
        %end%
                Guid %L%Id,
                CancellationToken cancellationToken = default);
        %end%
        %if owner%

            Task<IReadOnlyList<UserId>> ListDataOwnerIdsAsync(CancellationToken cancellationToken = default);
        %end%
        }
        """;

    internal const string StorePort = """
        using %P%.Application.Core.Common.Concurrency; //?VersionPrecondition
        using %P%.Application.Features.%F%.Model.Dtos; //?%A%ContentDto
        using %P%.Domain.Core.Common.Primitives; //?UserId

        namespace %P%.Application.Features.%F%.Ports.%A%Store;

        public interface I%A%Store
        {
        %if create%
            void Add(
        %if owner%
                UserId ownerId,
        %end%
                Guid %L%Id,
                %A%ContentDto content,
                DateTimeOffset createdAt);

        %end%
        %if update%
            Task<%A%ChangeStatus> StageReplacementAsync(
        %if owner%
                UserId ownerId,
        %end%
                Guid %L%Id,
                %A%ContentDto content,
                DateTimeOffset modifiedAt,
        %if etag%
                VersionPrecondition? precondition,
        %end%
                CancellationToken cancellationToken = default);

        %end%
        %if delete%
            Task<%A%ChangeStatus> StageRemovalAsync(
        %if owner%
                UserId ownerId,
        %end%
                Guid %L%Id,
        %if etag%
                VersionPrecondition? precondition,
        %end%
                CancellationToken cancellationToken = default);
        %end%
        %if purge%

            Task<int> StageRemovalForOwnerAsync(
                UserId ownerId,
                CancellationToken cancellationToken = default);
        %end%
        %if anon%

            Task<int> StageAnonymisationForOwnerAsync(
                UserId ownerId,
                CancellationToken cancellationToken = default);
        %end%
        }
        """;

    internal const string ChangeStatus = """
        namespace %P%.Application.Features.%F%.Ports.%A%Store;

        public enum %A%ChangeStatus
        {
            Staged,

            NotFound,
        %if etag%

            VersionMismatch,
        %end%
        }
        """;

    internal const string ServicePort = """
        using %P%.Application.Core.Common.Concurrency; //?Versioned|VersionPrecondition|ConcurrencyErrors
        using %P%.Application.Core.Common.Results;
        using %P%.Application.Features.%F%.Model.Dtos; //?%A%ContentDto|%A%Dto|%A%SummaryDto|%A%ListQueryDto
        using %P%.Application.Features.%F%.Model.Errors; //?%A%Errors

        namespace %P%.Application.Features.%F%.Services;

        public interface I%A%Service
        {
        %if list%
            Task<Result<PagedResult<%A%SummaryDto>>> ListAsync(
                %A%ListQueryDto query,
                CancellationToken cancellationToken = default);

        %end%
        %if get%
            Task<Result<%ReadDto%>> GetAsync(
                Guid %L%Id,
                CancellationToken cancellationToken = default);

        %end%
        %if create%
            Task<Result<%ReadDto%>> CreateAsync(
                %A%ContentDto content,
                CancellationToken cancellationToken = default);

        %end%
        %if update%
            Task<Result<%ReadDto%>> UpdateAsync(
                Guid %L%Id,
                %A%ContentDto content,
        %if etag%
                VersionPrecondition? precondition,
        %end%
                CancellationToken cancellationToken = default);

        %end%
        %if delete%
            Task<Result> DeleteAsync(
                Guid %L%Id,
        %if etag%
                VersionPrecondition? precondition,
        %end%
                CancellationToken cancellationToken = default);
        %end%
        }
        """;

    internal const string Service = """
        using %P%.Application.Core.Common.Collections; //?CollectionBinding
        using %P%.Application.Core.Common.Concurrency; //?Versioned|ConcurrencyErrors|VersionPrecondition
        using %P%.Application.Core.Common.Ports;
        using %P%.Application.Core.Common.Results;
        using %P%.Application.Core.Common.Validation; //?EnsureValidAsync
        using %P%.Application.Features.%F%.Model.Dtos; //?%A%ContentDto|%A%Dto|%A%SummaryDto|%A%ListQueryDto
        using %P%.Application.Features.%F%.Model.Errors; //?%A%Errors
        using %P%.Application.Features.%F%.Model.Policies; //?%A%CollectionPolicy
        using %P%.Application.Features.%F%.Ports.%A%Queries; //?I%A%Queries|%A%Filter
        using %P%.Application.Features.%F%.Ports.%A%Store; //?I%A%Store|%A%ChangeStatus
        using %P%.Domain.Core.Common.Primitives; //?UserId
        using FluentValidation; //?IValidator

        namespace %P%.Application.Features.%F%.Services;

        internal sealed class %A%Service(
        %if rq%
            I%A%Queries queries,
        %end%
        %if wq%
            I%A%Store store,
            IUnitOfWork unitOfWork,
        %end%
        %if clock%
            TimeProvider timeProvider,
        %end%
        %if content%
            IValidator<%A%ContentDto> validator,
        %end%
            ICurrentUser currentUser) : I%A%Service
        {
        %if list%
            public async Task<Result<PagedResult<%A%SummaryDto>>> ListAsync(
                %A%ListQueryDto query,
                CancellationToken cancellationToken = default)
            {
                ArgumentNullException.ThrowIfNull(query);

                var userId = currentUser.RequireUserId();

                if (userId.IsFailure)
                {
                    return userId.To<PagedResult<%A%SummaryDto>>();
                }

                var bound = CollectionBinding.Bind(
                    query,
                    %A%CollectionPolicy.Instance,
                    () => Result.Success(%A%Filter.None),
                    %DateKeys%);

                if (bound.IsFailure)
                {
                    return bound.To<PagedResult<%A%SummaryDto>>();
                }

                return await queries.%ListQuery%(%SvcOwner%bound.Value, cancellationToken);
            }

        %end%
        %if get%
            public async Task<Result<%ReadDto%>> GetAsync(Guid %L%Id, CancellationToken cancellationToken = default)
            {
                var userId = currentUser.RequireUserId();

                if (userId.IsFailure)
                {
                    return userId.To<%ReadDto%>();
                }

                return await ReadAsync(%SvcOwner%%L%Id, cancellationToken);
            }

        %end%
        %if create%
            public async Task<Result<%ReadDto%>> CreateAsync(
                %A%ContentDto content,
                CancellationToken cancellationToken = default)
            {
                ArgumentNullException.ThrowIfNull(content);

                var userId = currentUser.RequireUserId();

                if (userId.IsFailure)
                {
                    return userId.To<%ReadDto%>();
                }

                var validation = await validator.EnsureValidAsync(content, cancellationToken);

                if (validation.IsFailure)
                {
                    return validation.To<%ReadDto%>();
                }

                var %L%Id = Guid.CreateVersion7();

                store.Add(%SvcOwner%%L%Id, %Staged%, timeProvider.GetUtcNow());
                await unitOfWork.SaveChangesAsync(cancellationToken);

                return await ReadAsync(%SvcOwner%%L%Id, cancellationToken);
            }

        %end%
        %if update%
            public async Task<Result<%ReadDto%>> UpdateAsync(
                Guid %L%Id,
                %A%ContentDto content,
        %if etag%
                VersionPrecondition? precondition,
        %end%
                CancellationToken cancellationToken = default)
            {
                ArgumentNullException.ThrowIfNull(content);

                var userId = currentUser.RequireUserId();

                if (userId.IsFailure)
                {
                    return userId.To<%ReadDto%>();
                }

                var validation = await validator.EnsureValidAsync(content, cancellationToken);

                if (validation.IsFailure)
                {
                    return validation.To<%ReadDto%>();
                }

                var status = await store.StageReplacementAsync(
        %if owner%
                    userId.Value,
        %end%
                    %L%Id,
                    %Staged%,
                    timeProvider.GetUtcNow(),
        %if etag%
                    precondition,
        %end%
                    cancellationToken);

                if (status != %A%ChangeStatus.Staged)
                {
                    return Result.Failure<%ReadDto%>(FailureOf(status, %L%Id));
                }

                await unitOfWork.SaveChangesAsync(cancellationToken);

                return await ReadAsync(%SvcOwner%%L%Id, cancellationToken);
            }

        %end%
        %if delete%
            public async Task<Result> DeleteAsync(
                Guid %L%Id,
        %if etag%
                VersionPrecondition? precondition,
        %end%
                CancellationToken cancellationToken = default)
            {
                var userId = currentUser.RequireUserId();

                if (userId.IsFailure)
                {
                    return userId;
                }

                var status = await store.StageRemovalAsync(
        %if owner%
                    userId.Value,
        %end%
                    %L%Id,
        %if etag%
                    precondition,
        %end%
                    cancellationToken);

                if (status != %A%ChangeStatus.Staged)
                {
                    return Result.Failure(FailureOf(status, %L%Id));
                }

                await unitOfWork.SaveChangesAsync(cancellationToken);

                return Result.Success();
            }

        %end%
        %if single%
            private async Task<Result<%ReadDto%>> ReadAsync(
        %if owner%
                UserId ownerId,
        %end%
                Guid %L%Id,
                CancellationToken cancellationToken)
            {
                var %L% = await queries.GetAsync(%OwnerArg%%L%Id, cancellationToken);

                return %L% is null
                    ? Result.Failure<%ReadDto%>(%A%Errors.%A%NotFound(%L%Id))
                    : %L%;
            }

        %end%
        %if trim&fields%
            private static %A%ContentDto Trimmed(%A%ContentDto content) => content with { %FTrimmed% };
        %end%
        %if trim&!fields%
            private static %A%ContentDto Trimmed(%A%ContentDto content) => content with { Name = content.Name.Trim() };

        %end%
        %if change%
            private static Error FailureOf(%A%ChangeStatus status, Guid %L%Id) => status switch
            {
                %A%ChangeStatus.NotFound => %A%Errors.%A%NotFound(%L%Id),
        %if etag%
                %A%ChangeStatus.VersionMismatch => ConcurrencyErrors.PreconditionFailed,
        %end%
                _ => throw new ArgumentOutOfRangeException(nameof(status), status, "A staged change has no failure."),
            };
        %end%
        }
        """;

    internal const string ContentValidator = """
        using %P%.Application.Features.%F%.Model.Policies; //?%A%InputPolicy
        using FluentValidation;

        namespace %P%.Application.Features.%F%.Model.Dtos;

        public sealed class %A%ContentDtoValidator : AbstractValidator<%A%ContentDto>
        {
            public %A%ContentDtoValidator()
            {
        %if fields%
        %FRulesContent%
        %else%
                RuleFor(content => content.Name)
                    .Cascade(CascadeMode.Stop)
                    .NotEmpty().WithMessage("%An% %L% name is required.")
                    .Must(name => name.Trim().Length <= %Limits%.NameMaxLength)
                    .WithMessage($"%An% %L% name cannot exceed {%Limits%.NameMaxLength} characters.");

                RuleFor(content => content.Description)
                    .Cascade(CascadeMode.Stop)
                    .NotNull().WithMessage("%An% %L% description is required; it may be empty.")
                    .MaximumLength(%Limits%.DescriptionMaxLength)
                    .WithMessage($"%An% %L% description cannot exceed {%Limits%.DescriptionMaxLength} characters.");
        %end%
            }
        }
        """;

    internal const string DtoMapping = """
        using %P%.Application.Core.Common.Concurrency;
        using %P%.Application.Features.%F%.Model.Dtos;
        using %P%.Domain.Features.%F%.Model.Entities;

        namespace %P%.Application.Features.%F%.Model.Mapping;

        internal static class %A%DtoMapping
        {
            public static Versioned<%A%Dto> Detail(%A% %L%)
            {
                ArgumentNullException.ThrowIfNull(%L%);

                var detail = new %A%Dto(
                    %L%.Id,
        %if fields%
        %FDetailArgs%
        %else%
                    %L%.Name,
                    %L%.Description,
        %end%
                    %L%.CreatedAt,
                    %L%.LastModifiedAt);

                return new Versioned<%A%Dto>(detail, %L%.Version);
            }
        }
        """;

    internal const string AccessServicePort = """
        using %P%.Application.Core.Common.Concurrency;
        using %P%.Application.Core.Common.Results;
        using %P%.Domain.Features.%F%.Model.Entities;

        namespace %P%.Application.Features.%F%.Services;

        public interface I%A%Service
        {
            Task<Result<%A%>> %Load%(
                Guid %L%Id,
                VersionPrecondition? precondition,
                CancellationToken cancellationToken = default);
        }
        """;

    internal const string AccessService = """
        using %P%.Application.Core.Common.Concurrency;
        using %P%.Application.Core.Common.Ports;
        using %P%.Application.Core.Common.Results;
        using %P%.Application.Features.%F%.Model.Errors;
        using %P%.Domain.Features.%F%.Model.Entities;
        using %P%.Domain.Features.%F%.Repositories;

        namespace %P%.Application.Features.%F%.Services;

        internal sealed class %A%Service(I%A%Repository repository, ICurrentUser currentUser) : I%A%Service
        {
            public async Task<Result<%A%>> %Load%(
                Guid %L%Id,
                VersionPrecondition? precondition,
                CancellationToken cancellationToken = default)
            {
                var userId = currentUser.RequireUserId();

                if (userId.IsFailure)
                {
                    return userId.To<%A%>();
                }

                var aggregate = await repository.GetAsync(%SvcOwner%%L%Id, cancellationToken);

                if (aggregate is null)
                {
                    return Result.Failure<%A%>(%A%Errors.%A%NotFound(%L%Id));
                }

                return precondition is not null && !precondition.IsSatisfiedBy(aggregate.Version)
                    ? Result.Failure<%A%>(ConcurrencyErrors.PreconditionFailed)
                    : Result.Success(aggregate);
            }
        }
        """;

    internal const string GetQueryValidator = """
        using FluentValidation;

        namespace %P%.Application.Features.%F%.UseCases.Queries.Get%A%;

        public sealed class Get%A%QueryValidator : AbstractValidator<Get%A%Query>
        {
            public Get%A%QueryValidator() =>
                RuleFor(query => query.%A%Id)
                    .NotEmpty().WithMessage("%An% %L% id is required.");
        }
        """;

    internal const string GetUseCase = """
        using %P%.Application.Core.Common.Concurrency;
        using %P%.Application.Core.Common.Ports;
        using %P%.Application.Core.Common.Results;
        using %P%.Application.Features.%F%.Model.Dtos;
        using %P%.Application.Features.%F%.Model.Errors;
        using %P%.Application.Features.%F%.Ports.%A%Queries;
        using Mediator;

        namespace %P%.Application.Features.%F%.UseCases.Queries.Get%A%;

        public sealed record Get%A%Query(Guid %A%Id) : IQuery<Result<Versioned<%A%Dto>>>;

        public sealed class Get%A%QueryHandler(
            I%A%Queries queries,
            ICurrentUser currentUser) : IQueryHandler<Get%A%Query, Result<Versioned<%A%Dto>>>
        {
            public async ValueTask<Result<Versioned<%A%Dto>>> Handle(
                Get%A%Query query,
                CancellationToken cancellationToken)
            {
                ArgumentNullException.ThrowIfNull(query);

                var userId = currentUser.RequireUserId();

                if (userId.IsFailure)
                {
                    return userId.To<Versioned<%A%Dto>>();
                }

                var found = await queries.GetAsync(%SvcOwner%query.%A%Id, cancellationToken);

                return found is null
                    ? Result.Failure<Versioned<%A%Dto>>(%A%Errors.%A%NotFound(query.%A%Id))
                    : found;
            }
        }
        """;

    internal const string ListUseCase = """
        using %P%.Application.Core.Common.Collections;
        using %P%.Application.Core.Common.Ports;
        using %P%.Application.Core.Common.Results;
        using %P%.Application.Features.%F%.Model.Dtos;
        using %P%.Application.Features.%F%.Model.Policies;
        using %P%.Application.Features.%F%.Ports.%A%Queries;
        using Mediator;

        namespace %P%.Application.Features.%F%.UseCases.Queries.Get%F%;

        public sealed record Get%F%Query(
            string? Paging,
            int? Page,
            int? PageSize,
            string? Cursor,
            string? Sort) : ICollectionQuery, IQuery<Result<PagedResult<%A%SummaryDto>>>
        {
            public static Get%F%Query Offset(int? page, int? pageSize) => new(null, page, pageSize, null, null);
        }

        public sealed class Get%F%QueryHandler(I%A%Queries queries, ICurrentUser currentUser)
            : IQueryHandler<Get%F%Query, Result<PagedResult<%A%SummaryDto>>>
        {
            public async ValueTask<Result<PagedResult<%A%SummaryDto>>> Handle(
                Get%F%Query query,
                CancellationToken cancellationToken)
            {
                ArgumentNullException.ThrowIfNull(query);

                var userId = currentUser.RequireUserId();

                if (userId.IsFailure)
                {
                    return userId.To<PagedResult<%A%SummaryDto>>();
                }

                var bound = CollectionBinding.Bind(
                    query,
                    %A%CollectionPolicy.Instance,
                    () => Result.Success(%A%Filter.None),
                    %DateKeys%);

                if (bound.IsFailure)
                {
                    return bound.To<PagedResult<%A%SummaryDto>>();
                }

                return await queries.%ListQuery%(%SvcOwner%bound.Value, cancellationToken);
            }
        }
        """;

    internal const string CreateCommandValidator = """
        using %P%.Domain.Features.%F%.Model.Entities; //?%A%
        using FluentValidation;

        namespace %P%.Application.Features.%F%.UseCases.Commands.Create%A%;

        public sealed class Create%A%CommandValidator : AbstractValidator<Create%A%Command>
        {
            public Create%A%CommandValidator()
            {
        %if fields%
        %FRulesCommand%
        %else%
                RuleFor(command => command.Name)
                    .Cascade(CascadeMode.Stop)
                    .NotEmpty().WithMessage("%An% %L% name is required.")
                    .Must(name => name.Trim().Length <= %A%.NameMaxLength)
                    .WithMessage($"%An% %L% name cannot exceed {%A%.NameMaxLength} characters.");

                RuleFor(command => command.Description)
                    .Cascade(CascadeMode.Stop)
                    .NotNull().WithMessage("%An% %L% description is required; it may be empty.")
                    .MaximumLength(%A%.DescriptionMaxLength)
                    .WithMessage($"%An% %L% description cannot exceed {%A%.DescriptionMaxLength} characters.");
        %end%
            }
        }
        """;

    internal const string CreateUseCase = """
        using %P%.Application.Core.Common.Concurrency;
        using %P%.Application.Core.Common.Ports;
        using %P%.Application.Core.Common.Results;
        using %P%.Application.Core.Common.Validation;
        using %P%.Application.Features.%F%.Model.Dtos;
        using %P%.Application.Features.%F%.Model.Mapping;
        using %P%.Domain.Features.%F%.Model.Entities;
        using %P%.Domain.Features.%F%.Repositories;
        using Mediator;

        namespace %P%.Application.Features.%F%.UseCases.Commands.Create%A%;

        %if fields%
        public sealed record Create%A%Command(
        %FParamList%) : ICommand<Result<Versioned<%A%Dto>>>;
        %else%
        public sealed record Create%A%Command(string Name, string Description) : ICommand<Result<Versioned<%A%Dto>>>;
        %end%

        public sealed class Create%A%CommandHandler(
            I%A%Repository repository,
            IUnitOfWork unitOfWork,
            ICurrentUser currentUser) : ICommandHandler<Create%A%Command, Result<Versioned<%A%Dto>>>
        {
            public async ValueTask<Result<Versioned<%A%Dto>>> Handle(
                Create%A%Command command,
                CancellationToken cancellationToken)
            {
                ArgumentNullException.ThrowIfNull(command);

                var userId = currentUser.RequireUserId();

                if (userId.IsFailure)
                {
                    return userId.To<Versioned<%A%Dto>>();
                }

        %if fields%
                var created = DomainGuard.Try(() => %A%.Create(%SvcOwner%%FCommandArgs%));
        %else%
                var created = DomainGuard.Try(() => %A%.Create(%SvcOwner%command.Name, command.Description));
        %end%

                if (created.IsFailure)
                {
                    return created.To<Versioned<%A%Dto>>();
                }

                var %L% = created.Value;

                repository.Add(%L%);
                await unitOfWork.SaveChangesAsync(cancellationToken);

                return %A%DtoMapping.Detail(%L%);
            }
        }
        """;

    internal const string UpdateCommandValidator = """
        using %P%.Domain.Features.%F%.Model.Entities; //?%A%
        using FluentValidation;

        namespace %P%.Application.Features.%F%.UseCases.Commands.Update%A%;

        public sealed class Update%A%CommandValidator : AbstractValidator<Update%A%Command>
        {
            public Update%A%CommandValidator()
            {
                RuleFor(command => command.%A%Id)
                    .NotEmpty().WithMessage("%An% %L% id is required.");

        %if fields%
        %FRulesCommand%
        %else%
                RuleFor(command => command.Name)
                    .Cascade(CascadeMode.Stop)
                    .NotEmpty().WithMessage("%An% %L% name is required.")
                    .Must(name => name.Trim().Length <= %A%.NameMaxLength)
                    .WithMessage($"%An% %L% name cannot exceed {%A%.NameMaxLength} characters.");

                RuleFor(command => command.Description)
                    .Cascade(CascadeMode.Stop)
                    .NotNull().WithMessage("%An% %L% description is required; it may be empty.")
                    .MaximumLength(%A%.DescriptionMaxLength)
                    .WithMessage($"%An% %L% description cannot exceed {%A%.DescriptionMaxLength} characters.");
        %end%
            }
        }
        """;

    internal const string UpdateUseCase = """
        using %P%.Application.Core.Common.Concurrency;
        using %P%.Application.Core.Common.Ports;
        using %P%.Application.Core.Common.Results;
        using %P%.Application.Core.Common.Validation;
        using %P%.Application.Features.%F%.Model.Dtos;
        using %P%.Application.Features.%F%.Model.Mapping;
        using %P%.Application.Features.%F%.Services;
        using Mediator;

        namespace %P%.Application.Features.%F%.UseCases.Commands.Update%A%;

        public sealed record Update%A%Command(
            Guid %A%Id,
        %if fields%
        %FParamList%,
        %else%
            string Name,
            string Description,
        %end%
            VersionPrecondition? Precondition = null) : ICommand<Result<Versioned<%A%Dto>>>;

        public sealed class Update%A%CommandHandler(
            I%A%Service service,
            IUnitOfWork unitOfWork) : ICommandHandler<Update%A%Command, Result<Versioned<%A%Dto>>>
        {
            public async ValueTask<Result<Versioned<%A%Dto>>> Handle(
                Update%A%Command command,
                CancellationToken cancellationToken)
            {
                ArgumentNullException.ThrowIfNull(command);

                var loaded = await service.%Load%(command.%A%Id, command.Precondition, cancellationToken);

                if (loaded.IsFailure)
                {
                    return loaded.To<Versioned<%A%Dto>>();
                }

                var %L% = loaded.Value;

        %if fields%
                var update = DomainGuard.Try(() => %L%.Update(%FCommandArgs%));
        %else%
                var update = DomainGuard.Try(() => %L%.Update(command.Name, command.Description));
        %end%

                if (update.IsFailure)
                {
                    return update.To<Versioned<%A%Dto>>();
                }

                await unitOfWork.SaveChangesAsync(cancellationToken);

                return %A%DtoMapping.Detail(%L%);
            }
        }
        """;

    internal const string DeleteCommandValidator = """
        using FluentValidation;

        namespace %P%.Application.Features.%F%.UseCases.Commands.Delete%A%;

        public sealed class Delete%A%CommandValidator : AbstractValidator<Delete%A%Command>
        {
            public Delete%A%CommandValidator() =>
                RuleFor(command => command.%A%Id)
                    .NotEmpty().WithMessage("%An% %L% id is required.");
        }
        """;

    internal const string DeleteUseCase = """
        using %P%.Application.Core.Common.Concurrency;
        using %P%.Application.Core.Common.Ports;
        using %P%.Application.Core.Common.Results;
        using %P%.Application.Features.%F%.Services;
        using %P%.Domain.Features.%F%.Repositories;
        using Mediator;

        namespace %P%.Application.Features.%F%.UseCases.Commands.Delete%A%;

        public sealed record Delete%A%Command(Guid %A%Id, VersionPrecondition? Precondition = null) : ICommand<Result>;

        public sealed class Delete%A%CommandHandler(
            I%A%Service service,
            I%A%Repository repository,
            IUnitOfWork unitOfWork) : ICommandHandler<Delete%A%Command, Result>
        {
            public async ValueTask<Result> Handle(
                Delete%A%Command command,
                CancellationToken cancellationToken)
            {
                ArgumentNullException.ThrowIfNull(command);

                var loaded = await service.%Load%(command.%A%Id, command.Precondition, cancellationToken);

                if (loaded.IsFailure)
                {
                    return loaded;
                }

                repository.Remove(loaded.Value);
                await unitOfWork.SaveChangesAsync(cancellationToken);

                return Result.Success();
            }
        }
        """;

    internal const string RecordPlain = """
        namespace %P%.Infrastructure.Persistence.Features.%F%.Rows.Models;

        internal sealed class %A%Record
        {
            public Guid Id { get; set; }

        %if owner%
            public Guid OwnerId { get; set; }

        %end%
        %if fields%
        %FRecordProperties%
        %else%
            public string Name { get; set; } = string.Empty;

            public string Description { get; set; } = string.Empty;
        %end%

            public DateTimeOffset CreatedAt { get; set; }

            public DateTimeOffset? LastModifiedAt { get; set; }
        %if etag%

            public uint Version { get; set; }
        %end%
        }
        """;

    internal const string RecordAudited = """
        using %P%.Domain.Core.Common.Abstractions;

        namespace %P%.Infrastructure.Persistence.Features.%F%.Rows.Models;

        internal sealed class %A%Record : IAuditable
        {
            public Guid Id { get; set; }

        %if owner%
            public Guid OwnerId { get; set; }

        %end%
        %if fields%
        %FRecordProperties%
        %else%
            public string Name { get; set; } = string.Empty;

            public string Description { get; set; } = string.Empty;
        %end%

            public uint Version { get; set; }

            public DateTimeOffset CreatedAt { get; set; }

            public Guid? CreatedBy { get; set; }

            public DateTimeOffset? LastModifiedAt { get; set; }

            public Guid? LastModifiedBy { get; set; }

            void IAuditable.SetCreated(DateTimeOffset at, Guid? by)
            {
                CreatedAt = at;
                CreatedBy = by;
            }

            void IAuditable.SetLastModified(DateTimeOffset at, Guid? by)
            {
                LastModifiedAt = at;
                LastModifiedBy = by;
            }
        }
        """;

    internal const string Configuration = """
        %if rec%
        using %P%.Application.Features.%F%.Model.Policies; //?%A%InputPolicy
        %else%
        using %P%.Domain.Features.%F%.Model.Entities; //?%A%
        %end%
        using %P%.Infrastructure.Core.Common.Contexts; //?IsVersion
        using %P%.Infrastructure.Persistence.Common.Contexts;
        using %P%.Infrastructure.Persistence.Features.%F%.Rows.Models;
        using Microsoft.EntityFrameworkCore;
        using Microsoft.EntityFrameworkCore.Metadata.Builders;

        namespace %P%.Infrastructure.Persistence.Features.%F%.Rows.Configurations;

        internal sealed class %A%RecordConfiguration : IEntityTypeConfiguration<%A%Record>
        {
            public void Configure(EntityTypeBuilder<%A%Record> builder)
            {
                ArgumentNullException.ThrowIfNull(builder);

                builder.ToTable("%F%", AppDbContext.%F%Schema);

                builder.HasKey(record => record.Id);

                builder.Property(record => record.Id).ValueGeneratedNever();

        %if owner%
                builder.Property(record => record.OwnerId).IsRequired();

        %end%
        %if list%
        %if owner%
        %if fields%
        %FIndexes%
        %else%
                builder.HasIndex(record => new { record.OwnerId, record.Name, record.Id })
                    .HasDatabaseName("IX_%F%_OwnerId_Name_Id");
        %end%

                builder.HasIndex(record => new { record.OwnerId, record.CreatedAt, record.Id })
                    .HasDatabaseName("IX_%F%_OwnerId_CreatedAt_Id");
        %else%
        %if fields%
        %FIndexes%
        %else%
                builder.HasIndex(record => new { record.Name, record.Id })
                    .HasDatabaseName("IX_%F%_Name_Id");
        %end%

                builder.HasIndex(record => new { record.CreatedAt, record.Id })
                    .HasDatabaseName("IX_%F%_CreatedAt_Id");
        %end%

        %else%
        %if owner%
                builder.HasIndex(record => record.OwnerId)
                    .HasDatabaseName("IX_%F%_OwnerId");

        %end%
        %end%
        %if fields%
        %FConfiguration%
        %else%
                builder.Property(record => record.Name)
                    .HasMaxLength(%Limits%.NameMaxLength)
                    .IsRequired();

                builder.Property(record => record.Description)
                    .HasMaxLength(%Limits%.DescriptionMaxLength)
                    .IsRequired();
        %end%

                builder.Property(record => record.CreatedAt).IsRequired();
        %if agg%
                builder.Property(record => record.CreatedBy);
        %end%
                builder.Property(record => record.LastModifiedAt);
        %if agg%
                builder.Property(record => record.LastModifiedBy);
        %end%
        %if etag%

                builder.Property(record => record.Version).IsVersion();
        %end%
            }
        }
        """;

    internal const string Store = """
        using %P%.Application.Core.Common.Concurrency; //?VersionPrecondition
        using %P%.Application.Features.%F%.Model.Dtos; //?%A%ContentDto
        using %P%.Application.Features.%F%.Ports.%A%Store;
        using %P%.Domain.Core.Common.Primitives; //?UserId
        using %P%.Infrastructure.Persistence.Common.Contexts;
        using %P%.Infrastructure.Persistence.Features.%F%.Rows.Models;
        using Microsoft.EntityFrameworkCore; //?SingleOrDefaultAsync|ToListAsync

        namespace %P%.Infrastructure.Persistence.Features.%F%.Access.Stores;

        internal sealed class %A%Store(AppDbContext context) : I%A%Store
        {
        %if create%
            public void Add(
        %if owner%
                UserId ownerId,
        %end%
                Guid %L%Id,
                %A%ContentDto content,
                DateTimeOffset createdAt)
            {
        %if owner%
                ArgumentNullException.ThrowIfNull(ownerId);
        %end%
                ArgumentNullException.ThrowIfNull(content);

                context.%F%.Add(new %A%Record
                {
                    Id = %L%Id,
        %if owner%
                    OwnerId = ownerId.Value,
        %end%
        %if fields%
        %FStoreAssign%
        %else%
                    Name = content.Name,
                    Description = content.Description,
        %end%
                    CreatedAt = createdAt,
                });
            }

        %end%
        %if update%
            public async Task<%A%ChangeStatus> StageReplacementAsync(
        %if owner%
                UserId ownerId,
        %end%
                Guid %L%Id,
                %A%ContentDto content,
                DateTimeOffset modifiedAt,
        %if etag%
                VersionPrecondition? precondition,
        %end%
                CancellationToken cancellationToken = default)
            {
        %if owner%
                ArgumentNullException.ThrowIfNull(ownerId);
        %end%
                ArgumentNullException.ThrowIfNull(content);

                var (status, record) = await LoadForChangeAsync(
        %if owner%
                    ownerId,
        %end%
                    %L%Id,
        %if etag%
                    precondition,
        %end%
                    cancellationToken);

                if (record is not null)
                {
        %if fields%
        %FStoreUpdate%
        %else%
                    record.Name = content.Name;
                    record.Description = content.Description;
        %end%
                    record.LastModifiedAt = modifiedAt;
                }

                return status;
            }

        %end%
        %if delete%
            public async Task<%A%ChangeStatus> StageRemovalAsync(
        %if owner%
                UserId ownerId,
        %end%
                Guid %L%Id,
        %if etag%
                VersionPrecondition? precondition,
        %end%
                CancellationToken cancellationToken = default)
            {
        %if owner%
                ArgumentNullException.ThrowIfNull(ownerId);

        %end%
                var (status, record) = await LoadForChangeAsync(
        %if owner%
                    ownerId,
        %end%
                    %L%Id,
        %if etag%
                    precondition,
        %end%
                    cancellationToken);

                if (record is not null)
                {
                    context.%F%.Remove(record);
                }

                return status;
            }

        %end%
        %if change%
            private async Task<(%A%ChangeStatus Status, %A%Record? Record)> LoadForChangeAsync(
        %if owner%
                UserId ownerId,
        %end%
                Guid %L%Id,
        %if etag%
                VersionPrecondition? precondition,
        %end%
                CancellationToken cancellationToken)
            {
                var tracked = await context.%F%
                    .SingleOrDefaultAsync(record => record.Id == %L%Id%OwnerClause%, cancellationToken);

                if (tracked is null)
                {
                    return (%A%ChangeStatus.NotFound, null);
                }

        %if etag%
                return precondition is not null && !precondition.IsSatisfiedBy(tracked.Version)
                    ? (%A%ChangeStatus.VersionMismatch, null)
                    : (%A%ChangeStatus.Staged, tracked);
        %else%
                return (%A%ChangeStatus.Staged, tracked);
        %end%
            }
        %end%
        %if purge%

            public async Task<int> StageRemovalForOwnerAsync(
                UserId ownerId,
                CancellationToken cancellationToken = default)
            {
                ArgumentNullException.ThrowIfNull(ownerId);

                var records = await context.%F%
                    .Where(record => record.OwnerId == ownerId.Value)
                    .ToListAsync(cancellationToken);

                context.%F%.RemoveRange(records);

                return records.Count;
            }
        %end%
        %if anon%

            public async Task<int> StageAnonymisationForOwnerAsync(
                UserId ownerId,
                CancellationToken cancellationToken = default)
            {
                ArgumentNullException.ThrowIfNull(ownerId);

                var records = await context.%F%
                    .Where(record => record.OwnerId == ownerId.Value)
                    .ToListAsync(cancellationToken);

                foreach (var record in records)
                {
        %if fields%
        %FStoreAnonymise%
        %else%
                    record.Name = AnonymisedText;
                    record.Description = string.Empty;
        %end%
                }

                return records.Count;
            }
        %if anontext%

            private const string AnonymisedText = "[deleted account]";
        %end%
        %end%
        }
        """;

    internal const string MapperPort = """
        using %P%.Domain.Features.%F%.Model.Entities;
        using %P%.Infrastructure.Persistence.Features.%F%.Rows.Models;

        namespace %P%.Infrastructure.Persistence.Features.%F%.Rows.Mapping;

        internal interface I%A%Mapper
        {
            %A% ToAggregate(%A%Record record);

            %A%Record ToNewRecord(%A% aggregate);

            void WriteTo(%A% aggregate, %A%Record record);
        }
        """;

    internal const string Mapper = """
        using %P%.Domain.Core.Common.Primitives; //?UserId
        using %P%.Domain.Features.%F%.Model.Entities;
        using %P%.Infrastructure.Core.Common.Saving.Tracking;
        using %P%.Infrastructure.Persistence.Features.%F%.Rows.Models;

        namespace %P%.Infrastructure.Persistence.Features.%F%.Rows.Mapping;

        internal sealed class %A%Mapper : I%A%Mapper
        {
            public %A% ToAggregate(%A%Record record)
            {
                ArgumentNullException.ThrowIfNull(record);

                var aggregate = %A%.Rehydrate(
                    record.Id,
        %if owner%
                    UserId.Create(record.OwnerId),
        %end%
        %if fields%
        %FMapperRehydrate%
        %else%
                    record.Name,
                    record.Description);
        %end%

                StoredStamps.ApplyTo(aggregate, record, record.Version, record.Id, "%A%");

                return aggregate;
            }

            public %A%Record ToNewRecord(%A% aggregate)
            {
                ArgumentNullException.ThrowIfNull(aggregate);

                return new %A%Record
                {
                    Id = aggregate.Id,
        %if owner%
                    OwnerId = aggregate.OwnerId.Value,
        %end%
        %if fields%
        %FMapperNew%
        %else%
                    Name = aggregate.Name,
                    Description = aggregate.Description,
        %end%

                    Version = aggregate.Version,

                    CreatedAt = aggregate.CreatedAt,
                    CreatedBy = aggregate.CreatedBy,
                    LastModifiedAt = aggregate.LastModifiedAt,
                    LastModifiedBy = aggregate.LastModifiedBy,
                };
            }

            public void WriteTo(%A% aggregate, %A%Record record)
            {
                ArgumentNullException.ThrowIfNull(aggregate);
                ArgumentNullException.ThrowIfNull(record);

        %if fields%
        %FMapperWrite%
        %else%
                record.Name = aggregate.Name;
                record.Description = aggregate.Description;
        %end%
            }
        }
        """;

    internal const string TrackerPort = """
        using %P%.Domain.Features.%F%.Model.Entities;
        using %P%.Infrastructure.Core.Common.Saving.DomainEvents;
        using %P%.Infrastructure.Core.Common.Saving.Tracking;
        using %P%.Infrastructure.Persistence.Features.%F%.Rows.Models;

        namespace %P%.Infrastructure.Persistence.Features.%F%.Rows.Tracking;

        internal interface I%A%Tracker : IAggregateFlusher, IDomainEventSource
        {
            %A%? Find(Guid id);

            void Track(%A% aggregate, %A%Record record);

            %A%Record? FindRecord(Guid id);

            void MarkRemoved(%A% aggregate, %A%Record record);
        }
        """;

    internal const string Tracker = """
        using %P%.Domain.Features.%F%.Model.Entities;
        using %P%.Infrastructure.Core.Common.Saving.Tracking;
        using %P%.Infrastructure.Persistence.Features.%F%.Rows.Mapping;
        using %P%.Infrastructure.Persistence.Features.%F%.Rows.Models;
        using Microsoft.EntityFrameworkCore;

        namespace %P%.Infrastructure.Persistence.Features.%F%.Rows.Tracking;

        internal sealed class %A%Tracker(I%A%Mapper mapper)
            : AggregateTracker<%A%, %A%Record>(record => record.Version), I%A%Tracker
        {
            public override void FlushTo(DbContext context)
            {
                ArgumentNullException.ThrowIfNull(context);

                foreach (var tracked in TrackedAggregates)
                {
                    var entry = context.Entry(tracked.Record);

                    if (tracked.IsRemoved || entry.State is EntityState.Deleted or EntityState.Detached)
                    {
                        continue;
                    }

                    mapper.WriteTo(tracked.Aggregate, tracked.Record);

                    if (entry.State != EntityState.Added)
                    {
                        entry.Property(record => record.Version).OriginalValue = tracked.Aggregate.Version;
                    }
                }
            }
        }
        """;

    internal const string Repository = """
        using %P%.Domain.Core.Common.Primitives; //?UserId
        using %P%.Domain.Features.%F%.Model.Entities;
        using %P%.Domain.Features.%F%.Repositories;
        using %P%.Infrastructure.Persistence.Common.Contexts;
        using %P%.Infrastructure.Persistence.Features.%F%.Rows.Mapping;
        using %P%.Infrastructure.Persistence.Features.%F%.Rows.Models; //?%A%Record
        using %P%.Infrastructure.Persistence.Features.%F%.Rows.Tracking;
        using Microsoft.EntityFrameworkCore; //?FirstOrDefaultAsync|ToListAsync

        namespace %P%.Infrastructure.Persistence.Features.%F%.Access.Repositories;

        internal sealed class %A%Repository(
            AppDbContext context,
            I%A%Mapper mapper,
            I%A%Tracker tracker) : I%A%Repository
        {
        %if change%
        %if owner%
            public async Task<%A%?> GetAsync(UserId ownerId, Guid id, CancellationToken cancellationToken)
            {
                ArgumentNullException.ThrowIfNull(ownerId);

                if (tracker.Find(id) is { } alreadyLoaded)
                {
                    return alreadyLoaded.OwnerId == ownerId ? alreadyLoaded : null;
                }

                var record = await context.%F%.FirstOrDefaultAsync(
                    candidate => candidate.Id == id && candidate.OwnerId == ownerId.Value,
                    cancellationToken);

                return record is null ? null : LoadOrTrack(record);
            }
        %else%
            public async Task<%A%?> GetAsync(Guid id, CancellationToken cancellationToken)
            {
                if (tracker.Find(id) is { } alreadyLoaded)
                {
                    return alreadyLoaded;
                }

                var record = await context.%F%.FirstOrDefaultAsync(
                    candidate => candidate.Id == id,
                    cancellationToken);

                return record is null ? null : LoadOrTrack(record);
            }
        %end%

        %end%
        %if owner%
            public async Task<IReadOnlyList<%A%>> GetForOwnerAsync(
                UserId ownerId,
                CancellationToken cancellationToken)
            {
                ArgumentNullException.ThrowIfNull(ownerId);

                var records = await context.%F%
                    .Where(record => record.OwnerId == ownerId.Value)
                    .ToListAsync(cancellationToken);

                var aggregates = new List<%A%>(records.Count);

                foreach (var record in records)
                {
                    aggregates.Add(LoadOrTrack(record));
                }

                return aggregates;
            }

        %end%
        %if create%
            public void Add(%A% aggregate)
            {
                ArgumentNullException.ThrowIfNull(aggregate);

                var record = mapper.ToNewRecord(aggregate);

                context.%F%.Add(record);
                tracker.Track(aggregate, record);
            }

        %end%
        %if delete|owner%
            public void Remove(%A% aggregate)
            {
                ArgumentNullException.ThrowIfNull(aggregate);

                if (tracker.FindRecord(aggregate.Id) is not { } record)
                {
                    return;
                }

                context.%F%.Remove(record);
                tracker.MarkRemoved(aggregate, record);
            }

        %end%
        %if change|owner%
            private %A% LoadOrTrack(%A%Record record)
            {
                var aggregate = mapper.ToAggregate(record);

                tracker.Track(aggregate, record);

                return aggregate;
            }
        %end%
        }
        """;

    internal const string AccountDeletedConsumer = """
        using %P%.Application.Core.Common.Events; //?UserAccountDeleted
        using %P%.Application.Core.Common.Ports; //?IDeletedAccountDataPolicy|IUserAccountDirectory|IUnitOfWork
        using %P%.Domain.Core.Common.Primitives;
        using %P%.Application.Features.%F%.Ports.%A%Queries;
        %if rec%
        using %P%.Application.Features.%F%.Ports.%A%Store;
        %end%
        %if agg%
        using %P%.Domain.Features.%F%.Repositories;
        %end%

        namespace %P%.Application.Features.%F%.Services.Consumers;

        internal sealed class %Verb%%A%OnAccountDeletedConsumer(
        %if rec%
            I%A%Store store,
        %end%
        %if agg%
            I%A%Repository repository,
        %end%
            I%A%Queries queries,
            IUserAccountDirectory accounts,
            IUnitOfWork unitOfWork) : IDomainEventConsumer<UserAccountDeleted>, IDeletedAccountDataPolicy
        {
            public Task ConsumeAsync(UserAccountDeleted domainEvent, CancellationToken cancellationToken = default)
            {
                ArgumentNullException.ThrowIfNull(domainEvent);

                return ApplyAsync(domainEvent.UserId, cancellationToken);
            }

            public async Task<bool> ApplyAsync(UserId owner, CancellationToken cancellationToken = default)
            {
                ArgumentNullException.ThrowIfNull(owner);

                if (await accounts.ExistsAsync(owner, cancellationToken))
                {
                    return false;
                }

        %if purge%
        %if rec%
                int staged = await store.StageRemovalForOwnerAsync(owner, cancellationToken);

                if (staged == 0)
                {
                    return false;
                }
        %end%
        %if agg%
                var owned = await repository.GetForOwnerAsync(owner, cancellationToken);

                if (owned.Count == 0)
                {
                    return false;
                }

                foreach (var aggregate in owned)
                {
                    repository.Remove(aggregate);
                }
        %end%
        %end%
        %if anon%
        %if rec%
                int staged = await store.StageAnonymisationForOwnerAsync(owner, cancellationToken);

                if (staged == 0)
                {
                    return false;
                }
        %end%
        %if agg%
                var owned = await repository.GetForOwnerAsync(owner, cancellationToken);

                if (owned.Count == 0)
                {
                    return false;
                }

                foreach (var aggregate in owned)
                {
                    aggregate.Anonymise();
                }
        %end%
        %end%

                await unitOfWork.SaveChangesAsync(cancellationToken);

                return true;
            }

            public Task<IReadOnlyList<UserId>> ListDataOwnerIdsAsync(
                CancellationToken cancellationToken = default) =>
                queries.ListDataOwnerIdsAsync(cancellationToken);
        }
        """;

    internal const string DeletedAccountDataPolicy = """
        using %P%.Domain.Core.Common.Primitives;

        namespace %P%.Application.Core.Common.Ports;

        public interface IDeletedAccountDataPolicy
        {
            Task<bool> ApplyAsync(UserId owner, CancellationToken cancellationToken = default);

            Task<IReadOnlyList<UserId>> ListDataOwnerIdsAsync(CancellationToken cancellationToken = default);
        }
        """;

    internal const string UserAccountDirectory = """
        using %P%.Domain.Core.Common.Primitives;

        namespace %P%.Application.Core.Common.Ports;

        public interface IUserAccountDirectory
        {
            Task<bool> ExistsAsync(UserId userId, CancellationToken cancellationToken = default);
        }
        """;

    internal const string SweepUseCase = """
        using %P%.Application.Core.Common.Ports;
        using %P%.Application.Core.Common.Results;
        using Mediator;
        using Microsoft.Extensions.Logging;

        namespace %P%.Application.Core.Features.DeletedAccounts.UseCases.Commands.SweepDeletedAccountData;

        public sealed record SweepDeletedAccountDataCommand : ICommand<Result<int>>;

        public sealed class SweepDeletedAccountDataCommandHandler(
            IEnumerable<IDeletedAccountDataPolicy> policies,
            ILeaderLease lease,
            ILogger<SweepDeletedAccountDataCommandHandler> logger) : ICommandHandler<SweepDeletedAccountDataCommand, Result<int>>
        {
            private const string _leaseName = "%LowerP%.accounts.sweep-deleted-data";

            public async ValueTask<Result<int>> Handle(SweepDeletedAccountDataCommand command, CancellationToken cancellationToken)
            {
                int swept = 0;

                bool ranHere = await lease.TryRunExclusivelyAsync(
                    _leaseName,
                    async leaseToken => swept = await SweepAsync(leaseToken),
                    cancellationToken);

                if (!ranHere && logger.IsEnabled(LogLevel.Information))
                {
                    logger.LogInformation(
                        "Deleted-account data sweep skipped: another host holds the '{LeaseName}' lease.",
                        _leaseName);
                }

                return swept;
            }

            private async Task<int> SweepAsync(CancellationToken cancellationToken)
            {
                int swept = 0;

                foreach (var policy in policies)
                {
                    var owners = await policy.ListDataOwnerIdsAsync(cancellationToken);

                    foreach (var owner in owners)
                    {
                        try
                        {
                            if (await policy.ApplyAsync(owner, cancellationToken))
                            {
                                swept++;
                            }
                        }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (Exception exception)
                        {
                            logger.LogError(
                                exception,
                                "Sweeping the data of deleted account {Owner} through {Policy} failed; "
                                + "the owner is skipped and retried at the next pass.",
                                owner.Value,
                                policy.GetType().Name);
                        }
                    }
                }

                return swept;
            }
        }
        """;

    internal const string DeletedAccountSweepJobs = """
        using %J%.Features.Maintenance.Jobs;
        using %P%.Application.Core.Features.DeletedAccounts.UseCases.Commands.SweepDeletedAccountData;
        using Microsoft.Extensions.Options;

        namespace %J%.Features.BackgroundJobs.Jobs;

        internal static class DeletedAccountSweepJobsExtensions
        {
            internal static IServiceCollection AddDeletedAccountSweepJobs(
                this IServiceCollection services,
                IConfiguration configuration)
            {
                ArgumentNullException.ThrowIfNull(services);
                ArgumentNullException.ThrowIfNull(configuration);

                services.AddOptions<DeletedAccountSweepOptions>()
                    .Bind(configuration.GetSection(JobsMaintenanceOptions.SectionName));

                services.AddMaintenanceTask<SweepDeletedAccountDataCommand>(
                    "data of deleted accounts",
                    "Rows owned by an id that names no account stay where they are: the deletion event is "
                    + "the fast path, and this sweep is what makes it certain.",
                    provider => provider.GetRequiredService<IOptions<DeletedAccountSweepOptions>>()
                        .Value.SweepDeletedAccountsDataEnabled);

                return services;
            }
        }
        """;

    internal const string DeletedAccountSweepOptions = """
        namespace %J%.Features.BackgroundJobs.Jobs;

        internal sealed class DeletedAccountSweepOptions
        {
            public bool SweepDeletedAccountsDataEnabled { get; set; } = true;
        }
        """;

    internal const string LocalUserAccountDirectory = """
        using %P%.Application.Auth.Features.Accounts.Ports.Accounts;
        using %P%.Application.Core.Common.Ports;
        using %P%.Domain.Core.Common.Primitives;

        namespace %H%.Features.Auth.Composition;

        internal sealed class AccountUserAccountDirectory(IAccountsService accounts) : IUserAccountDirectory
        {
            public async Task<bool> ExistsAsync(UserId userId, CancellationToken cancellationToken = default)
            {
                ArgumentNullException.ThrowIfNull(userId);

                return await accounts.FindProfileAsync(userId.Value, cancellationToken) is not null;
            }
        }
        """;

    internal const string ExternalUserAccountDirectory = """
        using %P%.Application.Core.Common.Ports;
        using %P%.Application.Features.ExternalIdentity.Services;
        using %P%.Domain.Core.Common.Primitives;

        namespace %H%.Features.ExternalIdentity.Composition;

        internal sealed class ExternalIdentityUserAccountDirectory(
            IExternalIdentityService externalIdentities) : IUserAccountDirectory
        {
            public async Task<bool> ExistsAsync(UserId userId, CancellationToken cancellationToken = default)
            {
                ArgumentNullException.ThrowIfNull(userId);

                return await externalIdentities.FindProfileAsync(userId, cancellationToken) is not null;
            }
        }
        """;

    internal const string Queries = """
        using System.Linq.Expressions; //?Expression
        using %P%.Application.Core.Common.Collections; //?FeaturePageRequest
        using %P%.Application.Core.Common.Concurrency; //?Versioned
        using %P%.Application.Core.Common.Results; //?PagedResult
        using %P%.Application.Features.%F%.Model.Dtos; //?%A%SummaryDto|%A%Dto
        using %P%.Application.Features.%F%.Ports.%A%Queries;
        using %P%.Domain.Core.Common.Primitives; //?UserId
        using %P%.Infrastructure.Persistence.Common.Contexts;
        using %P%.Infrastructure.Persistence.Features.%F%.Rows.Models; //?%A%Record
        using Microsoft.EntityFrameworkCore;

        namespace %P%.Infrastructure.Persistence.Features.%F%.Access.Queries;

        internal sealed class %A%Queries(AppDbContext context) : I%A%Queries
        {
        %if list%
            public async Task<PagedResult<%A%SummaryDto>> %ListQuery%(
        %if owner%
                UserId ownerId,
        %end%
                FeaturePageRequest<%A%Filter> request,
                CancellationToken cancellationToken = default)
            {
        %if owner%
                ArgumentNullException.ThrowIfNull(ownerId);
        %end%
                ArgumentNullException.ThrowIfNull(request);

                IQueryable<%A%Record> scoped = context.%F%.AsNoTracking();
        %if owner%
                scoped = scoped.Where(record => record.OwnerId == ownerId.Value);
        %end%

                return request.Paging.Mode == PagingMode.Offset
                    ? await GetOffsetPageAsync(scoped, request, cancellationToken)
                    : await GetKeysetPageAsync(scoped, request, cancellationToken);
            }

        %end%
        %if single%
            public Task<%ReadDto%?> GetAsync(
        %if owner%
                UserId ownerId,
        %end%
                Guid %L%Id,
                CancellationToken cancellationToken = default)
            {
        %if owner%
                ArgumentNullException.ThrowIfNull(ownerId);

        %end%
                return context.%F%
                    .AsNoTracking()
                    .Where(record => record.Id == %L%Id%OwnerClause%)
        %if etag%
                    .Select(record => new Versioned<%A%Dto>(
        %if fields%
                        new %A%Dto(record.Id, %FRecordArgs%, record.CreatedAt, record.LastModifiedAt),
        %else%
                        new %A%Dto(record.Id, record.Name, record.Description, record.CreatedAt, record.LastModifiedAt),
        %end%
                        record.Version))
        %else%
                    .Select(record => new %A%Dto(
                        record.Id,
        %if fields%
        %FRecordArgLines%
        %else%
                        record.Name,
                        record.Description,
        %end%
                        record.CreatedAt,
                        record.LastModifiedAt))
        %end%
                    .FirstOrDefaultAsync(cancellationToken);
            }

        %end%
        %if owner%
            public async Task<IReadOnlyList<UserId>> ListDataOwnerIdsAsync(
                CancellationToken cancellationToken = default)
            {
                var ids = await context.%F%
                    .AsNoTracking()
                    .Select(record => record.OwnerId)
                    .Distinct()
                    .ToListAsync(cancellationToken);

                return [.. ids.Select(UserId.Create)];
            }

        %end%
        %if list%
            private static async Task<PagedResult<%A%SummaryDto>> GetOffsetPageAsync(
                IQueryable<%A%Record> scoped,
                FeaturePageRequest<%A%Filter> request,
                CancellationToken cancellationToken)
            {
                int totalCount = await scoped.CountAsync(cancellationToken);

                int page = request.Paging.Page!.Value;
                int pageSize = request.Paging.PageSize;

                var items = await %A%SortMap.ApplyOrder(scoped, request.Sort)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .Select(_toSummary)
                    .ToListAsync(cancellationToken);

                return PagedResult.Offset(items, page, pageSize, totalCount);
            }

            private static async Task<PagedResult<%A%SummaryDto>> GetKeysetPageAsync(
                IQueryable<%A%Record> scoped,
                FeaturePageRequest<%A%Filter> request,
                CancellationToken cancellationToken)
            {
                var term = request.Sort.Terms[0];
                int pageSize = request.Paging.PageSize;

                var keysetSource = request.Paging.Cursor is { } cursor
                    ? %A%SortMap.ApplyKeyset(scoped, term, cursor)
                    : scoped;

                var items = await %A%SortMap.ApplyOrder(keysetSource, request.Sort)
                    .Take(pageSize + 1)
                    .Select(_toSummary)
                    .ToListAsync(cancellationToken);

                bool hasNext = items.Count > pageSize;
                var page = hasNext ? items.GetRange(0, pageSize) : items;

                string? nextCursor = null;

                if (hasNext)
                {
                    var last = page[^1];

                    nextCursor = Cursor.After(term, %A%SortMap.KeyOf(last, term.Field), last.Id).Encode();
                }

                return PagedResult.Keyset(page, pageSize, nextCursor);
            }

            private static readonly Expression<Func<%A%Record, %A%SummaryDto>> _toSummary =
        %if fields%
                record => new %A%SummaryDto(record.Id, %FSummaryArgs%record.CreatedAt, record.LastModifiedAt);
        %else%
                record => new %A%SummaryDto(record.Id, record.Name, record.CreatedAt, record.LastModifiedAt);
        %end%
        %end%
        }
        """;

    internal const string SortMap = """
        using System.Globalization;
        using %P%.Application.Core.Common.Collections;
        using %P%.Application.Features.%F%.Model.Dtos;
        using %P%.Application.Features.%F%.Model.Policies;
        using %P%.Infrastructure.Persistence.Features.%F%.Rows.Models;

        namespace %P%.Infrastructure.Persistence.Features.%F%.Access.Queries;

        internal static class %A%SortMap
        {
            public static IOrderedQueryable<%A%Record> ApplyOrder(IQueryable<%A%Record> source, SortOrder sort)
            {
                ArgumentNullException.ThrowIfNull(source);
                ArgumentNullException.ThrowIfNull(sort);

                IOrderedQueryable<%A%Record>? ordered = null;

                foreach (var term in sort.Terms)
                {
                    ordered = ordered is null ? OrderByTerm(source, term) : ThenByTerm(ordered, term);
                }

                return sort.Terms[^1].Direction == SortDirection.Ascending
                    ? ordered!.ThenBy(record => record.Id)
                    : ordered!.ThenByDescending(record => record.Id);
            }

            public static IQueryable<%A%Record> ApplyKeyset(IQueryable<%A%Record> source, SortTerm term, Cursor cursor)
            {
                ArgumentNullException.ThrowIfNull(source);
                ArgumentNullException.ThrowIfNull(term);
                ArgumentNullException.ThrowIfNull(cursor);

                return term.Field switch
                {
                    %A%CollectionPolicy.NameField => ApplyNameKeyset(source, term.Direction, cursor),
                    %A%CollectionPolicy.CreatedAtField => ApplyCreatedAtKeyset(source, term.Direction, cursor),
                    _ => throw new InvalidOperationException(
                        $"'{term.Field}' has no keyset comparison: the policy should have rejected it first."),
                };
            }

            public static string KeyOf(%A%SummaryDto summary, string field)
            {
                ArgumentNullException.ThrowIfNull(summary);

                return field switch
                {
                    %A%CollectionPolicy.NameField => summary.Name,
                    %A%CollectionPolicy.CreatedAtField => summary.CreatedAt.ToString("O", CultureInfo.InvariantCulture),
                    _ => throw new InvalidOperationException(
                        $"'{field}' has no cursor key: the policy should have rejected it first."),
                };
            }

            private static IOrderedQueryable<%A%Record> OrderByTerm(IQueryable<%A%Record> source, SortTerm term) =>
                term.Field switch
                {
                    %A%CollectionPolicy.NameField => term.Direction == SortDirection.Ascending
                        ? source.OrderBy(record => record.Name)
                        : source.OrderByDescending(record => record.Name),
                    %A%CollectionPolicy.CreatedAtField => term.Direction == SortDirection.Ascending
                        ? source.OrderBy(record => record.CreatedAt)
                        : source.OrderByDescending(record => record.CreatedAt),
                    _ => throw new InvalidOperationException(
                        $"'{term.Field}' is not a sortable field: the policy should have rejected it first."),
                };

            private static IOrderedQueryable<%A%Record> ThenByTerm(IOrderedQueryable<%A%Record> source, SortTerm term) =>
                term.Field switch
                {
                    %A%CollectionPolicy.NameField => term.Direction == SortDirection.Ascending
                        ? source.ThenBy(record => record.Name)
                        : source.ThenByDescending(record => record.Name),
                    %A%CollectionPolicy.CreatedAtField => term.Direction == SortDirection.Ascending
                        ? source.ThenBy(record => record.CreatedAt)
                        : source.ThenByDescending(record => record.CreatedAt),
                    _ => throw new InvalidOperationException(
                        $"'{term.Field}' is not a sortable field: the policy should have rejected it first."),
                };

            private static IQueryable<%A%Record> ApplyNameKeyset(
                IQueryable<%A%Record> source,
                SortDirection direction,
                Cursor cursor) =>
                direction == SortDirection.Ascending
                    ? source.Where(record =>
                        record.Name.CompareTo(cursor.Key) > 0
                        || (record.Name == cursor.Key && record.Id.CompareTo(cursor.Id) > 0))
                    : source.Where(record =>
                        record.Name.CompareTo(cursor.Key) < 0
                        || (record.Name == cursor.Key && record.Id.CompareTo(cursor.Id) < 0));

            private static IQueryable<%A%Record> ApplyCreatedAtKeyset(
                IQueryable<%A%Record> source,
                SortDirection direction,
                Cursor cursor)
            {
                if (!DateTimeOffset.TryParse(
                    cursor.Key,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out DateTimeOffset parsed))
                {
                    throw new InvalidOperationException(
                        "The cursor's key is not a valid date/time: CursorKeys should have rejected it first.");
                }

                var key = parsed.ToUniversalTime();

                return direction == SortDirection.Ascending
                    ? source.Where(record =>
                        record.CreatedAt.CompareTo(key) > 0
                        || (record.CreatedAt == key && record.Id.CompareTo(cursor.Id) > 0))
                    : source.Where(record =>
                        record.CreatedAt.CompareTo(key) < 0
                        || (record.CreatedAt == key && record.Id.CompareTo(cursor.Id) < 0));
            }
        }
        """;

    internal const string CreateRequest = """
        using System.Text.Json.Serialization; //?JsonRequired

        namespace %P%.Api.Features.%F%.Contracts.Requests;

        %if owner%
        /// <summary>The %L% to create, whose owner is the authenticated caller.</summary>
        %else%
        /// <summary>The %L% to create.</summary>
        %end%
        %if fields%
        %FRequestDocs%
        public sealed record Create%A%Request(
        %FRequestParams%);
        %else%
        /// <param name="Name">Required, at most 200 characters once trimmed.</param>
        /// <param name="Description">Required but may be empty, at most 2000 characters.</param>
        public sealed record Create%A%Request(string Name, string Description);
        %end%
        """;

    internal const string UpdateRequest = """
        using System.Text.Json.Serialization; //?JsonRequired

        namespace %P%.Api.Features.%F%.Contracts.Requests;

        /// <summary>The new %Revised% of %a% %L%, whose id travels in the route.</summary>
        %if fields%
        %FRequestDocs%
        public sealed record Update%A%Request(
        %FRequestParams%);
        %else%
        /// <param name="Name">Required, at most 200 characters once trimmed.</param>
        /// <param name="Description">Required but may be empty, at most 2000 characters.</param>
        public sealed record Update%A%Request(string Name, string Description);
        %end%
        """;

    internal const string ListRequest = """
        namespace %P%.Api.Features.%F%.Contracts.Requests;

        /// <summary>The paging and sorting of a list, bound from the query string.</summary>
        /// <param name="Paging"><c>offset</c> (the default) or <c>cursor</c>.</param>
        /// <param name="Page">The page number, in offset mode.</param>
        /// <param name="PageSize">The number of items per page.</param>
        /// <param name="Cursor">The previous page's <c>nextCursor</c>, in cursor mode.</param>
        /// <param name="Sort">Comma-separated fields (%SortDoc%), each optionally suffixed <c>:asc</c> or <c>:desc</c>; at most one in cursor mode.</param>
        public sealed record Get%F%Request(
            string? Paging,
            int? Page,
            int? PageSize,
            string? Cursor,
            string? Sort);
        """;

    internal const string Response = """
        namespace %P%.Api.Features.%F%.Contracts.Responses;

        %if etag%
        /// <summary>The wire shape of one %L%, whose version travels in the <c>ETag</c> header.</summary>
        %else%
        /// <summary>The wire shape of one %L%.</summary>
        %end%
        public sealed record %A%Response(
            Guid Id,
        %if fields%
        %FDtoParams%
        %else%
            string Name,
            string Description,
        %end%
            DateTimeOffset CreatedAt,
            DateTimeOffset? LastModifiedAt);
        """;

    internal const string SummaryResponse = """
        namespace %P%.Api.Features.%F%.Contracts.Responses;

        /// <summary>The wire shape of one %L% inside a page%SummaryDoc%.</summary>
        public sealed record %A%SummaryResponse(
            Guid Id,
        %if fields%
        %FSummaryParams%
        %else%
            string Name,
        %end%
            DateTimeOffset CreatedAt,
            DateTimeOffset? LastModifiedAt);
        """;

    internal const string ResponseMapping = """
        using %P%.Api.Common.Contracts; //?PagedResponse
        using %P%.Api.Features.%F%.Contracts.Responses;
        using %P%.Application.Core.Common.Concurrency; //?Versioned
        using %P%.Application.Core.Common.Results;
        using %P%.Application.Features.%F%.Model.Dtos;

        namespace %P%.Api.Features.%F%.Contracts.Mapping;

        internal static class %A%ResponseMapping
        {
        %if dto%
            public static %A%Response ToResponse(%A%Dto %L%)
            {
                ArgumentNullException.ThrowIfNull(%L%);

        %if fields%
                return new %A%Response(%L%.Id, %FResponseArgs%%L%.CreatedAt, %L%.LastModifiedAt);
        %else%
                return new %A%Response(%L%.Id, %L%.Name, %L%.Description, %L%.CreatedAt, %L%.LastModifiedAt);
        %end%
            }

        %end%
        %if list%
            public static %A%SummaryResponse ToResponse(%A%SummaryDto summary)
            {
                ArgumentNullException.ThrowIfNull(summary);

        %if fields%
                return new %A%SummaryResponse(summary.Id, %FSummaryResponseArgs%summary.CreatedAt, summary.LastModifiedAt);
        %else%
                return new %A%SummaryResponse(summary.Id, summary.Name, summary.CreatedAt, summary.LastModifiedAt);
        %end%
            }

            public static Result<PagedResponse<%A%SummaryResponse>> ToPageResponse(
                Result<PagedResult<%A%SummaryDto>> result) =>
                result.Map(value => PagedResponse.From(value, ToResponse));

        %end%
        %if dto%
        %if etag%
            public static Result<%ReadResponse%> To%A%Response(Result<%ReadDto%> result) =>
                result.Map(value => new Versioned<%A%Response>(ToResponse(value.Value), value.Version));
        %else%
            public static Result<%ReadResponse%> To%A%Response(Result<%ReadDto%> result) =>
                result.Map(ToResponse);
        %end%
        %end%
        }
        """;

    internal const string Controller = """
        using %P%.Api.Common.Contracts; //?PagedResponse
        using %P%.Api.Common.Controllers;
        using %P%.Api.Common.Errors; //?ToActionResult
        using %P%.Api.Common.Idempotency; //?Idempotent
        using %P%.Api.Features.%F%.Contracts.Mapping; //?%A%ResponseMapping
        using %P%.Api.Features.%F%.Contracts.Requests; //?Create%A%Request|Update%A%Request|Get%F%Request
        using %P%.Api.Features.%F%.Contracts.Responses; //?%A%Response|%A%SummaryResponse
        %if rec%
        using %P%.Application.Features.%F%.Model.Dtos; //?%A%ContentDto|%A%ListQueryDto
        using %P%.Application.Features.%F%.Services;
        %else%
        %if create%
        using %P%.Application.Features.%F%.UseCases.Commands.Create%A%;
        %end%
        %if update%
        using %P%.Application.Features.%F%.UseCases.Commands.Update%A%;
        %end%
        %if delete%
        using %P%.Application.Features.%F%.UseCases.Commands.Delete%A%;
        %end%
        %if get%
        using %P%.Application.Features.%F%.UseCases.Queries.Get%A%;
        %end%
        %if list%
        using %P%.Application.Features.%F%.UseCases.Queries.Get%F%;
        %end%
        using Mediator;
        %end%
        using Microsoft.AspNetCore.Mvc;

        namespace %P%.Api.Features.%F%.Controllers;

        [Route("api/v{version:apiVersion}/%R%")]
        [Asp.Versioning.ApiVersion("1.0")]
        [ProducesResponseType(StatusCodes.Status401Unauthorized, Type = typeof(ProblemDetails))]
        public sealed class %F%Controller(
            %CtorParams%) : ApiControllerBase
        {
        %if list%
            /// <summary>Lists %Scope%%F%, sorted and paginated%ListTail%.</summary>
            [HttpGet]
            [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(PagedResponse<%A%SummaryResponse>))]
            [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ValidationProblemDetails))]
            public async Task<ActionResult<PagedResponse<%A%SummaryResponse>>> GetAll(
                [FromQuery] Get%F%Request request,
                CancellationToken cancellationToken = default)
            {
                ArgumentNullException.ThrowIfNull(request);

                var query = new %ListInput%(request.Paging, request.Page, request.PageSize, request.Cursor, request.Sort);

                return OkOrProblem(%A%ResponseMapping.ToPageResponse(await %CallList%));
            }

        %end%
        %if get%
        %if etag%
            /// <summary>Gets one %L%, and the <c>ETag</c> needed to change it.</summary>
        %else%
            /// <summary>Gets one %L%.</summary>
        %end%
            [HttpGet("{%L%Id:guid}", Name = nameof(Get%A%))]
            [HttpHead("{%L%Id:guid}")]
            [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(%A%Response))]
        %if etag%
            [ProducesResponseType(StatusCodes.Status304NotModified)]
        %end%
            [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(ProblemDetails))]
            public async Task<ActionResult<%A%Response>> Get%A%(Guid %L%Id, CancellationToken cancellationToken) =>
                OkOrProblem(%A%ResponseMapping.To%A%Response(await %CallGet%));

        %end%
        %if create%
            /// <summary>Creates %a% %L%; an <c>Idempotency-Key</c> header makes a retried request safe.</summary>
            [HttpPost]
            [Idempotent]
            [ProducesResponseType(StatusCodes.Status201Created, Type = typeof(%A%Response))]
            [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ValidationProblemDetails))]
            [ProducesResponseType(StatusCodes.Status409Conflict, Type = typeof(ProblemDetails))]
            public async Task<ActionResult<%A%Response>> Create(
                [FromBody] Create%A%Request request,
                CancellationToken cancellationToken)
            {
                ArgumentNullException.ThrowIfNull(request);

                var outcome = await %CallCreate%;
                var result = %A%ResponseMapping.To%A%Response(outcome);

        %if etag%
                return CreatedOrProblem(result, nameof(Get%A%), created => new { %L%Id = created.Id });
        %else%
                if (result.IsFailure)
                {
                    return result.Error!.ToActionResult(HttpContext);
                }

                return CreatedOrProblem(result, nameof(Get%A%), new { %L%Id = result.Value.Id });
        %end%
            }

        %end%
        %if update%
        %if etag%
            /// <summary>Replaces %a% %L%'s %Replaced%, on the version an <c>If-Match</c> names.</summary>
        %else%
            /// <summary>Replaces %a% %L%'s %Replaced%; of two concurrent writers the later one wins.</summary>
        %end%
            [HttpPut("{%L%Id:guid}")]
            [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(%A%Response))]
            [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ValidationProblemDetails))]
            [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(ProblemDetails))]
        %if etag%
            [ProducesResponseType(StatusCodes.Status409Conflict, Type = typeof(ProblemDetails))]
            [ProducesResponseType(StatusCodes.Status412PreconditionFailed, Type = typeof(ProblemDetails))]
            [ProducesResponseType(StatusCodes.Status428PreconditionRequired, Type = typeof(ProblemDetails))]
        %end%
            public async Task<ActionResult<%A%Response>> Update(
                Guid %L%Id,
                [FromBody] Update%A%Request request,
                CancellationToken cancellationToken)
            {
                ArgumentNullException.ThrowIfNull(request);

        %if etag%
                if (ReadPrecondition(out var precondition, out bool requiresExistence) is { } refusal)
                {
                    return refusal;
                }

                var outcome = await %CallUpdate%;
                var result = RequiringExistence(requiresExistence, outcome);

                return UpdatedOrProblem(%A%ResponseMapping.To%A%Response(result));
        %else%
                var outcome = await %CallUpdate%;

                return OkOrProblem(%A%ResponseMapping.To%A%Response(outcome));
        %end%
            }

        %end%
        %if delete%
        %if update&etag%
            /// <summary>Deletes %a% %L%, on the same <c>If-Match</c> terms as the update.</summary>
        %else%
            /// <summary>Deletes %a% %L%.</summary>
        %end%
            [HttpDelete("{%L%Id:guid}")]
            [ProducesResponseType(StatusCodes.Status204NoContent)]
        %if etag%
            [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ValidationProblemDetails))]
        %end%
            [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(ProblemDetails))]
        %if etag%
            [ProducesResponseType(StatusCodes.Status409Conflict, Type = typeof(ProblemDetails))]
            [ProducesResponseType(StatusCodes.Status412PreconditionFailed, Type = typeof(ProblemDetails))]
            [ProducesResponseType(StatusCodes.Status428PreconditionRequired, Type = typeof(ProblemDetails))]
        %end%
            public async Task<ActionResult> Delete(Guid %L%Id, CancellationToken cancellationToken)
            {
        %if etag%
                if (ReadPrecondition(out var precondition, out bool requiresExistence) is { } refusal)
                {
                    return refusal;
                }

                var outcome = await %CallDelete%;

                return NoContentOrProblem(RequiringExistence(requiresExistence, outcome));
        %else%
                return NoContentOrProblem(await %CallDelete%);
        %end%
            }
        %end%
        }
        """;

    internal const string ContentValidatorTests = """
        using %P%.Application.Features.%F%.Model.Dtos;
        using %P%.Application.Features.%F%.Model.Policies;
        using Shouldly;
        using Xunit;

        namespace %P%.Application.UnitTests.Features.%F%.Model.Dtos;

        public sealed class %A%ContentDtoValidatorTests
        {
            private static readonly %A%ContentDtoValidator _validator = new();

            private static CancellationToken TestToken => TestContext.Current.CancellationToken;

            [Fact]
            public async Task ANameAndADescription_AreAccepted() =>
                (await _validator.ValidateAsync(new %A%ContentDto("Groceries", "Milk"), TestToken)).IsValid.ShouldBeTrue();

            [Fact]
            public async Task AnEmptyDescription_IsAccepted() =>
                (await _validator.ValidateAsync(new %A%ContentDto("Groceries", string.Empty), TestToken)).IsValid.ShouldBeTrue();

            [Theory]
            [InlineData("")]
            [InlineData("   ")]
            public async Task ABlankName_IsRejected(string name) =>
                (await _validator.ValidateAsync(new %A%ContentDto(name, "Milk"), TestToken)).IsValid.ShouldBeFalse();

            [Fact]
            public async Task ANullName_IsRejected_NotDereferenced() =>
                (await _validator.ValidateAsync(new %A%ContentDto(null!, "Milk"), TestToken)).IsValid.ShouldBeFalse();

            [Fact]
            public async Task ANullDescription_IsRejected() =>
                (await _validator.ValidateAsync(new %A%ContentDto("Groceries", null!), TestToken)).IsValid.ShouldBeFalse();

            [Fact]
            public async Task ANameMeasuredAfterTrimming_MayFillTheLimitExactly()
            {
                string name = new('a', %Limits%.NameMaxLength);

                (await _validator.ValidateAsync(new %A%ContentDto($"  {name}  ", "Milk"), TestToken)).IsValid.ShouldBeTrue();
                (await _validator.ValidateAsync(new %A%ContentDto(name + "a", "Milk"), TestToken)).IsValid.ShouldBeFalse();
            }
        }
        """;

    internal const string CommandValidatorTests = """
        using %P%.Application.Features.%F%.UseCases.Commands.%Op%%A%;
        using Shouldly;
        using Xunit;

        namespace %P%.Application.UnitTests.Features.%F%.UseCases.Commands.%Op%%A%;

        public sealed class %Op%%A%CommandValidatorTests
        {
            private static readonly %Op%%A%CommandValidator _validator = new();

            private static CancellationToken TestToken => TestContext.Current.CancellationToken;

            private static %Op%%A%Command ACommand(string? name, string? description) => %CommandNew%;

            [Fact]
            public async Task ANameAndADescription_AreAccepted() =>
                (await _validator.ValidateAsync(ACommand("Groceries", "Milk"), TestToken)).IsValid.ShouldBeTrue();

            [Fact]
            public async Task AnEmptyDescription_IsAccepted() =>
                (await _validator.ValidateAsync(ACommand("Groceries", string.Empty), TestToken)).IsValid.ShouldBeTrue();

            [Theory]
            [InlineData("")]
            [InlineData("   ")]
            [InlineData(null)]
            public async Task ABlankOrMissingName_IsRejected(string? name) =>
                (await _validator.ValidateAsync(ACommand(name, "Milk"), TestToken)).IsValid.ShouldBeFalse();

            [Fact]
            public async Task AMissingDescription_IsRejected() =>
                (await _validator.ValidateAsync(ACommand("Groceries", null), TestToken)).IsValid.ShouldBeFalse();
        }
        """;

    internal const string ServiceTests = """
        using %P%.Application.Core.Common.Collections; //?FeaturePageRequest
        using %P%.Application.Core.Common.Concurrency; //?Versioned|VersionPrecondition
        using %P%.Application.Core.Common.Ports;
        using %P%.Application.Core.Common.Results;
        using %P%.Application.Features.%F%.Model.Dtos; //?%A%ContentDto|%A%Dto|%A%ListQueryDto
        using %P%.Application.Features.%F%.Ports.%A%Queries; //?I%A%Queries|%A%Filter
        using %P%.Application.Features.%F%.Ports.%A%Store; //?I%A%Store|%A%ChangeStatus
        using %P%.Application.Features.%F%.Services;
        using %P%.Application.Features.%F%.Model.Dtos; //?%A%ContentDtoValidator
        using %P%.Application.Features.%F%.Model.Policies; //?%A%InputPolicy
        using %P%.Application.UnitTests.TestDoubles;
        using %P%.Domain.Core.Common.Primitives;
        using NSubstitute;
        using Shouldly;
        using Xunit;

        namespace %P%.Application.UnitTests.Features.%F%.Services;

        public sealed class %A%ServiceTests
        {
            private static readonly UserId _callerId = UserId.Create(Guid.CreateVersion7());

        %if rq%
            private readonly I%A%Queries _queries = Substitute.For<I%A%Queries>();
        %end%
        %if wq%
            private readonly I%A%Store _store = Substitute.For<I%A%Store>();
            private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
        %end%

            private static CancellationToken TestToken => TestContext.Current.CancellationToken;

        %if content&fields%
            private static %A%ContentDto AValidContent => %FValidContent%;

        %end%
        %if content&!fields%
            private static %A%ContentDto AValidContent => new("Groceries", "Milk, eggs");

        %end%
            [Fact]
            public async Task AnAnonymousCaller_IsRefusedByEveryOperation()
            {
                var service = ServiceFor(StubCurrentUser.Anonymous);
        %if get|update|delete%
                var %L%Id = Guid.CreateVersion7();
        %end%

                Result[] results =
                [
        %if list%
                    await service.ListAsync(%ListInput%.Offset(null, null), TestToken),
        %end%
        %if get%
                    await service.GetAsync(%L%Id, TestToken),
        %end%
        %if create%
                    await service.CreateAsync(AValidContent, TestToken),
        %end%
        %if update%
                    await service.UpdateAsync(%L%Id, AValidContent%NullPrecondition%, TestToken),
        %end%
        %if delete%
                    await service.DeleteAsync(%L%Id%NullPrecondition%, TestToken),
        %end%
                ];

                results.ShouldAllBe(result => result.IsFailure);
                results.ShouldAllBe(result => result.Error!.Type == ErrorType.Unauthorized && result.Error.Code == "auth.required");
            }

            [Fact]
            public async Task AnAnonymousCaller_ReadsAndWritesNothing()
            {
                var service = ServiceFor(StubCurrentUser.Anonymous);
        %if get|update|delete%
                var %L%Id = Guid.CreateVersion7();
        %end%

        %if list%
                await service.ListAsync(%ListInput%.Offset(null, null), TestToken);
        %end%
        %if get%
                await service.GetAsync(%L%Id, TestToken);
        %end%
        %if create%
                await service.CreateAsync(AValidContent, TestToken);
        %end%
        %if update%
                await service.UpdateAsync(%L%Id, AValidContent%NullPrecondition%, TestToken);
        %end%
        %if delete%
                await service.DeleteAsync(%L%Id%NullPrecondition%, TestToken);
        %end%

        %if rq%
                _queries.ReceivedCalls().ShouldBeEmpty();
        %end%
        %if wq%
                _store.ReceivedCalls().ShouldBeEmpty();
                await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        %end%
            }

        %if get%
            [Fact]
            public async Task GetAsync_%An%%A%ThatIsNotThere_IsNotFound()
            {
                _queries.GetAsync(%AnyOwner%Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                    .Returns((%ReadDto%?)null);

                var result = await Service().GetAsync(Guid.CreateVersion7(), TestToken);

                result.Error!.Type.ShouldBe(ErrorType.NotFound);
                result.Error.Code.ShouldBe("%L%.notFound");
            }

        %end%
        %if create&fields%
        %FServiceInvalid%

        %end%
        %if create&!fields%
            [Theory]
            [InlineData("", "description")]
            [InlineData("   ", "description")]
            [InlineData(null, "description")]
            [InlineData("name", null)]
            public async Task CreateAsync_RejectsInvalidContent_AndStagesNothing(string? name, string? description)
            {
                var result = await Service().CreateAsync(new %A%ContentDto(name!, description!), TestToken);

                result.Error!.Type.ShouldBe(ErrorType.Validation);
                _store.ReceivedCalls().ShouldBeEmpty();
                await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
            }

        %end%
        %if delete%
            [Fact]
            public async Task DeleteAsync_%An%%A%ThatCannotBeStaged_FailsAndCommitsNothing()
            {
                _store.StageRemovalAsync(%AnyOwner%Arg.Any<Guid>(), %AnyPrecondition%Arg.Any<CancellationToken>())
                    .Returns(%A%ChangeStatus.NotFound);

                var result = await Service().DeleteAsync(Guid.CreateVersion7()%NullPrecondition%, TestToken);

                result.Error!.Code.ShouldBe("%L%.notFound");
                await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
            }

        %end%
            private %A%Service ServiceFor(ICurrentUser currentUser) =>
                new(%ServiceArgs%);
        %if get|create|delete%

            private %A%Service Service() => ServiceFor(StubCurrentUser.WithId(_callerId));
        %end%
        }
        """;

    internal const string AggregateTests = """
        using %P%.Domain.Core.Common.Exceptions;
        using %P%.Domain.Core.Common.Primitives; //?UserId
        using %P%.Domain.Features.%F%.Model.Entities;
        using Shouldly;
        using Xunit;

        namespace %P%.Domain.UnitTests.Features.%F%.Model.Entities;

        public sealed class %A%Tests
        {
        %if owner%
            private static readonly UserId _ownerId = UserId.Create(Guid.CreateVersion7());

        %end%
        %if create%
            [Fact]
            public void Create_KeepsTheTrimmedNameAndTheDescription()
            {
                var %L% = %A%.Create(%OwnerIdArg%"  Groceries  ", "Milk, eggs");

                %L%.Id.ShouldNotBe(Guid.Empty);
                %L%.Name.ShouldBe("Groceries");
                %L%.Description.ShouldBe("Milk, eggs");
        %if owner%
                %L%.OwnerId.ShouldBe(_ownerId);
        %end%
            }

            [Theory]
            [InlineData("")]
            [InlineData("   ")]
            public void Create_RefusesABlankName(string name) =>
                Should.Throw<DomainException>(() => %A%.Create(%OwnerIdArg%name, "Milk, eggs"));

            [Fact]
            public void Create_RefusesANameOverTheLimit() =>
                Should.Throw<DomainException>(
                    () => %A%.Create(%OwnerIdArg%new string('a', %A%.NameMaxLength + 1), string.Empty));

        %end%
        %if update%
            [Fact]
            public void Update_ReplacesTheNameAndTheDescription()
            {
                var %L% = %A%.Rehydrate(Guid.CreateVersion7(), %OwnerIdArg%"Groceries", "Milk");

                %L%.Update("  Errands  ", "Parcel");

                %L%.Name.ShouldBe("Errands");
                %L%.Description.ShouldBe("Parcel");
            }

            [Fact]
            public void Update_RefusesABlankName()
            {
                var %L% = %A%.Rehydrate(Guid.CreateVersion7(), %OwnerIdArg%"Groceries", "Milk");

                Should.Throw<DomainException>(() => %L%.Update("   ", "Parcel"));
                %L%.Name.ShouldBe("Groceries");
            }

            [Fact]
            public void Update_RefusesAMissingDescription_WithoutChangingTheName()
            {
                var %L% = %A%.Rehydrate(Guid.CreateVersion7(), %OwnerIdArg%"Groceries", "Milk");

                Should.Throw<DomainException>(() => %L%.Update("Errands", null!));
                %L%.Name.ShouldBe("Groceries");
                %L%.Description.ShouldBe("Milk");
            }

        %end%
            [Fact]
            public void Rehydrate_KeepsWhatWasStored_EvenOverTheCurrentLimit()
            {
                string stored = new('a', %A%.NameMaxLength + 1);

                var %L% = %A%.Rehydrate(Guid.CreateVersion7(), %OwnerIdArg%stored, "Milk");

                %L%.Name.ShouldBe(stored);
            }

            [Fact]
            public void Rehydrate_RefusesAnEmptyId() =>
                Should.Throw<DomainException>(() => %A%.Rehydrate(Guid.Empty, %OwnerIdArg%"Groceries", "Milk"));
        }
        """;

    internal const string ConfigurationTests = """
        using %P%.Application.Features.%F%.Model.Policies; //?%A%InputPolicy|%A%CollectionPolicy
        %if agg%
        using %P%.Domain.Features.%F%.Model.Entities; //?%A%
        %end%
        using %P%.Infrastructure.Core.Common.Contexts;
        using %P%.Infrastructure.Persistence.Common.Contexts;
        using %P%.Infrastructure.Persistence.Features.%F%.Rows.Models;
        using Microsoft.EntityFrameworkCore;
        using Microsoft.EntityFrameworkCore.Metadata;
        using Shouldly;
        using Xunit;

        namespace %P%.Infrastructure.Persistence.UnitTests.Features.%F%.Rows.Configurations;

        public sealed class %A%RecordConfigurationTests
        {
        %if etag%
            [Fact]
            public void TheVersion_IsTheDatabasesOwnConcurrencyToken()
            {
                var version = Entity().FindProperty(nameof(%A%Record.Version))!;

                version.IsConcurrencyToken.ShouldBeTrue();
                version.GetColumnName().ShouldBe(DatabaseProvider.VersionColumnName);
            }
        %else%
            [Fact]
            public void NoPropertyIsAConcurrencyToken() =>
                Entity().GetProperties().ShouldAllBe(property => !property.IsConcurrencyToken);
        %end%

        %if fields%
        %FConfigurationTests%
        %else%
            [Theory]
            [InlineData(nameof(%A%Record.Name), %Limits%.NameMaxLength)]
            [InlineData(nameof(%A%Record.Description), %Limits%.DescriptionMaxLength)]
            public void EveryTextColumn_IsAsLongAsTheValidatorAllows(string property, int expected) =>
                Entity().FindProperty(property)!.GetMaxLength().ShouldBe(expected);
        %end%

        %if list%
            [Fact]
            public void EverySortIndex_ServesASortableField_AndEndsWithTheId()
            {
                var indexes = Entity().GetIndexes().ToList();

                indexes.Count.ShouldBe(%A%CollectionPolicy.Instance.SortableFields.Count);

                foreach (var index in indexes)
                {
                    var names = index.Properties.Select(property => property.Name).ToList();

        %if owner%
                    names[0].ShouldBe(nameof(%A%Record.OwnerId));
        %end%
                    names[^1].ShouldBe(nameof(%A%Record.Id));
                }
            }

        %end%
            [Fact]
        %if prefixed%
            public void TheTable_IsPrefixedWithThe%F%Schema()
            {
                Entity().GetTableName().ShouldBe($"{AppDbContext.%F%Schema}_%F%");
                Entity().GetSchema().ShouldBeNull();
            }
        %else%
            public void TheTable_LivesInItsOwnSchema()
            {
                Entity().GetTableName().ShouldBe("%F%");
                Entity().GetSchema().ShouldBe(AppDbContext.%F%Schema);
            }
        %end%

            private static IEntityType Entity()
            {
                using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                    .UseDatabase(DatabaseProvider.DesignTimeConnectionString)
                    .Options);

                return context.Model.FindEntityType(typeof(%A%Record))!;
            }
        }
        """;

    internal const string MapperFidelityTests = """
        using %P%.Domain.Core.Common.Abstractions;
        using %P%.Domain.Core.Common.Primitives; //?UserId
        using %P%.Domain.Features.%F%.Model.Entities;
        using %P%.Infrastructure.Persistence.Features.%F%.Rows.Mapping;
        using Shouldly;
        using Xunit;

        namespace %P%.Infrastructure.Persistence.UnitTests.Features.%F%;

        public sealed class %A%MapperFidelityTests
        {
            [Fact]
            public void EveryPieceOfState_SurvivesTheRoundTrip()
            {
                var createdAt = new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);
                var modifiedAt = new DateTimeOffset(2026, 3, 5, 8, 9, 10, TimeSpan.Zero);
                var createdBy = new Guid("11111111-2222-3333-4444-555555555555");
                var modifiedBy = new Guid("66666666-7777-8888-9999-aaaaaaaaaaaa");
        %if owner%
                var ownerId = UserId.Create(new Guid("4b7f1d92-4c8a-4f4b-9a1e-0d2f3c4b5a60"));
        %end%

                var original = %A%.Rehydrate(
                    new Guid("0199a3c4-1111-7000-8000-000000000001"),
        %if owner%
                    ownerId,
        %end%
                    "Groceries",
                    "Milk, eggs");

                ((IVersioned)original).SetVersion(987_654u);
                ((IAuditable)original).SetCreated(createdAt, createdBy);
                ((IAuditable)original).SetLastModified(modifiedAt, modifiedBy);

                var mapper = new %A%Mapper();

                var roundTripped = mapper.ToAggregate(mapper.ToNewRecord(original));

                roundTripped.Id.ShouldBe(original.Id);
        %if owner%
                roundTripped.OwnerId.ShouldBe(ownerId);
        %end%
                roundTripped.Name.ShouldBe("Groceries");
                roundTripped.Description.ShouldBe("Milk, eggs");
                roundTripped.Version.ShouldBe(987_654u);
                roundTripped.CreatedAt.ShouldBe(createdAt);
                roundTripped.CreatedBy.ShouldBe(createdBy);
                roundTripped.LastModifiedAt.ShouldBe(modifiedAt);
                roundTripped.LastModifiedBy.ShouldBe(modifiedBy);
            }
        }
        """;


    internal const string FieldContentValidatorTests = """
        using %P%.Application.Features.%F%.Model.Dtos;
        using %P%.Application.Features.%F%.Model.Policies; //?%A%InputPolicy
        using Shouldly;
        using Xunit;

        namespace %P%.Application.UnitTests.Features.%F%.Model.Dtos;

        public sealed class %A%ContentDtoValidatorTests
        {
            private static readonly %A%ContentDtoValidator _validator = new();

            private static CancellationToken TestToken => TestContext.Current.CancellationToken;

        %FContentValidatorTests%

            private static %A%ContentDto AValidContentDto() => %FValidContent%;
        }
        """;

    internal const string FieldCommandValidatorTests = """
        using %P%.Application.Features.%F%.UseCases.Commands.%Op%%A%;
        using %P%.Domain.Features.%F%.Model.Entities; //?%A%
        using Shouldly;
        using Xunit;

        namespace %P%.Application.UnitTests.Features.%F%.UseCases.Commands.%Op%%A%;

        public sealed class %Op%%A%CommandValidatorTests
        {
            private static readonly %Op%%A%CommandValidator _validator = new();

            private static CancellationToken TestToken => TestContext.Current.CancellationToken;

        %FValidatorTests%

            private static %Op%%A%Command AValidCommand() => %FValidCommand%;
        }
        """;

    internal const string FieldAggregateTests = """
        using %P%.Domain.Core.Common.Exceptions;
        using %P%.Domain.Core.Common.Primitives; //?UserId
        using %P%.Domain.Features.%F%.Model.Entities;
        using Shouldly;
        using Xunit;

        namespace %P%.Domain.UnitTests.Features.%F%.Model.Entities;

        public sealed class %A%Tests
        {
        %if owner%
            private static readonly UserId _ownerId = UserId.Create(Guid.CreateVersion7());

        %end%
        %FAggregateTests%
        }
        """;

    internal const string FieldMapperFidelityTests = """
        using %P%.Domain.Core.Common.Abstractions;
        using %P%.Domain.Core.Common.Primitives; //?UserId
        using %P%.Domain.Features.%F%.Model.Entities;
        using %P%.Infrastructure.Persistence.Features.%F%.Rows.Mapping;
        using Shouldly;
        using Xunit;

        namespace %P%.Infrastructure.Persistence.UnitTests.Features.%F%;

        public sealed class %A%MapperFidelityTests
        {
            [Fact]
            public void EveryPieceOfState_SurvivesTheRoundTrip()
            {
                var createdAt = new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);
                var modifiedAt = new DateTimeOffset(2026, 3, 5, 8, 9, 10, TimeSpan.Zero);
                var createdBy = new Guid("11111111-2222-3333-4444-555555555555");
                var modifiedBy = new Guid("66666666-7777-8888-9999-aaaaaaaaaaaa");
        %if owner%
                var ownerId = UserId.Create(new Guid("4b7f1d92-4c8a-4f4b-9a1e-0d2f3c4b5a60"));
        %end%

                var original = %A%.Rehydrate(
                    new Guid("0199a3c4-1111-7000-8000-000000000001"),
        %if owner%
                    ownerId,
        %end%
        %FFidelityArgs%

                ((IVersioned)original).SetVersion(987_654u);
                ((IAuditable)original).SetCreated(createdAt, createdBy);
                ((IAuditable)original).SetLastModified(modifiedAt, modifiedBy);

                var mapper = new %A%Mapper();

                var roundTripped = mapper.ToAggregate(mapper.ToNewRecord(original));

                roundTripped.Id.ShouldBe(original.Id);
        %if owner%
                roundTripped.OwnerId.ShouldBe(ownerId);
        %end%
        %FFidelityAsserts%
                roundTripped.Version.ShouldBe(987_654u);
                roundTripped.CreatedAt.ShouldBe(createdAt);
                roundTripped.CreatedBy.ShouldBe(createdBy);
                roundTripped.LastModifiedAt.ShouldBe(modifiedAt);
                roundTripped.LastModifiedBy.ShouldBe(modifiedBy);
            }
        }
        """;

    internal const string FieldApiTests = """
        using System.Net;
        using System.Net.Http.Json;
        using %P%.Api.Common.Contracts; //?PagedResponse
        using %P%.Api.Features.%F%.Contracts.Requests;
        using %P%.Api.Features.%F%.Contracts.Responses;
        using %P%.Api.IntegrationTests.Infrastructure;
        using %P%.Application.Core.Common.Collections; //?Cursor
        using %P%.Application.Features.%F%.Model.Policies; //?%A%CollectionPolicy
        using Shouldly;
        using Xunit;

        namespace %P%.Api.IntegrationTests.Features.%F%;

        public sealed class %A%FieldTests(ApiFixture fixture) : IntegrationTestBase(fixture)
        {
            private const string _route = "/api/v1/%R%";

            [Fact]
            public async Task %An%%A%_IsCreatedWithEveryField_AndAnswersThemBack()
            {
                var (client, _, _) = await SignInAsync();

                using var response = await client.PostAsJsonAsync(_route, AValidRequest(), TestToken);

                response.StatusCode.ShouldBe(HttpStatusCode.Created);

                var created = await ApiJson.ReadAsync<%A%Response>(response, TestToken);

        %FResponseAsserts%
            }

        %FApiInstants%

        %FApiRefusal%

        %if list%
            [Fact]
            public async Task ACursorKey_SentWithANonZeroOffset_ResumesAfterTheSameInstant()
            {
                var (client, _, _) = await SignInAsync();
                var first = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

                Clock.AdjustTime(first);
                var firstId = await PostedIdAsync(client);

                Clock.AdjustTime(first.AddDays(1));
                var secondId = await PostedIdAsync(client);

                var term = SortOrder.Parse(%A%CollectionPolicy.CreatedAtField, %A%CollectionPolicy.Instance).Value.Terms[0];
                string cursor = Cursor.After(term, first.ToOffset(TimeSpan.FromHours(2)).ToString("O"), firstId).Encode();

                using var response = await client.GetAsync(
                    new Uri($"{_route}?paging=cursor&sort=createdAt:asc&pageSize=5&cursor={Uri.EscapeDataString(cursor)}", UriKind.Relative),
                    TestToken);

                response.StatusCode.ShouldBe(HttpStatusCode.OK);
                (await ApiJson.ReadAsync<PagedResponse<%A%SummaryResponse>>(response, TestToken)).Items.Select(item => item.Id).ShouldBe([secondId]);
            }

            private static async Task<Guid> PostedIdAsync(HttpClient client)
            {
                using var response = await client.PostAsJsonAsync(_route, AValidRequest(), TestToken);

                response.StatusCode.ShouldBe(HttpStatusCode.Created);

                return (await ApiJson.ReadAsync<%A%Response>(response, TestToken)).Id;
            }

        %end%

            private static Create%A%Request AValidRequest() => %FRequestSample%;
        }
        """;

    internal const string FieldSortMap = """
        using System.Globalization;
        using %P%.Application.Core.Common.Collections;
        using %P%.Application.Features.%F%.Model.Dtos;
        using %P%.Application.Features.%F%.Model.Policies;
        using %P%.Infrastructure.Persistence.Features.%F%.Rows.Models;

        namespace %P%.Infrastructure.Persistence.Features.%F%.Access.Queries;

        internal static class %A%SortMap
        {
            public static IOrderedQueryable<%A%Record> ApplyOrder(IQueryable<%A%Record> source, SortOrder sort)
            {
                ArgumentNullException.ThrowIfNull(source);
                ArgumentNullException.ThrowIfNull(sort);

                IOrderedQueryable<%A%Record>? ordered = null;

                foreach (var term in sort.Terms)
                {
                    ordered = ordered is null ? OrderByTerm(source, term) : ThenByTerm(ordered, term);
                }

                return sort.Terms[^1].Direction == SortDirection.Ascending
                    ? ordered!.ThenBy(record => record.Id)
                    : ordered!.ThenByDescending(record => record.Id);
            }

            public static IQueryable<%A%Record> ApplyKeyset(IQueryable<%A%Record> source, SortTerm term, Cursor cursor)
            {
                ArgumentNullException.ThrowIfNull(source);
                ArgumentNullException.ThrowIfNull(term);
                ArgumentNullException.ThrowIfNull(cursor);

                return term.Field switch
                {
        %FKeysetCases%
                    %A%CollectionPolicy.CreatedAtField => ApplyCreatedAtKeyset(source, term.Direction, cursor),
                    _ => throw new InvalidOperationException(
                        $"'{term.Field}' has no keyset comparison: the policy should have rejected it first."),
                };
            }

            public static string KeyOf(%A%SummaryDto summary, string field)
            {
                ArgumentNullException.ThrowIfNull(summary);

                return field switch
                {
        %FKeyCases%
                    %A%CollectionPolicy.CreatedAtField => summary.CreatedAt.ToString("O", CultureInfo.InvariantCulture),
                    _ => throw new InvalidOperationException(
                        $"'{field}' has no cursor key: the policy should have rejected it first."),
                };
            }

            private static IOrderedQueryable<%A%Record> OrderByTerm(IQueryable<%A%Record> source, SortTerm term) =>
                term.Field switch
                {
        %FOrderCases%
                    %A%CollectionPolicy.CreatedAtField => term.Direction == SortDirection.Ascending
                        ? source.OrderBy(record => record.CreatedAt)
                        : source.OrderByDescending(record => record.CreatedAt),
                    _ => throw new InvalidOperationException(
                        $"'{term.Field}' is not a sortable field: the policy should have rejected it first."),
                };

            private static IOrderedQueryable<%A%Record> ThenByTerm(IOrderedQueryable<%A%Record> source, SortTerm term) =>
                term.Field switch
                {
        %FThenCases%
                    %A%CollectionPolicy.CreatedAtField => term.Direction == SortDirection.Ascending
                        ? source.ThenBy(record => record.CreatedAt)
                        : source.ThenByDescending(record => record.CreatedAt),
                    _ => throw new InvalidOperationException(
                        $"'{term.Field}' is not a sortable field: the policy should have rejected it first."),
                };

        %FKeysetMethods%

            private static IQueryable<%A%Record> ApplyCreatedAtKeyset(
                IQueryable<%A%Record> source,
                SortDirection direction,
                Cursor cursor)
            {
                if (!DateTimeOffset.TryParse(
                    cursor.Key,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out DateTimeOffset parsed))
                {
                    throw new InvalidOperationException(
                        "The cursor's key is not a valid date/time: CursorKeys should have rejected it first.");
                }

                var key = parsed.ToUniversalTime();

                return direction == SortDirection.Ascending
                    ? source.Where(record =>
                        record.CreatedAt.CompareTo(key) > 0
                        || (record.CreatedAt == key && record.Id.CompareTo(cursor.Id) > 0))
                    : source.Where(record =>
                        record.CreatedAt.CompareTo(key) < 0
                        || (record.CreatedAt == key && record.Id.CompareTo(cursor.Id) < 0));
            }
        }
        """;
}
