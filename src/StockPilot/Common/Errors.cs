namespace StockPilot;

public static class ErrorCodes
{
    public const string InvalidRequest = "InvalidRequest";
    public const string SkuRequired = "SkuRequired";
    public const string ItemNotFound = "ItemNotFound";
    public const string InvalidQuantity = "InvalidQuantity";
    public const string InsufficientStock = "InsufficientStock";
    public const string InvalidState = "InvalidState";
    public const string FileMissing = "FileMissing";
    public const string MalformedData = "MalformedData";
    public const string IoError = "IoError";
    public const string Internal = "InternalError";
    public const string OrderTooLarge = "OrderTooLarge";
    public const string InvalidFeeInput = "InvalidFeeInput";
    public const string NativeUnavailable = "NativeUnavailable";
    public const string NativeRejected = "NativeRejected";
    public const string ReferenceRequired = "ReferenceRequired";
    public const string DuplicateDelivery = "DuplicateDelivery";
    public const string CapacityExceeded = "CapacityExceeded";
    public const string InvalidArguments = "InvalidArguments";
}

public sealed record Error(string Code, string Message);

public static class ErrorList
{
    public static Error Combine(ImmutableArray<Error> errors) =>
        errors.IsDefaultOrEmpty
            ? new Error(ErrorCodes.Internal, "Validation failed without a reason.")
            : new Error(errors[0].Code, string.Join(" ", errors.Select(e => e.Message)));
}
