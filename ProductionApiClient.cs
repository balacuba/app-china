using System.Net.Http.Headers;
using System.Security.Authentication;
using System.Text;
using System.Text.Json;

namespace WinFormsApp1;

internal sealed class ProductionApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly Uri _apiBaseUri;
    private readonly Uri _tokenUri;
    private readonly string _clientId;
    private readonly string _clientSecret;
    private readonly string _username;
    private readonly string _password;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _tokenExpiresAt;

    public ProductionApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _apiBaseUri = new Uri(
            GetSetting("FCIM_API_BASE_URL", "http://fcim.mkvision.com:3000/services/api/"),
            UriKind.Absolute);
        _tokenUri = new Uri(
            GetSetting(
                "FCIM_TOKEN_URL",
                "http://14.241.68.10:8800/auth/realms/QLSX/protocol/openid-connect/token"),
            UriKind.Absolute);
        _clientId = GetSetting("FCIM_CLIENT_ID", "planning_backend");
        _clientSecret = GetRequiredSetting("FCIM_CLIENT_SECRET");
        _username = GetRequiredSetting("FCIM_USERNAME");
        _password = GetRequiredSetting("FCIM_PASSWORD");
    }

    public async Task<SerialProductionInfo> GetSerialProductionInfoAsync(
        string serialNumber,
        CancellationToken cancellationToken = default)
    {
        var token = await GetAccessTokenAsync(cancellationToken);
        var query = $"?keyName=serialNumber&keyValue={Uri.EscapeDataString(serialNumber)}";
        var historyUri = new Uri(_apiBaseUri, $"serial-number-info/get-all-for-history{query}");
        using var historyRequest = new HttpRequestMessage(HttpMethod.Post, historyUri)
        {
            Content = JsonContent(new
            {
                pageIndex = 0,
                pageSize = 10,
                filter = new { }
            })
        };
        historyRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var historyDocument = await SendJsonAsync(historyRequest, cancellationToken);
        EnsureApiSuccess(historyDocument.RootElement, "Tra cứu lịch sử SN");
        var returnedSerialNumber = FindString(historyDocument.RootElement, "serialNumber");
        if (!string.IsNullOrWhiteSpace(returnedSerialNumber) &&
            !string.Equals(returnedSerialNumber, serialNumber, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "API lịch sử trả về thông tin của Serial Number khác.");
        }

        var workOrderCode = FindString(historyDocument.RootElement,
            "workOrderCode", "workOrder", "woCode", "wo");
        var currentStageCode = FindString(
            historyDocument.RootElement,
            "currentStageCode",
            "currentProcessCode");
        var currentStageName = FindString(
            historyDocument.RootElement,
            "currentStageName",
            "currentProcessName",
            "currentStage",
            "currentProcess");
        if (string.IsNullOrWhiteSpace(workOrderCode))
        {
            throw new InvalidOperationException(
                "API lịch sử không trả về workOrderCode cho Serial Number này.");
        }

        var profileUri = new Uri(_apiBaseUri,
            $"aps-profile-detail/{Uri.EscapeDataString(workOrderCode)}");
        using var profileRequest = new HttpRequestMessage(HttpMethod.Get, profileUri);
        profileRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var profileDocument = await SendJsonAsync(profileRequest, cancellationToken);
        EnsureApiSuccess(profileDocument.RootElement, "Lấy thông tin WO/công đoạn");
        currentStageCode ??= FindString(
            profileDocument.RootElement,
            "currentStageCode",
            "currentProcessCode");
        currentStageName ??= FindString(
            profileDocument.RootElement,
            "currentStageName",
            "currentProcessName",
            "currentStage",
            "currentProcess");

        var stages = FindStages(profileDocument.RootElement);
        if (stages.Count == 0)
        {
            throw new InvalidOperationException(
                $"API profile không trả về danh sách công đoạn cho WO '{workOrderCode}'.");
        }

        return new SerialProductionInfo(workOrderCode, currentStageCode, currentStageName, stages);
    }

    public async Task<StageCheckResult> CheckCanTestAsync(
        string stageCode,
        string serialNumber,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(stageCode))
        {
            throw new ArgumentException("Stage code không được để trống.", nameof(stageCode));
        }

        if (string.IsNullOrWhiteSpace(serialNumber))
        {
            throw new ArgumentException("Serial Number không được để trống.", nameof(serialNumber));
        }

        var token = await GetAccessTokenAsync(cancellationToken);
        var requestUri = new Uri(_apiBaseUri, "serial-number-info/partner/check-can-test");
        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = JsonContent(new { stageCode, serialNumber })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var responseDocument = await SendJsonAsync(
            request,
            cancellationToken,
            allowBusinessRejection: true);
        var responseCode = FindString(responseDocument.RootElement, "responseCode");
        var message = FindString(responseDocument.RootElement, "message");
        if (string.IsNullOrWhiteSpace(message))
        {
            throw new InvalidOperationException(
                "API kiểm tra công đoạn không trả về result.message.");
        }

        if (FindBoolean(responseDocument.RootElement, "ok") == false ||
            (responseCode is not null && responseCode != "00"))
        {
            return new StageCheckResult(false, message);
        }

        EnsureApiSuccess(responseDocument.RootElement, "Kiểm tra công đoạn của SN");
        return new StageCheckResult(
            string.Equals(message, "YES", StringComparison.OrdinalIgnoreCase),
            message);
    }

    public async Task SubmitResultAsync(
        string stageCode,
        string serialNumber,
        bool passed,
        string? errorCode = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(stageCode))
        {
            throw new ArgumentException("Stage code không được để trống.", nameof(stageCode));
        }

        if (string.IsNullOrWhiteSpace(serialNumber))
        {
            throw new ArgumentException("Serial Number không được để trống.", nameof(serialNumber));
        }

        if (!passed && string.IsNullOrWhiteSpace(errorCode))
        {
            throw new ArgumentException(
                "Khi gửi FAIL, errorCode là bắt buộc theo hợp đồng API.",
                nameof(errorCode));
        }

        var token = await GetAccessTokenAsync(cancellationToken);
        var requestUri = new Uri(_apiBaseUri, "production-supervisor/partner/test-products");
        var failedSerialNumbers = passed
            ? Array.Empty<FailedSerialNumber>()
            : new[] { new FailedSerialNumber(serialNumber, errorCode!) };
        var passedSerialNumbers = passed
            ? new[] { new PassedSerialNumber(serialNumber) }
            : Array.Empty<PassedSerialNumber>();
        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = JsonContent(new
            {
                stageCode,
                failedListSerialNumber = failedSerialNumbers,
                passedListSerialNumber = passedSerialNumbers
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var responseDocument = await SendJsonAsync(request, cancellationToken);
        EnsureApiSuccess(responseDocument.RootElement, "Gửi kết quả PASS/FAIL");
        if (FindBoolean(responseDocument.RootElement, "ok") is not true)
        {
            throw new InvalidOperationException(
                "API không xác nhận kết quả PASS/FAIL thành công (thiếu ok=true).");
        }
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(_accessToken) &&
            _tokenExpiresAt > DateTimeOffset.UtcNow.AddSeconds(30))
        {
            return _accessToken;
        }

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (!string.IsNullOrEmpty(_accessToken) &&
                _tokenExpiresAt > DateTimeOffset.UtcNow.AddSeconds(30))
            {
                return _accessToken;
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, _tokenUri)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "password",
                    ["client_id"] = _clientId,
                    ["client_secret"] = _clientSecret,
                    ["username"] = _username,
                    ["password"] = _password
                })
            };
            using var document = await SendJsonAsync(request, cancellationToken);
            if (!document.RootElement.TryGetProperty("access_token", out var tokenElement) ||
                tokenElement.ValueKind != JsonValueKind.String)
            {
                throw new InvalidOperationException(
                    "Token API không trả về access_token hợp lệ.");
            }

            var accessToken = tokenElement.GetString();
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                throw new InvalidOperationException(
                    "Token API không trả về access_token hợp lệ.");
            }

            _accessToken = accessToken;
            var expiresIn = document.RootElement.TryGetProperty("expires_in", out var expiresElement) &&
                            expiresElement.TryGetInt32(out var seconds)
                ? seconds
                : 60;
            _tokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(1, expiresIn));
            return accessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private async Task<JsonDocument> SendJsonAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken,
        bool allowBusinessRejection = false)
    {
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex) when (ContainsAuthenticationException(ex))
        {
            var endpoint = request.RequestUri;
            throw new HttpRequestException(
                $"Không thiết lập được TLS tới {endpoint?.Host}:{endpoint?.Port}. " +
                "Kiểm tra FCIM_TOKEN_URL/API URL và cấu hình HTTPS của máy chủ. " +
                "Không chuyển sang HTTP vì request chứa credentials hoặc access token.",
                ex);
        }

        using (response)
        {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var isBusinessRejection = allowBusinessRejection &&
            response.StatusCode == System.Net.HttpStatusCode.BadRequest;
        if (!response.IsSuccessStatusCode && !isBusinessRejection)
        {
            var responseDetails = FormatErrorBody(body);
            throw new HttpRequestException(
                $"{request.Method} {request.RequestUri?.AbsolutePath} trả HTTP {(int)response.StatusCode} ({response.StatusCode})." +
                (responseDetails.Length == 0 ? string.Empty : $" Phản hồi máy chủ: {responseDetails}"));
        }

        try
        {
            var document = JsonDocument.Parse(body);
            if (isBusinessRejection)
            {
                var apiOk = FindBoolean(document.RootElement, "ok");
                var apiResponseCode = FindString(document.RootElement, "responseCode");
                if (apiOk != false && (apiResponseCode is null || apiResponseCode == "00"))
                {
                    document.Dispose();
                    var responseDetails = FormatErrorBody(body);
                    throw new HttpRequestException(
                        $"{request.Method} {request.RequestUri?.AbsolutePath} trả HTTP {(int)response.StatusCode} ({response.StatusCode})." +
                        (responseDetails.Length == 0 ? string.Empty : $" Phản hồi máy chủ: {responseDetails}"));
                }
            }

            return document;
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"API {request.RequestUri?.AbsolutePath} trả về dữ liệu không phải JSON hợp lệ.", ex);
        }
        }
    }

    private static string FormatErrorBody(string body)
    {
        var normalized = string.Join(
            ' ',
            body.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        const int maxLength = 600;
        return normalized.Length <= maxLength
            ? normalized
            : normalized[..maxLength] + "...";
    }

    private static bool ContainsAuthenticationException(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is AuthenticationException)
            {
                return true;
            }
        }

        return false;
    }

    private static void EnsureApiSuccess(JsonElement root, string operation)
    {
        var ok = FindBoolean(root, "ok");
        if (ok == false)
        {
            var message = FindString(root, "message");
            throw new InvalidOperationException(
                $"{operation} thất bại{(string.IsNullOrWhiteSpace(message) ? "." : $": {message}")}");
        }

        var responseCode = FindString(root, "responseCode");
        if (responseCode is not null && responseCode != "00")
        {
            var message = FindString(root, "message");
            throw new InvalidOperationException(
                $"{operation} thất bại (responseCode {responseCode}){(string.IsNullOrWhiteSpace(message) ? "." : $": {message}")}");
        }

        if (responseCode is null && ok is not true)
        {
            throw new InvalidOperationException(
                $"{operation}: response API không xác nhận thành công (thiếu responseCode '00' hoặc ok=true).");
        }
    }

    private static List<ProductionStage> FindStages(JsonElement root)
    {
        var stages = new List<ProductionStage>();
        CollectStages(root, stages, inStageList: false);
        return stages
            .GroupBy(stage => stage.StageCode, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    private static void CollectStages(
        JsonElement element,
        List<ProductionStage> stages,
        bool inStageList)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var code = FindDirectString(element,
                inStageList
                    ? new[] { "stageCode", "processCode", "operationCode", "workStepCode", "code" }
                    : new[] { "stageCode", "processCode", "operationCode", "workStepCode" });
            if (!string.IsNullOrWhiteSpace(code))
            {
                var name = FindDirectString(element,
                    inStageList
                        ? new[] { "stageName", "processName", "operationName", "workStepName", "name", "description" }
                        : new[] { "stageName", "processName", "operationName", "workStepName" });
                stages.Add(new ProductionStage(code, string.IsNullOrWhiteSpace(name) ? code : name));
            }

            foreach (var property in element.EnumerateObject())
            {
                var isStageList = inStageList ||
                    property.Name.Contains("stage", StringComparison.OrdinalIgnoreCase) ||
                    property.Name.Contains("process", StringComparison.OrdinalIgnoreCase) ||
                    property.Name.Contains("operation", StringComparison.OrdinalIgnoreCase) ||
                    property.Name.Contains("congdoan", StringComparison.OrdinalIgnoreCase);
                CollectStages(property.Value, stages, isStageList);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                CollectStages(item, stages, inStageList);
            }
        }
    }

    private static string? FindDirectString(JsonElement element, string[] propertyNames)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (propertyNames.Contains(property.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (property.Value.ValueKind == JsonValueKind.String)
                {
                    return property.Value.GetString();
                }

                if (property.Value.ValueKind == JsonValueKind.Number)
                {
                    return property.Value.ToString();
                }
            }
        }

        return null;
    }

    private static string? FindString(JsonElement element, params string[] propertyNames)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                foreach (var name in propertyNames)
                {
                    if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        if (property.Value.ValueKind == JsonValueKind.String)
                        {
                            return property.Value.GetString();
                        }

                        if (property.Value.ValueKind == JsonValueKind.Number)
                        {
                            return property.Value.ToString();
                        }
                    }
                }
            }

            foreach (var property in element.EnumerateObject())
            {
                var value = FindString(property.Value, propertyNames);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var value = FindString(item, propertyNames);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
        }

        return null;
    }

    private static bool? FindBoolean(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase) &&
                    property.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    return property.Value.GetBoolean();
                }

                var value = FindBoolean(property.Value, propertyName);
                if (value.HasValue)
                {
                    return value;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var value = FindBoolean(item, propertyName);
                if (value.HasValue)
                {
                    return value;
                }
            }
        }

        return null;
    }

    private static StringContent JsonContent<T>(T value) =>
        new(JsonSerializer.Serialize(value, JsonOptions), Encoding.UTF8, "application/json");

    private static string GetRequiredSetting(string name) =>
        GetEnvironmentSetting(name) ??
        throw new InvalidOperationException(
            $"Thiếu cấu hình biến môi trường {name}. Hãy cấu hình trước khi chạy ứng dụng.");

    private static string GetSetting(string name, string defaultValue) =>
        GetEnvironmentSetting(name) ?? defaultValue;

    private static string? GetEnvironmentSetting(string name)
    {
        var processValue = Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Process);
        if (!string.IsNullOrWhiteSpace(processValue))
        {
            return processValue;
        }

        var userValue = Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User);
        return string.IsNullOrWhiteSpace(userValue) ? null : userValue;
    }

    private sealed record FailedSerialNumber(string SerialNumber, string ErrorCode);
    private sealed record PassedSerialNumber(string SerialNumber);
}

internal sealed record SerialProductionInfo(
    string WorkOrderCode,
    string? CurrentStageCode,
    string? CurrentStageName,
    IReadOnlyList<ProductionStage> Stages);

internal sealed record StageCheckResult(bool CanTest, string Message);

internal sealed record ProductionStage(string StageCode, string StageName);
