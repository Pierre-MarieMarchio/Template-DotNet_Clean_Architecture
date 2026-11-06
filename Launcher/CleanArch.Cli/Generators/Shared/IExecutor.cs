namespace CleanArch.Cli.Generators.Shared;

internal interface IExecutor
{
    int Run(TextEdits.Command command);

    (int Code, string Output) Read(TextEdits.Command command);
}
