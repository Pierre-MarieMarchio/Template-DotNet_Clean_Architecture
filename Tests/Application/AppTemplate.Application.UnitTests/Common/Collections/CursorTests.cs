using System.Buffers.Text;
using System.Text;
using AppTemplate.Application.Common.Collections;
using Shouldly;
using Xunit;
using SortDirection = AppTemplate.Application.Common.Collections.SortDirection;

namespace AppTemplate.Application.UnitTests.Common.Collections;

public sealed class CursorTests
{
    private static readonly FakeCollectionPolicy _policy = new();

    private static SortTerm ATerm() => SortOrder.Parse("name", _policy).Value.Terms[0];

    private static string Encode(string json) => Base64Url.EncodeToString(Encoding.UTF8.GetBytes(json));

    #region After

    [Fact]
    public void After_Rejects_ANullTerm() =>
        Should.Throw<ArgumentNullException>(() => Cursor.After(null!, "key", Guid.CreateVersion7()));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void After_Rejects_ANullOrEmptyKey(string? key) =>
        Should.Throw<ArgumentException>(() => Cursor.After(ATerm(), key!, Guid.CreateVersion7()));

    #endregion

    #region Round trip

    [Fact]
    public void EncodeThenDecode_RoundTrips()
    {
        var id = Guid.CreateVersion7();
        var cursor = Cursor.After(ATerm(), "Groceries", id);

        var decoded = Cursor.Decode(cursor.Encode(), _policy);

        decoded.IsSuccess.ShouldBeTrue();
        decoded.Value.Field.ShouldBe("name");
        decoded.Value.Direction.ShouldBe(SortDirection.Ascending);
        decoded.Value.Key.ShouldBe("Groceries");
        decoded.Value.Id.ShouldBe(id);
    }

    #endregion

    #region Length

    [Fact]
    public void Decode_Rejects_ACursorOverTheMaxEncodedLength()
    {
        string tooLong = new string('a', Cursor.MaxEncodedLength + 1);

        var result = Cursor.Decode(tooLong, _policy);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("cursor.invalid");
        result.Error.Message.ShouldContain("maximum length");
    }

    /// <summary>The bound is inclusive: exactly the ceiling must not be refused by the length gate.</summary>
    [Fact]
    public void Decode_DoesNotFailOnLength_AtExactlyTheMaxEncodedLength()
    {
        string atLimit = new string('a', Cursor.MaxEncodedLength);

        var result = Cursor.Decode(atLimit, _policy);

        if (result.IsFailure)
        {
            result.Error!.Message.ShouldNotContain("maximum length");
        }
    }

    #endregion

    #region Shape failures

    [Fact]
    public void Decode_Rejects_InvalidBase64Url()
    {
        var result = Cursor.Decode("not-valid-base64!!!", _policy);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("cursor.invalid");
    }

    [Fact]
    public void Decode_Rejects_ValidBase64UrlThatIsNotJson()
    {
        var result = Cursor.Decode(Base64Url.EncodeToString("not json"u8.ToArray()), _policy);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("cursor.invalid");
    }

    [Fact]
    public void Decode_Rejects_AMissingMember()
    {
        var result = Cursor.Decode(Encode("""{"f":"name","d":"asc"}"""), _policy);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("cursor.invalid");
    }

    [Fact]
    public void Decode_Rejects_AnUnrecognisedDirection()
    {
        string json = $$"""{"f":"name","d":"sideways","k":"x","i":"{{Guid.CreateVersion7()}}"}""";

        var result = Cursor.Decode(Encode(json), _policy);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("cursor.invalid");
    }

    [Fact]
    public void Decode_Rejects_AnUnparseableId()
    {
        var result = Cursor.Decode(Encode("""{"f":"name","d":"asc","k":"x","i":"not-a-guid"}"""), _policy);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("cursor.invalid");
    }

    [Fact]
    public void Decode_Rejects_ATamperedFieldName()
    {
        string json = $$"""{"f":"secretColumn","d":"asc","k":"x","i":"{{Guid.CreateVersion7()}}"}""";

        var result = Cursor.Decode(Encode(json), _policy);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("cursor.invalid");
    }

    [Fact]
    public void Decode_Rejects_AFieldNotWhitelistedForKeyset()
    {
        // lastModifiedAt sorts fine but does not support a keyset resume — SortOrder.Parse accepts
        // it, so this proves the refusal happens at Decode, on the field's SupportsKeyset flag.
        var term = SortOrder.Parse("lastModifiedAt", _policy).Value.Terms[0];
        string encoded = Cursor.After(term, "x", Guid.CreateVersion7()).Encode();

        var result = Cursor.Decode(encoded, _policy);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("cursor.invalid");
        result.Error.Message.ShouldContain("lastModifiedAt");
    }

    [Fact]
    public void Decode_NeverEchoesTheRawCursorInItsMessage()
    {
        const string raw = "not-valid-base64!!!";

        var result = Cursor.Decode(raw, _policy);

        result.Error!.Message.ShouldNotContain(raw);
    }

    #endregion

    #region Guards

    [Fact]
    public void Decode_Rejects_ANullRaw() =>
        Should.Throw<ArgumentNullException>(() => Cursor.Decode(null!, _policy));

    [Fact]
    public void Decode_Rejects_ANullPolicy() =>
        Should.Throw<ArgumentNullException>(() => Cursor.Decode("anything", null!));

    #endregion
}
