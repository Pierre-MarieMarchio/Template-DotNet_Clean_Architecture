using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using AppTemplate.Infrastructure.Persistence.Features.TodoLists.Access.Queries;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Persistence.UnitTests.Features.TodoLists.Access.Queries;

public sealed class TodoListLikePatternTests
{
    [Fact]
    public void Contains_WrapsThePatternInWildcards() =>
        TodoListLikePattern.Contains("milk").ShouldBe("%milk%");

    [Fact]
    public void Contains_Escapes_APercentSign() =>
        TodoListLikePattern.Contains("50% off").ShouldBe("%50\\% off%");

    [Fact]
    public void Contains_Escapes_AnUnderscore() =>
        TodoListLikePattern.Contains("a_b").ShouldBe("%a\\_b%");

    [Fact]
    public void Contains_Escapes_ABackslashItself() =>
        TodoListLikePattern.Contains("a\\b").ShouldBe("%a\\\\b%");

    [Fact]
    public void ILikeWithTheEscapedPattern_ProducesEscapeInTheSql()
    {
        using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=probe;Username=none;Password=none")
            .Options);

        string pattern = TodoListLikePattern.Contains("50% off_thing");

        string sql = context.TodoLists
            .Where(list => EF.Functions.ILike(list.Name, pattern, "\\"))
            .ToQueryString();

        sql.ShouldContain("ILIKE @pattern ESCAPE '\\'");
        sql.ShouldContain("@pattern='%50\\% off\\_thing%'");
    }
}
