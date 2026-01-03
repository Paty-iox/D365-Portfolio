const https = require('https');

const API_TIMEOUT_MS = 10000;

function getConfidence(score) {
    if (score >= 9.0) return 'High';
    if (score >= 7.0) return 'Medium';
    if (score >= 5.0) return 'Low';
    return 'VeryLow';
}

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
            res.on('data', function(chunk) { data += chunk; });
            res.on('end', function() {
                if (res.statusCode >= 200 && res.statusCode < 300) {
                    try {
                        resolve(JSON.parse(data));
                    } catch (e) {
                        reject(new Error('Invalid JSON response'));
                    }
                } else {
                    reject(new Error('Azure Maps API error: ' + res.statusCode));
                }
            });
        });

        req.on('error', reject);
        req.on('timeout', function() { req.destroy(); reject(new Error('Request timeout')); });
        req.end();
    });
}

module.exports = async function (context, req) {
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

    var azureMapsKey = process.env.AZURE_MAPS_KEY;
    if (!azureMapsKey) {
        context.log.error('AZURE_MAPS_KEY not set');
        context.res = { status: 500, headers: { 'Content-Type': 'application/json' }, body: { success: false, error: 'Geocoding service not configured' } };
        return;
    }

    if (!req.body || !req.body.address || typeof req.body.address !== 'string' || req.body.address.trim() === '') {
        context.res = { status: 400, headers: { 'Content-Type': 'application/json' }, body: { success: false, error: 'Address is required' } };
        return;
    }

    var trimmedAddress = req.body.address.trim();

    try {
        var data = await callAzureMaps(trimmedAddress, azureMapsKey);

        if (!data.results || data.results.length === 0) {
            context.res = { status: 200, headers: { 'Content-Type': 'application/json' }, body: { success: false, latitude: null, longitude: null, formattedAddress: null, confidence: null, error: 'No results found' } };
            return;
        }

        var result = data.results[0];
        context.res = {
            status: 200,
            headers: { 'Content-Type': 'application/json' },
            body: {
                success: true,
                latitude: result.position.lat,
                longitude: result.position.lon,
                formattedAddress: result.address.freeformAddress || null,
                confidence: getConfidence(result.score || 0)
            }
        };
    } catch (error) {
        context.log.error('Geocoding error: ' + error.message);
        context.res = { status: 500, headers: { 'Content-Type': 'application/json' }, body: { success: false, error: 'Geocoding service unavailable' } };
    }
};
