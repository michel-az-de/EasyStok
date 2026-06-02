using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace EasyStock.Web.TagHelpers;

/// <summary>
/// Stat card (KPI) com valor animado, delta vs periodo anterior e sparkline opcional.
/// Pode virar link clicavel (drill-down) passando href.
///
/// Port do EasyStock.Admin/TagHelpers/StatCardTagHelper.cs (convergencia DS, Fase 0/Demo).
/// Self-contained: inline SVG (sem sprite), count-up via Alpine x-init inline (core, sem plugin).
///
/// Uso:
///   &lt;es-stat-card label="A Receber 30d"
///                 value="R$ 84.300"
///                 delta="+12.5%" delta-trend="up" delta-meta="vs 30d"
///                 sparkline="120,135,128,145,162,158,180"
///                 href="/contas-a-receber" /&gt;
/// </summary>
[HtmlTargetElement("es-stat-card")]
public sealed class EsStatCardTagHelper : TagHelper
{
    public string? Label { get; set; }
    public string? Value { get; set; }

    /// <summary>Texto opcional secundario abaixo do valor (ex.: "MRR atual").</summary>
    public string? Sub { get; set; }

    /// <summary>String formatada de delta (ex.: "+12.5%", "-3.2%", "0%").</summary>
    public string? Delta { get; set; }

    /// <summary>up | down | flat (define a cor do badge de delta). Default: auto detecta pelo sinal.</summary>
    [HtmlAttributeName("delta-trend")]
    public string? DeltaTrend { get; set; }

    /// <summary>Texto a direita do delta (ex.: "vs ultimos 30d").</summary>
    [HtmlAttributeName("delta-meta")]
    public string? DeltaMeta { get; set; }

    /// <summary>CSV ou IEnumerable&lt;double&gt; — se preenchido renderiza sparkline atras do valor.</summary>
    public object? Sparkline { get; set; }

    /// <summary>up | down | flat | auto. Auto compara primeiro vs ultimo.</summary>
    [HtmlAttributeName("sparkline-trend")]
    public string SparklineTrend { get; set; } = "auto";

    /// <summary>Quando setado, o card vira &lt;a href="..."&gt; clicavel (drill-down).</summary>
    public string? Href { get; set; }

    /// <summary>Animacao do numero via Alpine x-init (CountUp simples). Default false.</summary>
    public bool Animated { get; set; }

    /// <summary>Valor numerico para CountUp (default: parse do Value se animated=true).</summary>
    [HtmlAttributeName("animated-target")]
    public double? AnimatedTarget { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        var isLink = !string.IsNullOrEmpty(Href);
        output.TagName = isLink ? "a" : "div";
        output.TagMode = TagMode.StartTagAndEndTag;

        var classes = "es-stat-card" + (isLink ? " is-link card-interactive" : "");
        output.Attributes.SetAttribute("class", classes);
        if (isLink) output.Attributes.SetAttribute("href", Href!);

        var sb = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(Label))
        {
            sb.Append("<div class=\"es-stat-label\">");
            sb.Append(WebUtility.HtmlEncode(Label));
            sb.Append("</div>");
        }

        if (Animated && AnimatedTarget.HasValue)
        {
            var target = AnimatedTarget.Value.ToString(CultureInfo.InvariantCulture);
            var displayInit = WebUtility.HtmlEncode(Value ?? "0").Replace("\\", "\\\\").Replace("'", "\\'");
            sb.Append("<div class=\"es-stat-value\" ");
            sb.Append("x-data=\"{ display: '").Append(displayInit).Append("' }\" ");
            sb.Append("x-init=\"(() => { const t = ").Append(target).Append("; const d = 800; const s = performance.now(); const f = (n) => ");
            sb.Append("Number.isInteger(t) ? Math.round(n).toLocaleString('pt-BR') : n.toLocaleString('pt-BR', { maximumFractionDigits: 1 }); ");
            sb.Append("const tick = (now) => { const p = Math.min(1, (now - s) / d); const e = 1 - Math.pow(1 - p, 3); display = f(t * e); if (p < 1) requestAnimationFrame(tick); }; requestAnimationFrame(tick); })()\" ");
            sb.Append("x-text=\"display\">");
            sb.Append(displayInit);
            sb.Append("</div>");
        }
        else
        {
            sb.Append("<div class=\"es-stat-value\">");
            sb.Append(WebUtility.HtmlEncode(Value ?? "—"));
            sb.Append("</div>");
        }

        // Delta + meta + sub
        var hasMeta = !string.IsNullOrWhiteSpace(Delta) || !string.IsNullOrWhiteSpace(Sub) || !string.IsNullOrWhiteSpace(DeltaMeta);
        if (hasMeta)
        {
            sb.Append("<div class=\"es-stat-meta\">");

            if (!string.IsNullOrWhiteSpace(Delta))
            {
                var trendClass = ResolveDeltaTrendClass(Delta, DeltaTrend);
                var arrow = trendClass switch
                {
                    "es-stat-delta-up" => "<svg class=\"es-icon\" width=\"10\" height=\"10\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"3\" stroke-linecap=\"round\" stroke-linejoin=\"round\" aria-hidden=\"true\" focusable=\"false\"><polyline points=\"18 15 12 9 6 15\"/></svg>",
                    "es-stat-delta-down" => "<svg class=\"es-icon\" width=\"10\" height=\"10\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"3\" stroke-linecap=\"round\" stroke-linejoin=\"round\" aria-hidden=\"true\" focusable=\"false\"><polyline points=\"6 9 12 15 18 9\"/></svg>",
                    _ => string.Empty
                };
                sb.Append("<span class=\"es-stat-delta ").Append(trendClass).Append("\">");
                sb.Append(arrow);
                sb.Append(WebUtility.HtmlEncode(Delta));
                sb.Append("</span>");
            }

            if (!string.IsNullOrWhiteSpace(DeltaMeta))
            {
                sb.Append("<span>");
                sb.Append(WebUtility.HtmlEncode(DeltaMeta));
                sb.Append("</span>");
            }

            if (!string.IsNullOrWhiteSpace(Sub))
            {
                sb.Append("<span style=\"margin-left:auto;\">");
                sb.Append(WebUtility.HtmlEncode(Sub));
                sb.Append("</span>");
            }

            sb.Append("</div>");
        }

        // Sparkline (background)
        if (Sparkline is not null && !(Sparkline is string s && string.IsNullOrWhiteSpace(s)))
        {
            sb.Append("<div class=\"es-stat-sparkline\" aria-hidden=\"true\">");
            sb.Append(BuildSparklineSvg(Sparkline, SparklineTrend));
            sb.Append("</div>");
        }

        output.Content.SetHtmlContent(sb.ToString());
    }

    internal static string ResolveDeltaTrendClass(string delta, string? explicitTrend)
    {
        if (!string.IsNullOrWhiteSpace(explicitTrend))
        {
            return explicitTrend switch
            {
                "up" => "es-stat-delta-up",
                "down" => "es-stat-delta-down",
                _ => "es-stat-delta-flat"
            };
        }
        var trimmed = delta.Trim();
        if (trimmed.StartsWith('+')) return "es-stat-delta-up";
        if (trimmed.StartsWith('-') || trimmed.StartsWith('−')) return "es-stat-delta-down";
        return "es-stat-delta-flat";
    }

    internal static string BuildSparklineSvg(object input, string trend)
    {
        var nums = SparklineNormalize(input);
        if (nums.Count < 2)
        {
            return "<svg class=\"es-sparkline\" viewBox=\"0 0 120 36\" preserveAspectRatio=\"none\" aria-hidden=\"true\" focusable=\"false\"><path class=\"es-sparkline-line\" d=\"M0,18 L120,18\"/></svg>";
        }
        var width = 120; var height = 36;
        var min = double.MaxValue; var max = double.MinValue;
        foreach (var v in nums) { if (v < min) min = v; if (v > max) max = v; }
        if (Math.Abs(max - min) < 1e-9) max = min + 1;

        var pad = 2.0;
        var n = nums.Count;
        var pts = new (double x, double y)[n];
        for (var i = 0; i < n; i++)
        {
            var x = (double)i / (n - 1) * width;
            var ratio = (nums[i] - min) / (max - min);
            var y = height - pad - (ratio * (height - pad * 2));
            pts[i] = (x, y);
        }
        var trendClass = trend switch
        {
            "up" => "is-up",
            "down" => "is-down",
            "flat" => "is-flat",
            "neutral" => "",
            _ => Math.Abs(nums[^1] - nums[0]) < (Math.Abs(nums[0]) * 0.005) ? "is-flat" : (nums[^1] > nums[0] ? "is-up" : "is-down")
        };

        var line = new StringBuilder();
        for (var i = 0; i < n; i++)
        {
            line.Append(i == 0 ? 'M' : 'L');
            line.Append(pts[i].x.ToString("0.##", CultureInfo.InvariantCulture));
            line.Append(',');
            line.Append(pts[i].y.ToString("0.##", CultureInfo.InvariantCulture));
            if (i < n - 1) line.Append(' ');
        }
        var area = new StringBuilder("M0,").Append(height);
        for (var i = 0; i < n; i++)
        {
            area.Append(" L").Append(pts[i].x.ToString("0.##", CultureInfo.InvariantCulture))
                .Append(',').Append(pts[i].y.ToString("0.##", CultureInfo.InvariantCulture));
        }
        area.Append(" L").Append(width).Append(',').Append(height).Append(" Z");

        return $"<svg class=\"es-sparkline {trendClass}\" viewBox=\"0 0 {width} {height}\" preserveAspectRatio=\"none\" aria-hidden=\"true\" focusable=\"false\">" +
               $"<path class=\"es-sparkline-area\" d=\"{area}\"/>" +
               $"<path class=\"es-sparkline-line\" d=\"{line}\"/></svg>";
    }

    internal static List<double> SparklineNormalize(object input)
    {
        var list = new List<double>();
        if (input is string csv)
        {
            foreach (var part in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (double.TryParse(part, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ||
                    double.TryParse(part, NumberStyles.Any, CultureInfo.GetCultureInfo("pt-BR"), out v))
                    list.Add(v);
            }
        }
        else if (input is System.Collections.IEnumerable e)
        {
            foreach (var item in e)
            {
                if (item is null) continue;
                if (item is IConvertible c)
                {
                    try { list.Add(c.ToDouble(CultureInfo.InvariantCulture)); } catch { }
                }
            }
        }
        return list;
    }
}
