using System.Text.Json;

namespace Tubatools.AiXamlCard;

/// <summary>一张卡片的磁盘布局：<c>cards/&lt;slug&gt;/card.xaml</c>，代码后置 <c>card.cs</c> 可选。</summary>
public sealed record CardFiles(string Slug, string Directory, string XamlPath, string CodePath, DateTimeOffset ChangedAt)
{
    public bool HasCode => File.Exists(CodePath);
    public string Title => CardMarkup.TitleOf(ReadXaml(), Slug) ;
    public string ReadXaml()
    {
        try { return File.ReadAllText(XamlPath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return ""; }
    }
    public string ReadCode()
    {
        try { return File.Exists(CodePath) ? File.ReadAllText(CodePath) : ""; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return ""; }
    }
}

/// <summary>
/// 卡片存放位置。刻意放在 AI 工作区内（<c>&lt;workspace&gt;/cards</c>），模型可以用它自己的
/// 文件工具直接写；插件只读这里，不参与生成过程。
/// </summary>
public sealed class CardStore(string workspaceRoot)
{
    public const string FolderName = "cards";
    public const string XamlName = "card.xaml";
    public const string CodeName = "card.cs";

    public string Root { get; } = Path.Combine(workspaceRoot, FolderName);

    public void EnsureRoot()
    {
        try { Directory.CreateDirectory(Root); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    public IReadOnlyList<CardFiles> List()
    {
        if (!Directory.Exists(Root)) return [];
        var cards = new List<CardFiles>();
        foreach (var directory in Directory.EnumerateDirectories(Root))
        {
            var xaml = Path.Combine(directory, XamlName);
            if (!File.Exists(xaml)) continue;
            var code = Path.Combine(directory, CodeName);
            DateTimeOffset changed;
            try
            {
                var stamp = File.GetLastWriteTimeUtc(xaml);
                if (File.Exists(code)) stamp = Max(stamp, File.GetLastWriteTimeUtc(code));
                changed = new DateTimeOffset(stamp, TimeSpan.Zero);
            }
            catch (IOException) { continue; }
            catch (UnauthorizedAccessException) { continue; }
            cards.Add(new CardFiles(Path.GetFileName(directory), directory, xaml, code, changed));
        }
        return cards.OrderBy(c => c.ChangedAt).ToArray();
    }

    private static DateTime Max(DateTime left, DateTime right) => left > right ? left : right;

    /// <summary>生成一张自检卡片；写完立刻能被宿主挂载，用来验证整条链路。</summary>
    public CardFiles? WriteSample(string slug, string title)
    {
        EnsureRoot();
        var directory = Path.Combine(Root, SafeSlug(slug));
        try
        {
            Directory.CreateDirectory(directory);
            Directory.CreateDirectory(Path.Combine(directory, CardMarkup.AssetsFolder));
            File.WriteAllText(Path.Combine(directory, XamlName), SampleXaml(title));
            File.WriteAllText(Path.Combine(directory, CodeName), SampleCode);
            return new CardFiles(SafeSlug(slug), directory, Path.Combine(directory, XamlName),
                Path.Combine(directory, CodeName), DateTimeOffset.UtcNow);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    public static string SafeSlug(string value)
    {
        var builder = new System.Text.StringBuilder(value.Length);
        foreach (var c in value)
            builder.Append(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' ? c : '-');
        var text = builder.ToString().Trim('-');
        return text.Length == 0 ? "card" : text.Length > 40 ? text[..40] : text;
    }

    private static string SampleXaml(string title) => $$"""
        <Border xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                AutomationProperties.Name="{{title}}"
                Background="TubaCard" BorderBrush="TubaStroke" BorderThickness="1"
                CornerRadius="10" Padding="16" MaxWidth="520" HorizontalAlignment="Left">
          <StackPanel Spacing="10">
            <TextBlock Text="{{title}}" FontSize="15" FontWeight="SemiBold" Foreground="TubaText" />
            <TextBlock Text="这张卡片由 AI 写入 XAML，并用 Roslyn 动态编译了代码后置。" TextWrapping="Wrap" Foreground="TubaMuted" />
            <Slider x:Name="Hours" Header="每天使用小时" Minimum="1" Maximum="24" Value="8" StepFrequency="1" />
            <TextBlock x:Name="Estimate" Foreground="TubaText" />
            <StackPanel Orientation="Horizontal" Spacing="8">
              <Button x:Name="Explain" Content="让助手解释" />
              <Button x:Name="Reset" Content="重置" />
            </StackPanel>
          </StackPanel>
        </Border>
        """;

    private const string SampleCode = """
        public sealed class CardController : CardControllerBase
        {
            public override void OnReady()
            {
                if (Find<Slider>("Hours") is { } slider) slider.ValueChanged += (_, _) => Update();
                if (Find<Button>("Explain") is { } explain) explain.Click += (_, _) => Send("请把这几个数字换算成每年的总小时数，并给出节省建议。");
                if (Find<Button>("Reset") is { } reset) reset.Click += (_, _) =>
                {
                    if (Find<Slider>("Hours") is { } target) target.Value = 8;
                    Notice("已恢复默认值。");
                };
                Update();
            }

            private void Update()
            {
                if (Find<Slider>("Hours") is not { } slider || Find<TextBlock>("Estimate") is not { } label) return;
                var yearly = slider.Value * 365;
                label.Text = $"每天 {slider.Value:0} 小时，一年约 {yearly:0} 小时。";
                label.Foreground = yearly > 5000 ? CardPalette.WarnBrush : CardPalette.GoodBrush;
            }
        }
        """;
}
