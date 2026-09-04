using AppTemplate.Application.Common.Idempotency;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Common.Idempotency;

public sealed class IdempotencyKeyTests
{
    private static readonly Guid _userId = Guid.CreateVersion7();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_Rejects_ABlankKey(string? key)
    {
        var result = IdempotencyKey.Create(_userId, key, "POST /api/v1/todo-lists", "fingerprint", 128);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("idempotency.keyInvalid");
    }

    [Fact]
    public void Create_Accepts_AKeyAtExactlyTheMaxLength()
    {
        string atLimit = new string('a', 10);

        var result = IdempotencyKey.Create(_userId, atLimit, "POST /api/v1/todo-lists", "fingerprint", 10);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Key.ShouldBe(atLimit);
    }

    [Fact]
    public void Create_Rejects_AKeyOneOverTheMaxLength()
    {
        string overLimit = new string('a', 11);

        var result = IdempotencyKey.Create(_userId, overLimit, "POST /api/v1/todo-lists", "fingerprint", 10);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("idempotency.keyInvalid");
    }

    [Fact]
    public void Create_Succeeds_ForAnOrdinaryKey()
    {
        var result = IdempotencyKey.Create(_userId, "client-generated-key", "POST /api/v1/todo-lists", "fingerprint", 128);

        result.IsSuccess.ShouldBeTrue();
        result.Value.UserId.ShouldBe(_userId);
        result.Value.Key.ShouldBe("client-generated-key");
        result.Value.Endpoint.ShouldBe("POST /api/v1/todo-lists");
        result.Value.Fingerprint.ShouldBe("fingerprint");
    }
}
