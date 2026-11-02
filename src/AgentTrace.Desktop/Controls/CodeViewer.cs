using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;
using AgentTrace.Desktop.Theme;
using AgentTrace.Presentation.Highlight;
using AgentTrace.Presentation.Text;

namespace AgentTrace.Desktop.Controls;

/// <summary>
/// Read-only viewer (AvaloniaEdit): text with syntax highlighting and line numbers, or a diff with a green/red background
/// per line, +/− prefixes, two number columns and highlighted content. Highlighting is requested per line, only for the visible ones.
/// </summary>
public sealed class CodeViewer : UserControl
{
    private readonly TextEditor _editor;
    private readonly RowColorizer _colorizer = new();
    private readonly DiffBackgroundRenderer _background = new();
    private DoubleLineNumberMargin? _diffMargin;

    public CodeViewer()
    {
        _editor = new TextEditor
        {
            IsReadOnly = true,
            ShowLineNumbers = true,
            FontFamily = new FontFamily("Cascadia Mono, Consolas, Menlo, DejaVu Sans Mono, monospace"),
            FontSize = 13,
            Background = AppColors.Brush(AppColors.Background),
            Foreground = AppColors.Brush(AppColors.Text),
            LineNumbersForeground = AppColors.Brush(AppColors.Muted),
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Document = new TextDocument(),
        };
        _editor.TextArea.TextView.LineTransformers.Add(_colorizer);
        Content = _editor;
    }

    public TextEditor Editor => _editor;

    /// <summary>The text shown right now (for tests).</summary>
    public string Text => _editor.Document.Text;

    /// <summary>Shows a file. With <paramref name="keepPosition"/> the reading position is kept (reload of an open file).</summary>
    public void ShowText(IReadOnlyList<string> lines, string fileName, bool keepPosition = false)
    {
        double offset = keepPosition ? _editor.VerticalOffset : 0;
        SetDiffMode(false);
        SyntaxTokenizer tokenizer = SyntaxTokenizer.ForFile(fileName, lines);
        _colorizer.Setup(line => tokenizer.Tokens(line), null, 0);
        _editor.Document = new TextDocument(string.Join("\n", lines));
        _editor.ScrollToVerticalOffset(offset);
        if (!keepPosition) _editor.TextArea.Caret.Offset = 0;
    }

    /// <summary>Shows a diff: green/red background per line, +/− prefixes, two number columns and highlighted content.</summary>
    public void ShowDiff(IReadOnlyList<DiffRow> rows, string fileName)
    {
        SetDiffMode(true);
        IReadOnlyList<string> codeOnly = [.. rows.Select(r => r.IsCode ? r.Text : "")];
        SyntaxTokenizer tokenizer = SyntaxTokenizer.IndependentLines(fileName, codeOnly);
        _colorizer.Setup(line => tokenizer.Tokens(line), rows, 1);
        _background.Rows = rows;
        _diffMargin!.Set([.. rows.Select(r => r.OldLine ?? 0)], [.. rows.Select(r => r.NewLine ?? 0)]);
        _editor.Document = new TextDocument(string.Join("\n", rows.Select(r => r.DisplayText)));
        _editor.ScrollToHome();
        _editor.TextArea.Caret.Offset = 0;
    }

    private void SetDiffMode(bool diff)
    {
        if (diff == (_diffMargin is not null)) return;
        TextView view = _editor.TextArea.TextView;
        if (diff)
        {
            _editor.ShowLineNumbers = false;
            _diffMargin = new DoubleLineNumberMargin(_editor);
            _editor.TextArea.LeftMargins.Insert(0, _diffMargin);
            view.BackgroundRenderers.Add(_background);
        }
        else
        {
            _editor.TextArea.LeftMargins.Remove(_diffMargin!);
            view.BackgroundRenderers.Remove(_background);
            _diffMargin = null;
            _background.Rows = null;
            _editor.ShowLineNumbers = true;
        }
    }

    private sealed class RowColorizer : DocumentColorizingTransformer
    {
        private static readonly IBrush Keyword = AppColors.Brush(AppColors.SyntaxKeyword);
        private static readonly IBrush Str = AppColors.Brush(AppColors.SyntaxString);
        private static readonly IBrush Comment = AppColors.Brush(AppColors.SyntaxComment);
        private static readonly IBrush Number = AppColors.Brush(AppColors.SyntaxNumber);

        private Func<int, IReadOnlyList<Token>> _tokens = _ => [];
        private IReadOnlyList<DiffRow>? _rows;
        private int _prefix;

        public void Setup(Func<int, IReadOnlyList<Token>> tokens, IReadOnlyList<DiffRow>? rows, int prefix)
        {
            _tokens = tokens;
            _rows = rows;
            _prefix = prefix;
        }

        protected override void ColorizeLine(DocumentLine line)
        {
            int index = line.LineNumber - 1;
            if (_rows is not null)
            {
                if (index >= _rows.Count) return;
                DiffRow row = _rows[index];
                switch (row.Kind)
                {
                    case DiffRow.RowKind.Header:
                        Paint(line.Offset, line.EndOffset, AppColors.Ignored, bold: true);
                        return;
                    case DiffRow.RowKind.Hunk:
                        Paint(line.Offset, line.EndOffset, AppColors.Read, bold: false);
                        return;
                    case DiffRow.RowKind.Message:
                        Paint(line.Offset, line.EndOffset, AppColors.Muted, bold: false);
                        return;
                    case DiffRow.RowKind.Add:
                        Paint(line.Offset, line.Offset + 1, AppColors.Created, bold: true);
                        break;
                    case DiffRow.RowKind.Del:
                        Paint(line.Offset, line.Offset + 1, AppColors.Deleted, bold: true);
                        break;
                }
            }
            foreach (Token token in _tokens(index))
            {
                IBrush brush = token.Type switch
                {
                    TokenType.Keyword => Keyword,
                    TokenType.String => Str,
                    TokenType.Comment => Comment,
                    TokenType.Number => Number,
                    _ => throw new System.Diagnostics.UnreachableException(),
                };
                int start = line.Offset + _prefix + token.Start;
                int end = Math.Min(line.EndOffset, line.Offset + _prefix + token.End);
                if (start >= end) continue;
                ChangeLinePart(start, end, e => e.TextRunProperties.SetForegroundBrush(brush));
            }
        }

        private void Paint(int start, int end, Color color, bool bold)
        {
            if (start >= end) return;
            IBrush brush = AppColors.Brush(color);
            ChangeLinePart(start, end, e =>
            {
                e.TextRunProperties.SetForegroundBrush(brush);
                if (bold) e.TextRunProperties.SetTypeface(new Typeface(e.TextRunProperties.Typeface.FontFamily, e.TextRunProperties.Typeface.Style, FontWeight.Bold));
            });
        }
    }

    /// <summary>Background per line (added, removed or hunk header) for the diff mode.</summary>
    private sealed class DiffBackgroundRenderer : IBackgroundRenderer
    {
        private static readonly IBrush Added = new SolidColorBrush(Color.Parse("#4026A641"));
        private static readonly IBrush Removed = new SolidColorBrush(Color.Parse("#40F85149"));
        private static readonly IBrush Hunk = new SolidColorBrush(Color.Parse("#30388BFD"));

        public IReadOnlyList<DiffRow>? Rows { get; set; }

        public KnownLayer Layer => KnownLayer.Background;

        public void Draw(TextView textView, DrawingContext drawingContext)
        {
            if (Rows is null || !textView.VisualLinesValid) return;
            foreach (VisualLine visual in textView.VisualLines)
            {
                int index = visual.FirstDocumentLine.LineNumber - 1;
                if (index >= Rows.Count) continue;
                IBrush? brush = Rows[index].Kind switch
                {
                    DiffRow.RowKind.Add => Added,
                    DiffRow.RowKind.Del => Removed,
                    DiffRow.RowKind.Hunk => Hunk,
                    _ => null,
                };
                if (brush is null) continue;
                double y = visual.GetTextLineVisualYPosition(visual.TextLines[0], VisualYPosition.LineTop) - textView.VerticalOffset;
                drawingContext.FillRectangle(brush, new Rect(0, y, textView.Bounds.Width, visual.Height));
            }
        }
    }

    /// <summary>Margin with two columns of line numbers (original and new). A 0 is left blank.</summary>
    private sealed class DoubleLineNumberMargin(TextEditor editor) : AbstractMargin
    {
        private const double ColumnWidth = 52;
        private static readonly IBrush Foreground = AppColors.Brush(AppColors.Muted);

        private int[] _old = [];
        private int[] _new = [];

        public void Set(int[] oldNumbers, int[] newNumbers)
        {
            _old = oldNumbers;
            _new = newNumbers;
            InvalidateVisual();
        }

        protected override Size MeasureOverride(Size availableSize) => new(ColumnWidth * 2 + 8, 0);

        protected override void OnTextViewChanged(TextView? oldTextView, TextView? newTextView)
        {
            if (oldTextView is not null) oldTextView.VisualLinesChanged -= OnVisualLinesChanged;
            base.OnTextViewChanged(oldTextView, newTextView);
            if (newTextView is not null) newTextView.VisualLinesChanged += OnVisualLinesChanged;
            InvalidateVisual();
        }

        private void OnVisualLinesChanged(object? sender, EventArgs e) => InvalidateVisual();

        public override void Render(DrawingContext context)
        {
            TextView? textView = TextView;
            if (textView is null || !textView.VisualLinesValid) return;
            var typeface = new Typeface(editor.FontFamily);
            foreach (VisualLine visual in textView.VisualLines)
            {
                int index = visual.FirstDocumentLine.LineNumber - 1;
                if (index >= _old.Length) continue;
                double y = visual.GetTextLineVisualYPosition(visual.TextLines[0], VisualYPosition.TextTop) - textView.VerticalOffset;
                Draw(context, _old[index], 0, y, typeface);
                Draw(context, _new[index], ColumnWidth, y, typeface);
            }
        }

        private void Draw(DrawingContext context, int number, double x, double y, Typeface typeface)
        {
            if (number == 0) return;
            var text = new FormattedText(number.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, typeface, editor.FontSize - 1, Foreground);
            context.DrawText(text, new Point(x + ColumnWidth - text.Width - 4, y));
        }
    }
}
