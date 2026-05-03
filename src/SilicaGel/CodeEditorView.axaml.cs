using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Highlighting;

namespace SilicaGel;

public partial class CodeEditorView : UserControl
{
    private TextEditor? _editor;
    private CompletionWindow? _completionWindow;
    private ErrorSquigglyRenderer? _squigglyRenderer;
    private EditorTabViewModel? _vm;

    /// <summary>The currently focused editor — used by File/Edit menu commands.</summary>
    public static TextEditor? Active { get; private set; }
    public static event Action? ActiveChanged;

    public CodeEditorView()
    {
        AvaloniaXamlLoader.Load(this);
        _editor = this.FindControl<TextEditor>("Editor");

        DataContextChanged    += (_, _) => OnDataContextChanged();
        AttachedToVisualTree  += (_, _) => { OnDataContextChanged(); SetActive(); };
        GotFocus              += (_, _) => SetActive();
        if (_editor != null) _editor.GotFocus += (_, _) => SetActive();
    }

    private void SetActive()
    {
        if (_editor == null) return;
        Active = _editor;
        ApplySettings();
        ActiveChanged?.Invoke();
    }

    public void ApplySettings()
    {
        if (_editor == null) return;
        var s = AppSettings.Load();
        _editor.FontFamily = new Avalonia.Media.FontFamily(s.FontFamily);
        _editor.FontSize   = s.FontSize;
        _editor.WordWrap   = s.WordWrap;
        _editor.ShowLineNumbers = s.LineNumbers;
        _editor.Options.IndentationSize = s.TabSize;
    }

    private void OnDataContextChanged()
    {
        if (_editor == null) return;
        if (DataContext is not EditorTabViewModel vm) return;
        if (_vm == vm) return;

        // Detach previous bindings
        if (_vm != null)
        {
            _vm.DiagnosticsChanged -= OnDiagnosticsChanged;
            _editor.TextArea.TextEntered -= TextArea_TextEntered;
            _editor.TextArea.KeyDown     -= TextArea_KeyDown;
            if (_squigglyRenderer != null)
                _editor.TextArea.TextView.BackgroundRenderers.Remove(_squigglyRenderer);
        }

        _vm = vm;
        _editor.Document = vm.Document;

        // Highlighting
        var hl = HighlightingManager.Instance.GetDefinition(vm.Language);
        _editor.SyntaxHighlighting = hl;

        // Squiggly renderer
        _squigglyRenderer = new ErrorSquigglyRenderer();
        _squigglyRenderer.SetDiagnostics(vm.Diagnostics);
        _editor.TextArea.TextView.BackgroundRenderers.Add(_squigglyRenderer);

        vm.DiagnosticsChanged += OnDiagnosticsChanged;

        // Completion + Ctrl+Space
        _editor.TextArea.TextEntered += TextArea_TextEntered;
        _editor.TextArea.KeyDown     += TextArea_KeyDown;
    }

    private void OnDiagnosticsChanged()
    {
        if (_squigglyRenderer == null || _vm == null || _editor == null) return;
        _squigglyRenderer.SetDiagnostics(_vm.Diagnostics);
        _editor.TextArea.TextView.InvalidateVisual();
    }

    private void TextArea_KeyDown(object? sender, KeyEventArgs e)
    {
        // Ctrl/Cmd + Space → open completion
        if (e.Key == Key.Space && (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0)
        {
            ShowCompletion();
            e.Handled = true;
        }
    }

    private void TextArea_TextEntered(object? sender, TextInputEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Text)) return;
        char c = e.Text[0];
        // Trigger on letter or '.'
        if (char.IsLetter(c) || c == '.')
        {
            // Avoid showing every keystroke if window already open — just refresh
            if (_completionWindow == null) ShowCompletion();
        }
    }

    private void ShowCompletion()
    {
        if (_editor == null || _vm == null) return;

        var data = _vm.Language switch
        {
            "Silica" => Completions.ForSilica,
            "Gel"    => Completions.ForGel,
            _ => null,
        };
        if (data == null) return;

        _completionWindow?.Close();
        _completionWindow = new CompletionWindow(_editor.TextArea);
        var list = _completionWindow.CompletionList.CompletionData;
        foreach (var item in data) list.Add(item);

        _completionWindow.Closed += (_, _) => _completionWindow = null;
        _completionWindow.Show();
    }
}
