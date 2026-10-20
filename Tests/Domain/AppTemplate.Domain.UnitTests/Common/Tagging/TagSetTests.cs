using AppTemplate.Domain.Common.Tagging;
using AppTemplate.Domain.Core.Common.Exceptions;
using Shouldly;
using Xunit;

namespace AppTemplate.Domain.UnitTests.Common.Tagging;

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

    [Fact]
    public void ARefusedReplacement_LeavesTheTagsHeldBeforeIt()
    {
        var tags = ASet();

        tags.Add(Tag.Create("keep"));
        tags.Add(Tag.Create("drop"));

        Should.Throw<DomainException>(() => tags.Replace(
            [Tag.Create("w"), Tag.Create("x"), Tag.Create("y"), Tag.Create("z")]));

        tags.Tags.Select(tag => tag.Value).ShouldBe(["keep", "drop"], ignoreOrder: true);
    }

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
