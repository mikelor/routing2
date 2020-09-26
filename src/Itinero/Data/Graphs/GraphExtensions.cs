using System;
using System.Collections.Generic;
using Itinero.Data.Graphs.Mutation;
using Itinero.Data.Graphs.Writing;

namespace Itinero.Data.Graphs
{
    internal static class GraphExtensions
    {
        internal static Graph Mutate(this Graph graph, Action<GraphMutator> mutate)
        {
            using var mutable = graph.GetAsMutable();
            mutate(mutable);
            return mutable.ToGraph();
        }

        internal static void Write(this Graph graph, Action<GraphWriter> write)
        {
            using var writer = graph.GetWriter();
            write(writer);
        }

        public static (double longitude, double latitude) GetVertex(this Graph graph, VertexId vertexId)
        {
            if (!graph.TryGetVertex(vertexId, out var longitude, out var latitude)) throw new ArgumentOutOfRangeException(nameof(vertexId), "Vertex not found!");

            return (longitude, latitude);
        }

        public static IEnumerable<VertexId> GetVertices(this Graph graph)
        {
            var enumerator = graph.GetVertexEnumerator();
            while (enumerator.MoveNext())
            {
                yield return enumerator.Current;
            }
        }

        public static IEnumerable<EdgeId> GetEdges(this Graph graph)
        {
            var edgeEnumerator = graph.GetEdgeEnumerator();
            foreach (var vertex in graph.GetVertices())
            {
                if (!edgeEnumerator.MoveTo(vertex)) continue;

                while (edgeEnumerator.MoveNext())
                {
                    if (!edgeEnumerator.Forward) continue;

                    yield return edgeEnumerator.Id;
                }
            }
        }
    }
}