using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace DotNetTestRunner.Presentation.Behaviors;

/// <summary>
/// Attached behavior that renders the plain-text execution log into a read-only
/// <see cref="RichTextBox"/>, colorizing section headers, echoed commands, and
/// passed/failed test result lines so failures stand out at a glance.
/// </summary>
public static class OutputLogBehavior
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
        "Text",
        typeof(string),
        typeof(OutputLogBehavior),
        new PropertyMetadata(string.Empty, OnTextChanged));

    private static readonly Brush HeaderBrush = CreateFrozenBrush("#4C8DFF");
    private static readonly Brush MutedBrush = CreateFrozenBrush("#7C879C");
    private static readonly Brush FailBrush = CreateFrozenBrush("#FF7A89");
    private static readonly Brush PassBrush = CreateFrozenBrush("#55D699");
    private static readonly Brush DefaultBrush = CreateFrozenBrush("#DCE3F0");

    /// <summary>Gets the plain-text log content bound to a <see cref="RichTextBox"/>.</summary>
    public static string GetText(DependencyObject element)
    {
        return (string)element.GetValue(TextProperty);
    }

    /// <summary>Sets the plain-text log content to render into a <see cref="RichTextBox"/>.</summary>
    public static void SetText(DependencyObject element, string value)
    {
        element.SetValue(TextProperty, value);
    }

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not RichTextBox richTextBox)
        {
            return;
        }

        string text = e.NewValue as string ?? string.Empty;
        richTextBox.Document = BuildDocument(text);
        richTextBox.ScrollToEnd();
    }

    private static FlowDocument BuildDocument(string text)
    {
        FlowDocument document = new()
        {
            PagePadding = new Thickness(0),
            FontFamily = new FontFamily("Consolas"),
        };

        Paragraph paragraph = new() { Margin = new Thickness(0) };
        string[] lines = text.Length == 0 ? [] : text.Replace("\r\n", "\n").Split('\n');

        for (int i = 0; i < lines.Length; i++)
        {
            paragraph.Inlines.Add(new Run(lines[i]) { Foreground = GetLineBrush(lines[i]) });

            if (i < lines.Length - 1)
            {
                paragraph.Inlines.Add(new LineBreak());
            }
        }

        document.Blocks.Add(paragraph);
        return document;
    }

    private static Brush GetLineBrush(string line)
    {
        string trimmed = line.TrimStart();

        if (trimmed.StartsWith("---", StringComparison.Ordinal))
        {
            return HeaderBrush;
        }

        if (trimmed.StartsWith(">", StringComparison.Ordinal))
        {
            return MutedBrush;
        }

        if (trimmed.StartsWith("Failed", StringComparison.Ordinal) ||
            trimmed.Contains("Failed!", StringComparison.Ordinal))
        {
            return FailBrush;
        }

        if (trimmed.StartsWith("Passed", StringComparison.Ordinal))
        {
            return PassBrush;
        }

        return DefaultBrush;
    }

    private static Brush CreateFrozenBrush(string hex)
    {
        Brush brush = (Brush)new BrushConverter().ConvertFromString(hex)!;
        brush.Freeze();
        return brush;
    }
}
