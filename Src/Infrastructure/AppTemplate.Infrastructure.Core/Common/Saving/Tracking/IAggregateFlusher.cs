using Microsoft.EntityFrameworkCore;

namespace AppTemplate.Infrastructure.Core.Common.Saving.Tracking;

public interface IAggregateFlusher
{
    bool IsStagedOn(DbContext context);

    void FlushTo(DbContext context);

    void RefreshFromStore();
}
