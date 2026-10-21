# Adding a setting

## When

A feature needs a value a deployment chooses: a limit, an interval, a switch. At the end, the value
is bound from its own section, checked before the host serves anything, read where the rule lives,
and listed in [the configuration reference](../reference/configuration.md).

A rule that holds whatever the deployment is not a setting: it stays an invariant of the model, with
its domain test. A setting that bounds a request is read by the command's validator, so a request
past it answers `400`; one that shapes the work is read by the use case.

## Steps

`<Section>` is the section's name, `<Features>` the feature's folder.

1. Write `<Section>Options` in
   `Src/Application/AppTemplate.Application/Features/<Features>/Configuration/Options/`, with
   `public const string SectionName = "<Section>";` and one `{ get; set; }` property per key, its
   default the value a deployment gets. `ExternalKeyTests` refuses a section name that is not the
   type name without `Options`; if `LayoutConventionTests` refuses the folder, add
   `Configuration/Options` to that project's vocabulary there.
2. Write `<Section>OptionsValidator : IValidateOptions<<Section>Options>` beside it, failing with a
   message that names the key, as `IdempotencyPurgeOptions` does:
   `'<Section>:<Key>' must be between 1 and 100000.`
3. Register both in the feature's `Add<Features>` of `ApplicationModule`, which then takes the
   configuration:

   ```csharp
   services.AddOptions<<Section>Options>()
       .Bind(configuration.GetSection(<Section>Options.SectionName))
       .ValidateOnStart();
   services.TryAddEnumerable(
       ServiceDescriptor.Singleton<IValidateOptions<<Section>Options>, <Section>OptionsValidator>());
   ```

   The build then names every call site to pass `IConfiguration` to: `Program.cs`, each composition
   in `HostComposition`, the feature calls in the application tests, and any test host that composes
   the feature.
4. Inject `IOptions<<Section>Options>` into the validator or the use case that applies the rule.
5. Add a `` ### `<Section>` `` heading under its group in
   [the configuration reference](../reference/configuration.md), with a
   `| Key | Type | Default | Notes |` table and one row per key: the bounds the validator holds, then
   what the key changes. `ConfigurationSurfaceTests` fails until every bound key has its row, and
   while a row names a key no options class binds.
6. Give the key its deployment value in `Src/Presentation/AppTemplate.Api/appsettings.json` when an
   operator is expected to change it. For the Compose stack, map `<Section>__<Key>` to a variable in
   the API's `environment` in `docker-compose.yml`, and add that variable to `.env.example` under one
   `#` line saying what it governs.
7. Test the validator at its boundaries, each refusal beside the value that passes, and test that
   composing the feature with the key out of range makes `IStartupValidator.Validate()` throw
   `OptionsValidationException`. [Testing](testing.md#house-rules) holds the rules.

## Check

`dotnet test --project Tests/Architecture/AppTemplate.Architecture.Tests` passes, and the API started
with the key out of range, `<Section>__<Key>=0 dotnet run Tools/Tasks.cs run`, stops before serving
and names `<Section>:<Key>`.

## Undo

Delete the options class and its validator, the registration and the `IConfiguration` parameter it
brought, the section in the configuration reference, and the key in `appsettings.json`,
`docker-compose.yml` and `.env.example`.
