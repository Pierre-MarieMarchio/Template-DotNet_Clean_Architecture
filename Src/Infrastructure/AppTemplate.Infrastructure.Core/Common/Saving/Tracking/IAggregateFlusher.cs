using Microsoft.EntityFrameworkCore;

namespace AppTemplate.Infrastructure.Core.Common.Saving.Tracking;

public interface IAggregateFlusher : IStagedOnContext
{
    void FlushTo(DbContext context);

    void RefreshFromStore();
}
