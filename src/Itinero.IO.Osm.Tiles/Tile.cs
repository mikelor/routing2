﻿using System;

namespace Itinero.IO.Osm.Tiles
{
    internal class Tile
    {
        public Tile(uint x, uint y, int zoom)
        {
            X = x;
            Y = y;
            Zoom = zoom;

            CalculateBounds();
        }

        private void CalculateBounds()
        {
            var n = Math.PI - 2.0 * Math.PI * Y / Math.Pow(2.0, Zoom);
            Left = X / Math.Pow(2.0, Zoom) * 360.0 - 180.0;
            Top = 180.0 / Math.PI * Math.Atan(Math.Sinh(n));

            n = Math.PI - 2.0 * Math.PI * (Y + 1) / Math.Pow(2.0, Zoom);
            Right = (X + 1) / Math.Pow(2.0, Zoom) * 360.0 - 180.0;
            Bottom = 180.0 / Math.PI * Math.Atan(Math.Sinh(n));
        }

        /// <summary>
        /// Gets X.
        /// </summary>
        public uint X { get; private set; }

        /// <summary>
        /// Gets Y.
        /// </summary>
        public uint Y { get; private set; }

        /// <summary>
        /// Gets the zoom level.
        /// </summary>
        public int Zoom { get; private set; }

        /// <summary>
        /// Gets the top.
        /// </summary>
        public double Top { get; private set; }

        /// <summary>
        /// Get the bottom.
        /// </summary>
        public double Bottom { get; private set; }

        /// <summary>
        /// Get the left.
        /// </summary>
        public double Left { get; private set; }

        /// <summary>
        /// Gets the right.
        /// </summary>
        public double Right { get; private set; }

        /// <summary>
        /// Updates the data in this tile to correspond with the given local tile id.
        /// </summary>
        /// <param name="localId">The local tile id.</param>
        public void UpdateToLocalId(ulong localId)
        {
            var xMax = (ulong) (1 << (int) Zoom);

            X = (uint) (localId % xMax);
            Y = (uint) (localId / xMax);
        }

        /// <summary>
        /// Gets the local tile id.
        /// </summary>
        /// <remarks>This is the id relative to the zoom level.</remarks>
        public uint LocalId {
            get {
                var xMax = 1 << (int) Zoom;

                return (uint) (Y * xMax + X);
            }
        }

        /// <summary>
        /// Returns the tile for the given local id and zoom level.
        /// </summary>
        /// <param name="localId">The local id.</param>
        /// <param name="zoom">The zoom level.</param>
        /// <returns>The tile.</returns>
        public static Tile FromLocalId(ulong localId, int zoom)
        {
            var xMax = (ulong) (1 << zoom);

            return new Tile((uint) (localId % xMax),
                (uint) (localId / xMax), zoom);
        }

        /// <summary>
        /// Calculates the maximum local id for the given zoom level.
        /// </summary>
        /// <param name="zoom">The zoom level.</param>
        /// <returns>The maximum local id for the given zoom level</returns>
        public static ulong MaxLocalId(int zoom)
        {
            var xMax = (ulong) (1 << zoom);

            return xMax * xMax;
        }

        /// <summary>
        /// Converts a lat/lon pair to a set of local coordinates.
        /// </summary>
        /// <param name="latitude">The latitude.</param>
        /// <param name="longitude">The longitude.</param>
        /// <param name="resolution">The resolution.</param>
        /// <returns>A local coordinate pair.</returns>
        public (int x, int y) ToLocalCoordinates(double longitude, double latitude, int resolution)
        {
            var latStep = (Top - Bottom) / resolution;
            var lonStep = (Right - Left) / resolution;
            var top = Top;
            var left = Left;

            return ((int) ((longitude - left) / lonStep), (int) ((top - latitude) / latStep));
        }

        /// <summary> 
        /// Converts a set of local coordinates to a lat/lon pair.
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <param name="resolution"></param>
        /// <returns>A global coordinate pair.</returns>
        public (double longitude, double latitude) FromLocalCoordinates(int x, int y, int resolution)
        {
            var latStep = (Top - Bottom) / resolution;
            var lonStep = (Right - Left) / resolution;
            var top = Top;
            var left = Left;

            return (left + lonStep * x, top - y * latStep);
        }

        /// <summary>
        /// Gets the tile at the given coordinates for the given zoom level.
        /// </summary>
        /// <param name="latitude">The latitude.</param>
        /// <param name="longitude">The longitude.</param>
        /// <param name="zoom">The zoom level.</param>
        /// <returns>The tile a the given coordinates.</returns>
        public static Tile WorldToTile(double longitude, double latitude, int zoom)
        {
            var n = (int) Math.Floor(Math.Pow(2, zoom)); // replace by bitshifting?

            var rad = latitude / 180d * Math.PI;

            var x = (uint) ((longitude + 180.0f) / 360.0f * n);
            var y = (uint) (
                (1.0f - Math.Log(Math.Tan(rad) + 1.0f / Math.Cos(rad))
                    / Math.PI) / 2f * n);

            return new Tile(x, y, zoom);
        }

        public bool IsInside(double longitude, double latitude)
        {
            return !(Top <= latitude ||
                     Bottom >= latitude ||
                     Left >= longitude ||
                     Right <= longitude);
        }

        public override string ToString()
        {
            return $"{X},{Y}@{Zoom}";
        }
    }
}