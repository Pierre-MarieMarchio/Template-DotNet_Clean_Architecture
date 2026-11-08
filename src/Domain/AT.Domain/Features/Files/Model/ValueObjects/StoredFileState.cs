namespace AppTemplate.Domain.Features.Files.Model.ValueObjects;

public enum StoredFileState
{
    Pending = 0,

    Deposited = 2,

    Available = 1,

    Quarantined = 3,
}
