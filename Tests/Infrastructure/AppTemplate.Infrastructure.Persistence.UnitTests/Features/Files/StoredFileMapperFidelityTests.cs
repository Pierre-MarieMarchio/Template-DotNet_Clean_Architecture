using System.Reflection;
using AppTemplate.Domain.Features.Files.Entities;
using AppTemplate.Infrastructure.Persistence.Features.Files.Mapping;
using AppTemplate.Infrastructure.Persistence.Features.Files.Models;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Persistence.UnitTests.Features.Files;

/// <summary>
/// The guarantee that pays for the decision to keep persistence models separate from the domain model:
/// nothing is lost on the way through them.
/// </summary>
/// <remarks>
/// <para>
/// The same reflection harness as <see cref="TodoLists.TodoListMapperFidelityTests"/>, which states
/// why the aggregate's state is enumerated from the type rather than asserted by hand, and the three
/// things that make it fail.
/// </para>
/// <para>
/// <b>For this aggregate the stakes are not "a value came back wrong".</b> One of the properties walked
/// below is <c>ObjectKey</c>, the only record of where a file's bytes are, and a mapper that changes it
/// makes the orphan sweep delete a live file's content. That is why
/// <see cref="TheHarness_DetectsAMapperThatForgetsAProperty"/> breaks the key specifically, and why
/// <see cref="StoredFileMapperObjectKeyTests"/> states the same guarantee again in its own terms.
/// </para>
/// </remarks>
public sealed class StoredFileMapperFidelityTests
{
    /// <summary>
    /// Aggregate-root members that are not stored state. Checked against the type below, so this list
    /// cannot rot into a silent exemption.
    /// </summary>
    private static readonly string[] _membersThatAreNotStoredState =
    [
        // Deliberately not persisted. Events are raised, published after the commit and dropped; an
        // event that outlived the request would be delivered twice.
        nameof(StoredFile.DomainEvents),
    ];

    [Fact]
    public void EveryPieceOfState_SurvivesTheRoundTrip()
    {
        var original = AStoredFileAggregate.FullyPopulated();

        var roundTripped = RoundTrip(new StoredFileMapper(), original);

        AssertStateSurvived(original, roundTripped);
    }

    /// <summary>
    /// The exclusion list is only safe while every name in it still exists. A renamed property would
    /// otherwise stay excluded under its old name and simply stop being compared under its new one.
    /// </summary>
    [Fact]
    public void TheExclusionList_NamesRealMembers()
    {
        foreach (string member in _membersThatAreNotStoredState)
        {
            StateProperties(excluded: [])
                .Select(property => property.Name)
                .ShouldContain(member, $"'{member}' is excluded from the fidelity check but no longer exists.");
        }
    }

    /// <summary>
    /// Non-vacuity: the enumeration has to be finding the properties it claims to check. Without this, a
    /// reflection filter that matched nothing would make the assertion above pass over an empty loop.
    /// </summary>
    [Fact]
    public void TheEnumeration_FindsTheStateItIsMeantToCheck()
    {
        var properties = StateProperties(_membersThatAreNotStoredState).Select(property => property.Name).ToList();

        properties.ShouldContain(nameof(StoredFile.Id));
        properties.ShouldContain(nameof(StoredFile.OwnerId));
        properties.ShouldContain(nameof(StoredFile.ObjectKey));
        properties.ShouldContain(nameof(StoredFile.Name));
        properties.ShouldContain(nameof(StoredFile.DeclaredMediaType));
        properties.ShouldContain(nameof(StoredFile.Size));
        properties.ShouldContain(nameof(StoredFile.Checksum));
        properties.ShouldContain(nameof(StoredFile.State));
        properties.ShouldContain(nameof(StoredFile.RegisteredAt));
        properties.ShouldContain(nameof(StoredFile.AvailableAt));
        properties.ShouldContain(nameof(StoredFile.Version));
        properties.ShouldContain(nameof(StoredFile.CreatedAt));
        properties.ShouldContain(nameof(StoredFile.CreatedBy));
        properties.ShouldContain(nameof(StoredFile.LastModifiedAt));
        properties.ShouldContain(nameof(StoredFile.LastModifiedBy));
    }

    /// <summary>
    /// Proof that the harness can fail. <see cref="ForgetfulMapper"/> is the real mapper with the object
    /// key overwritten by another perfectly valid key — the shape a scheme change or a "helpful"
    /// normalisation would take — and the same reflection walk must reject it. If this test ever passes,
    /// the comparison is comparing nothing, and the mapper could quietly point a row at bytes that are
    /// not its own.
    /// </summary>
    [Fact]
    public void TheHarness_DetectsAMapperThatForgetsAProperty()
    {
        var original = AStoredFileAggregate.FullyPopulated();

        var roundTripped = RoundTrip(new ForgetfulMapper(), original);

        var failure = Should.Throw<ShouldAssertException>(
            () => AssertStateSurvived(original, roundTripped));

        failure.Message.ShouldContain(nameof(StoredFile.ObjectKey));
    }

    // ---- The comparison ------------------------------------------------------------------------

    private static StoredFile RoundTrip(IStoredFileMapper mapper, StoredFile original) =>
        mapper.ToAggregate(mapper.ToNewRecord(original));

    private static void AssertStateSurvived(StoredFile original, StoredFile roundTripped)
    {
        foreach (var property in StateProperties(_membersThatAreNotStoredState))
        {
            object? before = property.GetValue(original);
            object? after = property.GetValue(roundTripped);

            before.ShouldNotBe(
                DefaultOf(property.PropertyType),
                $"StoredFile.{property.Name} is at its type's default in the sample, so comparing it "
                + "before and after the round trip would pass even if the mapper never copied it. Give "
                + "it a distinctive value in AStoredFileAggregate.");

            after.ShouldBe(
                before,
                $"StoredFile.{property.Name} did not survive aggregate -> record -> aggregate. The mapper "
                + "is losing it silently: nothing throws, and the value simply comes back as its "
                + "default. Add it to StoredFileMapper in both directions.");
        }
    }

    /// <summary>
    /// The readable state of the aggregate: public instance properties with a getter, minus the ones
    /// excluded by name. Indexers are skipped because they are not state.
    /// </summary>
    private static IEnumerable<PropertyInfo> StateProperties(string[] excluded) =>
        typeof(StoredFile).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.GetMethod is not null)
            .Where(property => property.GetIndexParameters().Length == 0)
            .Where(property => !excluded.Contains(property.Name, StringComparer.Ordinal))
            .OrderBy(property => property.Name, StringComparer.Ordinal);

    private static object? DefaultOf(Type type) =>
        type.IsValueType && Nullable.GetUnderlyingType(type) is null ? Activator.CreateInstance(type) : null;

    /// <summary>
    /// The mapper as it would be if somebody re-derived the key instead of carrying it. Used only by
    /// <see cref="TheHarness_DetectsAMapperThatForgetsAProperty"/>, and it deliberately delegates
    /// everything else to the real implementation so that the one difference is the one thing under test.
    /// </summary>
    private sealed class ForgetfulMapper : IStoredFileMapper
    {
        private readonly StoredFileMapper _real = new();

        public StoredFile ToAggregate(StoredFileRecord record) => _real.ToAggregate(record);

        public StoredFileRecord ToNewRecord(StoredFile aggregate)
        {
            var record = _real.ToNewRecord(aggregate);

            // The forgotten line: the row ends up naming an object nobody ever wrote. Still a valid key,
            // so nothing throws anywhere — which is precisely the failure this harness has to catch.
            record.ObjectKey = AStoredFileAggregate.OtherObjectKeyValue;

            return record;
        }

        public void WriteTo(StoredFile aggregate, StoredFileRecord record) => _real.WriteTo(aggregate, record);
    }
}
