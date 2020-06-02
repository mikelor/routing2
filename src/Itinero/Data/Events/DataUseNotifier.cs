using System;
using Itinero.Data.Graphs;

namespace Itinero.Data.Events
{
    /// <summary>
    /// Notifies listeners about data use in a router db.
    /// </summary>
    public class DataUseNotifier
    {
        /// <summary>
        /// Event raised when a vertex was touched.
        /// </summary>
        public event Action<Network, VertexId>? OnVertexTouched;
        
        internal void NotifyVertex(Network network, VertexId vertex)
        {
            OnVertexTouched?.Invoke(network, vertex);
        }

        /// <summary>
        /// Event raised when data within a bounding box was touched.
        /// </summary>
        public event Action<Network, ((double longitude, double latitude) topLeft, (double longitude, double latitude) bottomRight)>? OnBoxTouched;

        internal void NotifyBox(Network network, 
            ((double longitude, double latitude) topLeft, (double longitude, double latitude) bottomRight) box)
        {
            OnBoxTouched?.Invoke(network, box);
        }
    }
}