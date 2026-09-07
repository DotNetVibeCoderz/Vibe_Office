// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

namespace OfficeNet.Core;

/// <summary>
/// Raised when a document cannot be read, written or interpreted — a malformed package, a part
/// that does not match its declared content type, an unsupported feature.
/// </summary>
/// <remarks>
/// Argument validation stays on the framework exceptions (<see cref="ArgumentException"/>,
/// <see cref="ArgumentOutOfRangeException"/>): those report a caller's mistake, while this one
/// reports something about the document. Keeping them separate is what lets an application catch
/// "this file is broken" without also swallowing its own bugs.
/// </remarks>
public class OfficeNetException : Exception
{
    /// <summary>Creates the exception.</summary>
    public OfficeNetException(string message) : base(message)
    {
    }

    /// <summary>Creates the exception with an inner cause.</summary>
    public OfficeNetException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>Raised when a document uses a feature this library does not implement.</summary>
public sealed class OfficeNetNotSupportedException : OfficeNetException
{
    /// <summary>Creates the exception.</summary>
    public OfficeNetNotSupportedException(string message) : base(message)
    {
    }

    /// <summary>Creates the exception with an inner cause.</summary>
    public OfficeNetNotSupportedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>Raised when a password-protected document cannot be decrypted.</summary>
public sealed class OfficeNetPasswordException : OfficeNetException
{
    /// <summary>Creates the exception.</summary>
    public OfficeNetPasswordException(string message) : base(message)
    {
    }

    /// <summary>Creates the exception with an inner cause.</summary>
    public OfficeNetPasswordException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
