﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Itinero.Network;
using Itinero.Routes.Paths;
using Itinero.Routing.DataStructures;
using Itinero.Snapping;

[assembly: InternalsVisibleTo("Itinero.Tests")]
[assembly: InternalsVisibleTo("Itinero.Tests.Benchmarks")]
[assembly: InternalsVisibleTo("Itinero.Tests.Functional")]

namespace Itinero.Routing.Flavours.Dijkstra;

/// <summary>
/// A dijkstra implementation.
/// </summary>
internal class Dijkstra
{
    private readonly PathTree _tree = new();
    private readonly HashSet<VertexId> _visits = new();
    private readonly BinaryHeap<uint> _heap = new();

    public async Task<(Path? path, double cost)> RunAsync(RoutingNetwork network, SnapPoint source,
        SnapPoint target,
        DijkstraWeightFunc getDijkstraWeight, Func<VertexId, Task<bool>>? settled = null,
        Func<VertexId, Task<bool>>? queued = null)
    {
        var paths = await this.RunAsync(network, source, new[] { target }, getDijkstraWeight, settled, queued);

        return paths.Length < 1 ? (null, double.MaxValue) : paths[0];
    }

    /// <summary>
    /// Run a one-to-many Dijkstra search
    /// </summary>
    /// <param name="network"></param>
    /// <param name="source"></param>
    /// <param name="targets"></param>
    /// <param name="getDijkstraWeight"></param>
    /// <param name="settled"></param>
    /// <param name="queued">Queued notifies listeners when a vertex is queued. If this function returns false, the requested vertex won't be used during routeplanning.</param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    public async Task<(Path? path, double cost)[]> RunAsync(RoutingNetwork network, SnapPoint source,
        IReadOnlyList<SnapPoint> targets,
        DijkstraWeightFunc getDijkstraWeight, Func<VertexId, Task<bool>>? settled = null,
        Func<VertexId, Task<bool>>? queued = null)
    {
        // Returns the worst cost of all targets, i.e. the cost of the most costly target to reach
        // Will be Double.MAX_VALUE if at least one target hasn't been reached
        static double GetWorst((uint pointer, double cost)[] targets)
        {
            var worst = 0d;
            for (var i = 0; i < targets.Length; i++)
            {
                if (!(targets[i].cost > worst))
                {
                    continue;
                }

                worst = targets[i].cost;
                if (worst >= double.MaxValue)
                {
                    break;
                }
            }

            return worst;
        }

        var enumerator = network.GetEdgeEnumerator();

        _tree.Clear();
        _visits.Clear();
        _heap.Clear();

        // add sources.
        // add forward.
        if (!enumerator.MoveTo(source.EdgeId, true))
        {
            throw new Exception($"Edge in source {source} not found!");
        }

        var sourceCostForward = getDijkstraWeight(enumerator, Enumerable.Empty<(EdgeId edge, byte? turn)>()).cost;
        var sourceForwardVisit = uint.MaxValue;
        if (sourceCostForward > 0)
        {
            // can traverse edge in the forward direction.
            var sourceOffsetCostForward = sourceCostForward * (1 - source.OffsetFactor());
            sourceForwardVisit = _tree.AddVisit(enumerator, uint.MaxValue);
            _heap.Push(sourceForwardVisit, sourceOffsetCostForward);
        }

        // add backward.
        if (!enumerator.MoveTo(source.EdgeId, false))
        {
            throw new Exception($"Edge in source {source} not found!");
        }

        var sourceCostBackward = getDijkstraWeight(enumerator, Enumerable.Empty<(EdgeId edge, byte? turn)>()).cost;
        var sourceBackwardVisit = uint.MaxValue;
        if (sourceCostBackward > 0)
        {
            // can traverse edge in the backward direction.
            var sourceOffsetCostBackward = sourceCostBackward * source.OffsetFactor();
            sourceBackwardVisit = _tree.AddVisit(enumerator, uint.MaxValue);
            _heap.Push(sourceBackwardVisit, sourceOffsetCostBackward);
        }

        // add targets.
        var bestTargets = new (uint pointer, double cost)[targets.Count];
        var targetsPerVertex = new Dictionary<VertexId, List<int>>();
        for (var t = 0; t < targets.Count; t++)
        {
            bestTargets[t] = (uint.MaxValue, double.MaxValue);
            var target = targets[t];

            // add targets to vertices.
            if (!enumerator.MoveTo(target.EdgeId, true))
            {
                throw new Exception($"Edge in target {target} not found!");
            }

            if (!targetsPerVertex.TryGetValue(enumerator.Tail, out var targetsAtVertex))
            {
                targetsAtVertex = new List<int>();
                targetsPerVertex[enumerator.Tail] = targetsAtVertex;
            }

            targetsAtVertex.Add(t);
            if (!targetsPerVertex.TryGetValue(enumerator.Head, out targetsAtVertex))
            {
                targetsAtVertex = new List<int>();
                targetsPerVertex[enumerator.Head] = targetsAtVertex;
            }

            targetsAtVertex.Add(t);

            // consider paths 'within' a single edge.
            if (source.EdgeId == target.EdgeId)
            {
                // the source and target are on the same edge.
                if (source.Offset == target.Offset)
                {
                    // source and target are identical.
                    bestTargets[t] = (sourceForwardVisit, 0);
                }
                else if (source.Offset < target.Offset &&
                         sourceForwardVisit != uint.MaxValue)
                {
                    // the source is earlier in the direction of the edge
                    // and the edge can be traversed in this direction.
                    if (!enumerator.MoveTo(source.EdgeId, true))
                    {
                        throw new Exception($"Edge in source {source} not found!");
                    }

                    var weight = getDijkstraWeight(enumerator, Enumerable.Empty<(EdgeId edge, byte? turn)>()).cost *
                                 (target.OffsetFactor() - source.OffsetFactor());
                    bestTargets[t] = (sourceForwardVisit, weight);
                }
                else if (sourceBackwardVisit != uint.MaxValue)
                {
                    // the source is earlier against the direction of the edge
                    // and the edge can be traversed in this direction.
                    if (!enumerator.MoveTo(source.EdgeId, false))
                    {
                        throw new Exception($"Edge in source {source} not found!");
                    }

                    var weight = getDijkstraWeight(enumerator, Enumerable.Empty<(EdgeId edge, byte? turn)>()).cost *
                                 (source.OffsetFactor() - target.OffsetFactor());
                    bestTargets[t] = (sourceBackwardVisit, weight);
                }
            }
        }

        // update worst target cost.
        var worstTargetCost = GetWorst(bestTargets);

        // keep going until heap is empty.
        while (_heap.Count > 0)
        {
            if (_visits.Count > 1 << 20)
            {
                // TODO: come up with a stop condition that makes more sense to prevent the global network being loaded
                // when a route is not found.
                break;
            }

            // dequeue new visit.
            var currentPointer = _heap.Pop(out var currentCost);
            var currentVisit = _tree.GetVisit(currentPointer);
            while (_visits.Contains(currentVisit.vertex))
            {
                // visited before, skip.
                currentPointer = uint.MaxValue;
                if (_heap.Count == 0)
                {
                    break;
                }

                currentPointer = _heap.Pop(out currentCost);
                currentVisit = _tree.GetVisit(currentPointer);
            }

            if (currentPointer == uint.MaxValue)
            {
                break;
            }

            // log visit.
            _visits.Add(currentVisit.vertex);

            if (settled != null && await settled(currentVisit.vertex))
            {
                // break if requested.
                break;
            }

            // check if the search needs to stop.
            if (currentCost >= worstTargetCost)
            {
                // impossible to improve on cost to any target.
                break;
            }

            // check neighbours.
            if (!enumerator.MoveTo(currentVisit.vertex))
            {
                // no edges, move on!
                continue;
            }

            // check if this is a target.
            if (!targetsPerVertex.TryGetValue(currentVisit.vertex, out var targetsAtVertex))
            {
                targetsAtVertex = null;
            }

            while (enumerator.MoveNext())
            {
                // filter out if u-turns or visits on the same edge.
                var neighbourEdge = enumerator.EdgeId;
                if (neighbourEdge == currentVisit.edge)
                {
                    continue;
                }

                // gets the cost of the current edge.
                var (neighbourCost, turnCost) =
                    getDijkstraWeight(enumerator, _tree.GetPreviousEdges(currentPointer));
                if (neighbourCost is >= double.MaxValue or <= 0)
                {
                    continue;
                }

                if (turnCost is >= double.MaxValue or < 0)
                {
                    continue;
                }

                // if the vertex has targets, check if this edge is a match.
                var neighbourPointer = uint.MaxValue;
                if (targetsAtVertex != null)
                {
                    // We have found a target!

                    // only consider targets when found for the 'from' vertex.
                    // and when this in not a u-turn.
                    foreach (var t in targetsAtVertex)
                    {
                        var target = targets[t];
                        if (target.EdgeId != neighbourEdge)
                        {
                            continue;
                        }

                        // there is a target on this edge, calculate the cost.
                        // calculate the cost from the 'from' vertex to the target.
                        var targetCost = enumerator.Forward
                            ? neighbourCost * target.OffsetFactor()
                            : neighbourCost * (1 - target.OffsetFactor());
                        // this is the case where the target is on this edge 
                        // and there is a path to 'from' before.
                        targetCost += currentCost;

                        // add turn cost.
                        targetCost += turnCost;

                        // if this is an improvement, use it!
                        var targetBestCost = bestTargets[t].cost;
                        if (!(targetCost < targetBestCost))
                        {
                            continue;
                        }

                        // this is an improvement.
                        neighbourPointer = _tree.AddVisit(enumerator, currentPointer);
                        bestTargets[t] = (neighbourPointer, targetCost);

                        // update worst.
                        worstTargetCost = GetWorst(bestTargets);
                    }
                }

                if (queued != null &&
                    await queued(enumerator.Head))
                {
                    // don't queue this vertex if the queued function returns true.
                    continue;
                }

                // add visit if not added yet.
                if (neighbourPointer == uint.MaxValue)
                {
                    neighbourPointer = _tree.AddVisit(enumerator, currentPointer);
                }

                // add visit to heap.
                _heap.Push(neighbourPointer, neighbourCost + currentCost + turnCost);
            }
        }

        var paths = new (Path? path, double cost)[targets.Count];
        for (var p = 0; p < paths.Length; p++)
        {
            var bestTarget = bestTargets[p];
            if (bestTarget.pointer == uint.MaxValue)
            {
                paths[p] = (null, double.MaxValue);
                continue;
            }

            // build resulting path.
            var path = new Path(network);
            var visit = _tree.GetVisit(bestTarget.pointer);

            // path is at least two edges.
            while (true)
            {
                if (visit.previousPointer == uint.MaxValue)
                {
                    path.Prepend(visit.edge, visit.forward);
                    break;
                }

                path.Prepend(visit.edge, visit.forward);
                visit = _tree.GetVisit(visit.previousPointer);
            }

            // add the offsets.
            var target = targets[p];
            path.Offset1 = path.First.direction ? source.Offset : (ushort)(ushort.MaxValue - source.Offset);
            path.Offset2 = path.Last.direction
                ? target.Offset
                : (ushort)(ushort.MaxValue - target.Offset);

            paths[p] = (path, bestTarget.cost);
        }

        return paths;
    }

    /// <summary>
    /// Gets a default dijkstra instance.
    /// </summary>
    public static Dijkstra Default
    {
        get
        {
            return new Dijkstra();
        }
    }
}
