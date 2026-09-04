using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Common.UseCases;

namespace AppTemplate.Application.Features.Files.UseCases.Commands.ReclaimOrphanedContent;

/// <summary>
/// No command: what it sweeps is the whole store, and there is no caller whose files those are —
/// which is also why it must never read <c>ICurrentUser</c>. See
/// <see cref="ReclaimOrphanedContentUseCase"/>. The count it answers with is how many objects were
/// deleted.
/// </summary>
public interface IReclaimOrphanedContentUseCase : IUseCase<Result<int>>;
