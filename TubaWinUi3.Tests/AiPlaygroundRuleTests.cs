using TubaWinUi3.Services.AiPlayground;
using TubaWinUi3.Services.AiPlayground.Rules;
using Xunit;

namespace TubaWinUi3.Tests;

public class RuleLexerParserTests
{
    [Fact]
    public void Parse_SingleComparisonRule()
    {
        var (rules, errors) = RuleParser.Parse("""when label == "person" && score > 0.8 then stroke=#E81123 width=3""");
        Assert.Empty(errors);
        Assert.Single(rules);
        Assert.False(rules[0].IsElse);
        Assert.True(rules[0].Actions.ContainsKey("stroke"));
        Assert.True(rules[0].Actions.ContainsKey("width"));
    }

    [Fact]
    public void Parse_ElseRuleHasNullCondition()
    {
        var (rules, errors) = RuleParser.Parse("else then stroke=#888888 width=1");
        Assert.Empty(errors);
        Assert.Single(rules);
        Assert.True(rules[0].IsElse);
        Assert.Equal("#888888", rules[0].Actions["stroke"].Evaluate(null!).ToString());
    }

    [Fact]
    public void Parse_ReportsErrorsWithLineNumbers_AndKeepsGoodRules()
    {
        var text = "when label == \"a\" then stroke=#111111\n" +
                   "this is not valid\n" +
                   "when score >= 0.5 then width=2";
        var (rules, errors) = RuleParser.Parse(text);
        Assert.Equal(2, rules.Count);
        Assert.Single(errors);
        Assert.Equal(2, errors[0].Line);
    }

    [Fact]
    public void Parse_CommentLinesAreSkipped()
    {
        var (rules, errors) = RuleParser.Parse("// a comment\nwhen score > 0 then width=2");
        Assert.Empty(errors);
        Assert.Single(rules);
    }

    [Fact]
    public void Parse_EmptyAndWhitespaceAreIgnored()
    {
        var (rules, errors) = RuleParser.Parse("\n\n   \n");
        Assert.Empty(rules);
        Assert.Empty(errors);
    }

    [Fact]
    public void Parse_UnclosedParen_ThrowsSyntaxWithLine()
    {
        var (_, errors) = RuleParser.Parse("when (score > 0.5 then width=2");
        Assert.Single(errors);
    }

    [Fact]
    public void ColorToken_IsNotTreatedAsComment()
    {
        var (rules, errors) = RuleParser.Parse("when score > 0.5 then stroke=#0F6CBD");
        Assert.Empty(errors);
        Assert.Equal("#0F6CBD", rules[0].Actions["stroke"].Evaluate(null!).ToString());
    }
}

public class RuleEngineTests
{
    private static DetectionRuleContext Ctx(float score, int classId, string raw, float w = 10, float h = 10) =>
        new(new DetectionItem
        {
            Label = raw,
            RawLabel = raw,
            ClassId = classId,
            Score = score,
            X1 = 0, Y1 = 0, X2 = w, Y2 = h,
        });

    [Fact]
    public void MatchFirst_ReturnsFirstMatchingRule()
    {
        var (rules, _) = RuleParser.Parse(
            "when label == \"person\" then stroke=#FF0000\n" +
            "when score > 0.5 then stroke=#00FF00");
        var action = RuleEngine.MatchFirst(rules, Ctx(0.9f, 0, "person"));
        Assert.Equal("#FF0000", action!.Stroke);
    }

    [Fact]
    public void MatchFirst_FallsThroughToLaterRule()
    {
        var (rules, _) = RuleParser.Parse(
            "when label == \"person\" then stroke=#FF0000\n" +
            "when score > 0.5 then stroke=#00FF00");
        var action = RuleEngine.MatchFirst(rules, Ctx(0.9f, 2, "car"));
        Assert.Equal("#00FF00", action!.Stroke);
    }

    [Fact]
    public void MatchFirst_UsesElseWhenNothingMatches()
    {
        var (rules, _) = RuleParser.Parse(
            "when label == \"person\" then stroke=#FF0000\n" +
            "else then stroke=#888888");
        var action = RuleEngine.MatchFirst(rules, Ctx(0.1f, 2, "car"));
        Assert.Equal("#888888", action!.Stroke);
    }

    [Fact]
    public void MatchFirst_ReturnsNullWithoutElse()
    {
        var (rules, _) = RuleParser.Parse("when label == \"person\" then stroke=#FF0000");
        Assert.Null(RuleEngine.MatchFirst(rules, Ctx(0.9f, 2, "car")));
    }

    [Theory]
    [InlineData("when score > 0.5 then width=3", true)]
    [InlineData("when score > 0.9 then width=3", false)]
    [InlineData("when score >= 0.5 then width=3", true)]
    [InlineData("when class_id == 0 then width=3", true)]
    [InlineData("when class_id != 0 then width=3", false)]
    [InlineData("when !(class_id == 0) then width=3", false)]
    [InlineData("when w == 10 && h == 10 then width=3", true)]
    [InlineData("when w > 100 then width=3", false)]
    [InlineData("when contains(label, \"per\") then width=3", true)]
    [InlineData("when startsWith(label, \"per\") then width=3", true)]
    [InlineData("when endsWith(label, \"son\") then width=3", true)]
    [InlineData("when upper(label) == \"PERSON\" then width=3", true)]
    public void MatchFirst_ExpressionCases(string rule, bool shouldMatch)
    {
        var (rules, _) = RuleParser.Parse(rule);
        var action = RuleEngine.MatchFirst(rules, Ctx(0.7f, 0, "person"));
        Assert.Equal(shouldMatch, action is not null);
    }

    [Fact]
    public void OrAndNot_Precedence()
    {
        // && 高于 ||
        var (rules, _) = RuleParser.Parse("when class_id == 5 || class_id == 0 && score > 0.5 then width=9");
        Assert.NotNull(RuleEngine.MatchFirst(rules, Ctx(0.9f, 0, "person")));
        Assert.Null(RuleEngine.MatchFirst(rules, Ctx(0.1f, 0, "person")));
        Assert.NotNull(RuleEngine.MatchFirst(rules, Ctx(0.1f, 5, "bus")));
    }

    [Fact]
    public void CountFunction_UsesHistogram()
    {
        var counts = new Dictionary<string, int> { ["0"] = 3 };
        var ctx = new DetectionRuleContext(
            new DetectionItem { Label = "person", RawLabel = "person", ClassId = 0, Score = 0.9f, X1 = 0, Y1 = 0, X2 = 1, Y2 = 1 },
            counts);
        var (rules, _) = RuleParser.Parse("when count(class_id) >= 3 then width=5");
        Assert.NotNull(RuleEngine.MatchFirst(rules, ctx));
    }

    [Fact]
    public void FormatLabel_ReplacesPlaceholders()
    {
        var ctx = Ctx(0.87f, 0, "person");
        Assert.Equal("person 87%", RuleEngine.FormatLabel("{label} {score:P0}", ctx));
        Assert.Equal("person", RuleEngine.FormatLabel("{label}", ctx));
        Assert.Equal("id=0", RuleEngine.FormatLabel("id={class_id}", ctx));
    }

    [Fact]
    public void Evaluator_NeverThrowsOnUnknownFieldOrFunction()
    {
        // 未知字段求值为空串、未知函数返回空 → 不应抛异常（条件为真时仍返回动作）
        var (rules, errors) = RuleParser.Parse("when unknown_field != \"x\" then stroke=#123456 width=3");
        Assert.Empty(errors);
        var action = RuleEngine.MatchFirst(rules, Ctx(0.5f, 1, "car"));
        Assert.NotNull(action);
        Assert.Equal("#123456", action!.Stroke);

        // 未命中则返回 null（不抛）
        var (rules2, _) = RuleParser.Parse("when unknown_field == \"x\" then width=3");
        Assert.Null(RuleEngine.MatchFirst(rules2, Ctx(0.5f, 1, "car")));
    }
}

public class RuleStoreTests
{
    [Fact]
    public void ColorForLabel_IsStableAndValidHex()
    {
        var a = RuleStore.ColorForLabel("person");
        var b = RuleStore.ColorForLabel("person");
        var c = RuleStore.ColorForLabel("car");
        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
        Assert.Matches("^#[0-9A-F]{6}$", a);
        Assert.Matches("^#[0-9A-F]{6}$", c);
    }

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        var root = Path.Combine(Path.GetTempPath(), $"tuba_rules_{Guid.NewGuid():N}");
        RuleStore.RootOverride = root;
        try
        {
            RuleStore.SaveText("m1", "draw", "when score > 0.5 then width=3");
            Assert.Equal("when score > 0.5 then width=3", RuleStore.LoadText("m1", "draw"));
            Assert.True(RuleStore.HasCustom("m1", "draw"));

            RuleStore.ResetToDefault("m1", "draw");
            Assert.False(RuleStore.HasCustom("m1", "draw"));
            Assert.Equal("", RuleStore.LoadText("m1", "draw")); // 默认绘制规则为空文本
        }
        finally
        {
            RuleStore.RootOverride = null;
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }
}

public class FrameSourceTests
{
    private static FrameBuffer MakeFrame(int w, int h)
    {
        var data = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = (y * w + x) * 4;
                data[i] = (byte)(x % 256);
                data[i + 1] = (byte)(y % 256);
                data[i + 2] = 128;
                data[i + 3] = 255;
            }
        return new FrameBuffer { Bgra = data, Width = w, Height = h, Stride = w * 4 };
    }

    [Fact]
    public void ScaledCopy_ResizesAndKeepsBgraOrder()
    {
        var frame = MakeFrame(8, 4);
        var (data, w, h) = IFrameSource.ScaledCopy(frame, 4, 2);
        Assert.Equal(4, w);
        Assert.Equal(2, h);
        Assert.Equal(4 * 2 * 4, data.Length);
        Assert.Equal(128, data[2]);          // B 通道保留
        Assert.Equal(255, data[3]);
    }

    [Fact]
    public void PrepareYolo_FromFrame_MatchesPathVariant()
    {
        // 同一张图：FrameBuffer 入口与 path 入口应产出相同张量
        var path = Path.Combine(Path.GetTempPath(), $"tuba_frame_{Guid.NewGuid():N}.png");
        try
        {
            using (var bmp = new System.Drawing.Bitmap(32, 16))
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            {
                g.Clear(System.Drawing.Color.CornflowerBlue);
                bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            }

            var fromPath = ImagePreprocess.PrepareYolo(path, 64);
            using var loaded = new System.Drawing.Bitmap(path);
            var fb = new FrameBuffer
            {
                Bgra = BitmapToBgra(loaded),
                Width = loaded.Width,
                Height = loaded.Height,
                Stride = loaded.Width * 4,
            };
            var fromFrame = ImagePreprocess.PrepareYolo(fb, 64);

            Assert.Equal(fromPath.Width, fromFrame.Width);
            Assert.Equal(fromPath.Height, fromFrame.Height);
            Assert.Equal(fromPath.Data.Length, fromFrame.Data.Length);
            // R 通道（plane 0）应一致（BGR/BGRA 布局一致）
            for (int i = 0; i < fromPath.Data.Length; i++)
            {
                if (Math.Abs(fromPath.Data[i] - fromFrame.Data[i]) > 0.02f)
                    Assert.Fail($"像素 {i} 不一致：{fromPath.Data[i]} vs {fromFrame.Data[i]}");
            }
        }
        finally { File.Delete(path); }
    }

    private static byte[] BitmapToBgra(System.Drawing.Bitmap bmp)
    {
        var data = new byte[bmp.Width * bmp.Height * 4];
        var rect = new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height);
        var bits = bmp.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            int stride = bits.Stride;
            var row = new byte[stride];
            for (int y = 0; y < bmp.Height; y++)
            {
                System.Runtime.InteropServices.Marshal.Copy(bits.Scan0 + y * stride, row, 0, stride);
                Buffer.BlockCopy(row, 0, data, y * bmp.Width * 4, bmp.Width * 4);
            }
        }
        finally { bmp.UnlockBits(bits); }
        return data;
    }
}
