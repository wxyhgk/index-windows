using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace Index.UI.Controls;

/// <summary>
/// Lightweight text input used by search surfaces. It deliberately avoids WinUI's
/// native text-editing host, which can fail-fast when third-party desktop input hooks
/// are injected into an unpackaged WinUI process.
/// </summary>
internal sealed class SearchInputBox : Button
{
    private readonly TextBlock _label = new()
    {
        TextTrimming = TextTrimming.CharacterEllipsis,
        VerticalAlignment = VerticalAlignment.Center
    };
    private string _text = string.Empty;
    private string _placeholderText = string.Empty;

    public SearchInputBox()
    {
        var content = new Grid { ColumnSpacing = 8 };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.Children.Add(new FontIcon
        {
            Glyph = "\uE721",
            FontSize = 13,
            Opacity = 0.62,
            VerticalAlignment = VerticalAlignment.Center
        });
        Grid.SetColumn(_label, 1);
        content.Children.Add(_label);
        IsTabStop = true;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        Padding = new Thickness(12, 0, 12, 0);
        BorderThickness = new Thickness(1);
        Content = content;
        Click += (_, _) => Focus(FocusState.Programmatic);
        CharacterReceived += OnCharacterReceived;
        KeyDown += OnInputKeyDown;
        GotFocus += (_, _) => UpdateLabel();
        LostFocus += (_, _) => UpdateLabel();
        UpdateLabel();
    }

    public string Text
    {
        get => _text;
        set
        {
            var normalized = value ?? string.Empty;
            if (string.Equals(_text, normalized, StringComparison.Ordinal))
                return;
            _text = normalized;
            UpdateLabel();
            TextChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public string PlaceholderText
    {
        get => _placeholderText;
        set
        {
            _placeholderText = value ?? string.Empty;
            UpdateLabel();
        }
    }

    public event EventHandler? TextChanged;

    private void OnCharacterReceived(UIElement sender, CharacterReceivedRoutedEventArgs args)
    {
        if (char.IsControl(args.Character))
            return;

        Text += args.Character;
        args.Handled = true;
    }

    private void OnInputKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key != VirtualKey.Back || _text.Length == 0)
            return;

        var remove = char.IsLowSurrogate(_text[^1]) && _text.Length > 1 ? 2 : 1;
        Text = _text[..^remove];
        args.Handled = true;
    }

    private void UpdateLabel()
    {
        var empty = string.IsNullOrEmpty(_text);
        _label.Text = empty ? _placeholderText : _text;
        _label.Opacity = empty ? 0.58 : 1;
    }
}
