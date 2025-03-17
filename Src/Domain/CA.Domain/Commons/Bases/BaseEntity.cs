namespace CA.Domain.Commons.Bases;

public abstract class BaseEntity<Tkey>
{
    public Tkey? Id { get; set; }
    public bool IsDeleted { get; set; } = false;

}
public abstract class BaseEntity : BaseEntity<Guid>
{
}
