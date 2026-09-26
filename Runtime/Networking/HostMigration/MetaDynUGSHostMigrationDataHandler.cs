using Unity.Services.Multiplayer;

namespace MetaDyn.Networking.HostMigration
{
    /// <summary>
    /// Bridges UGS Sessions host-migration callbacks to MetaDyn's persistent
    /// migration coordinator without taking ownership of Relay or NGO startup.
    /// </summary>
    internal sealed class MetaDynUGSHostMigrationDataHandler : IMigrationDataHandler
    {
        private readonly MetaDynUGSMigrationCoordinator _coordinator;

        public MetaDynUGSHostMigrationDataHandler(MetaDynUGSMigrationCoordinator coordinator)
        {
            _coordinator = coordinator;
        }

        public byte[] Generate()
        {
            return _coordinator.GenerateLifecycleSnapshot();
        }

        public void Apply(byte[] migrationData)
        {
            _coordinator.ApplyLifecycleSnapshot(migrationData);
        }
    }
}
