using System.Buffers.Binary;
using System.Buffers.Text;
using System.Formats.Cbor;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace AppTemplate.Api.IntegrationTests.Auth;

/// <summary>
/// A WebAuthn authenticator in memory: one ES256 credential, created with the <c>none</c>
/// attestation and asserted with a DER signature, as a browser would hand them to the API.
/// </summary>
/// <remarks>
/// Every optional argument replaces one input of the ceremony and nothing else, so a refusal test
/// differs from the accepted ceremony in exactly the value it names.
/// </remarks>
internal sealed class SoftwareAuthenticator : IDisposable
{
    private const byte _userPresent = 0x01;
    private const byte _userVerified = 0x04;
    private const byte _backupEligible = 0x08;
    private const byte _backedUp = 0x10;
    private const byte _attestedCredentialData = 0x40;

    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    public byte[] CredentialId { get; } = RandomNumberGenerator.GetBytes(32);

    public string CredentialIdBase64Url => Base64Url.EncodeToString(CredentialId);

    /// <summary>The <c>user.id</c> of the creation options, as the authenticator stores it.</summary>
    public string? UserHandle { get; private set; }

    /// <summary>The counter the next assertion increments, unless <see cref="KeepsNoCounter"/>.</summary>
    public uint SignCount { get; set; }

    /// <summary>Reports zero on every assertion, as synced passkeys do.</summary>
    public bool KeepsNoCounter { get; init; }

    public bool BackupEligible { get; init; }

    public bool BackedUp { get; init; }

    public string CreateCredential(
        string creationOptionsJson,
        string origin,
        string? clientDataOrigin = null,
        string? challenge = null,
        string? rpId = null,
        byte? flags = null,
        string clientDataType = "webauthn.create")
    {
        var options = JsonNode.Parse(creationOptionsJson)
            ?? throw new InvalidOperationException("The creation options are empty.");

        UserHandle = (string?)options["user"]?["id"]
            ?? throw new InvalidOperationException($"The creation options name no user: {creationOptionsJson}");

        byte[] clientData = ClientData(
            clientDataType,
            challenge ?? (string)options["challenge"]!,
            clientDataOrigin ?? origin);

        byte[] authenticatorData =
        [
            .. RpIdHash(rpId ?? (string?)options["rp"]?["id"] ?? new Uri(origin).Host),
            flags ?? (byte)(_userPresent | _userVerified | _attestedCredentialData | BackupFlags()),
            .. Counter(0),
            .. new byte[16],
            .. Length(CredentialId.Length),
            .. CredentialId,
            .. CosePublicKey(),
        ];

        return new JsonObject
        {
            ["id"] = CredentialIdBase64Url,
            ["rawId"] = CredentialIdBase64Url,
            ["type"] = "public-key",
            ["authenticatorAttachment"] = "platform",
            ["response"] = new JsonObject
            {
                ["clientDataJSON"] = Base64Url.EncodeToString(clientData),
                ["attestationObject"] = Base64Url.EncodeToString(AttestationObject(authenticatorData)),
                ["transports"] = new JsonArray("internal"),
            },
            ["clientExtensionResults"] = new JsonObject(),
        }.ToJsonString();
    }

    public string GetAssertion(
        string requestOptionsJson,
        string origin,
        string? clientDataOrigin = null,
        string? challenge = null,
        string? rpId = null,
        byte? flags = null,
        uint? signCount = null,
        string? userHandle = null,
        bool corruptSignature = false,
        string clientDataType = "webauthn.get")
    {
        var options = JsonNode.Parse(requestOptionsJson)
            ?? throw new InvalidOperationException("The request options are empty.");

        SignCount = signCount ?? (KeepsNoCounter ? 0 : SignCount + 1);

        byte[] clientData = ClientData(
            clientDataType,
            challenge ?? (string)options["challenge"]!,
            clientDataOrigin ?? origin);

        byte[] authenticatorData =
        [
            .. RpIdHash(rpId ?? (string?)options["rpId"] ?? new Uri(origin).Host),
            flags ?? (byte)(_userPresent | _userVerified | BackupFlags()),
            .. Counter(SignCount),
        ];

        byte[] signature = _key.SignData(
            [.. authenticatorData, .. SHA256.HashData(clientData)],
            HashAlgorithmName.SHA256,
            DSASignatureFormat.Rfc3279DerSequence);

        if (corruptSignature)
        {
            signature[^1] ^= 0xFF;
        }

        return new JsonObject
        {
            ["id"] = CredentialIdBase64Url,
            ["rawId"] = CredentialIdBase64Url,
            ["type"] = "public-key",
            ["authenticatorAttachment"] = "platform",
            ["response"] = new JsonObject
            {
                ["clientDataJSON"] = Base64Url.EncodeToString(clientData),
                ["authenticatorData"] = Base64Url.EncodeToString(authenticatorData),
                ["signature"] = Base64Url.EncodeToString(signature),
                ["userHandle"] = userHandle ?? UserHandle
                    ?? throw new InvalidOperationException("No credential was created, so no user handle is stored."),
            },
            ["clientExtensionResults"] = new JsonObject(),
        }.ToJsonString();
    }

    public void Dispose() => _key.Dispose();

    private byte BackupFlags() =>
        (byte)((BackupEligible ? _backupEligible : 0) | (BackedUp ? _backedUp : 0));

    private static byte[] ClientData(string type, string challenge, string origin) =>
        Encoding.UTF8.GetBytes(new JsonObject
        {
            ["type"] = type,
            ["challenge"] = challenge,
            ["origin"] = origin,
            ["crossOrigin"] = false,
        }.ToJsonString());

    private static byte[] RpIdHash(string rpId) => SHA256.HashData(Encoding.UTF8.GetBytes(rpId));

    private static byte[] Counter(uint value)
    {
        byte[] bytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);

        return bytes;
    }

    private static byte[] Length(int value)
    {
        byte[] bytes = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, checked((ushort)value));

        return bytes;
    }

    /// <summary>The credential's public key as a COSE EC2 key: P-256, ES256.</summary>
    private byte[] CosePublicKey()
    {
        var point = _key.ExportParameters(includePrivateParameters: false).Q;
        var writer = new CborWriter(CborConformanceMode.Ctap2Canonical);

        writer.WriteStartMap(5);
        writer.WriteInt32(1);
        writer.WriteInt32(2);
        writer.WriteInt32(3);
        writer.WriteInt32(-7);
        writer.WriteInt32(-1);
        writer.WriteInt32(1);
        writer.WriteInt32(-2);
        writer.WriteByteString(point.X!);
        writer.WriteInt32(-3);
        writer.WriteByteString(point.Y!);
        writer.WriteEndMap();

        return writer.Encode();
    }

    private static byte[] AttestationObject(byte[] authenticatorData)
    {
        var writer = new CborWriter(CborConformanceMode.Ctap2Canonical);

        writer.WriteStartMap(3);
        writer.WriteTextString("fmt");
        writer.WriteTextString("none");
        writer.WriteTextString("attStmt");
        writer.WriteStartMap(0);
        writer.WriteEndMap();
        writer.WriteTextString("authData");
        writer.WriteByteString(authenticatorData);
        writer.WriteEndMap();

        return writer.Encode();
    }
}
