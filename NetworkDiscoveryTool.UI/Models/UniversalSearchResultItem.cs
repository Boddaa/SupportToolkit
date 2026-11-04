using System;
using Brush = System.Windows.Media.Brush;
using BrushConverter = System.Windows.Media.BrushConverter;

namespace NetworkDiscoveryTool.UI.Models;

public sealed class UniversalSearchResultItem
{
    private static readonly BrushConverter Converter = new();

    public string Title { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public bool HasStatus => !string.IsNullOrEmpty(Status);
    public string ExtraInfo { get; set; } = string.Empty;
    public bool HasExtraInfo => !string.IsNullOrEmpty(ExtraInfo);

    public string IconGlyph { get; set; } = "\uE7F8";
    public Brush IconColor { get; set; } = (Brush)Converter.ConvertFrom("#38BDF8")!;
    public Brush IconBg { get; set; } = (Brush)Converter.ConvertFrom("#150EA5E9")!;
    public Brush IconBorder { get; set; } = (Brush)Converter.ConvertFrom("#300EA5E9")!;

    public Brush BadgeColor { get; set; } = (Brush)Converter.ConvertFrom("#38BDF8")!;
    public Brush BadgeBg { get; set; } = (Brush)Converter.ConvertFrom("#150EA5E9")!;
    public Brush BadgeBorder { get; set; } = (Brush)Converter.ConvertFrom("#300EA5E9")!;

    public Brush StatusDotColor { get; set; } = (Brush)Converter.ConvertFrom("#10B981")!;
    public Brush StatusTextColor { get; set; } = (Brush)Converter.ConvertFrom("#10B981")!;
    public Brush StatusBg { get; set; } = (Brush)Converter.ConvertFrom("#1510B981")!;
    public Brush StatusBorder { get; set; } = (Brush)Converter.ConvertFrom("#3010B981")!;

    public Action? OnClick { get; set; }
}
