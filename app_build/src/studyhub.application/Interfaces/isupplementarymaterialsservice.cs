using studyhub.domain.Entities;

namespace studyhub.application.Interfaces;

public interface ISupplementaryMaterialsService
{
    Task<List<Material>> GetMaterialsByCourseAsync(Guid courseId);
    Task RefreshMaterialsAsync(Guid courseId);
}
