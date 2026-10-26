using System.Net;
using System.Text;
using ChineseLearningServer;

namespace ChineseLearning.Tests;

// Making a learner's Gemini key in their Google account, against a stand-in
// for Google Cloud's project, service and API key endpoints.
public sealed class GoogleCloudKeysTests
{
    private const string Key = "AIzaSyFAKE-key-from-the-stand-in-0000000";

    [Fact]
    public async Task TheFirstTimeAProjectWithGeminiAndAKeyIsMade()
    {
        var google = new FakeGoogleCloud();
        var key = await new GoogleCloudKeys(new HttpClient(google)).GetOrCreateAsync("access", CancellationToken.None);

        Assert.Equal(Key, key);
        Assert.Contains(google.Calls, call => call.StartsWith("POST https://cloudresourcemanager.googleapis.com/v1/projects", StringComparison.Ordinal));
        Assert.Contains(google.Calls, call => call.Contains("/services/generativelanguage.googleapis.com:enable", StringComparison.Ordinal));
        Assert.Contains(google.Calls, call => call.StartsWith("POST https://apikeys.googleapis.com/v2/projects/hoc-tieng-trung-", StringComparison.Ordinal));
        Assert.Contains("generativelanguage.googleapis.com", google.KeyRequest);
        Assert.All(google.Tokens, token => Assert.Equal("access", token));
    }

    [Fact]
    public async Task LaterTheSameKeyIsGivenBack()
    {
        var google = new FakeGoogleCloud { HasProject = true, HasKey = true };
        var key = await new GoogleCloudKeys(new HttpClient(google)).GetOrCreateAsync("access", CancellationToken.None);

        Assert.Equal(Key, key);
        Assert.DoesNotContain(google.Calls, call => call.StartsWith("POST https://cloudresourcemanager", StringComparison.Ordinal));
        Assert.DoesNotContain(google.Calls, call => call.StartsWith("POST https://apikeys", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnAccountThatHasNotAcceptedGoogleCloudTermsIsToldWhatToDo()
    {
        var google = new FakeGoogleCloud { RefuseProjects = true };

        var refused = await Assert.ThrowsAsync<GeminiKeyException>(() =>
            new GoogleCloudKeys(new HttpClient(google)).GetOrCreateAsync("access", CancellationToken.None));

        Assert.Contains("điều khoản Google Cloud", refused.Message);
    }

    private sealed class FakeGoogleCloud : HttpMessageHandler
    {
        public bool HasProject { get; set; }

        public bool HasKey { get; set; }

        public bool RefuseProjects { get; set; }

        public List<string> Calls { get; } = [];

        public List<string?> Tokens { get; } = [];

        public string KeyRequest { get; private set; } = "";

        private bool _projectAskedOnce;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var address = request.RequestUri!.ToString();
            Calls.Add($"{request.Method} {address}");
            Tokens.Add(request.Headers.Authorization?.Parameter);
            var post = request.Method == HttpMethod.Post;

            if (address.StartsWith("https://cloudresourcemanager.googleapis.com/v1/projects?", StringComparison.Ordinal))
            {
                return Reply(HasProject ? """{"projects":[{"projectId":"hoc-tieng-trung-abc123","lifecycleState":"ACTIVE"}]}""" : "{}");
            }

            if (post && address == "https://cloudresourcemanager.googleapis.com/v1/projects")
            {
                return RefuseProjects
                    ? Reply("""{"error":{"code":400,"message":"Callers must accept Terms of Service","status":"FAILED_PRECONDITION"}}""", HttpStatusCode.BadRequest)
                    : Reply("""{"name":"operations/cp.1"}""");
            }

            if (address == "https://cloudresourcemanager.googleapis.com/v1/operations/cp.1")
            {
                // Still making it the first time it is asked about.
                var done = _projectAskedOnce;
                _projectAskedOnce = true;
                return Reply(done ? """{"name":"operations/cp.1","done":true}""" : """{"name":"operations/cp.1"}""");
            }

            if (post && address.EndsWith("/services/generativelanguage.googleapis.com:enable", StringComparison.Ordinal))
            {
                return Reply("""{"name":"operations/noop.DONE_OPERATION","done":true}""");
            }

            if (address.EndsWith("/locations/global/keys", StringComparison.Ordinal))
            {
                if (!post)
                {
                    return Reply(HasKey
                        ? """{"keys":[{"name":"projects/1/locations/global/keys/k1","displayName":"Học tiếng Trung"}]}"""
                        : "{}");
                }

                KeyRequest = await request.Content!.ReadAsStringAsync(cancellationToken);
                return Reply("""{"name":"operations/akmf.1","done":true,"response":{"name":"projects/1/locations/global/keys/k1","keyString":""" +
                             $"\"{Key}\"" + "}}");
            }

            if (address == "https://apikeys.googleapis.com/v2/projects/1/locations/global/keys/k1/keyString")
            {
                return Reply($$"""{"keyString":"{{Key}}"}""");
            }

            return Reply("""{"error":{"message":"not expected"}}""", HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Reply(string json, HttpStatusCode status = HttpStatusCode.OK) =>
            new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }
}
