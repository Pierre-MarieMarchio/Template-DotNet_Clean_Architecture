# Adding a setting

## When

A feature needs a value a deployment chooses: a limit, an interval, a switch. At the end, the value
is bound from its own section, checked before the host serves anything, read where the rule lives,
and listed in [the configuration reference](../reference/configuration.md).

A rule that holds whatever the deployment is not a setting: it stays an invariant of the model, with
its domain test. A setting that bounds a request is read by the command's validator, so a request
past it answers `400`; one that shapes the work is read by the use case.

<!--#if (canAddFeature)-->
## The command

```bash
dotnet run Tools/Tasks.cs new-setting <Features> <Section> --keys "<Key>:<type>(<min>..<max>)=<default>, <Key>:<type>"
```

`<Features>` is a feature that a host registers, and `<Section>` the new section's name, without
`Options`. `--keys` lists one entry per key, a type from a closed set, so that no key is written
without its rule:

| Type | Written as | The validator refuses |
|---|---|---|
| `int`, `long` | `MaxItems:int(1..100)=10` | a value outside the bounds; the default sits inside them |
| `duration` | `Interval:duration(00:00:01..01:00:00)=00:05:00` | a `TimeSpan` of whole seconds outside the bounds |
| `bool` | `Strict:bool=true` | nothing: a bool has no invalid value, so a section holds at least one other key |
| `string` | `Label:string(1..20)=main`, or `Owner:string(3..40)` | a length outside the range; with no default the key is required and its minimum is 1 |
| `secret` | `ApiKey:secret` | a blank value: the key is required and has no default |

A number, a duration or a bool carries its default, which the class and the shipped file both
hold: an absent number cannot be told from `0`. Only a required key stops the host when its value is
missing, and it is the one that has no value in `appsettings.json`.

It writes steps 1 to 3, 5, 6 and 7 below, or nothing: every file is computed in memory and every
anchor checked before the first write, and a missing anchor names the edit and stops with a
non-zero exit. A section that already exists is refused, so running it twice never duplicates
anything. `--dry-run` prints what it would write and writes nothing.

- The options class and its validator in `Features/<Features>/Configuration/Options/`, and the test
  of each key's bounds and of the start-up refusal beside the other feature tests.
- The registration in `Add<Features>`, read from the container with `BindConfiguration` where the
  call does not take the configuration yet, so no call site changes.
- The section in `appsettings.json` of each host that composes the feature.
- The section's table in [the configuration reference](../reference/configuration.md), with the
  bounds as notes, under the group named for the feature.
- The `Configuration/Options` folder word in `LayoutConventionTests`, when the project lacks it.
- For a required key, its value in `ValidSettings` of `HostComposition`, so the architecture tests
  still compose a host, and its variable in `docker-compose.yml` and `.env.example`, empty: Compose
  refuses to start until it is set. A secret never holds a value in a tracked file.

What stays yours: step 4, the sentence in each Notes cell saying what the key changes, the secret's
value in `dotnet user-secrets` or the environment, and a value for any test that starts a host.

Where the project holds no `Add<Features>` in its `ApplicationModule`, follow the steps by hand.

## The steps by hand

<!--#endif-->
<!--#if (!canAddFeature)-->
## Steps

<!--#endif-->
`<Section>` is the section's name, `<Features>` the feature's folder.

1. Write `<Section>Options` in
   `Src/Application/AT.Application/Features/<Features>/Configuration/Options/`, with
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
   the feature. A call that does not take the configuration can read it from the container instead,
   with `.BindConfiguration(<Section>Options.SectionName)` in place of `.Bind(...)`, and no call site
   changes.
4. Inject `IOptions<<Section>Options>` into the validator or the use case that applies the rule.
5. Add a `` ### `<Section>` `` heading under its group in
   [the configuration reference](../reference/configuration.md), with a
   `| Key | Type | Default | Notes |` table and one row per key: the bounds the validator holds, then
   what the key changes. `ConfigurationSurfaceTests` fails until every bound key has its row, and
   while a row names a key no options class binds.
<!--#if (api)-->
6. Give the key its deployment value in `Src/Presentation/AT.Api/appsettings.json` when an
   operator is expected to change it. For the Compose stack, map `<Section>__<Key>` to a variable in
   the API's `environment` in `docker-compose.yml`, and add that variable to `.env.example` under one
   `#` line saying what it governs. `ConfigurationSurfaceTests` also fails while a key shipped in an
   `appsettings` file or set by Compose or `.env.example` has no row in the reference.
<!--#endif-->
<!--#if (!api)-->
6. Give the key its deployment value in the `appsettings.json` of each host that reads it when an
   operator is expected to change it. For the Compose stack, map `<Section>__<Key>` to a variable in
   `x-host-environment` in `docker-compose.yml`, and add that variable to `.env.example` under one
   `#` line saying what it governs. `ConfigurationSurfaceTests` also fails while a key shipped in an
   `appsettings` file or set by Compose or `.env.example` has no row in the reference.
<!--#endif-->
7. Test the validator at its boundaries, each refusal beside the value that passes, and test that
   composing the feature with the key out of range makes `IStartupValidator.Validate()` throw
   `OptionsValidationException`. [Testing](testing.md#house-rules) holds the rules.

## Check

<!--#if (api)-->
`dotnet test --project Tests/Architecture/AT.Architecture.Tests` passes, and the API started
with the key out of range, `<Section>__<Key>=0 dotnet run Tools/Tasks.cs run`, stops before serving
and names `<Section>:<Key>`.
<!--#endif-->
<!--#if (!api)-->
`dotnet test --project Tests/Architecture/AT.Architecture.Tests` passes, and a host started
with the key out of range, `<Section>__<Key>=0` set in its environment, stops before serving and
names `<Section>:<Key>`.
<!--#endif-->

## Undo

Delete the options class and its validator, their tests, the registration and the `IConfiguration`
parameter it brought, the section in the configuration reference, and the key in `appsettings.json`,
`docker-compose.yml`, `.env.example` and `ValidSettings` of `HostComposition`.
