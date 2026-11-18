using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace RFloor;

public partial class MainWindow
{
    private sealed record LabelRule(string Kind, string Title, bool Visible, string Format, string Tokens);
    private static readonly LabelRule[] LabelRules = [
        new("room", "Комната", true, "{name}\n{width} × {height} = {area}", "{name} {width} {height} {area} {x} {y}"),
        new("wall", "Стена", false, "{name}\n{length}", "{name} {length} {x} {y}"),
        new("door", "Проём", true, "{name}\n{length}", "{name} {length} {x} {y}"),
        new("router", "Роутер", true, "{name}", "{name} {x} {y}"),
        new("decoration", "Ориентир", false, "{name}", "{name} {width} {height} {area} {length} {x} {y}"),
        new("text", "Подпись", true, "{name}", "{name} {x} {y}"),
        new("tape", "Рулетка", true, "{name}\n{length}", "{name} {length} {x} {y}"),
        new("point", "Точка замера", true, "{name}", "{name} {x} {y}")
    ];

    private JsonObject labelDefaults = new();
    private static readonly Regex labelToken = new(@"\{([a-z]+)\}");
    private static LabelRule RuleFor(ObjectEntry entry)
    {
        string kind = entry.Kind == "decoration" && entry.Item["kind"]?.ToString() is "router" or "text"
            ? entry.Item["kind"]!.ToString() : entry.Kind;
        return LabelRules.Single(r => r.Kind == kind);
    }

    private JsonObject? DefaultLabel(LabelRule rule) => labelDefaults[rule.Kind] as JsonObject;
    private static bool? LabelFlag(JsonObject? options, string key) =>
        options?[key] is JsonValue v && v.TryGetValue<bool>(out var flag) ? flag : null;
    private static string? LabelString(JsonObject? options, string key) =>
        options?[key] is JsonValue v && v.TryGetValue<string>(out var text) ? text : null;
    private static double? LabelSize(JsonObject? options) => Numeric(options?["size"], out double size) ? Math.Clamp(size, 7, 64) : null;

    private static bool? LabelVisibilityOverride(JsonObject obj) =>
        LabelFlag(obj["label"] as JsonObject, "visible") ?? (LabelFlag(obj, "hideLabel") is bool hidden ? !hidden : null);

    private bool IsLabelVisible(ObjectEntry entry)
    {
        var rule = RuleFor(entry);
        return showObjectLabels && (LabelVisibilityOverride(entry.Item) ?? LabelFlag(DefaultLabel(rule), "visible") ?? rule.Visible);
    }

    private string ObjectLabel(ObjectEntry entry)
    {
        var obj = entry.Item;
        var rule = RuleFor(entry);
        string format = LabelString(obj["label"] as JsonObject,"format") ?? LabelString(DefaultLabel(rule), "format") ?? rule.Format;
        bool segment = IsPlanSegment(entry.Kind);
        double width = Numeric(obj["w"], out var w) ? w : 0, height = Numeric(obj["h"], out var h) ? h : 0;
        var position = segment ? End(obj, "a") : new Point(Number(obj["x"]), Number(obj["y"]));
        if (rule.Kind == "router") position = DecorationBounds(obj).Center;
        double length = segment ? Length(obj) : Math.Sqrt(width * width + height * height);
        double area = obj["kind"]?.ToString() switch { "ellipse" => width * height * Math.PI / 4, "line" => 0, _ => width * height };
        return labelToken.Replace(format, match => match.Groups[1].Value switch
        {
            "name" => ObjectName(obj, entry.Label),
            "width" => L("{0:0.##} м", width),
            "height" => L("{0:0.##} м", height),
            "area" => L("{0:0.##} м²", area),
            "length" => L("{0:0.##} м", length),
            "x" => L("{0:0.##} м", position.X),
            "y" => L("{0:0.##} м", position.Y),
            _ => match.Value
        });
    }

    private sealed record LabelTypography(string? Font, double Size, bool Bold, bool Italic, bool Underline)
    {
        public JsonObject ToJson() => new()
        {
            ["font"] = Font, ["size"] = Size, ["bold"] = Bold, ["italic"] = Italic, ["underline"] = Underline
        };
    }

    private static LabelTypography ReadLabelTypography(JsonObject? options, LabelTypography fallback) =>
        new(options?.ContainsKey("font") == true ? LabelString(options,"font") : fallback.Font,
            LabelSize(options) ?? fallback.Size, LabelFlag(options,"bold") ?? fallback.Bold,
            LabelFlag(options,"italic") ?? fallback.Italic, LabelFlag(options,"underline") ?? fallback.Underline);

    private static LabelTypography StandardTypography(LabelRule rule) => new(null, rule.Kind == "text" ? 13 : 11, false, false, false);

    private LabelTypography ObjectTypography(ObjectEntry entry)
    {
        var rule = RuleFor(entry);
        var options = entry.Item["label"] as JsonObject;
        return ReadLabelTypography(options?["typography"] as JsonObject,
            ReadLabelTypography(DefaultLabel(rule)?["typography"] as JsonObject, StandardTypography(rule)));
    }

    private TextBlock LabelText(ObjectEntry entry)
    {
        var style = ObjectTypography(entry);
        var label = new TextBlock
        {
            Text = ObjectLabel(entry), FontSize = style.Size,
            FontWeight = style.Bold ? FontWeights.Bold : FontWeights.Normal,
            FontStyle = style.Italic ? FontStyle.Italic : FontStyle.Normal,
            TextDecorations = style.Underline ? TextDecorations.Underline : null
        };
        if (!string.IsNullOrWhiteSpace(style.Font))
            label.FontFamily = new FontFamily(style.Font);
        return label;
    }

    private sealed class LabelStyleFields : StackPanel
    {
        private readonly ComboBox font = new();
        private readonly NumericUpDown size = new() {Minimum=7,Maximum=64,Increment=1,FormatString="0.##"};
        private readonly CheckBox bold = new() {Content=L("Полужирный")};
        private readonly CheckBox italic = new() {Content=L("Курсив")};
        private readonly CheckBox underline = new() {Content=L("Подчёркнутый")};
        private static readonly Lazy<string[]> systemFonts = new(() =>
            FontManager.Current.SystemFonts.Select(f => f.Name).Distinct().Order(StringComparer.CurrentCultureIgnoreCase).ToArray());

        public LabelStyleFields()
        {
            Spacing = 4;
            font.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<string>((name,_) =>
                new TextBlock {Text=name ?? L("Шрифт приложения"), TextTrimming=TextTrimming.CharacterEllipsis});
            Children.Add(new TextBlock {Text=L("Шрифт")});
            Children.Add(font);
            Children.Add(new TextBlock {Text=L("Размер шрифта")});
            Children.Add(size);
            var toggles = new WrapPanel {ItemSpacing=10};
            toggles.Children.Add(bold);
            toggles.Children.Add(italic);
            toggles.Children.Add(underline);
            Children.Add(toggles);
        }

        public void Load(LabelTypography style)
        {
            var available = systemFonts.Value;
            if (style.Font is {} saved && !available.Contains(saved)) available = [saved, ..available];
            var names = new[] { L("Шрифт приложения") }.Concat(available).ToArray();
            font.ItemsSource = names;
            font.SelectedIndex = style.Font is null ? 0 : Array.IndexOf(names, style.Font);
            size.Value = (decimal)style.Size;
            bold.IsChecked = style.Bold;
            italic.IsChecked = style.Italic;
            underline.IsChecked = style.Underline;
        }

        public LabelTypography Read() => new(font.SelectedIndex > 0 ? font.SelectedItem as string : null,
            (double)(size.Value ?? 11), bold.IsChecked == true, italic.IsChecked == true, underline.IsChecked == true);
    }

    private double ObjectLabelOpacity(JsonObject obj) =>
        Numeric((obj["label"] as JsonObject)?["opacity"],out var opacity)?Math.Clamp(opacity,0,1):labelOpacity;

    private readonly NumericUpDown propertyLabelOpacity = new() {Minimum=0,Maximum=100,Increment=5,FormatString="0.##"};
    private readonly TextBlock propertyLabelOpacityTitle = new();
    private double initialLabelOpacity;
    private readonly ComboBox propertyLabelVisibility = new();
    private readonly CheckBox propertyCustomLabel = new();
    private LabelStyleFields? propertyLabelFields;

    private void LoadObjectLabelProperties(ObjectEntry selected)
    {
        if (propertyLabelFields is null)
        {
            var fields = new LabelStyleFields();
            propertyLabelFields = fields;
            propertyCustomLabel.IsCheckedChanged += (_,_) => fields.IsVisible = propertyCustomLabel.IsChecked == true;
            PropertyLabelPanel.Children.Add(propertyLabelVisibility);
            PropertyLabelPanel.Children.Add(propertyLabelOpacityTitle);
            PropertyLabelPanel.Children.Add(propertyLabelOpacity);
            PropertyLabelPanel.Children.Add(propertyCustomLabel);
            PropertyLabelPanel.Children.Add(fields);
        }
        propertyLabelOpacityTitle.Text=L("Непрозрачность подписи, %");
        initialLabelOpacity=ObjectLabelOpacity(selected.Item);
        propertyLabelOpacity.Value=(decimal)(initialLabelOpacity*100);
        propertyLabelVisibility.ItemsSource = new[] { L("Подпись по умолчанию"), L("Показывать подпись"), L("Скрывать подпись") };
        propertyLabelVisibility.SelectedIndex = LabelVisibilityOverride(selected.Item) switch { true => 1, false => 2, _ => 0 };
        propertyCustomLabel.Content = L("Своё оформление");
        propertyCustomLabel.IsChecked = (selected.Item["label"] as JsonObject)?["typography"] is JsonObject;
        propertyLabelFields.IsVisible = propertyCustomLabel.IsChecked == true;
        propertyLabelFields.Load(ObjectTypography(selected));
    }

    private static void SetObjectLabelVisibility(JsonObject obj, bool? show)
    {
        obj.Remove("hideLabel");
        var options = obj["label"] as JsonObject ?? new JsonObject();
        if (show is bool visible) options["visible"] = visible;
        else options.Remove("visible");
        if (options.Count == 0) obj.Remove("label");
        else if (options.Parent is null) obj["label"] = options;
    }

    private void SaveObjectLabelProperties(JsonObject obj)
    {
        SetObjectLabelVisibility(obj, propertyLabelVisibility.SelectedIndex switch { 1 => true, 2 => false, _ => null });
        var options = obj["label"] as JsonObject ?? new JsonObject();
        double opacity=(double)(propertyLabelOpacity.Value??(decimal)(initialLabelOpacity*100))/100;
        if(Math.Abs(opacity-initialLabelOpacity)>1e-6)options["opacity"]=opacity;
        if (propertyCustomLabel.IsChecked == true) options["typography"] = propertyLabelFields!.Read().ToJson();
        else options.Remove("typography");
        if (options.Count == 0) obj.Remove("label");
        else if (options.Parent is null) obj["label"] = options;
    }
}
