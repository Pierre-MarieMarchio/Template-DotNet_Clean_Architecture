using AppTemplate.Domain.Common.Tagging;
using AppTemplate.Domain.Core.Common.Exceptions;
using Shouldly;
using Xunit;

namespace AppTemplate.Domain.UnitTests.Common.Tagging;

/// <summary>
/// The three rules every tagged thing in this application obeys, tested here rather than once per
/// aggregate: what the to-do item and the stored file share is not a shape but this behaviour, so
/// this is the file that says what it is.
/// </summary>
public sealed class TagSetTests
{
    private const int _capacity = 3;

    [Fact]
    public void ATagIsHeldOnce_HoweverManyTimesItIsSent()
    {
        var tags = ASet();

        tags.Add(Tag.Create("urgent"));
        tags.Add(Tag.Create("URGENT"));
        tags.Add(Tag.Create(" urgent "));

        tags.Tags.Select(tag => tag.Value).ShouldBe(["urgent"]);
    }

    /// <summary>
    /// The reason the no-op is not an error: a client that had to read before writing to stay
    /// correct would race every other client, and a retried request would fail on its second
    /// attempt for having succeeded on its first.
    /// </summary>
    [Fact]
    public void AddingATagAlreadyHeld_IsANoOp_EvenWhenTheSetIsFull()
    {
        var tags = AFullSet();

        Should.NotThrow(() => tags.Add(Tag.Create("a")));

        tags.Tags.Count.ShouldBe(_capacity);
    }

    [Fact]
    public void AddingANewTagToAFullSet_IsRefused()
    {
        var tags = AFullSet();

        var refusal = Should.Throw<DomainException>(() => tags.Add(Tag.Create("d")));

        refusal.Message.ShouldBe("A test subject cannot carry more than 3 tags.");
    }

    [Fact]
    public void RemovingATagNotHeld_IsANoOp()
    {
        var tags = ASet();

        tags.Add(Tag.Create("urgent"));

        Should.NotThrow(() => tags.Remove(Tag.Create("absent")));

        tags.Tags.Select(tag => tag.Value).ShouldBe(["urgent"]);
    }

    [Fact]
    public void Replace_IsTotal_AndNotAMerge()
    {
        var tags = ASet();

        tags.Add(Tag.Create("keep"));
        tags.Add(Tag.Create("drop"));

        tags.Replace([Tag.Create("keep"), Tag.Create("new")]);

        tags.Tags.Select(tag => tag.Value).ShouldBe(["keep", "new"], ignoreOrder: true);
    }

    /// <summary>
    /// The cap applies to the result, not to the union of what is held and what is wanted:
    /// swapping a full set for a different full set is not a request for twice the cap. This is the
    /// property that makes the removals happen before the additions rather than after.
    /// </summary>
    [Fact]
    public void ReplacingAFullSet_WithADifferentFullSet_IsAllowed()
    {
        var tags = AFullSet();

        Should.NotThrow(() => tags.Replace([Tag.Create("x"), Tag.Create("y"), Tag.Create("z")]));

        tags.Tags.Select(tag => tag.Value).ShouldBe(["x", "y", "z"], ignoreOrder: true);
    }

    [Fact]
    public void ReplacingWithMoreTagsThanTheCapAllows_IsRefused()
    {
        var tags = ASet();

        Should.Throw<DomainException>(() => tags.Replace(
            [Tag.Create("a"), Tag.Create("b"), Tag.Create("c"), Tag.Create("d")]));
    }

    /// <summary>
    /// Duplicates inside the replacement collapse before the cap is read, because
    /// <see cref="Tag"/> normalises before it compares — so four spellings of two tags are two.
    /// </summary>
    [Fact]
    public void Replace_CollapsesDuplicatesBeforeTheCapIsRead()
    {
        var tags = ASet();

        tags.Replace([Tag.Create("a"), Tag.Create("A"), Tag.Create("b"), Tag.Create(" B ")]);

        tags.Tags.Select(tag => tag.Value).ShouldBe(["a", "b"], ignoreOrder: true);
    }

    [Fact]
    public void ASetWithNoCapacity_IsRefusedAtConstruction() =>
        Should.Throw<ArgumentOutOfRangeException>(() => new TagSet(0, "test subject"));

    [Fact]
    public void ASetWithNoSubjectToNameInARefusal_IsRefusedAtConstruction() =>
        Should.Throw<ArgumentException>(() => new TagSet(_capacity, " "));

    private static TagSet ASet() => new(_capacity, "test subject");

    private static TagSet AFullSet()
    {
        var tags = ASet();

        tags.Add(Tag.Create("a"));
        tags.Add(Tag.Create("b"));
        tags.Add(Tag.Create("c"));

        return tags;
    }
}
