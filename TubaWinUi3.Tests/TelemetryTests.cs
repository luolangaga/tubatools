using System.Net;
using TubaWinUi3.Services.Telemetry;

namespace TubaWinUi3.Tests
{
    /// <summary>
    /// 匿名遥测：脱敏、匿名设备标识、连接地址回退与响应码归类。
    /// 这些纯函数决定了上报数据里会不会夹带用户名/路径，以及国内被污染的
    /// DNS 环境下能不能连上采集端，改错会静默丢数据或泄露隐私。
    /// </summary>
    public class TelemetryTests
    {
        [Fact]
        public void Sanitize_ScrubsWindowsUserProfile()
            => Assert.Equal(
                @"C:\Users\<用户>\AppData\Local\Temp\a.log",
                TelemetryService.Sanitize(@"C:\Users\luolan\AppData\Local\Temp\a.log"));

        [Fact]
        public void Sanitize_ScrubsUnixHome()
        {
            Assert.Equal("/home/<用户>/data", TelemetryService.Sanitize("/home/luolan/data"));
            Assert.Equal("/Users/<用户>/data", TelemetryService.Sanitize("/Users/luolan/data"));
        }

        [Fact]
        public void Sanitize_ScrubsUserInsideStackTrace()
        {
            var stack = "   在 System.IO.File.WriteAllText(String path)\r\n" +
                        @"   在 App.Save() 位置 C:\Users\luolan\Desktop\tubawinui3\App.cs:行号 42";
            var result = TelemetryService.Sanitize(stack);

            Assert.DoesNotContain("luolan", result);
            Assert.Contains(@"C:\Users\<用户>\Desktop", result);
        }

        [Fact]
        public void Sanitize_KeepsUnrelatedText()
            => Assert.Equal("连接失败：超时", TelemetryService.Sanitize("连接失败：超时"));

        [Fact]
        public void Sanitize_HandlesNullAndEmpty()
        {
            Assert.Equal(string.Empty, TelemetryService.Sanitize(null));
            Assert.Equal(string.Empty, TelemetryService.Sanitize(""));
        }

        [Fact]
        public void Sanitize_TruncatesOverlongText()
        {
            var result = TelemetryService.Sanitize(new string('x', 10_000));

            Assert.Equal(4001, result.Length);
            Assert.EndsWith("…", result);
        }

        [Fact]
        public void BuildDeviceId_IsDeterministic()
            => Assert.Equal(TelemetryService.BuildDeviceId("seed-a"), TelemetryService.BuildDeviceId("seed-a"));

        [Fact]
        public void BuildDeviceId_DiffersBySeed()
            => Assert.NotEqual(TelemetryService.BuildDeviceId("seed-a"), TelemetryService.BuildDeviceId("seed-b"));

        [Fact]
        public void BuildDeviceId_DoesNotLeakSeedAndIsUrlSafe()
        {
            var id = TelemetryService.BuildDeviceId("f5c3a1e2-0000-1111-2222-333344445555");

            Assert.Equal(22, id.Length);
            Assert.DoesNotContain("f5c3a1e2", id);
            Assert.Matches("^[A-Za-z0-9_-]+$", id);
        }

        [Fact]
        public void Endpoint_UsesDirectAxiomIngest()
        {
            var uri = new Uri(AxiomTelemetryConfig.Endpoint);
            Assert.Equal("https", uri.Scheme);
            Assert.Equal("us-east-1.aws.edge.axiom.co", uri.Host);
            Assert.Equal("/v1/ingest/tuba-telemetry", uri.AbsolutePath);
        }

        [Fact]
        public void ConvertEnvelope_PreservesTimeDeviceAndExceptionDetails()
        {
            var envelope = System.Text.Json.Nodes.JsonNode.Parse("""
                {"time":"2026-10-01T12:00:00Z","tags":{"ai.user.id":"anonymous","ai.session.id":"session"},
                "data":{"baseType":"ExceptionData","baseData":{"properties":{"source":"test"},
                "exceptions":[{"typeName":"System.Exception","message":"outer","parsedStack":[]},
                {"typeName":"System.IO.IOException","message":"inner"}]}}}
                """)!.AsObject();
            var result = AxiomTelemetryPayload.ConvertEnvelope(envelope);
            Assert.Equal("2026-10-01T12:00:00Z", result["_time"]!.GetValue<string>());
            Assert.Equal("anonymous", result["user_Id"]!.GetValue<string>());
            Assert.Equal("exception", result["kind"]!.GetValue<string>());
            Assert.Equal("System.Exception", result["type"]!.GetValue<string>());
            Assert.Equal("inner", result["innermostMessage"]!.GetValue<string>());
            Assert.Equal(2, result["details"]!.AsArray().Count);
            Assert.Equal(result["event_id"]!.GetValue<string>(),
                AxiomTelemetryPayload.ConvertEnvelope(envelope)["event_id"]!.GetValue<string>());
        }

        [Fact]
        public void ConvertBatch_AcceptsLegacyGzipSpoolWithoutLosingEvents()
        {
            var telemetry = new Microsoft.ApplicationInsights.DataContracts.EventTelemetry("heartbeat");
            telemetry.Context.User.Id = "anonymous";
            telemetry.Context.InstrumentationKey = "ff9e238f-9642-4aa9-a217-734434e79872";
            var transmission = new Microsoft.ApplicationInsights.Channel.Transmission(
                new Uri(AxiomTelemetryConfig.Endpoint), [telemetry], TimeSpan.FromSeconds(10));
            var batch = AxiomTelemetryPayload.ConvertBatch(transmission.Content, transmission.ContentEncoding);
            using var input = new MemoryStream(batch);
            using var gzip = new System.IO.Compression.GZipStream(input, System.IO.Compression.CompressionMode.Decompress);
            using var result = System.Text.Json.JsonDocument.Parse(gzip);
            Assert.Equal(1, result.RootElement.GetArrayLength());
            Assert.Equal("heartbeat", result.RootElement[0].GetProperty("name").GetString());
            Assert.Equal("anonymous", result.RootElement[0].GetProperty("user_Id").GetString());
            Assert.False(result.RootElement[0].TryGetProperty("iKey", out _));
        }

        [Theory]
        [InlineData("{\"ingested\":2,\"failed\":0}", true)]
        [InlineData("{\"ingested\":1,\"failed\":1}", false)]
        [InlineData("{\"ingested\":0,\"failed\":0}", false)]
        [InlineData("{}", false)]
        [InlineData("not json", false)]
        public void AxiomAcceptance_DoesNotTreatPartialSuccessAsDelivered(string response, bool accepted)
            => Assert.Equal(accepted, AxiomTelemetryPayload.IsFullyAccepted(response));

        [Fact]
        public void MergeCandidates_PrefersCachedThenSystemThenFallbackThenPinned()
        {
            var cached = IPAddress.Parse("10.0.0.1");
            var system = new[] { IPAddress.Parse("10.0.0.2") };
            var fallbackHost = new[] { IPAddress.Parse("10.0.0.3") };
            var pinned = new[] { IPAddress.Parse("10.0.0.4") };

            var result = TelemetryEndpointResolver.MergeCandidates(cached, system, fallbackHost, pinned, 10);

            Assert.Equal(
                new[] { "10.0.0.1", "10.0.0.2", "10.0.0.3", "10.0.0.4" },
                result.Select(a => a.ToString()).ToArray());
        }

        [Fact]
        public void MergeCandidates_DeduplicatesAndRespectsLimit()
        {
            var duplicate = IPAddress.Parse("10.0.0.9");
            var result = TelemetryEndpointResolver.MergeCandidates(
                duplicate,
                [duplicate, IPAddress.Parse("10.0.0.10")],
                [IPAddress.Parse("10.0.0.11")],
                [IPAddress.Parse("10.0.0.12")],
                max: 2);

            Assert.Equal(new[] { "10.0.0.9", "10.0.0.10" }, result.Select(a => a.ToString()).ToArray());
        }

        [Fact]
        public void MergeCandidates_SkipsUnspecifiedAddresses()
            => Assert.Empty(TelemetryEndpointResolver.MergeCandidates(
                null,
                [IPAddress.Any, IPAddress.IPv6Any],
                [],
                [],
                max: 4));

        [Theory]
        [InlineData(200, "Delivered")]
        [InlineData(206, "Delivered")]
        [InlineData(400, "Discard")]
        [InlineData(401, "Discard")]
        [InlineData(403, "Discard")]
        [InlineData(404, "Discard")]
        [InlineData(413, "Discard")]
        [InlineData(439, "Discard")]
        [InlineData(429, "RetryLater")]
        [InlineData(500, "RetryLater")]
        [InlineData(503, "RetryLater")]
        public void ClassifyResponse_MapsStatusCodes(int statusCode, string expected)
            => Assert.Equal(expected, TelemetryTransport.ClassifyResponse(statusCode).ToString());

        [Fact]
        public void BuildSanitizedTelemetry_SanitizesChainAndKeepsTypeNames()
        {
            var inner = new InvalidOperationException(@"打不开 C:\Users\luolan\Desktop\a.txt");
            var outer = new Exception("外层失败", inner);

            var telemetry = TelemetryService.BuildSanitizedTelemetry(outer, new Dictionary<string, string>());
            var details = telemetry.ExceptionDetailsInfoList;

            Assert.Equal(2, details.Count);
            Assert.Equal(typeof(Exception).FullName, details[0].TypeName);
            Assert.Equal(typeof(InvalidOperationException).FullName, details[1].TypeName);
            Assert.Equal("外层失败", details[0].Message);
            Assert.Contains("<用户>", details[1].Message);
            Assert.DoesNotContain("luolan", details[1].Message);
        }

        [Fact]
        public void FlattenExceptionChain_ExpandsAggregateAndCapsDepth()
        {
            var aggregate = new AggregateException("批量失败",
                new InvalidOperationException("a"),
                new ArgumentException("b"));

            var flattened = TelemetryService.FlattenExceptionChain(aggregate, 8);
            Assert.Equal(3, flattened.Count);
            Assert.IsType<AggregateException>(flattened[0]);
            Assert.IsType<InvalidOperationException>(flattened[1]);
            Assert.IsType<ArgumentException>(flattened[2]);

            var deep = new Exception("1", new Exception("2", new Exception("3", new Exception("4"))));
            Assert.Equal(2, TelemetryService.FlattenExceptionChain(deep, 2).Count);
        }

        [Fact]
        public void BuildFingerprint_GroupsSameExceptionAndSeparatesOthers()
        {
            var first = TelemetryService.BuildFingerprint(new InvalidOperationException("相同消息"));
            var second = TelemetryService.BuildFingerprint(new InvalidOperationException("相同消息"));
            var other = TelemetryService.BuildFingerprint(new ArgumentException("相同消息"));

            Assert.Equal(first, second);
            Assert.NotEqual(first, other);
        }

        [Fact]
        public void BuildFingerprint_TruncatesLongMessage()
            => Assert.Equal(
                TelemetryService.BuildFingerprint(new Exception(new string('x', 1000))),
                TelemetryService.BuildFingerprint(new Exception(new string('x', 200) + "yyyy")));
    }
}
