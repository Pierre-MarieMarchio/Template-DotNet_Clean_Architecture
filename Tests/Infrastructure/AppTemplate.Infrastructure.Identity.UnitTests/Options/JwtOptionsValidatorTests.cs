using System.Text;
using AppTemplate.Infrastructure.Identity.Options;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Identity.UnitTests.Options;

/// <summary>
/// The validator runs under <c>ValidateOnStart</c>, so what it rejects is what stops the process from
/// booting. A short signing key has to be one of those things: HS256 with a key shorter than its own
/// output is a weakened signature, and the failure would otherwise surface as an opaque IDX10653 at
/// the first login.
/// </summary>
public sealed class JwtOptionsValidatorTests
{
    private static readonly JwtOptionsValidator _validator = new();

    [Fact]
    public void Validate_RejectsAKeyShorterThanTheEnforcedFloor()
    {
        string tooShort = new('k', JwtOptions.MinimumKeyLengthInBytes - 1);
        Encoding.UTF8.GetByteCount(tooShort).ShouldBeLessThan(JwtOptions.MinimumKeyLengthInBytes);

        var result = _validator.Validate(name: null, Valid(tooShort));

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain($"{JwtOptions.MinimumKeyLengthInBytes} bytes");
    }

    /// <summary>
    /// The floor is counted in bytes, not characters, so a key that is long enough as a string but
    /// short as UTF-8 must not slip through. Nothing else in the file would catch that.
    /// </summary>
    [Fact]
    public void Validate_CountsTheKeyInBytesRatherThanCharacters()
    {
        string thirtyTwoCharacters = new('k', JwtOptions.MinimumKeyLengthInBytes);

        _validator.Validate(name: null, Valid(thirtyTwoCharacters)).Succeeded.ShouldBeTrue();
        _validator.Validate(name: null, Valid(new string('k', JwtOptions.MinimumKeyLengthInBytes - 1)))
            .Failed.ShouldBeTrue();
    }

    [Fact]
    public void Validate_RejectsAMissingKey()
    {
        _validator.Validate(name: null, Valid(key: "   ")).Failed.ShouldBeTrue();
    }

    private static JwtOptions Valid(string key) => new()
    {
        Key = key,
        Issuer = "https://localhost/app-template",
        Audience = "app-template-api",
        AccessTokenLifetimeInMinutes = 15,
    };
}
