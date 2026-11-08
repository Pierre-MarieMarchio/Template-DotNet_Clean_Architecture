using AppTemplate.Domain.Features.Files.Model.Entities;
using AppTemplate.Infrastructure.Persistence.Features.Files.Rows.Models;

namespace AppTemplate.Infrastructure.Persistence.Features.Files.Rows.Mapping;

internal interface IStoredFileMapper
{
    StoredFile ToAggregate(StoredFileRecord record);

    StoredFileRecord ToNewRecord(StoredFile aggregate);

    void WriteTo(StoredFile aggregate, StoredFileRecord record);
}
