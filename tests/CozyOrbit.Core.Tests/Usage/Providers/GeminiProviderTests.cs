using CozyOrbit.Core.Usage.Providers;
using Xunit;

namespace CozyOrbit.Core.Tests.Usage.Providers;

public class GeminiProviderTests
{
    [Fact]
    public void JsonlMessagesAndUpdates()
    {
        using var tmp = new TempDir();
        var file = tmp.Write(".gemini/tmp/hash/chats/session-1.jsonl", string.Join("\n",
            "{\"type\":\"session_metadata\",\"sessionId\":\"s1\",\"startTime\":\"2026-07-03T01:00:00.000Z\"}",
            "{\"type\":\"user\",\"id\":\"m1\",\"timestamp\":\"2026-07-03T01:00:05.000Z\",\"content\":[{\"text\":\"hi\"}]}",
            "{\"type\":\"gemini\",\"id\":\"m2\",\"timestamp\":\"2026-07-03T01:00:10.000Z\",\"model\":\"gemini-2.5-pro\",\"tokens\":{\"input\":1000,\"output\":50,\"cached\":600,\"thoughts\":30,\"tool\":20,\"total\":1100}}",
            "{\"type\":\"gemini\",\"id\":\"m3\",\"timestamp\":\"2026-07-03T01:01:00.000Z\",\"model\":\"gemini-2.5-flash\",\"tokens\":{\"input\":10,\"output\":5,\"cached\":0,\"thoughts\":0,\"tool\":0,\"total\":15}}",
            "{\"type\":\"message_update\",\"id\":\"m3\",\"tokens\":{\"input\":10,\"output\":8,\"cached\":0,\"thoughts\":2,\"tool\":0,\"total\":20}}"));
        var entries = GeminiUsageProvider.ParseFile(file);
        Assert.Equal(2, entries.Count);
        var m2 = entries.Single(e => e.Id.EndsWith("|m2"));
        Assert.Equal(420, m2.Input);
        Assert.Equal(600, m2.CacheRead);
        Assert.Equal(80, m2.Output);
        Assert.Equal(1100, m2.Total);
        var m3 = entries.Single(e => e.Id.EndsWith("|m3"));
        Assert.Equal(10, m3.Output);
        Assert.Equal(20, m3.Total);
    }

    [Fact]
    public void LegacyJson()
    {
        using var tmp = new TempDir();
        var file = tmp.Write(".gemini/tmp/hash/chats/old.json",
            "{\"sessionId\":\"s0\",\"startTime\":\"2026-07-02T00:00:00.000Z\",\"messages\":[" +
            "{\"id\":\"a1\",\"type\":\"gemini\",\"timestamp\":\"2026-07-02T00:10:00.000Z\",\"model\":\"gemini-2.5-pro\",\"tokens\":{\"input\":100,\"output\":10,\"cached\":0,\"thoughts\":0,\"tool\":0,\"total\":110}}," +
            "{\"id\":\"a2\",\"type\":\"user\",\"content\":[{\"text\":\"x\"}]}]}");
        var entries = GeminiUsageProvider.ParseFile(file);
        Assert.Single(entries);
        Assert.Equal(110, entries[0].Total);
    }
}
