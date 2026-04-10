namespace DotNetQuery.Core.Tests;

public class QueryKeyTests
{
    [Test]
    public async Task From_StoresParts()
    {
        var key = QueryKey.From("users", 42);

        using var _ = Assert.Multiple();
        await Assert.That(key.Parts.Count).IsEqualTo(2);
        await Assert.That(key.Parts[0]).IsEqualTo("users");
        await Assert.That(key.Parts[1]).IsEqualTo(42);
    }

    [Test]
    public async Task Default_HasSingleNullTerminatorPart()
    {
        using var _ = Assert.Multiple();
        await Assert.That(QueryKey.Default.Parts.Count).IsEqualTo(1);
        await Assert.That(QueryKey.Default.Parts[0]).IsEqualTo("\0");
    }

    [Test]
    public async Task Equals_SameParts_ReturnsTrue()
    {
        var a = QueryKey.From("users", 1);
        var b = QueryKey.From("users", 1);

        await Assert.That(a).IsEqualTo(b);
    }

    [Test]
    public async Task Equals_DifferentParts_ReturnsFalse()
    {
        var a = QueryKey.From("users", 1);
        var b = QueryKey.From("users", 2);

        await Assert.That(a).IsNotEqualTo(b);
    }

    [Test]
    public async Task Equals_DifferentPartCount_ReturnsFalse()
    {
        var a = QueryKey.From("users");
        var b = QueryKey.From("users", 1);

        await Assert.That(a).IsNotEqualTo(b);
    }

    [Test]
    public async Task Equals_Null_ReturnsFalse()
    {
        var key = QueryKey.From("users");

        await Assert.That(key.Equals(null)).IsFalse();
    }

    [Test]
    public async Task GetHashCode_EqualKeys_ProduceSameHash()
    {
        var a = QueryKey.From("users", 1);
        var b = QueryKey.From("users", 1);

        await Assert.That(a.GetHashCode()).IsEqualTo(b.GetHashCode());
    }

    [Test]
    public async Task GetHashCode_DifferentKeys_ProduceDifferentHash()
    {
        var a = QueryKey.From("users", 1);
        var b = QueryKey.From("users", 2);

        await Assert.That(a.GetHashCode()).IsNotEqualTo(b.GetHashCode());
    }

    [Test]
    public async Task ToString_JoinsPartsWithColon()
    {
        var key = QueryKey.From("users", 42, "profile");

        await Assert.That(key.ToString()).IsEqualTo("users:42:profile");
    }

    [Test]
    public async Task ToString_SinglePart_ReturnsPartOnly()
    {
        var key = QueryKey.From("users");

        await Assert.That(key.ToString()).IsEqualTo("users");
    }

    [Test]
    public async Task CanBeUsedAsDictionaryKey()
    {
        var dict = new Dictionary<QueryKey, string>();
        var key = QueryKey.From("users", 1);

        dict[key] = "value";

        await Assert.That(dict[QueryKey.From("users", 1)]).IsEqualTo("value");
    }

    [Test]
    public async Task From_NullElement_ThrowsArgumentException()
    {
        // Passing a non-null array that contains a null element
        var act = () => QueryKey.From("users", null!);

        var ex = await Assert.That(act).ThrowsException().And.IsTypeOf<ArgumentException>();
        await Assert.That(ex?.ParamName).IsEqualTo("parts");
    }

    [Test]
    public async Task From_MultipleNullElements_ThrowsArgumentException()
    {
        var act = () => QueryKey.From(null!, null!);

        await Assert.That(act).ThrowsException().And.IsTypeOf<ArgumentException>();
    }

    [Test]
    public async Task From_NullArray_ThrowsArgumentNullException()
    {
        // When a single null! is passed, the compiler treats it as a null array, not a null element.
        var act = () => QueryKey.From(null!);

        var ex = await Assert.That(act).ThrowsException().And.IsTypeOf<ArgumentNullException>();
        await Assert.That(ex?.ParamName).IsEqualTo("parts");
    }
}
