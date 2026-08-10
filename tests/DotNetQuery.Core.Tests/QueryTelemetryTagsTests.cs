namespace DotNetQuery.Core.Tests;

public class QueryTelemetryTagsTests
{
    [Test]
    public async Task ToTagValue_UnknownTrigger_ReturnsUnknown()
    {
        var trigger = (FetchTrigger)999;

        await Assert.That(trigger.ToTagValue()).IsEqualTo("unknown");
    }
}
