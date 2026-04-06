using studyhub.application.Contracts.Maintenance;

namespace studyhub.application.Interfaces;

public interface IAppMaintenanceService
{
    Task<AppMaintenanceOperationResult> ClearBrokenOperationalStateAsync(CancellationToken cancellationToken = default);
}
