namespace Api.Infrastructure.Common;

public sealed class OptionsValidationException(string message) : Exception(message);
