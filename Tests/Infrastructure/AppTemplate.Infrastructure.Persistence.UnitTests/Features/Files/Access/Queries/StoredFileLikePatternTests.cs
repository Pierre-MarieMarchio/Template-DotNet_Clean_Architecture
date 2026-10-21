using AppTemplate.Infrastructure.Core.Common.Contexts;
using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using AppTemplate.Infrastructure.Persistence.Features.Files.Access.Queries;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Persistence.UnitTests.Features.Files.Access.Queries;

public sealed class StoredFileLikePatternTests
{
    [Fact]
    public void Contains_WrapsThePatternInWildcards() =>
        StoredFileLikePattern.Contains("report").ShouldBe("%report%");

    [Fact]
    public void Contains_Escapes_APercentSign() =>
        StoredFileLikePattern.Contains("50% off").ShouldBe("%50\\% off%");

    [Fact]
    public void Contains_Escapes_AnUnderscore() =>
        StoredFileLikePattern.Contains("a_b").ShouldBe("%a\\_b%");

    [Fact]
    public void Contains_Escapes_ABackslashItself() =>
        StoredFileLikePattern.Contains("a\\b").ShouldBe("%a\\\\b%");

    [Fact]
    public void ILikeWithTheEscapedPattern_ProducesEscapeInTheSql()
    {
        using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseDatabase(DatabaseProvider.DesignTimeConnectionString)
            .Options);

        string pattern = StoredFileLikePattern.Contains("50% off_thing");

        string sql = context.StoredFiles
            .Where(file => EF.Functions.ILike(file.Name, pattern, "\\"))
            .ToQueryString();

        sql.ShouldContain("ILIKE @pattern ESCAPE '\\'");
        sql.ShouldContain("@pattern='%50\\% off\\_thing%'");
    }
}
