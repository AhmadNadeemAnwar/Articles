namespace Articles.Domain.Exceptions;

/// <summary>A business rule was violated. Surfaces as HTTP 400.</summary>
public class DomainException : Exception
{
    public DomainException(string message) : base(message) { }
}
