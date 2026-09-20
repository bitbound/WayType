namespace WayType.Libraries.Core.Speech;

public sealed class AiEndpointException(string message, int? statusCode = null, string? detail = null)
    : Exception(detail is null ? message : $"{message} {detail}")
{
    public int? StatusCode { get; } = statusCode;

    public string? Detail { get; } = detail;
}
