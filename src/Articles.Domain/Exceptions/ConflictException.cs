namespace Articles.Domain.Exceptions;

/// <summary>Resource already exists or state transition clashes. Surfaces as HTTP 409.</summary>
public class ConflictException : Exception
{
    public ConflictException(string message) : base(message) { }
}
