using System.Collections.Generic;
using System.Linq;
using Itinero.Profiles.EdgeTypesMap;
using Itinero.Routing.Costs.EdgeTypes;

namespace Itinero.Profiles
{
    internal class RouterDbProfileConfiguration
    {
        private readonly Dictionary<string, (Profile profile, EdgeFactorCache cache)> _profiles;
        private readonly RouterDb _routerDb;

        public RouterDbProfileConfiguration(RouterDb routerDb)
        {
            _routerDb = routerDb;
            _profiles = new Dictionary<string, (Profile profile, EdgeFactorCache cache)>();
        }

        public bool HasProfile(string name)
        {
            return _profiles.ContainsKey(name);
        }

        public void AddProfiles(IEnumerable<Profile> profiles)
        {
            foreach (var profile in profiles) {
                _profiles[profile.Name] = (profile, new EdgeFactorCache());
            }

            UpdateEdgeTypeMap();
        }

        internal bool TryGetProfileHandlerEdgeTypesCache(Profile profile, out EdgeFactorCache? cache)
        {
            cache = null;
            if (!_profiles.TryGetValue(profile.Name, out var profileValue)) {
                return false;
            }

            cache = profileValue.cache;
            return true;
        }

        private void UpdateEdgeTypeMap()
        {
            // only update the edge type map when it is based on the active profiles.
            if (_routerDb.EdgeTypeMap is not ProfilesEdgeTypeMap) {
                return;
            }

            // update edge type map to include the new profile(s).
            var edgeTypeMap = new ProfilesEdgeTypeMap(_profiles.Values.Select(x => x.profile));
            _routerDb.EdgeTypeMap = edgeTypeMap;
        }

        public IEnumerable<Profile> Profiles => _profiles.Values.Select(x => x.profile);
    }
}