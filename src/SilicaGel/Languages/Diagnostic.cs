namespace SilicaGel;

public enum DiagnosticSeverity { Error, Warning, Info }

public sealed record Diagnostic(int Line, int Column, int Length, string Message, DiagnosticSeverity Severity)
{
    public string SeverityText => Severity switch
    {
        DiagnosticSeverity.Error   => "Error",
        DiagnosticSeverity.Warning => "Warning",
        _ => "Info",
    };
}
