using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace EasyStock.Admin.TagHelpers;

/// <summary>
/// Sparkline SVG inline gerado server-side a partir de uma série de valores.
///
/// Uso (decimal[] do Razor):
///   @{ var serie = new[] { 12.0, 14.0, 11.0, 18.0, 22.0, 19.0, 25.0 }; }
///   <es-sparkline values="@serie" width="120" height="36" show-area="true" show-dot="true" />
///
/// Uso (string CSV — útil quando vem de querystring/log):
///   <es-sparkline values="12,14,11,18,22,19,25" />
///
/// Tendência (line color):
///   - trend="auto" (default) — compara primeiro vs último para colorir verde/vermelho
///   - trend="up" | "down" | "flat" | "neutral"
/// </summary>
[HtmlTargetElement("es-sparkline", TagStructure = TagStructure.WithoutEndTag)]
public sealed class SparklineTagHelper : TagHelper
{
    /// <summary>Aceita IEnumerable&lt;double&gt;, double[], decimal[], int[], string CSV "1,2,3".</summary>
    public object? Values { get; set; }

    public int Width { get; set; } = 120;
    public int Height { get; set; } = 36;

    [HtmlAttributeName("show-area")]
    public bool ShowArea { get; set; } = true;

    [HtmlAttributeName("show-dot")]
    public bool ShowDot { get; set; } = true;

    /// <summary>auto (default) | up | down | flat | neutral</summary>
    public string Trend { get; set; } = "auto";

    [HtmlAttributeName("aria-label")]
    public string? AriaLabel { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        var nums = Normalize(Values);

        output.TagName = "svg";
        output.TagMode = TagMode.StartTagAndEndTag;

        var trendClass = ResolveTrendClass(nums);
        output.Attributes.SetAttribute("class", "es-sparkline " + trendClass);
        output.Attributes.SetAttribute("viewBox", $"0 0 {Width} {Height}");
        output.Attributes.SetAttribute("width", Width);
        output.Attributes.SetAttribute("height", Height);
        output.Attributes.SetAttribute("preserveAspectRatio", "none");

        if (string.IsNullOrEmpty(AriaLabel))
        {
            output.Attributes.SetAttribute("aria-hidden", "true");
            output.Attributes.SetAttribute("focusable", "false");
        }
        else
        {
            output.Attributes.SetAttribute("role", "img");
            output.Attributes.SetAttribute("aria-label", AriaLabel);
        }

        if (nums.Count < 2)
        {
            // Linha reta no centro como fallback.
            var midY = (Height / 2.0).ToString("0.##", CultureInfo.InvariantCulture);
            var lineD = $"M0,{midY} L{Width},{midY}";
            output.Content.SetHtmlContent($"<path class=\"es-sparkline-line\" d=\"{lineD}\"/>");
            return;
        }

        var min = double.MaxValue;
        var max = double.MinValue;
        foreach (var v in nums) { if (v < min) min = v; if (v > max) max = v; }
        if (Math.Abs(max - min) < 1e-9) { max = min + 1; }

        var pad = 2.0;
        var n = nums.Count;
        var pts = new (double x, double y)[n];
        for (var i = 0; i < n; i++)
        {
            var x = (double)i / (n - 1) * Width;
            var ratio = (nums[i] - min) / (max - min);
            var y = Height - pad - (ratio * (Height - pad * 2));
            pts[i] = (x, y);
        }

        var sb = new StringBuilder();

        // Area path
        if (ShowArea)
        {
            var area = new StringBuilder();
            area.Append("M0,");
            area.Append(Height.ToString(CultureInfo.InvariantCulture));
            for (var i = 0; i < n; i++)
            {
                area.Append(" L");
                area.Append(pts[i].x.ToString("0.##", CultureInfo.InvariantCulture));
                area.Append(',');
                area.Append(pts[i].y.ToString("0.##", CultureInfo.InvariantCulture));
            }
            area.Append(" L");
            area.Append(Width.ToString(CultureInfo.InvariantCulture));
            area.Append(',');
            area.Append(Height.ToString(CultureInfo.InvariantCulture));
            area.Append(" Z");
            sb.Append("<path class=\"es-sparkline-area\" d=\"").Append(area).Append("\"/>");
        }

        // Line path
        var line = new StringBuilder();
        for (var i = 0; i < n; i++)
        {
            line.Append(i == 0 ? 'M' : 'L');
            line.Append(pts[i].x.ToString("0.##", CultureInfo.InvariantCulture));
            line.Append(',');
            line.Append(pts[i].y.ToString("0.##", CultureInfo.InvariantCulture));
            if (i < n - 1) line.Append(' ');
        }
        sb.Append("<path class=\"es-sparkline-line\" d=\"").Append(line).Append("\"/>");

        // Dot (último ponto)
        if (ShowDot)
        {
            var last = pts[n - 1];
            sb.Append("<circle class=\"es-sparkline-dot\" cx=\"")
              .Append(last.x.ToString("0.##", CultureInfo.InvariantCulture))
              .Append("\" cy=\"")
              .Append(last.y.ToString("0.##", CultureInfo.InvariantCulture))
              .Append("\" r=\"2\"/>");
        }

        output.Content.SetHtmlContent(sb.ToString());
    }

    private string ResolveTrendClass(IReadOnlyList<double> nums)
    {
        if (Trend != "auto") return $"is-{Trend}";
        if (nums.Count < 2) return "is-neutral";
        var first = nums[0];
        var last = nums[^1];
        if (Math.Abs(last - first) < (Math.Abs(first) * 0.005)) return "is-flat";
        return last > first ? "is-up" : "is-down";
    }

    private static List<double> Normalize(object? input)
    {
        var list = new List<double>();
        if (input is null) return list;
        switch (input)
        {
            case string csv:
                foreach (var part in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (double.TryParse(part, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ||
                        double.TryParse(part, NumberStyles.Any, CultureInfo.GetCultureInfo("pt-BR"), out v))
                    {
                        list.Add(v);
                    }
                }
                break;
            case System.Collections.IEnumerable enumerable:
                foreach (var item in enumerable)
                {
                    if (item is null) continue;
                    if (item is IConvertible conv)
                    {
                        try { list.Add(conv.ToDouble(CultureInfo.InvariantCulture)); }
                        catch { /* ignora itens não numéricos */ }
                    }
                }
                break;
        }
        return list;
    }
}
