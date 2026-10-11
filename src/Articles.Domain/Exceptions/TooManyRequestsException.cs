namespace Articles.Domain.Exceptions;

/// <summary>The caller has been throttled. Surfaces as HTTP 429.</summary>
public class TooManyRequestsException : Exception
{
    public TooManyRequestsException(string message) : base(message) { }
}
