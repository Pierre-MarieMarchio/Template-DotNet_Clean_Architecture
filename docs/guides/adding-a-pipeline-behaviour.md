---
type: guide
title: Adding a pipeline behaviour
description: How to add a behaviour that runs around every use case, at a place in the pipeline a test holds.
---

# Adding a pipeline behaviour

## When

Something has to happen around every use case, whichever host or job sends its message: a measure,
a guard, a check before the handler runs. At the end, a behaviour of the use-case pipeline wraps each
message it applies to, at a place in the order that a test holds.

A behaviour wraps messages only. What belongs to one feature stays in its handler; a reaction to a
fact is a domain event's consumer, which no behaviour reaches
([ADR 0003](../decisions/0003-use-cases-are-mediator-messages-and-ports-are-explicit.md)).
<!--#if (api)-->
What belongs to HTTP, a header, a status or a route's precondition, belongs to the
[request pipeline](../architecture/request-pipeline.md) of the API.
<!--#endif-->
<!--#if (!api)-->
What belongs to a host's transport, a header, a cookie or a tool's answer, belongs to that host.
<!--#endif-->

## The behaviours the pipeline runs

`AddUseCasePipeline` in `src/Application/AT.Application.Core/ApplicationCoreModule.cs` registers
the behaviours once. Each registration that brings use cases calls it, a feature's as much as the
platform's own, so every host runs them without an edit of its own. The order of registration is
the order of nesting: the first registered wraps all the others.

| Order | Behaviour | Why there |
|---|---|---|
| 1 | `TracingBehaviour` | the activity and the duration cover everything below, a refusal included |
| 2 | `LoggingBehaviour` | it logs the failed `Result` any later step answers |
| 3 | `ValidationBehaviour` | last, so nothing between it and the handler can be skipped for a message its validator refuses |

[Use cases are messages](../architecture/domain-and-results.md#use-cases-are-messages) says what each
one does.

## Steps

1. Write `<Name>Behaviour<TMessage, TResponse>` in `src/Application/AT.Application.Core/Common/UseCases/`,
   beside the three, a public sealed class implementing `IPipelineBehavior<TMessage, TResponse>`.
   `AppTemplate.Application.Core` references no feature, so the behaviour cannot name one.

   ```csharp
   public sealed class <Name>Behaviour<TMessage, TResponse> : IPipelineBehavior<TMessage, TResponse>
       where TMessage : notnull, IMessage
   {
       public async ValueTask<TResponse> Handle(
           TMessage message,
           MessageHandlerDelegate<TMessage, TResponse> next,
           CancellationToken cancellationToken)
       {
           ArgumentNullException.ThrowIfNull(next);

           return await next(message, cancellationToken);
       }
   }
   ```

2. Narrow the messages it wraps with its constraints, as `ValidationBehaviour` does: the container
   leaves a behaviour out of a message's pipeline when the message does not meet them.
   `where TMessage : notnull, ICommand<TResponse>` wraps the commands alone, and
   `where TResponse : IFailable<TResponse>` the messages whose answer is a `Result`.
3. Refuse with a value, never an exception: under `IFailable<TResponse>`, a refusal answers
   `TResponse.Failure(<error>)` and does not call `next`; an exception stays for a bug, as
   [domain and results](../architecture/domain-and-results.md#result-for-expected-failures-exceptions-for-bugs)
   says. The error's code is declared once, in `CommonErrors`, since the behaviour
   knows no feature.
<!--#if (api)-->
   A new code takes its row in [the API reference](../reference/api.md#error-codes).
<!--#endif-->
4. Register it in `AddUseCasePipeline`, with `TryAddEnumerable` as the three are, at its place:
   before `ValidationBehaviour` in most cases, so that the validation stays the last step; after it
   only when the behaviour must see nothing but valid messages, and then the reason in the order test
   changes with it.
5. Make its dependencies resolvable wherever the pipeline is composed: a dependency is a port of
   `AppTemplate.Application.Core` that every host registers, or a service `AddUseCasePipeline`
   registers itself. `ContainerCompositionTests` composes the hosts under strict validation and names
   a dependency nobody registers.
6. Record a measure on `UseCaseInstruments`, whose activity source and meter every host already
   exports: `ObservabilityRegistrationTests` holds that registration, and a meter of its own would
   need one in each host. A new instrument takes its row in
   [observability](../features/observability.md).
7. Test it in `tests/Application/AT.Application.Core.UnitTests/Common/UseCases/UseCasePipelineTests.cs`.
   Its `Pipeline` sends probe messages through a generated mediator and the real registration:
   register there what the behaviour depends on, then prove what it does with a message it applies to
   and a message it leaves alone. `ThePipeline_IsRegisteredOnce_HoweverManyFeaturesAskForIt` lists
   the order and says why it is so; it fails until the new behaviour is in its list. Break the
   behaviour once and watch its test go red.
8. Add its row to the table of [use cases are messages](../architecture/domain-and-results.md#use-cases-are-messages)
   and to the table above. Write the project ADR that records it, numbered from 1000 as
   [changing an inherited choice](../decisions/README.md#changing-an-inherited-choice) says: it names
   the behaviour, its place in the order, and what it adds to
   [ADR 0003](../decisions/0003-use-cases-are-mediator-messages-and-ports-are-explicit.md), which
   stays as it is.

## Check

`dotnet cleanarch test --no-integration` passes: `UseCasePipelineTests` holds the order and
what the behaviour does, `ContainerCompositionTests` that every host resolves it, and
`ContainerCompositionTests.EveryMessage_PassesThroughTheValidationBehaviour` that the validation
still wraps every message.

## Undo

Delete the behaviour, its registration, its tests and its row, and restore the list of
`ThePipeline_IsRegisteredOnce_HoweverManyFeaturesAskForIt`.
