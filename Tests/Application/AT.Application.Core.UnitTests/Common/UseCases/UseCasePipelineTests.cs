using System.Diagnostics;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.UseCases;
using FluentValidation;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.Core.UnitTests.Common.UseCases;

public sealed class UseCasePipelineTests
{
    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AMessageThatBreaksItsValidator_NeverReachesItsHandler()
    {
        using var pipeline = new Pipeline();

        var result = await pipeline.Send(new ProbeCommand(""));

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("request.validationFailed");
        result.Error.Details!.Keys.ShouldBe(["name"]);
        pipeline.Ledger.Reached.ShouldBeEmpty(
            "A validator registered for a message is the gate to its handler: nothing the handler " +
            "does may happen for a message that breaks it.");
    }

    [Fact]
    public async Task AFailedValidation_AnswersWithTheResponseTypeTheHandlerDeclares()
    {
        using var pipeline = new Pipeline();

        var result = await pipeline.Send(new ProbeValueCommand(-1));

        result.ShouldBeOfType<Result<int>>().Error!.Code.ShouldBe("request.validationFailed");
        pipeline.Ledger.Reached.ShouldBeEmpty();
    }

    [Fact]
    public async Task AMessageThatKeepsItsValidator_ReachesItsHandler()
    {
        using var pipeline = new Pipeline();

        var result = await pipeline.Send(new ProbeCommand("kept"));

        result.IsSuccess.ShouldBeTrue();
        pipeline.Ledger.Reached.ShouldBe([nameof(ProbeCommand)]);
    }

    [Fact]
    public async Task AMessageWithoutAValidator_ReachesItsHandler()
    {
        using var pipeline = new Pipeline();

        var result = await pipeline.Send(new UnvalidatedProbeQuery());

        result.Value.ShouldBe(42);
        pipeline.Ledger.Reached.ShouldBe([nameof(UnvalidatedProbeQuery)]);
    }

    [Fact]
    public async Task EveryMessage_IsTracedUnderItsOwnName_WithItsOutcome()
    {
        var stopped = new List<Activity>();

        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == UseCaseInstruments.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = stopped.Add,
        };

        ActivitySource.AddActivityListener(listener);

        using var pipeline = new Pipeline();

        await pipeline.Send(new ProbeCommand(""));
        await pipeline.Send(new UnvalidatedProbeQuery());

        var failed = stopped.Single(activity => activity.OperationName == nameof(ProbeCommand));
        failed.GetTagItem(UseCaseInstruments.OutcomeTag).ShouldBe("failure");
        failed.GetTagItem(UseCaseInstruments.ErrorCodeTag).ShouldBe("request.validationFailed");

        stopped.Single(activity => activity.OperationName == nameof(UnvalidatedProbeQuery))
            .GetTagItem(UseCaseInstruments.OutcomeTag).ShouldBe("success");
    }

    [Fact]
    public async Task AFailedUseCase_IsLoggedWithItsCode_AndNothingOfItsMessage()
    {
        using var pipeline = new Pipeline();

        await pipeline.Send(new ProbeCommand(""));

        var entry = pipeline.Log.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Information);
        entry.Category.ShouldBe(UseCaseInstruments.Name);
        entry.Message.ShouldContain(nameof(ProbeCommand));
        entry.Message.ShouldContain("request.validationFailed");
    }

    [Fact]
    public void ThePipeline_IsRegisteredOnce_HoweverManyFeaturesAskForIt()
    {
        var services = new ServiceCollection();

        services.AddUseCasePipeline();
        services.AddUseCasePipeline();

        services
            .Where(descriptor => descriptor.ServiceType == typeof(IPipelineBehavior<,>))
            .Select(descriptor => descriptor.ImplementationType)
            .ShouldBe(
                [typeof(TracingBehaviour<,>), typeof(LoggingBehaviour<,>), typeof(ValidationBehaviour<,>)],
                "The trace opens first so that the log and the validation run inside it, and the " +
                "validation runs last so that nothing between it and the handler can be skipped.");
    }

    private sealed class Pipeline : IDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly IServiceScope _scope;

        public Pipeline()
        {
            var services = new ServiceCollection();

            services.AddMediator(options =>
            {
                options.ServiceLifetime = ServiceLifetime.Scoped;
                options.GenerateTypesAsInternal = true;
                options.Assemblies = [typeof(UseCasePipelineTests)];
            });

            services.AddSingleton(Ledger);
            services.AddSingleton<ILoggerFactory>(Log);
            services.AddScoped<IValidator<ProbeCommand>, ProbeCommandValidator>();
            services.AddScoped<IValidator<ProbeValueCommand>, ProbeValueCommandValidator>();
            services.AddUseCasePipeline();

            _provider = services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true,
            });

            _scope = _provider.CreateScope();
        }

        public ProbeLedger Ledger { get; } = new();

        public RecordingLoggerFactory Log { get; } = new();

        public ValueTask<TResponse> Send<TResponse>(ICommand<TResponse> command) =>
            _scope.ServiceProvider.GetRequiredService<ISender>().Send(command, TestToken);

        public ValueTask<TResponse> Send<TResponse>(IQuery<TResponse> query) =>
            _scope.ServiceProvider.GetRequiredService<ISender>().Send(query, TestToken);

        public void Dispose()
        {
            _scope.Dispose();
            _provider.Dispose();
        }
    }
}

internal sealed record ProbeCommand(string Name) : ICommand<Result>;

internal sealed class ProbeCommandHandler(ProbeLedger ledger) : ICommandHandler<ProbeCommand, Result>
{
    public ValueTask<Result> Handle(ProbeCommand command, CancellationToken cancellationToken)
    {
        ledger.Reached.Add(nameof(ProbeCommand));

        return ValueTask.FromResult(Result.Success());
    }
}

internal sealed class ProbeCommandValidator : AbstractValidator<ProbeCommand>
{
    public ProbeCommandValidator() => RuleFor(command => command.Name).NotEmpty();
}

internal sealed record ProbeValueCommand(int Value) : ICommand<Result<int>>;

internal sealed class ProbeValueCommandHandler(ProbeLedger ledger) : ICommandHandler<ProbeValueCommand, Result<int>>
{
    public ValueTask<Result<int>> Handle(ProbeValueCommand command, CancellationToken cancellationToken)
    {
        ledger.Reached.Add(nameof(ProbeValueCommand));

        return ValueTask.FromResult(Result.Success(command.Value));
    }
}

internal sealed class ProbeValueCommandValidator : AbstractValidator<ProbeValueCommand>
{
    public ProbeValueCommandValidator() => RuleFor(command => command.Value).GreaterThanOrEqualTo(0);
}

internal sealed record UnvalidatedProbeQuery : IQuery<Result<int>>;

internal sealed class UnvalidatedProbeQueryHandler(ProbeLedger ledger) : IQueryHandler<UnvalidatedProbeQuery, Result<int>>
{
    public ValueTask<Result<int>> Handle(UnvalidatedProbeQuery query, CancellationToken cancellationToken)
    {
        ledger.Reached.Add(nameof(UnvalidatedProbeQuery));

        return ValueTask.FromResult(Result.Success(42));
    }
}

internal sealed class ProbeLedger
{
    public List<string> Reached { get; } = [];
}

internal sealed record LogEntry(string Category, LogLevel Level, string Message);

internal sealed class RecordingLoggerFactory : ILoggerFactory
{
    public List<LogEntry> Entries { get; } = [];

    public ILogger CreateLogger(string categoryName) => new RecordingLogger(categoryName, Entries);

    public void AddProvider(ILoggerProvider provider)
    {
    }

    public void Dispose()
    {
    }

    private sealed class RecordingLogger(string category, List<LogEntry> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            entries.Add(new LogEntry(category, logLevel, formatter(state, exception)));
    }
}
