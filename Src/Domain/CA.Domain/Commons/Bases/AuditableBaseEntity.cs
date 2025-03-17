namespace CA.Domain.Commons.Bases;

public abstract class AuditableBaseEntity<Tkey> : BaseEntity<Tkey>
{
    public Guid CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? LastModifiedBy { get; set; }
    public DateTime? ModifiedAt { get; set; }
}

public abstract class AuditableBaseEntity : AuditableBaseEntity<Guid>
{
}
