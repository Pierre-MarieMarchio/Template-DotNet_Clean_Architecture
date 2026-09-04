using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using AppTemplate.Infrastructure.Persistence.Features.Files.Queries;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Persistence.UnitTests.Features.Files.Queries;

public sealed class StoredFileLikePatternTests
{
    #region Pattern escaping

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
    public void Contains_Rejects_ANullTerm() =>
        Should.Throw<ArgumentNullException>(() => StoredFileLikePattern.Contains(null!));

    #endregion

    #region SQL shape

    /// <summary>
    /// The same call <c>StoredFileQueries</c> makes: <see cref="StoredFileLikePattern.Contains"/> feeding
    /// <c>EF.Functions.ILike</c>. Proves the escape character reaches PostgreSQL as <c>ESCAPE '\'</c> and
    /// that a caller's own <c>%</c> travels only inside the parameter, never into the SQL text.
    /// </summary>
    [Fact]
    public void ILikeWithTheEscapedPattern_ProducesEscapeInTheSql()
    {
        using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=probe;Username=none;Password=none")
            .Options);

        string pattern = StoredFileLikePattern.Contains("50% off_thing");

        string sql = context.StoredFiles
            .Where(file => EF.Functions.ILike(file.Name, pattern, "\\"))
            .ToQueryString();

        sql.ShouldContain("ILIKE @pattern ESCAPE '\\'");
        sql.ShouldContain("@pattern='%50\\% off\\_thing%'");
    }

    #endregion
}
