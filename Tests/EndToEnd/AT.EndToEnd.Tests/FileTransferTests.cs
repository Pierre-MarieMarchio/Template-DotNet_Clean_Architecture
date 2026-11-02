using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Shouldly;
using Xunit;

namespace AppTemplate.EndToEnd.Tests;

public sealed class FileTransferTests
{
    [Fact]
    public async Task AFile_DepositedOnItsSignedGrant_IsServedBackOnceInspected()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var api = Stack.Client(Stack.Api);
        using var store = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
        var account = Accounts.New();
        byte[] payload = Encoding.UTF8.GetBytes($"end-to-end {Guid.NewGuid():N}\n");

        await Accounts.RegisterAndConfirmAsync(api, account, cancellationToken);
        string bearer = (await Accounts.SignInAsync(api, account, cancellationToken)).AccessToken;

        JsonElement registration;
        using (var registered = await RegisterAsync(api, bearer, payload, cancellationToken))
        {
            registration = await Wire.ExpectJsonAsync(registered, HttpStatusCode.Created, cancellationToken);
        }

        string fileId = Wire.Text(registration, "id");

        using (var deposited = await DepositAsync(store, registration.GetProperty("upload"), payload, cancellationToken))
        {
            deposited.IsSuccessStatusCode.ShouldBeTrue(await deposited.Content.ReadAsStringAsync(cancellationToken));
        }

        using (var confirmed = await Wire.SendAsync(api, HttpMethod.Post, $"api/v1/files/{fileId}/confirm", cancellationToken, bearer: bearer))
        {
            Wire.Text(await Wire.ExpectJsonAsync(confirmed, HttpStatusCode.OK, cancellationToken), "status").ShouldBe("deposited");
        }

        await Poll.UntilAsync(
            $"The inspection of file {fileId}",
            TimeSpan.FromMinutes(3),
            async token =>
            {
                using var read = await Wire.SendAsync(api, HttpMethod.Get, $"api/v1/files/{fileId}", token, bearer: bearer);
                string status = Wire.Text(await Wire.ExpectJsonAsync(read, HttpStatusCode.OK, token), "status");

                status.ShouldNotBe("quarantined");

                return status == "available" ? status : null;
            },
            cancellationToken);

        using var content = await Wire.SendAsync(api, HttpMethod.Get, $"api/v1/files/{fileId}/content", cancellationToken, bearer: bearer);
        await Wire.ExpectAsync(content, HttpStatusCode.Found, cancellationToken);

        using var download = await store.GetAsync(content.Headers.Location, cancellationToken);
        download.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await download.Content.ReadAsByteArrayAsync(cancellationToken)).ShouldBe(payload);
    }

    private static async Task<HttpResponseMessage> RegisterAsync(
        HttpClient api,
        string bearer,
        byte[] payload,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("api/v1/files", UriKind.Relative))
        {
            Content = JsonContent.Create(new
            {
                category = "document",
                name = "end-to-end.txt",
                declaredMediaType = "text/plain",
                sizeInBytes = payload.Length,
                checksum = Convert.ToHexStringLower(SHA256.HashData(payload)),
            }),
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        return await api.SendAsync(request, cancellationToken);
    }

    private static async Task<HttpResponseMessage> DepositAsync(
        HttpClient store,
        JsonElement grant,
        byte[] payload,
        CancellationToken cancellationToken)
    {
        using var content = new ByteArrayContent(payload);

        foreach (var header in grant.GetProperty("requiredHeaders").EnumerateObject())
        {
            if (string.Equals(header.Name, "Content-Type", StringComparison.OrdinalIgnoreCase))
            {
                content.Headers.ContentType = MediaTypeHeaderValue.Parse(header.Value.GetString()!);
            }
            else if (!string.Equals(header.Name, "Content-Length", StringComparison.OrdinalIgnoreCase))
            {
                content.Headers.TryAddWithoutValidation(header.Name, header.Value.GetString());
            }
        }

        using var request = new HttpRequestMessage(new HttpMethod(Wire.Text(grant, "method")), new Uri(Wire.Text(grant, "url")))
        {
            Content = content,
        };

        return await store.SendAsync(request, cancellationToken);
    }
}
