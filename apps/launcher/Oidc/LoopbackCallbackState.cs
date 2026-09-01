namespace Divinity.Launcher.Oidc;

public sealed class LoopbackCallbackState
{
    private readonly string _expectedState;
    private readonly string _callbackPath;
    private bool _accepted;

    public LoopbackCallbackState(string expectedState, string callbackPath)
    {
        _expectedState = expectedState;
        _callbackPath = callbackPath.StartsWith('/') ? callbackPath : $"/{callbackPath}";
    }

    public CallbackValidationResult Accept(Uri callbackUri)
    {
        if (!string.Equals(callbackUri.AbsolutePath, _callbackPath, StringComparison.Ordinal))
        {
            return CallbackValidationResult.Rejected(CallbackValidationStatus.UnexpectedPath, "Unexpected callback path.");
        }

        if (_accepted)
        {
            return CallbackValidationResult.Rejected(CallbackValidationStatus.DuplicateCallback, "Duplicate callback rejected.");
        }

        var query = QueryString.Parse(callbackUri.Query);
        if (query.TryGetValue("error", out var providerError))
        {
            _accepted = true;
            return CallbackValidationResult.Rejected(CallbackValidationStatus.ProviderError, $"Provider returned login error: {providerError}.");
        }

        if (!query.TryGetValue("state", out var state) || !string.Equals(state, _expectedState, StringComparison.Ordinal))
        {
            _accepted = true;
            return CallbackValidationResult.Rejected(CallbackValidationStatus.InvalidState, "Callback state did not match the active login attempt.");
        }

        if (!query.TryGetValue("code", out var code) || string.IsNullOrWhiteSpace(code))
        {
            _accepted = true;
            return CallbackValidationResult.Rejected(CallbackValidationStatus.MissingCode, "Callback did not contain an authorization code.");
        }

        _accepted = true;
        return CallbackValidationResult.Accepted(code);
    }
}

public enum CallbackValidationStatus
{
    Accepted,
    UnexpectedPath,
    InvalidState,
    MissingCode,
    ProviderError,
    DuplicateCallback
}

public sealed record CallbackValidationResult(CallbackValidationStatus Status, string? Code, string Message)
{
    public bool Success => Status == CallbackValidationStatus.Accepted;

    public static CallbackValidationResult Accepted(string code) =>
        new(CallbackValidationStatus.Accepted, code, "Authorization callback accepted.");

    public static CallbackValidationResult Rejected(CallbackValidationStatus status, string message) =>
        new(status, null, message);
}
