namespace Articles.Domain.Exceptions;

/// <summary>Caller is authenticated but not permitted to act on this resource. Surfaces as HTTP 403.</summary>
public class ForbiddenException : Exception
{
    public ForbiddenException(string message) : base(message) { }
}
