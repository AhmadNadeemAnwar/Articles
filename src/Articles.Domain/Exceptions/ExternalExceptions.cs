namespace Articles.Domain.Exceptions;

/// <summary>A service this API depends on (such as GitHub) failed or refused the request. Surfaces as HTTP 502.</summary>
public class ExternalServiceException : Exception
{
    public ExternalServiceException(string message) : base(message) { }
}

/// <summary>A feature is switched off because its configuration is missing. Surfaces as HTTP 503.</summary>
public class ServiceUnavailableException : Exception
{
    public ServiceUnavailableException(string message) : base(message) { }
}
