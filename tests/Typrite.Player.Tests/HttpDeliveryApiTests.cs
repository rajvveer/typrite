using System.Net;
using Typrite.Player.Delivery;

namespace Typrite.Player.Tests;

public class HttpDeliveryApiTests
{
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(respond(request));
    }

    private static HttpDeliveryApi Api(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        new(new HttpClient(new Handler(respond)) { BaseAddress = new Uri("https://api.test/") }, Guid.NewGuid(), _ => ValueTask.FromResult("tok"));

    [Fact]
    public async Task Creative_fetch_refuses_redirects()
    {
        var api = Api(_ => new HttpResponseMessage(HttpStatusCode.Found) { Headers = { Location = new Uri("http://evil.test/x.png") } });

        await Assert.ThrowsAsync<HttpRequestException>(() => api.FetchCreativeAsync(new Uri("https://cdn.test/c.png"), 1024, CancellationToken.None));
    }

    [Fact]
    public async Task Creative_fetch_enforces_the_byte_limit_while_streaming()
    {
        var api = Api(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[2048]) });

        await Assert.ThrowsAsync<HttpRequestException>(() => api.FetchCreativeAsync(new Uri("https://cdn.test/c.png"), 1024, CancellationToken.None));
    }

    [Fact]
    public async Task Bearer_token_is_sent_only_to_the_api_origin()
    {
        string? toApi = null;
        string? toCdn = null;
        var api = Api(req =>
        {
            if (req.RequestUri!.Host == "api.test")
            {
                toApi = req.Headers.Authorization?.Parameter;
            }
            else
            {
                toCdn = req.Headers.Authorization?.Parameter;
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1]) };
        });

        await api.FetchCreativeAsync(new Uri("https://api.test/api/v1/creatives/x/content"), 10, CancellationToken.None);
        await api.FetchCreativeAsync(new Uri("https://cdn.test/c.png"), 10, CancellationToken.None);

        Assert.Equal("tok", toApi);
        Assert.Null(toCdn);
    }
}
