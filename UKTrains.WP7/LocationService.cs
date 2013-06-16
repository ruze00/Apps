﻿using FSharp;
using System;
using System.Device.Location;

namespace UKTrains
{
    public static class LocationService
    {
        public static event Action LocationChanged;
        public static GeoCoordinate CurrentPosition { get; private set; }
        private static DateTimeOffset currentPositionTimestamp;

        static LocationService()
        {
            var latitude = Settings.GetDouble(Setting.CurrentLat);
            var longitude = Settings.GetDouble(Setting.CurrentLong);
            if (!double.IsNaN(latitude) && !double.IsNaN(longitude))
            {
                CurrentPosition = new GeoCoordinate(latitude, longitude);
            }

            if (Settings.GetBool(Setting.LocationServicesEnabled))
            {
                var watcher = new GeoCoordinateWatcher(GeoPositionAccuracy.High)
                {
                    MovementThreshold = 50
                };

                watcher.PositionChanged += OnGeoPositionChanged;
                watcher.Start();
            }
        }

        private static void OnGeoPositionChanged(object sender, GeoPositionChangedEventArgs<GeoCoordinate> e)
        {
            if (e.Position.Timestamp > currentPositionTimestamp)
            {       
                var previousPosition = CurrentPosition;
                var newPosition = e.Position.Location;
                if (previousPosition == null || previousPosition.IsUnknown || currentPositionTimestamp == default(DateTimeOffset) ||
                    GeoUtils.LatLong.Create(previousPosition.Latitude, previousPosition.Longitude) - GeoUtils.LatLong.Create(newPosition.Latitude, newPosition.Longitude) >= 0.1)
                {
                    CurrentPosition = newPosition;
                    Settings.Set(Setting.CurrentLat, CurrentPosition.Latitude);
                    Settings.Set(Setting.CurrentLong, CurrentPosition.Longitude);
                    if (LocationChanged != null)
                    {
                        LocationChanged();
                    }
                }
                currentPositionTimestamp = e.Position.Timestamp;
            }
        }
    }
}