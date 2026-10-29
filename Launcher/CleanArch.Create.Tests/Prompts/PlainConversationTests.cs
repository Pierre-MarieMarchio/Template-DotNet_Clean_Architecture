using CleanArch.Create.Prompts;
using Shouldly;
using Spectre.Console.Testing;
using Xunit;

namespace CleanArch.Create.Tests.Prompts;

public sealed class PlainConversationTests
{
    [Fact]
    public void AnAnswerTakenWithoutAReason_IsShownWithoutEmptyBrackets()
    {
        var console = new TestConsole();

        new PlainConversation(console).Answered("Framework", "blazor-server", string.Empty);

        console.Output.TrimEnd().ShouldBe("Framework: blazor-server");
    }
}
