/**
 * GeocodeLocation Azure Function
 * Converts street addresses to latitude/longitude coordinates using Azure Maps API
 */

const https = require('https');

// Constants
const API_TIMEOUT_MS = 10000;

/**
 * Map Azure Maps score to confidence level
 */
function getConfidence(score) {
    if (score >= 9.0) return 'High';
    if (score >= 7.0) return 'Medium';
    if (score >= 5.0) return 'Low';
    return 'VeryLow';
}

/**
 * Make HTTPS GET request to Azure Maps
 */
function callAzureMaps(address, apiKey) {
    return new Promise((resolve, reject) => {
        const encodedAddress = encodeURIComponent(address);
        const path = '/search/address/json?api-version=1.0&subscription-key=' + apiKey + '&query=' + encodedAddress + '&limit=1&language=en-US';

        const options = {
            hostname: 'atlas.microsoft.com',
            path: path,
            method: 'GET',
            timeout: API_TIMEOUT_MS
        };

        const req = https.request(options, function(res) {
            let data = '';

            res.on('data', function(chunk) {
                data += chunk;
            });

            res.on('end', function() {
                if (res.statusCode >= 200 && res.statusCode < 300) {
                    try {
                        resolve(JSON.parse(data));
                    } catch (e) {
                        reject(new Error('Invalid JSON response from Azure Maps'));
                    }
                } else {
                    reject(new Error('Azure Maps API error: ' + res.statusCode));
                }
            });
        });

        req.on('error', function(e) {
            reject(e);
        });

        req.on('timeout', function() {
            req.destroy();
            reject(new Error('Request timeout'));
        });

        req.end();
    });
}

/**
 * Main function handler
 */
module.exports = async function (context, req) {
    context.log('GeocodeLocation function triggered');

    // Handle CORS preflight
    if (req.method === 'OPTIONS') {
        context.res = {
            status: 204,
            headers: {
                'Access-Control-Allow-Origin': '*',
                'Access-Control-Allow-Methods': 'POST, OPTIONS',
                'Access-Control-Allow-Headers': 'Content-Type, x-functions-key'
            }
        };
        return;
    }

    // Check for Azure Maps key
    var azureMapsKey = process.env.AZURE_MAPS_KEY;
    if (!azureMapsKey) {
        context.log.error('AZURE_MAPS_KEY environment variable is not set');
        context.res = {
            status: 500,
            headers: { 'Content-Type': 'application/json' },
            body: { success: false, error: 'Geocoding service is not configured' }
        };
        return;
    }

    // Validate request body
    if (!req.body) {
        context.res = {
            status: 400,
            headers: { 'Content-Type': 'application/json' },
            body: { success: false, error: 'Request body is required' }
        };
        return;
    }

    // Validate address field
    var address = req.body.address;
    if (!address || typeof address !== 'string' || address.trim() === '') {
        context.res = {
            status: 400,
            headers: { 'Content-Type': 'application/json' },
            body: { success: false, error: 'Address is required' }
        };
        return;
    }

    var trimmedAddress = address.trim();
    context.log('Geocoding address: ' + trimmedAddress.substring(0, 50));

    try {
        var data = await callAzureMaps(trimmedAddress, azureMapsKey);

        if (!data.results || data.results.length === 0) {
            context.res = {
                status: 200,
                headers: { 'Content-Type': 'application/json' },
                body: {
                    success: false,
                    latitude: null,
                    longitude: null,
                    formattedAddress: null,
                    confidence: null,
                    error: 'No results found for the provided address'
                }
            };
            return;
        }

        var result = data.results[0];
        var response = {
            success: true,
            latitude: result.position.lat,
            longitude: result.position.lon,
            formattedAddress: result.address.freeformAddress || null,
            confidence: getConfidence(result.score || 0)
        };

        context.log('Geocoding successful: ' + response.latitude + ', ' + response.longitude);

        context.res = {
            status: 200,
            headers: { 'Content-Type': 'application/json' },
            body: response
        };

    } catch (error) {
        context.log.error('Geocoding error: ' + error.message);
        context.res = {
            status: 500,
            headers: { 'Content-Type': 'application/json' },
            body: { success: false, error: 'Geocoding service unavailable' }
        };
    }
};
