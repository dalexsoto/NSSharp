namespace NSSharp.Lexer;

/// <summary>
/// Configuration options for the ObjC lexer.
/// </summary>
public sealed class ObjCLexerOptions
{
    /// <summary>
    /// Default options: macro heuristic enabled, no extra macros.
    /// </summary>
    public static ObjCLexerOptions Default { get; } = new();

    /// <summary>
    /// When true (default), identifiers matching UPPER_SNAKE_CASE pattern
    /// (e.g. PSPDF_CLASS_SWIFT, NS_SWIFT_NAME) are automatically detected
    /// and skipped as macros. Structural macros like NS_ENUM and
    /// NS_ASSUME_NONNULL_BEGIN are always preserved regardless of this setting.
    /// </summary>
    public bool MacroHeuristic { get; init; } = true;

    /// <summary>
    /// Macros that should map to <c>extern</c> instead of being skipped.
    /// Use for vendor export macros (e.g. "PSPDF_EXPORT", "FB_EXTERN").
    /// </summary>
    public IReadOnlyList<string> ExternMacros { get; init; } = [];

    /// <summary>
    /// Additional macros to always skip, regardless of naming pattern.
    /// Use for macros that don't follow UPPER_SNAKE_CASE but should be ignored.
    /// </summary>
    public IReadOnlyList<string> ExtraSkipMacros { get; init; } = [];
}
