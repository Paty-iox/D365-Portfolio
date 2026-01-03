var ApexInsurance = window.ApexInsurance || {};

ApexInsurance.ClaimForm = (function () {
    "use strict";

    var FIELD_INCIDENT_LOCATION = "new_incidentlocation";
    var FIELD_INCIDENT_LATITUDE = "new_incidentlatitude";
    var FIELD_INCIDENT_LONGITUDE = "new_incidentlongitude";
    var WEB_RESOURCE_NAME = "WebResource_ClaimLocationMap";
    var ENV_VAR_AZURE_MAPS_KEY = "new_azuremapskey";
    var cachedAzureMapsKey = null;

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
                            // Key not configured - check environment variable
                            console.warn("Azure Maps key not set: " + ENV_VAR_AZURE_MAPS_KEY);
                            resolve(null);
                        }
                    },
                    function (error) {
                        console.error("Error retrieving Azure Maps key: " + error.message);
                        resolve(null);
                    }
                );
            } catch (error) {
                console.error("getAzureMapsKey error: " + error.message);
                resolve(null);
            }
        });
    }

    function getDynamicsOrigin() {
        try {
            var clientUrl = Xrm.Utility.getGlobalContext().getClientUrl();
            var url = new URL(clientUrl);
            return url.origin;
        } catch (e) {
            return window.location.origin;
        }
    }

    function sendMapKey(formContext) {
        getAzureMapsKey(formContext).then(function (key) {
            var webResourceControl = formContext.getControl(WEB_RESOURCE_NAME);
            if (!webResourceControl) return;

            var iframe = webResourceControl.getObject();
            if (iframe && iframe.contentWindow) {
                iframe.contentWindow.postMessage({ type: "initMapKey", key: key }, getDynamicsOrigin());
            }
        });
    }

    function updateMapWebResource(formContext) {
        try {
            var lat = formContext.getAttribute(FIELD_INCIDENT_LATITUDE);
            var lon = formContext.getAttribute(FIELD_INCIDENT_LONGITUDE);
            var location = formContext.getAttribute(FIELD_INCIDENT_LOCATION);

            var latValue = lat ? lat.getValue() : null;
            var lonValue = lon ? lon.getValue() : null;
            var locationValue = location ? location.getValue() : "";

            var webResourceControl = formContext.getControl(WEB_RESOURCE_NAME);
            if (!webResourceControl) return;

            var baseUrl = Xrm.Utility.getGlobalContext().getClientUrl() + "/WebResources/new_new_ClaimLocationMap";
            var params = [];

            if (latValue !== null && latValue !== undefined && !isNaN(latValue)) params.push("lat=" + encodeURIComponent(latValue));
            if (lonValue !== null && lonValue !== undefined && !isNaN(lonValue)) params.push("lon=" + encodeURIComponent(lonValue));
            if (locationValue) params.push("location=" + encodeURIComponent(locationValue));

            webResourceControl.setSrc(params.length > 0 ? baseUrl + "?" + params.join("&") : baseUrl);
            setTimeout(function () { sendMapKey(formContext); }, 500);
        } catch (error) {
            console.error("updateMapWebResource error: " + error.message);
        }
    }

    function sendMessageToMap(formContext) {
        try {
            var lat = formContext.getAttribute(FIELD_INCIDENT_LATITUDE);
            var lon = formContext.getAttribute(FIELD_INCIDENT_LONGITUDE);
            var location = formContext.getAttribute(FIELD_INCIDENT_LOCATION);

            var webResourceControl = formContext.getControl(WEB_RESOURCE_NAME);
            if (!webResourceControl) return;

            var iframe = webResourceControl.getObject();
            if (iframe && iframe.contentWindow) {
                iframe.contentWindow.postMessage({
                    type: "updateLocation",
                    lat: lat ? lat.getValue() : null,
                    lon: lon ? lon.getValue() : null,
                    location: location ? location.getValue() : ""
                }, getDynamicsOrigin());
            }
        } catch (error) {
            console.error("sendMessageToMap error: " + error.message);
        }
    }

    return {
        onLoad: function (executionContext) {
            try {
                updateMapWebResource(executionContext.getFormContext());
            } catch (error) {
                console.error("onLoad error: " + error.message);
            }
        },

        onCoordinatesChange: function (executionContext) {
            try {
                sendMessageToMap(executionContext.getFormContext());
            } catch (error) {
                console.error("onCoordinatesChange error: " + error.message);
            }
        },

        onLocationChange: function (executionContext) {
            try {
                sendMessageToMap(executionContext.getFormContext());
            } catch (error) {
                console.error("onLocationChange error: " + error.message);
            }
        },

        refreshMap: function (formContext) {
            try {
                if (formContext && formContext.data) updateMapWebResource(formContext);
            } catch (error) {
                console.error("refreshMap error: " + error.message);
            }
        }
    };
})();
