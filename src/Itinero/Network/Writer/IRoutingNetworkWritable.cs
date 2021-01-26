using System;
using System.Collections.Generic;
using Itinero.Network.Tiles;

namespace Itinero.Network.Writer {
    internal interface IRoutingNetworkWritable {
        int Zoom { get; }

        RouterDb RouterDb { get; }

        bool TryGetVertex(VertexId vertexId, out double longitude, out double latitude, out float? elevation);

        (NetworkTile tile, Func<IEnumerable<(string key, string value)>, uint> func) GetTileForWrite(uint localTileId);

        void ClearWriter();
    }
}