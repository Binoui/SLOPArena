using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using SlopArena.Shared;
using Xunit;

namespace SlopArena.Tests;

public class FeedbackTests
{
    private static FeedbackSubmission Sample(FeedbackImage? image = null) => new(
        Guid.Parse("78ca3171-8bac-4a20-9fb9-a885d5c50673"), FeedbackKind.Feel, "The landing feels late here",
        new FeedbackContext("0.1.0-playtest.48", 35, "WindowsPlayer", "PvPMatch", FeedbackPhase.Match,
            DateTimeOffset.Parse("2026-11-04T12:00:00Z"), new FeedbackMatchContext("pvp", "stock", "slop_pit",
                Guid.Parse("230d7309-7f7f-4836-988e-780f6ba92215"), 1, 1240,
                new[] { new FeedbackFighter(1, "fightguy", "1.0.0"), new FeedbackFighter(2, "nyx") })), image);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MultipartMatchesGoldenAndHasNoIdentity(bool screenshot)
    {
        var image = screenshot ? new FeedbackImage(new byte[] { 255, 216, 255, 217 }, 1, 1) : null;
        using var http = new HttpClient(new MockHttpHandler(request =>
        {
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            Assert.Equal("secret", request.Headers.Authorization.Parameter);
            Assert.Equal("/feedback", request.RequestUri!.AbsolutePath);
            var parts = Assert.IsType<MultipartFormDataContent>(request.Content).ToArray();
            Assert.Equal(screenshot ? 2 : 1, parts.Length);
            Assert.Equal("metadata", parts[0].Headers.ContentDisposition!.Name!.Trim('"'));
            Assert.Equal("application/json", parts[0].Headers.ContentType!.MediaType);
            var raw = parts[0].ReadAsStringAsync().GetAwaiter().GetResult();
            var fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "feedback-v1.json");
            if (!File.Exists(fixture)) fixture = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Fixtures", "feedback-v1.json"));
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(File.ReadAllText(fixture)), JsonNode.Parse(raw)));
            foreach (var key in new[] { "steamId", "reporter", "token", "displayName", "playerId" }) Assert.DoesNotContain(key, raw);
            if (screenshot)
            {
                Assert.Equal("image/jpeg", parts[1].Headers.ContentType!.MediaType);
                Assert.Equal(image!.Jpeg.ToArray(), parts[1].ReadAsByteArrayAsync().GetAwaiter().GetResult());
            }
            return new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent("{\"reportId\":\"230d7309-7f7f-4836-988e-780f6ba92215\",\"receivedAtUtc\":\"2026-11-04T12:00:00Z\"}") };
        })) { BaseAddress = new Uri("http://test/") };
        using var client = new MasterServerClient(http);
        Assert.IsType<FeedbackSendResult.Accepted>(await client.SendFeedbackAsync("secret", Sample(image)));
    }

    [Theory]
    [InlineData(200, "retry")]
    [InlineData(400, "reject")]
    [InlineData(401, "retry")]
    [InlineData(404, "retry")]
    [InlineData(413, "reject")]
    [InlineData(415, "reject")]
    [InlineData(422, "reject")]
    [InlineData(429, "retry")]
    [InlineData(500, "retry")]
    public async Task StatusMapping(int status, string expected)
    {
        using var http = new HttpClient(new MockHttpHandler(_ =>
        {
            var response = new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent("{}") };
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(45));
            return response;
        })) { BaseAddress = new Uri("http://test/") };
        using var client = new MasterServerClient(http);
        var result = await client.SendFeedbackAsync("jwt", Sample());
        if (expected == "reject") Assert.IsType<FeedbackSendResult.Rejected>(result);
        else Assert.IsType<FeedbackSendResult.Retryable>(result);
        if (status == 429) Assert.Equal(TimeSpan.FromSeconds(45), ((FeedbackSendResult.Retryable)result).RetryAfter);
        if (status == 404) Assert.Contains("isn't available yet", ((FeedbackSendResult.Retryable)result).Reason);
    }

    [Fact]
    public async Task RetryPreservesSubmissionAfterResponseLoss()
    {
        string? first = null;
        int calls = 0;
        using var http = new HttpClient(new MockHttpHandler(request =>
        {
            var part = ((MultipartFormDataContent)request.Content!).First();
            string metadata = part.ReadAsStringAsync().GetAwaiter().GetResult();
            if (++calls == 1) { first = metadata; throw new HttpRequestException(); }
            Assert.Equal(first, metadata);
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        })) { BaseAddress = new Uri("http://test/") };
        using var client = new MasterServerClient(http);
        var submission = Sample();
        Assert.IsType<FeedbackSendResult.Retryable>(await client.SendFeedbackAsync("jwt", submission));
        Assert.IsType<FeedbackSendResult.Retryable>(await client.SendFeedbackAsync("jwt", submission));
        Assert.Equal(2, calls);
    }

    [Fact]
    public void FrontendCannotCarryStaleMatch()
    {
        var context = Sample().Context;
        Assert.Null(new FeedbackContext("v", 1, "p", "s", FeedbackPhase.Frontend, DateTimeOffset.UtcNow, context.Match).Match);
        Assert.Throws<ArgumentException>(() => new FeedbackSubmission(Guid.NewGuid(), FeedbackKind.Bug, new string('a', 2001), context, null));
    }
}
