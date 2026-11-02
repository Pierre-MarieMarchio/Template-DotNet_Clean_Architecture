using Microsoft.EntityFrameworkCore;

namespace AppTemplate.Infrastructure.Core.Common.Saving.Tracking;

public interface IStagedOnContext
{
    bool IsStagedOn(DbContext context);
}
