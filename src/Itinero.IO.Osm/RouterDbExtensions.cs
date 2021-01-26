using System;
using OsmSharp.Streams;

namespace Itinero.IO.Osm {
    /// <summary>
    /// Contains extensions method for the router db.
    /// </summary>
    public static class RouterDbExtensions {
        /// <summary>
        /// Loads the given OSM data into the router db.
        /// </summary>
        /// <param name="routerDb">The router db.</param>
        /// <param name="data">The data.</param>
        /// <param name="configure">The configure function.</param>
        public static void UseOsmData(this RouterDb routerDb, OsmStreamSource data,
            Action<DataProviderSettings>? configure = null) {
            // get writer.
            if (routerDb.HasMutableNetwork) {
                throw new InvalidOperationException(
                    $"Cannot add data to a {nameof(RouterDb)} that is only being written to.");
            }

            using var routerDbWriter = routerDb.GetMutableNetwork();

            // create settings.
            var settings = new DataProviderSettings();
            configure?.Invoke(settings);

            // get settings.
            var tagsFilter = settings.TagsFilter;
            var elevationHandler = settings.ElevationHandler;

            // use writer to fill router db.
            var routerDbStreamTarget = new RouterDbStreamTarget(routerDbWriter, tagsFilter, elevationHandler);
            routerDbStreamTarget.RegisterSource(data);
            routerDbStreamTarget.Initialize();
            routerDbStreamTarget.Pull();
        }
    }
}