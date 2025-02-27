﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Itinero.Geo;
using Itinero.Network.Enumerators.Edges;
using Itinero.Snapping;

namespace Itinero.Network.Search;

internal static class EdgeSearch
{
    /// <summary>
    /// Returns the closest edge to the center of the given box that has at least one vertex inside the given box.
    /// </summary>
    /// <param name="network">The network.</param>
    /// <param name="searchBox">The box to search in.</param>
    /// <param name="maxDistance">The maximum distance of any snap point returned relative to the center of the search box.</param>
    /// <param name="acceptableFunc">The function to determine if an edge is acceptable or not. If null any edge will be accepted.</param>
    /// <returns>The closest edge to the center of the box inside the given box.</returns>
    public static SnapPoint SnapInBox(this RoutingNetwork network,
        ((double longitude, double, float? e) topLeft, (double longitude, double latitude, float? e) bottomRight)
            searchBox,
        Func<IEdgeEnumerator<RoutingNetwork>, bool>? acceptableFunc = null, double maxDistance = double.MaxValue)
    {
        static bool CheckAcceptable(bool? isAcceptable, IEdgeEnumerator<RoutingNetwork> eEnum, Func<IEdgeEnumerator<RoutingNetwork>, bool>? acceptableFunc)
        {
            if (isAcceptable.HasValue)
            {
                return isAcceptable.Value;
            }

            if (acceptableFunc != null &&
                !acceptableFunc.Invoke(eEnum))
            { // edge cannot be used.
                return false;
            }

            return true;
        }

        var edgeEnumerator = network.SearchEdgesInBox(searchBox);
        var center = searchBox.Center();

        const double exactTolerance = 1;
        var bestDistance = maxDistance;
        (EdgeId edgeId, ushort offset) bestSnapPoint = (EdgeId.Empty, ushort.MaxValue);
        while (edgeEnumerator.MoveNext())
        {
            if (bestDistance <= 0)
            {
                break; // break when exact on an edge.
            }

            // search for the local snap point that improves the current best snap point.
            (EdgeId edgeId, double offset) localSnapPoint = (EdgeId.Empty, 0);
            var isAcceptable = (bool?)null;
            var completeShape = edgeEnumerator.GetCompleteShape();
            var length = 0.0;
            using (var completeShapeEnumerator = completeShape.GetEnumerator())
            {
                completeShapeEnumerator.MoveNext();
                var previous = completeShapeEnumerator.Current;

                // start with the first location.
                var distance = previous.DistanceEstimateInMeter(center);
                if (distance < bestDistance)
                {
                    isAcceptable = CheckAcceptable(isAcceptable, edgeEnumerator, acceptableFunc);
                    if (!isAcceptable.Value)
                    {
                        continue;
                    }

                    if (distance < exactTolerance)
                    {
                        distance = 0;
                    }

                    bestDistance = distance;
                    localSnapPoint = (edgeEnumerator.EdgeId, 0);
                }

                // loop over all pairs.
                while (completeShapeEnumerator.MoveNext())
                {
                    var current = completeShapeEnumerator.Current;

                    var segmentLength = previous.DistanceEstimateInMeter(current);

                    // first check the actual current location, it may be an exact match.
                    distance = current.DistanceEstimateInMeter(center);
                    if (distance < bestDistance)
                    {
                        isAcceptable = CheckAcceptable(isAcceptable, edgeEnumerator, acceptableFunc);
                        if (!isAcceptable.Value)
                        {
                            break;
                        }

                        if (distance < exactTolerance)
                        {
                            distance = 0;
                        }

                        bestDistance = distance;
                        localSnapPoint = (edgeEnumerator.EdgeId, length + segmentLength);
                    }

                    // update length.
                    var startLength = length;
                    length += segmentLength;

                    // TODO: figure this out, there has to be a way to not project every segment.
                    //                        // check if we even need to check.
                    //                        var previousDistance = previous.DistanceEstimateInMeter(center);
                    //                        var shapePointDistance = current.DistanceEstimateInMeter(center);
                    //                        if (previousDistance + segmentLength > bestDistance &&
                    //                            shapePointDistance + segmentLength > bestDistance)
                    //                        {
                    //                            continue;
                    //                        }

                    // project on line segment.
                    var line = (previous, current);
                    var originalPrevious = previous;
                    previous = current;
                    if (bestDistance <= 0)
                    {
                        // we need to continue, we need the total length.
                        continue;
                    }

                    var projected = line.ProjectOn(center);
                    if (!projected.HasValue)
                    {
                        continue;
                    }

                    distance = projected.Value.DistanceEstimateInMeter(center);
                    if (!(distance < bestDistance))
                    {
                        continue;
                    }

                    isAcceptable = CheckAcceptable(isAcceptable, edgeEnumerator, acceptableFunc);
                    if (!isAcceptable.Value)
                    {
                        break;
                    }

                    if (distance < exactTolerance)
                    {
                        distance = 0;
                    }

                    bestDistance = distance;
                    localSnapPoint = (edgeEnumerator.EdgeId,
                        startLength + originalPrevious.DistanceEstimateInMeter(projected.Value));
                }
            }

            // move to the nex edge if no better point was found.
            if (localSnapPoint.edgeId == EdgeId.Empty)
            {
                continue;
            }

            // calculate the actual offset.
            var offset = ushort.MaxValue;
            if (localSnapPoint.offset < length)
            {
                if (localSnapPoint.offset <= 0)
                {
                    offset = 0;
                }
                else
                {
                    offset = (ushort)(localSnapPoint.offset / length * ushort.MaxValue);
                }
            }

            // invert offset if edge is reversed.
            if (!edgeEnumerator.Forward)
            {
                offset = (ushort)(ushort.MaxValue - offset);
            }

            bestSnapPoint = (localSnapPoint.edgeId, offset);
        }

        return new SnapPoint(bestSnapPoint.edgeId, bestSnapPoint.offset);
    }

    /// <summary>
    /// Snaps all points in the given box that could potentially be snapping points.
    /// </summary>
    /// <param name="routerDb"></param>
    /// <param name="searchBox">The box to search in.</param>
    /// <param name="maxDistance">The maximum distance of any snap point returned relative to the center of the search box.</param>
    /// <param name="acceptableFunc">The function to determine if an edge is acceptable or not. If null any edge will be accepted.</param>
    /// <param name="nonOrthogonalEdges">When true the best potential location on each edge is returned, when false only orthogonal projected points.</param>
    /// <returns>All edges that could potentially be relevant snapping points, not only the closest.</returns>
    public static IEnumerable<SnapPoint> SnapAllInBox(this RoutingNetwork routerDb,
        ((double longitude, double latitude, float? e) topLeft, (double longitude, double latitude, float? e)
            bottomRight) searchBox,
        Func<IEdgeEnumerator<RoutingNetwork>, bool>? acceptableFunc = null, bool nonOrthogonalEdges = true, double maxDistance = double.MaxValue)
    {
        var edges = new HashSet<EdgeId>();

        bool CheckAcceptable(bool? isAcceptable, IEdgeEnumerator<RoutingNetwork> eEnum)
        {
            if (isAcceptable.HasValue)
            {
                return isAcceptable.Value;
            }

            if (acceptableFunc != null &&
                !acceptableFunc.Invoke(eEnum))
            { // edge cannot be used.
                return false;
            }

            return true;
        }

        var edgeEnumerator = routerDb.SearchEdgesInBox(searchBox);
        var center = searchBox.Center();

        while (edgeEnumerator.MoveNext())
        {
            if (edges.Contains(edgeEnumerator.EdgeId)) continue;

            edges.Add(edgeEnumerator.EdgeId);

            // search for the best snap point for the current edge.
            (EdgeId edgeId, double offset, bool isOrthoganal, double distance) bestEdgeSnapPoint =
                (EdgeId.Empty, 0, false, maxDistance);
            var isAcceptable = (bool?)null;
            var completeShape = edgeEnumerator.GetCompleteShape();
            var length = 0.0;
            using (var completeShapeEnumerator = completeShape.GetEnumerator())
            {
                completeShapeEnumerator.MoveNext();
                var previous = completeShapeEnumerator.Current;

                // start with the first location.
                var distance = previous.DistanceEstimateInMeter(center);
                if (distance < bestEdgeSnapPoint.distance)
                {
                    isAcceptable = CheckAcceptable(null, edgeEnumerator);
                    if (!isAcceptable.Value)
                    {
                        continue;
                    }

                    bestEdgeSnapPoint = (edgeEnumerator.EdgeId, 0, false, distance);
                }

                // loop over all pairs.
                while (completeShapeEnumerator.MoveNext())
                {
                    var current = completeShapeEnumerator.Current;

                    var segmentLength = previous.DistanceEstimateInMeter(current);

                    // first check the actual current location, it may be an exact match.
                    distance = current.DistanceEstimateInMeter(center);
                    if (distance < bestEdgeSnapPoint.distance)
                    {
                        isAcceptable = CheckAcceptable(isAcceptable, edgeEnumerator);
                        if (!isAcceptable.Value)
                        {
                            break;
                        }

                        bestEdgeSnapPoint = (edgeEnumerator.EdgeId, length + segmentLength, false, distance);
                    }

                    // update length.
                    var startLength = length;
                    length += segmentLength;

                    // project on line segment.
                    var line = (previous, current);
                    var originalPrevious = previous;
                    previous = current;

                    var projected = line.ProjectOn(center);
                    if (projected.HasValue)
                    {
                        distance = projected.Value.DistanceEstimateInMeter(center);
                        if (distance < bestEdgeSnapPoint.distance)
                        {
                            isAcceptable = CheckAcceptable(isAcceptable, edgeEnumerator);
                            if (isAcceptable.Value)
                            {
                                bestEdgeSnapPoint = (edgeEnumerator.EdgeId,
                                    startLength + originalPrevious.DistanceEstimateInMeter(projected.Value),
                                    true, distance);
                            }
                        }
                    }
                }
            }

            // move to the nex edge if no snap point was found.
            if (bestEdgeSnapPoint.edgeId == EdgeId.Empty)
            {
                continue;
            }

            // check type and return if needed.
            var returnEdge = bestEdgeSnapPoint.isOrthoganal || nonOrthogonalEdges;
            if (!returnEdge)
            {
                continue;
            }

            // calculate the actual offset.
            var offset = ushort.MaxValue;
            if (bestEdgeSnapPoint.offset < length)
            {
                if (bestEdgeSnapPoint.offset <= 0)
                {
                    offset = 0;
                }
                else
                {
                    offset = (ushort)(bestEdgeSnapPoint.offset / length * ushort.MaxValue);
                }
            }

            // invert offset if edge is reversed.
            if (!edgeEnumerator.Forward)
            {
                offset = (ushort)(ushort.MaxValue - offset);
            }

            yield return new SnapPoint(bestEdgeSnapPoint.edgeId, offset);
        }
    }

    /// <summary>
    /// Returns the closest vertex to the center of the given box.
    /// </summary>
    /// <param name="network">The network.</param>
    /// <param name="searchBox">The box to search in.</param>
    /// <param name="maxDistance">The maximum distance of any vertex returned relative to the center of the search box.</param>
    /// <param name="acceptableFunc">The function to determine if an edge is acceptable or not. If null any edge will be accepted.</param>
    /// <returns>The closest edge to the center of the box inside the given box.</returns>
    public static VertexId SnapToVertexInBox(this RoutingNetwork network,
        ((double longitude, double latitude, float? e) topLeft, (double longitude, double latitude, float? e)
            bottomRight) searchBox,
        Func<IEdgeEnumerator<RoutingNetwork>, bool>? acceptableFunc = null, double maxDistance = double.MaxValue)
    {
        static bool CheckAcceptable(IEdgeEnumerator<RoutingNetwork> eEnum, Func<IEdgeEnumerator<RoutingNetwork>, bool>? acceptableFunc = null)
        {
            if (acceptableFunc != null &&
                !acceptableFunc.Invoke(eEnum))
            { // edge cannot be used.
                return false;
            }

            return true;
        }

        var center = searchBox.Center();
        var closestDistance = maxDistance;
        var closest = VertexId.Empty;

        var vertices = network.SearchVerticesInBox(searchBox);
        var edgeEnumerator = network.GetEdgeEnumerator();
        foreach (var (vertex, location) in vertices)
        {
            var d = center.DistanceEstimateInMeter(location);
            if (d > closestDistance) continue;

            if (acceptableFunc == null)
            {
                closest = vertex;
                closestDistance = d;
                continue;
            }

            edgeEnumerator.MoveTo(vertex);
            while (edgeEnumerator.MoveNext())
            {
                if (!CheckAcceptable(edgeEnumerator, acceptableFunc)) continue;

                closest = vertex;
                closestDistance = d;
                break;
            }
        }

        return closest;
    }

    /// <summary>
    /// Snaps all vertices in the given box that could potentially be snapping vertices.
    /// </summary>
    /// <param name="network">The network.</param>
    /// <param name="searchBox">The box to search in.</param>
    /// <param name="maxDistance">The maximum distance of any vertex returned relative to the center of the search box.</param>
    /// <param name="acceptableFunc">The function to determine if an edge is acceptable or not. If null any edge will be accepted.</param>
    /// <returns>All the vertices within the box with at least one acceptable edge.</returns>
    public static IEnumerable<VertexId> SnapToAllVerticesInBox(this RoutingNetwork network,
        ((double longitude, double latitude, float? e) topLeft, (double longitude, double latitude, float? e)
            bottomRight) searchBox,
        Func<IEdgeEnumerator<RoutingNetwork>, bool>? acceptableFunc = null, double maxDistance = double.MaxValue)
    {
        static bool CheckAcceptable(IEdgeEnumerator<RoutingNetwork> eEnum, Func<IEdgeEnumerator<RoutingNetwork>, bool>? acceptableFunc = null)
        {
            if (acceptableFunc != null &&
                !acceptableFunc.Invoke(eEnum))
            { // edge cannot be used.
                return false;
            }

            return true;
        }

        var center = searchBox.Center();
        var vertices = network.SearchVerticesInBox(searchBox);
        var edgeEnumerator = network.GetEdgeEnumerator();
        foreach (var (vertex, location) in vertices)
        {
            edgeEnumerator.MoveTo(vertex);

            var d = center.DistanceEstimateInMeter(location);
            if (d > maxDistance) continue;

            while (edgeEnumerator.MoveNext())
            {
                if (!CheckAcceptable(edgeEnumerator, acceptableFunc)) continue;

                yield return vertex;
                break;
            }
        }
    }

    /// <summary>
    /// Enumerates all edges that have at least one vertex in the given bounding box.
    /// </summary>
    /// <param name="network">The network.</param>
    /// <param name="box">The box to enumerate in.</param>
    /// <returns>An enumerator with all the vertices and their location.</returns>
    public static IEdgeEnumerator<RoutingNetwork> SearchEdgesInBox(this RoutingNetwork network,
        ((double longitude, double latitude, float? e) topLeft, (double longitude, double latitude, float? e)
            bottomRight) box)
    {
        var vertices = network.SearchVerticesInBox(box);
        return new VertexEdgeEnumerator(network, vertices.Select((i) => i.vertex));
    }
}
