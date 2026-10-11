namespace Articles.Domain.Exceptions;

/// <summary>The requested resource does not exist. Surfaces as HTTP 404.</summary>
public class NotFoundException : Exception
{
    public NotFoundException(string name, object key)
        : base($"{name} '{key}' was not found.") { }

    public NotFoundException(string message) : base(message) { }
}
