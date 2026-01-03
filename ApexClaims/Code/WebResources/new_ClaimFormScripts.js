/**
 * Apex Insurance - Claim Form Scripts
 * Handles form events and web resource integration for the Claim entity
 */

var ApexInsurance = window.ApexInsurance || {};

ApexInsurance.ClaimForm = (function () {
    "use strict";

    // Field schema names
    var FIELD_INCIDENT_LOCATION = "new_incidentlocation";
    var FIELD_INCIDENT_LATITUDE = "new_incidentlatitude";
    var FIELD_INCIDENT_LONGITUDE = "new_incidentlongitude";

    // Web resource control name (must match what's configured in the form)
    var WEB_RESOURCE_NAME = "WebResource_ClaimLocationMap";

    // Configuration via Dataverse Environment Variables
    // See ApexClaims README for setup instructions
    var ENV_VAR_AZURE_MAPS_KEY = "new_azuremapskey";

    // Cached Azure Maps key
    var cachedAzureMapsKey = null;

    /**
     * Gets the Azure Maps key from Dataverse environment variable
     * @param {Object} formContext - The form context
     * @returns {Promise<string>} - The Azure Maps key
     */
    function getAzureMapsKey(formContext) {
        return new Promise(function (resolve) {
            if (cachedAzureMapsKey) {
                resolve(cachedAzureMapsKey);
                return;
            }

            try {
                Xrm.WebApi.retrieveMultipleRecords(
                    "environmentvariablevalue",
                    "?$select=value&$expand=EnvironmentVariableDefinitionId($select=schemaname)&$filter=EnvironmentVariableDefinitionId/schemaname eq '" + ENV_VAR_AZURE_MAPS_KEY + "' and statecode eq 0"
                ).then(
                    function (result) {
                        if (result.entities && result.entities.length > 0) {
                            cachedAzureMapsKey = result.entities[0].value;
                            resolve(cachedAzureMapsKey);
                        } else {
                            console.warn("ApexInsurance.ClaimForm: Azure Maps key not configured. Set environment variable: " + ENV_VAR_AZURE_MAPS_KEY);
                            resolve(null);
                        }
                    },
                    function (error) {
                        console.error("ApexInsurance.ClaimForm: Error retrieving Azure Maps key - " + error.message);
                        resolve(null);
                    }
                );
            } catch (error) {
                console.error("ApexInsurance.ClaimForm: Error in getAzureMapsKey - " + error.message);
                resolve(null);
            }
        });
    }

    /**
     * Gets the current Dynamics origin URL
     * @returns {string} - The origin URL
     */
    function getDynamicsOrigin() {
        try {
            var clientUrl = Xrm.Utility.getGlobalContext().getClientUrl();
            var url = new URL(clientUrl);
            return url.origin;
        } catch (e) {
            return window.location.origin;
        }
    }

    /**
     * Sends the Azure Maps key to the iframe
     * @param {Object} formContext - The form context
     */
    function sendMapKey(formContext) {
        getAzureMapsKey(formContext).then(function (key) {
            var webResourceControl = formContext.getControl(WEB_RESOURCE_NAME);
            if (!webResourceControl) {
                return;
            }

            var iframe = webResourceControl.getObject();
            if (iframe && iframe.contentWindow) {
                var origin = getDynamicsOrigin();
                iframe.contentWindow.postMessage({
                    type: "initMapKey",
                    key: key
                }, origin);
            }
        });
    }

    /**
     * Updates the map web resource with current coordinates
     * @param {Object} formContext - The form context
     */
    function updateMapWebResource(formContext) {
        try {
            // Get field values
            var lat = formContext.getAttribute(FIELD_INCIDENT_LATITUDE);
            var lon = formContext.getAttribute(FIELD_INCIDENT_LONGITUDE);
            var location = formContext.getAttribute(FIELD_INCIDENT_LOCATION);

            var latValue = lat ? lat.getValue() : null;
            var lonValue = lon ? lon.getValue() : null;
            var locationValue = location ? location.getValue() : "";

            // Get the web resource control
            var webResourceControl = formContext.getControl(WEB_RESOURCE_NAME);

            if (!webResourceControl) {
                console.warn("ApexInsurance.ClaimForm: Web resource control '" + WEB_RESOURCE_NAME + "' not found");
                return;
            }

            // Build URL with parameters (key is sent separately via postMessage)
            var baseUrl = Xrm.Utility.getGlobalContext().getClientUrl() + "/WebResources/new_new_ClaimLocationMap";
            var params = [];

            if (latValue !== null && latValue !== undefined && !isNaN(latValue)) {
                params.push("lat=" + encodeURIComponent(latValue));
            }

            if (lonValue !== null && lonValue !== undefined && !isNaN(lonValue)) {
                params.push("lon=" + encodeURIComponent(lonValue));
            }

            if (locationValue) {
                params.push("location=" + encodeURIComponent(locationValue));
            }

            var url = baseUrl;
            if (params.length > 0) {
                url += "?" + params.join("&");
            }

            // Set the web resource URL
            webResourceControl.setSrc(url);

            // Send the map key after iframe loads
            setTimeout(function () {
                sendMapKey(formContext);
            }, 500);

            console.log("ApexInsurance.ClaimForm: Map updated");

        } catch (error) {
            console.error("ApexInsurance.ClaimForm: Error updating map - " + error.message);
        }
    }

    /**
     * Sends location update message to iframe
     * @param {Object} formContext - The form context
     */
    function sendMessageToMap(formContext) {
        try {
            // Get field values
            var lat = formContext.getAttribute(FIELD_INCIDENT_LATITUDE);
            var lon = formContext.getAttribute(FIELD_INCIDENT_LONGITUDE);
            var location = formContext.getAttribute(FIELD_INCIDENT_LOCATION);

            var latValue = lat ? lat.getValue() : null;
            var lonValue = lon ? lon.getValue() : null;
            var locationValue = location ? location.getValue() : "";

            // Get the web resource control
            var webResourceControl = formContext.getControl(WEB_RESOURCE_NAME);

            if (!webResourceControl) {
                return;
            }

            // Get the iframe content window
            var iframe = webResourceControl.getObject();
            if (iframe && iframe.contentWindow) {
                var origin = getDynamicsOrigin();
                iframe.contentWindow.postMessage({
                    type: "updateLocation",
                    lat: latValue,
                    lon: lonValue,
                    location: locationValue
                }, origin);
            }

        } catch (error) {
            console.error("ApexInsurance.ClaimForm: Error sending message to map - " + error.message);
        }
    }

    // Public API
    return {
        /**
         * Form OnLoad event handler
         * Called when the form loads
         * @param {Object} executionContext - The execution context
         */
        onLoad: function (executionContext) {
            try {
                var formContext = executionContext.getFormContext();

                console.log("ApexInsurance.ClaimForm: Form loaded");

                // Update map with current coordinates
                updateMapWebResource(formContext);

            } catch (error) {
                console.error("ApexInsurance.ClaimForm: Error in onLoad - " + error.message);
            }
        },

        /**
         * Coordinates OnChange event handler
         * Called when latitude or longitude changes
         * @param {Object} executionContext - The execution context
         */
        onCoordinatesChange: function (executionContext) {
            try {
                var formContext = executionContext.getFormContext();

                console.log("ApexInsurance.ClaimForm: Coordinates changed");

                // Update map with new coordinates
                sendMessageToMap(formContext);

            } catch (error) {
                console.error("ApexInsurance.ClaimForm: Error in onCoordinatesChange - " + error.message);
            }
        },

        /**
         * Location OnChange event handler
         * Called when the incident location text changes
         * @param {Object} executionContext - The execution context
         */
        onLocationChange: function (executionContext) {
            try {
                var formContext = executionContext.getFormContext();

                console.log("ApexInsurance.ClaimForm: Location changed");

                // Update the map location label
                sendMessageToMap(formContext);

            } catch (error) {
                console.error("ApexInsurance.ClaimForm: Error in onLocationChange - " + error.message);
            }
        },

        /**
         * Manual refresh of the map
         * @param {Object} formContext - The form context
         */
        refreshMap: function (formContext) {
            try {
                if (formContext && formContext.data) {
                    updateMapWebResource(formContext);
                }
            } catch (error) {
                console.error("ApexInsurance.ClaimForm: Error in refreshMap - " + error.message);
            }
        }
    };

})();
