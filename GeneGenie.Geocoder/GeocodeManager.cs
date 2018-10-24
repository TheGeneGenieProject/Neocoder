﻿// <copyright file="GeocodeManager.cs" company="GeneGenie.com">
// Copyright (c) GeneGenie.com. All Rights Reserved.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root for license information.
// </copyright>

namespace GeneGenie.Geocoder
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using GeneGenie.Geocoder.Interfaces;
    using GeneGenie.Geocoder.Models.Geo;
    using GeneGenie.Geocoder.Services;

    /// <summary>
    /// The main entry point for looking up an address.
    /// Handles selection of the next available geocoder and retrying other geocoders if the response is not usable.
    /// </summary>
    public class GeocodeManager
    {
        private readonly IGeocoderSelector geocoderSelector;
        private readonly KeyComposer keyComposer;

        /// <summary>
        /// Initializes a new instance of the <see cref="GeocodeManager"/> class.
        /// </summary>
        /// <param name="geocoderSelector">An instance of <see cref="IGeocoderSelector"/> that is responsible for returning the next available geocoder.</param>
        /// <param name="keyComposer">An instance of <see cref="KeyComposer"/> used to construct a unique code per address looked up.</param>
        public GeocodeManager(IGeocoderSelector geocoderSelector, KeyComposer keyComposer)
        {
            this.geocoderSelector = geocoderSelector;
            this.keyComposer = keyComposer;
        }

        /// <summary>
        /// Takes an address as a string and then selects the next available geocoder to
        /// look up the address. Cycles through geocoders if they fails in order to get a result.
        /// </summary>
        /// <param name="address">The plain text address to look up.</param>
        /// <returns>A <see cref="GeocodeResponse"/> with the status of the lookup and a list of locations found.</returns>
        public async Task<GeocodeResponse> GeocodeAddressAsync(string address)
        {
            var geocodeRequest = new GeocodeRequest
            {
                Address = address,
                AddressKey = keyComposer.GenerateSourceKey(address),
                BoundsHint = null,
            };
            var addressLookupResult = new GeocodeResponse();

            var geocodersTried = new Dictionary<GeocoderNames, GeocodeStatus>();
            do
            {
                var excludeGeocoders = geocodersTried.Select(g => g.Key).ToList();
                var geocoder = await geocoderSelector.SelectNextGeocoderAsync(excludeGeocoders);
                if (geocoder == null)
                {
                    break;
                }

                var geocoderResponse = await geocoder.GeocodeAddressAsync(geocodeRequest);

                if (geocoderResponse.ResponseStatus == GeocodeStatus.Success)
                {
                    addressLookupResult.Locations = geocoderResponse
                        .Locations
                        .Select(l => new GeocodeResponseLocation
                        {
                            Bounds = l.Bounds,
                            FormattedAddress = l.FormattedAddress,
                            Location = l.Location,
                        })
                        .ToList();
                    addressLookupResult.GeocoderId = geocoder.GeocoderId;
                    addressLookupResult.Status = AddressLookupStatus.Geocoded;
                }

                geocodersTried.Add(geocoder.GeocoderId, geocoderResponse.ResponseStatus);
            }
            while (addressLookupResult.Status != AddressLookupStatus.Geocoded);

            addressLookupResult.Status = SummariseGeocodeStatus(geocodersTried);
            return addressLookupResult;
        }

        private AddressLookupStatus SummariseGeocodeStatus(Dictionary<GeocoderNames, GeocodeStatus> geocodersTried)
        {
            if (geocodersTried.ContainsValue(GeocodeStatus.Success))
            {
                return AddressLookupStatus.Geocoded;
            }

            if (geocodersTried.All(g => g.Value == GeocodeStatus.ZeroResults))
            {
                return AddressLookupStatus.ZeroResults;
            }

            if (geocodersTried.All(g => g.Value == GeocodeStatus.Error || g.Value == GeocodeStatus.InvalidRequest))
            {
                return AddressLookupStatus.PermanentGeocodeError;
            }

            if (geocodersTried.Any(g => g.Value == GeocodeStatus.RequestDenied || g.Value == GeocodeStatus.TooManyRequests))
            {
                return AddressLookupStatus.TemporaryGeocodeError;
            }

            return AddressLookupStatus.MultipleIssues;
        }
    }
}
