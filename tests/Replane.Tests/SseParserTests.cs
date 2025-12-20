namespace Replane.Tests;

public class SseParserTests
{
    [Fact]
    public void Feed_SingleEvent_ReturnsEvent()
    {
        var parser = new SseParser();

        var events = parser.Feed("event: test\ndata: {\"foo\":\"bar\"}\n\n").ToList();

        events.Should().HaveCount(1);
        events[0].EventType.Should().Be("test");
        events[0].Data.Should().NotBeNull();
        events[0].Data!.Value.GetProperty("foo").GetString().Should().Be("bar");
    }

    [Fact]
    public void Feed_MultipleEvents_ReturnsAllEvents()
    {
        var parser = new SseParser();

        var events = parser.Feed("event: first\ndata: 1\n\nevent: second\ndata: 2\n\n").ToList();

        events.Should().HaveCount(2);
        events[0].EventType.Should().Be("first");
        events[1].EventType.Should().Be("second");
    }

    [Fact]
    public void Feed_PartialChunks_AssemblesCorrectly()
    {
        var parser = new SseParser();

        // Send partial data
        var events1 = parser.Feed("event: te").ToList();
        events1.Should().BeEmpty();

        var events2 = parser.Feed("st\ndata: {\"x\"").ToList();
        events2.Should().BeEmpty();

        var events3 = parser.Feed(":1}\n\n").ToList();
        events3.Should().HaveCount(1);
        events3[0].EventType.Should().Be("test");
    }

    [Fact]
    public void Feed_MultiLineData_JoinsWithNewlines()
    {
        var parser = new SseParser();

        var events = parser.Feed("data: line1\ndata: line2\ndata: line3\n\n").ToList();

        events.Should().HaveCount(1);
        events[0].RawData.Should().Be("line1\nline2\nline3");
    }

    [Fact]
    public void Feed_CommentLines_AreIgnored()
    {
        var parser = new SseParser();

        var events = parser.Feed(": this is a comment\nevent: test\n: another comment\ndata: {}\n\n").ToList();

        events.Should().HaveCount(1);
        events[0].EventType.Should().Be("test");
    }

    [Fact]
    public void Feed_EventId_IsParsed()
    {
        var parser = new SseParser();

        var events = parser.Feed("id: 123\nevent: test\ndata: {}\n\n").ToList();

        events.Should().HaveCount(1);
        events[0].Id.Should().Be("123");
    }

    [Fact]
    public void Feed_RetryField_IsParsed()
    {
        var parser = new SseParser();

        var events = parser.Feed("retry: 5000\nevent: test\ndata: {}\n\n").ToList();

        events.Should().HaveCount(1);
        events[0].Retry.Should().Be(5000);
    }

    [Fact]
    public void Feed_CarriageReturnLineEndings_AreHandled()
    {
        var parser = new SseParser();

        var events = parser.Feed("event: test\r\ndata: {}\r\n\r\n").ToList();

        events.Should().HaveCount(1);
        events[0].EventType.Should().Be("test");
    }

    [Fact]
    public void Feed_InvalidJson_ReturnsRawData()
    {
        var parser = new SseParser();

        var events = parser.Feed("event: test\ndata: not json\n\n").ToList();

        events.Should().HaveCount(1);
        events[0].Data.Should().BeNull();
        events[0].RawData.Should().Be("not json");
    }

    [Fact]
    public void Feed_SpaceAfterColon_IsStripped()
    {
        var parser = new SseParser();

        var events = parser.Feed("event: test\ndata: {\"x\": 1}\n\n").ToList();

        events.Should().HaveCount(1);
        events[0].EventType.Should().Be("test");
        events[0].Data!.Value.GetProperty("x").GetInt32().Should().Be(1);
    }

    [Fact]
    public void Feed_FieldWithoutColon_IsHandled()
    {
        var parser = new SseParser();

        var events = parser.Feed("event: test\ndata\n\n").ToList();

        events.Should().HaveCount(1);
        events[0].RawData.Should().Be("");
    }

    [Fact]
    public void Feed_EmptyEvent_NotEmitted()
    {
        var parser = new SseParser();

        var events = parser.Feed("\n\n").ToList();

        events.Should().BeEmpty();
    }

    [Fact]
    public void Reset_ClearsBuffer()
    {
        var parser = new SseParser();
        parser.Feed("event: test").ToList();

        parser.Reset();

        var events = parser.Feed("event: other\ndata: {}\n\n").ToList();
        events.Should().HaveCount(1);
        events[0].EventType.Should().Be("other");
    }
}
