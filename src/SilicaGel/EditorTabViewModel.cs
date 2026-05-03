using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Input;
using AvaloniaEdit.Document;
using Axion.Scripting;
using ReactiveUI;

namespace SilicaGel;

public sealed class EditorTabViewModel : ViewModelBase
{
    public string FilePath  { get; }
    public string Language  { get; }
    public TextDocument Document { get; }

    private bool _dirty;
    public bool IsDirty { get => _dirty; set { if (SetField(ref _dirty, value)) OnPropertyChanged(nameof(Header)); } }
    public string Header => (Path.GetFileName(FilePath)) + (IsDirty ? " •" : "");

    private string _status = "";
    public string StatusText { get => _status; set => SetField(ref _status, value); }

    public ICommand CloseCommand { get; }
    public event Action<EditorTabViewModel>? RequestClose;

    /// <summary>Runs the script in-process via the standalone interpreter.</summary>
    public string Run()
    {
        var sw = new StringWriter();
        sw.WriteLine($"▶ Running {Path.GetFileName(FilePath)} ({Language})...");
        var start = DateTime.Now;
        try
        {
            ProgramNode prog = Language switch
            {
                "Gel"    => Gel.Parse(Document.Text),
                "Silica" => SilicaV2.Parse(Document.Text),
                _ => throw new Exception($"cannot run {Language} files (only .gel and .sil are supported)"),
            };
            var interp = new Interpreter(sw);
            interp.Run(prog);
            sw.WriteLine($"✓ Finished in {(DateTime.Now - start).TotalMilliseconds:0} ms");
        }
        catch (InterpreterRuntimeException ire) { sw.WriteLine($"✗ Runtime error (line {ire.Line}): {ire.Message}"); }
        catch (SilicaV2.ParseError pe) { sw.WriteLine($"✗ Silica parse error (line {pe.Line}, col {pe.Col}): {pe.Message}"); }
        catch (Exception ex) { sw.WriteLine($"✗ {ex.GetType().Name}: {ex.Message}"); }
        return sw.ToString();
    }

    // ── Diagnostics (squigglies + Problems panel) ────────────────────────────
    public IReadOnlyList<Diagnostic> Diagnostics { get; private set; } = Array.Empty<Diagnostic>();
    public event Action? DiagnosticsChanged;

    public string ProblemSummary
    {
        get
        {
            int e = 0, w = 0;
            foreach (var d in Diagnostics)
                if (d.Severity == DiagnosticSeverity.Error) e++;
                else if (d.Severity == DiagnosticSeverity.Warning) w++;
            if (e == 0 && w == 0) return "✓ no problems";
            return $"⚠ {e} error{(e==1?"":"s")}, {w} warning{(w==1?"":"s")}";
        }
    }

    private CancellationTokenSource? _validationCts;

    public EditorTabViewModel(string filePath, string contents)
    {
        FilePath = filePath;
        Language = DetectLanguage(filePath);
        Document = new TextDocument(contents);
        Document.TextChanged += (_, _) => { IsDirty = true; ScheduleValidate(); };
        CloseCommand = ReactiveCommand.Create(() => RequestClose?.Invoke(this));

        // Initial validation
        ScheduleValidate(immediate: true);
    }

    public void Save()
    {
        File.WriteAllText(FilePath, Document.Text);
        IsDirty = false;
        StatusText = $"saved {DateTime.Now:HH:mm:ss}";
    }

    private void ScheduleValidate(bool immediate = false)
    {
        if (Language != "Gel" && Language != "Silica") { Diagnostics = Array.Empty<Diagnostic>(); DiagnosticsChanged?.Invoke(); OnPropertyChanged(nameof(ProblemSummary)); return; }

        _validationCts?.Cancel();
        var cts = new CancellationTokenSource();
        _validationCts = cts;
        var snapshot = Document.Text;
        var lang = Language;
        var delay = immediate ? 0 : 350;

        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            try { if (delay > 0) await System.Threading.Tasks.Task.Delay(delay, cts.Token); }
            catch { return; }
            if (cts.IsCancellationRequested) return;

            var diags = ScriptValidator.Validate(snapshot, lang);

            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (cts.IsCancellationRequested) return;
                Diagnostics = diags;
                DiagnosticsChanged?.Invoke();
                OnPropertyChanged(nameof(ProblemSummary));
            });
        });
    }

    public static string DetectLanguage(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".gel"  => "Gel",
            ".sil"  => "Silica",
            ".cs"   => "C#",
            ".json" => "JSON",
            ".md"   => "Markdown",
            _       => "Plain Text",
        };
    }
}
