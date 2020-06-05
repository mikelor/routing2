using System;
using System.Collections.Generic;
using Itinero.Data.Graphs.EdgeTypes;
using Itinero.Data.Tiles;
using Reminiscence.Arrays;

namespace Itinero.Data.Graphs.Tiles
{
    internal partial class GraphTile
    {
        private const int CoordinateSizeInBytes = 3; // 3 bytes = 24 bits = 4096 x 4096, the needed resolution depends on the zoom-level, higher, less resolution.
        private const int TileResolutionInBits = CoordinateSizeInBytes * 8 / 2;
        private const int TileSizeInIndex = 5; // 4 bytes for the pointer, 1 for the size.
        
        private readonly uint _tileId;
        private readonly int _zoom; // the zoom level.

        // the next vertex id.
        private uint _nextVertexId = 0;
        // the vertex coordinates.
        private readonly ArrayBase<byte> _coordinates;
        // the pointers, per vertex, to their first edge.
        // TODO: investigate if it's worth storing these with less precision, one tile will never contain this much data.
        private readonly ArrayBase<uint> _pointers;
        
        // the next edge id.
        private uint _nextEdgeId = 0;
        // the edges.
        private readonly ArrayBase<byte> _edges;

        /// <summary>
        /// Creates a new tile.
        /// </summary>
        /// <param name="zoom">The zoom level.</param>
        /// <param name="tileId">The tile id.</param>
        public GraphTile(int zoom, uint tileId)
        {
            _zoom = zoom;
            _tileId = tileId;
            
            _pointers = new MemoryArray<uint>(0);
            _edges = new MemoryArray<byte>(0);
            
            
            _coordinates = new MemoryArray<byte>(0);
            _shapes = new MemoryArray<byte>(0);
            _attributes = new MemoryArray<byte>(0);
            _strings = new MemoryArray<string>(0);
        }
        
        private GraphTile(int zoom, uint tileId, ArrayBase<uint> pointers, ArrayBase<byte> edges,
            ArrayBase<byte> coordinates, ArrayBase<byte> shapes, ArrayBase<byte> attributes,
            ArrayBase<string> strings, uint nextVertexId, uint nextEdgeId, uint nextAttributePointer,
            uint nextShapePointer, uint nextStringId)
        {
            _zoom = zoom;
            _tileId = tileId;
            _pointers = pointers;
            _edges = edges;
            _coordinates = coordinates;
            _shapes = shapes;
            _attributes = attributes;
            _strings = strings;

            _nextVertexId = nextVertexId;
            _nextEdgeId = nextEdgeId;
            _nextAttributePointer = nextAttributePointer;
            _nextShapePointer = nextShapePointer;
            _nextStringId = nextStringId;
        }

        /// <summary>
        /// Clones this graph tile.
        /// </summary>
        /// <returns>The copy of this tile.</returns>
        public GraphTile Clone()
        {
            return new GraphTile(_zoom, _tileId, _pointers.Clone(), _edges.Clone(), _coordinates.Clone(),
                _shapes.Clone(), _attributes.Clone(), _strings.Clone(), _nextVertexId, _nextEdgeId, _nextAttributePointer, _nextShapePointer, _nextStringId);
        }

        /// <summary>
        /// Gets the tile id.
        /// </summary>
        public uint TileId => _tileId;

        /// <summary>
        /// Gets the number of vertices.
        /// </summary>
        public uint VertexCount => _nextVertexId;

        /// <summary>
        /// Adds a new vertex and returns its id.
        /// </summary>
        /// <param name="longitude">The longitude.</param>
        /// <param name="latitude">The latitude.</param>
        /// <returns>The ID of the new vertex.</returns>
        public VertexId AddVertex(double longitude, double latitude)
        {
            // set coordinate.
            SetCoordinate(_nextVertexId, longitude, latitude);

            // create id.
            var vertexId = new VertexId(_tileId, _nextVertexId);
            _nextVertexId++;
            
            // make room for edges.
            if (vertexId.LocalId >= _pointers.Length) _pointers.Resize(_pointers.Length + 1024);

            return vertexId;
        }

        /// <summary>
        /// Gets the vertex with the given id.
        /// </summary>
        /// <param name="vertex">The vertex.</param>
        /// <param name="longitude">The longitude.</param>
        /// <param name="latitude">The latitude.</param>
        /// <returns>True if the vertex exists.</returns>
        public bool TryGetVertex(VertexId vertex, out double longitude, out double latitude)
        {
            longitude = default;
            latitude = default;
            if (vertex.LocalId >= _nextVertexId) return false;
            
            GetCoordinate(vertex.LocalId, out longitude, out latitude);
            return true;
        }

        /// <summary>
        /// Adds a new edge and returns its id.
        /// </summary>
        /// <param name="vertex1">The first vertex.</param>
        /// <param name="vertex2">The second vertex.</param>
        /// <param name="shape">The shape."</param>
        /// <param name="attributes">The attributes."</param>
        /// <param name="edgeId">The edge id if this edge is a part of another tile.</param>
        /// <param name="edgeTypeId">The edge type id, if any.</param>
        /// <param name="length">The length in centimeters.</param>
        /// <returns>The new edge id.</returns>
        public EdgeId AddEdge(VertexId vertex1, VertexId vertex2, IEnumerable<(double longitude, double latitude)>? shape = null,
            IEnumerable<(string key, string value)>? attributes = null, EdgeId? edgeId = null, uint? edgeTypeId = null, uint? length = null)
        {
            if (vertex1.TileId != _tileId)
            { // this is a special case, an edge is added that is not part of this tile.
                // but it needs to be added because need to able to jump to neighbouring tiles.
                // the edge is added in this tile **and** in the other tile.
                if (edgeId == null) throw new ArgumentException("Cannot add an edge that doesn't start in this tile without a proper tile id.",
                    nameof(edgeId));
                
                // reverse the edge.
                var t = vertex1;
                vertex1 = vertex2;
                vertex2 = t;
            }
            else
            { // this edge starts in this tile, it get an id from this tile.
                edgeId = new EdgeId(_tileId, _nextEdgeId);
            }

            // write the edge data.
            var newEdgePointer = _nextEdgeId;
            var size = EncodeVertex(_edges, _tileId,_nextEdgeId, vertex1);
            _nextEdgeId += size;
            size = EncodeVertex(_edges, _tileId,_nextEdgeId, vertex2);
            _nextEdgeId += size;
            
            // get previous pointers if vertices already has edges
            // set the new pointers.
            // TODO: save the offset pointers, this prevents the need to decode two vertices for every edge.
            // we also need to decode just one next pointer for each edge.
            // we do need to save the first pointer in the global pointers list.
            // we can check if it's the first while adding it again.
            uint? v1p = null;
            if (vertex1.TileId == _tileId)
            {
                v1p = _pointers[vertex1.LocalId].DecodeNullableData();
                _pointers[vertex1.LocalId] = newEdgePointer.EncodeToNullableData();
            }

            uint? v2p = null;
            if (vertex2.TileId == _tileId)
            {
                v2p = _pointers[vertex2.LocalId].DecodeNullableData();
                _pointers[vertex2.LocalId] = newEdgePointer.EncodeToNullableData();
            }

            // set next pointers.
            size = EncodePointer(_edges, _nextEdgeId, v1p);
            _nextEdgeId += size;
            size = EncodePointer(_edges, _nextEdgeId, v2p);
            _nextEdgeId += size;
            
            // write edge id explicitly if not in this edge.
            if (vertex1.TileId != vertex2.TileId)
            { // this data will only be there for edges crossing tile boundaries.
                size = EncodeEdgeId(_edges, _tileId,_nextEdgeId, edgeId.Value);
                _nextEdgeId += size;
            }
            
            // write edge profile id.
            _nextEdgeId += SetDynamicUIn32Nullable(_edges, _nextEdgeId, edgeTypeId);

            // write length.
            _nextEdgeId += SetDynamicUIn32Nullable(_edges, _nextEdgeId, length);
            
            // take care of shape if any.
            uint? shapePointer = null;
            if (shape != null)
            {
                shapePointer = SetShape(shape);
            }
            size = EncodePointer(_edges, _nextEdgeId, shapePointer);
            _nextEdgeId += size;

            // take care of attributes if any.
            uint? attributesPointer = null;
            if (attributes != null)
            {
                attributesPointer = SetAttributes(attributes);
            }
            size = EncodePointer(_edges, _nextEdgeId, attributesPointer);
            _nextEdgeId += size;

            return edgeId.Value;
        }
        
        internal GraphTile ApplyNewEdgeTypeFunc(GraphEdgeTypeIndex edgeTypeIndex)
        {
            var edges = new MemoryArray<byte>(_edges.Length);
            var pointers = new MemoryArray<uint>(_pointers.Length);
            var nextEdgeId = _nextEdgeId;
            var p = 0U;
            var newP = 0U;
            while (p < nextEdgeId)
            {
                // read edge data.
                p += DecodeVertex(p, out var local1Id, out var tile1Id);
                var vertex1 = new VertexId(tile1Id, local1Id);
                p += DecodeVertex(p, out var local2Id, out var tile2Id);
                var vertex2 = new VertexId(tile2Id, local2Id);
                p += DecodePointer(p, out _);
                p += DecodePointer(p, out _);
                EdgeId? edgeId = null;
                if (tile1Id != tile2Id)
                {
                    p += DecodeEdgeId(p, out edgeId);
                }
                p += (uint)_edges.GetDynamicInt32Nullable(p, out var _);
                p += (uint)_edges.GetDynamicInt32Nullable(p, out var length);
                p += DecodePointer(p, out var shapePointer);
                p += DecodePointer(p, out var attributePointer);
                
                // generate new edge type id.
                var newEdgeTypeId = edgeTypeIndex.Get(this.GetAttributes(attributePointer));
                
                // write edge data again.
                var newEdgePointer = newP;
                newP += EncodeVertex(edges, _tileId, newP, vertex1);
                newP += EncodeVertex(edges, _tileId, newP, vertex2);
                uint? v1p = null;
                if (vertex1.TileId == _tileId)
                {
                    v1p = pointers[vertex1.LocalId].DecodeNullableData();
                    pointers[vertex1.LocalId] = newEdgePointer.EncodeToNullableData();
                }
                uint? v2p = null;
                if (vertex2.TileId == _tileId)
                {
                    v2p = pointers[vertex2.LocalId].DecodeNullableData();
                    pointers[vertex2.LocalId] = newEdgePointer.EncodeToNullableData();
                }

                newP += EncodePointer(edges, newP, v1p);
                newP += EncodePointer(edges, newP, v2p);
                if (edgeId != null)
                {
                    newP += EncodeEdgeId(edges, _tileId, newP, edgeId.Value);
                }
                newP += (uint)edges.SetDynamicUInt32Nullable(newP, newEdgeTypeId);
                newP += (uint)edges.SetDynamicUInt32Nullable(newP, length);
                newP += EncodePointer(edges, newP, shapePointer);
                newP += EncodePointer(edges, newP, attributePointer);
            }
            
            return new GraphTile(_zoom, _tileId, pointers, edges, _coordinates,
                _shapes, _attributes, _strings, _nextVertexId, _nextEdgeId, _nextAttributePointer, _nextShapePointer, _nextStringId);
        }

        private void SetCoordinate(uint localId, double longitude, double latitude)
        {    
            var tileCoordinatePointer = localId * CoordinateSizeInBytes * 2;
            if (_coordinates.Length <= tileCoordinatePointer + CoordinateSizeInBytes * 2)
            {
                _coordinates.Resize(_coordinates.Length + 1024);
            }

            const int resolution = (1 << TileResolutionInBits) - 1;
            var (x, y) = TileStatic.ToLocalTileCoordinates(_zoom, _tileId, longitude, latitude, resolution);
            _coordinates.SetFixed(tileCoordinatePointer, CoordinateSizeInBytes, x);
            _coordinates.SetFixed(tileCoordinatePointer + CoordinateSizeInBytes, CoordinateSizeInBytes, y);
        }

        private void GetCoordinate(uint localId, out double longitude, out double latitude)
        {
            var tileCoordinatePointer = localId * CoordinateSizeInBytes * 2;
            
            const int resolution = (1 << TileResolutionInBits) - 1;
            _coordinates.GetFixed(tileCoordinatePointer, CoordinateSizeInBytes, out var x);
            _coordinates.GetFixed(tileCoordinatePointer + CoordinateSizeInBytes, CoordinateSizeInBytes, out var y);

            TileStatic.FromLocalTileCoordinates(_zoom, _tileId, x, y, resolution, out longitude, out latitude);
        }

        internal uint VertexEdgePointer(uint vertex)
        {
            return this._pointers[vertex];
        }

        internal static uint EncodeVertex(ArrayBase<byte> edges, uint localTileId, uint location, VertexId vertexId)
        {
            if (vertexId.TileId == localTileId)
            { // same tile, only store local id.
                if (edges.Length <= location + 5)
                {
                    edges.Resize(edges.Length + 1024);
                }
                
                return (uint)edges.SetDynamicUInt32(location, vertexId.LocalId);
            }
            
            // other tile, store full id.
            if (edges.Length <= location + 10)
            {
                edges.Resize(edges.Length + 1024);
            }
            
            var encodedId = vertexId.Encode();
            return (uint) edges.SetDynamicUInt64(location, encodedId);
        }

        internal uint DecodeVertex(uint location, out uint localId, out uint tileId)
        {
            var size = (uint) _edges.GetDynamicUInt64(location, out var encodedId);
            if (encodedId < uint.MaxValue)
            {
                localId = (uint) encodedId;
                tileId = _tileId;
                return size;
            }

            VertexId.Decode(encodedId, out tileId, out localId);
            return size;
        }

        internal static uint EncodePointer(ArrayBase<byte> edges, uint location, uint? pointer)
        {
            if (edges.Length <= location + 5)
            {
                edges.Resize(edges.Length + 1024);
            }
            return (uint) edges.SetDynamicUInt32(location, 
                pointer.EncodeAsNullableData());
        }

        internal uint DecodePointer(uint location, out uint? pointer)
        {
            var size = _edges.GetDynamicUInt32(location, out var data);
            pointer = data.DecodeNullableData();
            return (uint)size;
        }

        internal static uint EncodeEdgeId(ArrayBase<byte> edges, uint localTileId, uint location, EdgeId edgeId)
        {
            ulong? encoded = null;
            if (edgeId.TileId != localTileId)
            {
                encoded = edgeId.Encode();
            
                if (edges.Length <= location + 9)
                {
                    edges.Resize(edges.Length + 1024);
                }
            }
            return (uint) edges.SetDynamicUInt64(location, 
                encoded.EncodeAsNullableData());
        }

        internal static uint SetDynamicUIn32Nullable(ArrayBase<byte> edges, uint pointer, uint? data)
        {
            while (edges.Length <= pointer + 5)
            {
                edges.Resize(edges.Length + 1024);
            }
            return (uint) edges.SetDynamicUInt32Nullable(pointer, data);
        }

        internal uint DecodeEdgeId(uint location, out EdgeId? edgeId)
        {            
            var size = (uint) _edges.GetDynamicUInt64(location, out var encodedId);
            var encodeNullable = encodedId.DecodeNullableData();

            edgeId = null;
            if (encodeNullable != null)
            {
                edgeId = EdgeId.Decode(encodeNullable.Value);
            }
            
            return size;
        }

        internal uint DecodeEdgePointerId(uint location, out uint? edgeProfileId)
        {
            return (uint) _edges.GetDynamicInt32Nullable(location, out edgeProfileId);
        }
    }
}