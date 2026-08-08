namespace Api.Shared.Common;

public sealed class OptionsValidationException(string message) : Exception(message);
