using AppTemplate.Domain.Core.Common.Primitives;
using Shouldly;
using Xunit;

namespace AppTemplate.Domain.Core.UnitTests.Common.Primitives;

public sealed class EntityTests
{
    [Fact]
    public void Equals_ReturnsTrue_ForSameTypeAndSameId()
    {
        var id = Guid.CreateVersion7();

        var left = new SampleEntity(id);
        var right = new SampleEntity(id);

        left.Equals(right).ShouldBeTrue();
        (left == right).ShouldBeTrue();
        (left != right).ShouldBeFalse();
    }

    [Fact]
    public void Equals_ReturnsFalse_ForSameTypeAndDifferentId()
    {
        var left = new SampleEntity(Guid.CreateVersion7());
        var right = new SampleEntity(Guid.CreateVersion7());

        left.Equals(right).ShouldBeFalse();
        (left == right).ShouldBeFalse();
    }

    [Fact]
    public void Equals_ReturnsFalse_ForDifferentSubtypesWithTheSameId()
    {
        var id = Guid.CreateVersion7();

        var entity = new SampleEntity(id);
        var other = new OtherSampleEntity(id);

        entity.Equals(other).ShouldBeFalse();
        other.Equals(entity).ShouldBeFalse();
        (entity == (Entity<Guid>)other).ShouldBeFalse();
    }

    [Fact]
    public void Equals_ReturnsFalse_ForNull()
    {
        var entity = new SampleEntity(Guid.CreateVersion7());

        entity.Equals(null).ShouldBeFalse();
        entity.Equals((object?)null).ShouldBeFalse();
        (entity == null).ShouldBeFalse();
        (entity != null).ShouldBeTrue();
    }

    [Fact]
    public void Equals_ReturnsFalse_ForAnUnrelatedObject()
    {
        var entity = new SampleEntity(Guid.CreateVersion7());

        entity.Equals("not an entity").ShouldBeFalse();
    }

    [Fact]
    public void Equality_TreatsTwoNullReferencesAsEqual()
    {
        SampleEntity? left = null;
        SampleEntity? right = null;

        (left == right).ShouldBeTrue();
        (left != right).ShouldBeFalse();
    }

    [Fact]
    public void GetHashCode_IsEqual_ForEqualEntities()
    {
        var id = Guid.CreateVersion7();

        new SampleEntity(id).GetHashCode().ShouldBe(new SampleEntity(id).GetHashCode());
    }

    [Fact]
    public void GetHashCode_Differs_ForDifferentSubtypesWithTheSameId()
    {
        var id = Guid.CreateVersion7();

        new SampleEntity(id).GetHashCode().ShouldNotBe(new OtherSampleEntity(id).GetHashCode());
    }

    [Fact]
    public void HashSet_TreatsEqualEntitiesAsOneElement()
    {
        var id = Guid.CreateVersion7();

        var set = new HashSet<Entity<Guid>>
        {
            new SampleEntity(id),
            new SampleEntity(id),
            new OtherSampleEntity(id),
        };

        set.Count.ShouldBe(2);
    }

    [Fact]
    public void Id_IsTheValuePassedToTheConstructor()
    {
        var id = Guid.CreateVersion7();

        new SampleEntity(id).Id.ShouldBe(id);
    }
}

internal sealed class SampleEntity(Guid id) : Entity<Guid>(id);

internal sealed class OtherSampleEntity(Guid id) : Entity<Guid>(id);

internal sealed class StringKeyedEntity(string id) : Entity<string>(id);
