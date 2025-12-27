/**
 * WeatherLookup Azure Function
 * Retrieves historical weather conditions from Open-Meteo API
 */

const https = require('https');

// API Configuration
const OPEN_METEO_HOST = 'archive-api.open-meteo.com';
const API_TIMEOUT_MS = 10000;

// WMO Weather Code Descriptions
const WEATHER_CODES = {
    0: 'Clear Sky',
    1: 'Mainly Clear',
    2: 'Partly Cloudy',
    3: 'Overcast',
    45: 'Foggy',
    48: 'Depositing Rime Fog',
    51: 'Light Drizzle',
    53: 'Moderate Drizzle',
    55: 'Dense Drizzle',
    56: 'Light Freezing Drizzle',
    57: 'Dense Freezing Drizzle',
    61: 'Slight Rain',
    63: 'Moderate Rain',
    65: 'Heavy Rain',
    66: 'Light Freezing Rain',
    67: 'Heavy Freezing Rain',
    71: 'Slight Snow',
    73: 'Moderate Snow',
    75: 'Heavy Snow',
    77: 'Snow Grains',
    80: 'Slight Rain Showers',
    81: 'Moderate Rain Showers',
    82: 'Violent Rain Showers',
    85: 'Slight Snow Showers',
    86: 'Heavy Snow Showers',
    95: 'Thunderstorm',
    96: 'Thunderstorm with Slight Hail',
    99: 'Thunderstorm with Heavy Hail'
};

// Unit conversion helpers
function celsiusToFahrenheit(c) {
    return Math.round(c * 9 / 5 + 32);
}

function kmhToMph(kmh) {
    return Math.round(kmh * 0.621371);
}

function mmToInches(mm) {
    return (mm * 0.0393701).toFixed(2);
}

// Get weather description from code
function getWeatherDescription(code) {
    return WEATHER_CODES[code] || 'Unknown';
}

// Parse and validate date input
function parseDate(dateInput) {
    if (!dateInput) {
        return { valid: false, error: 'Date is required' };
    }

    // Handle both "YYYY-MM-DD" and "YYYY-MM-DDTHH:mm:ssZ" formats
    let dateStr = dateInput;
    if (dateInput.includes('T')) {
        dateStr = dateInput.split('T')[0];
    }

    // Validate format
    const dateRegex = /^\d{4}-\d{2}-\d{2}$/;
    if (!dateRegex.test(dateStr)) {
        return { valid: false, error: 'Date must be in YYYY-MM-DD format' };
    }

    // Check if valid date
    const date = new Date(dateStr + 'T00:00:00Z');
    if (isNaN(date.getTime())) {
        return { valid: false, error: 'Invalid date' };
    }

    // Check if date is in the future
    const today = new Date();
    today.setHours(0, 0, 0, 0);
    const inputDate = new Date(dateStr + 'T00:00:00Z');

    // Allow up to 5 days ago minimum (Open-Meteo historical data has a delay)
    if (inputDate > today) {
        return { valid: false, error: 'Date cannot be in the future. Historical weather API only supports past dates.' };
    }

    return { valid: true, date: dateStr };
}

// Validate coordinates
function validateCoordinates(latitude, longitude) {
    if (latitude === undefined || latitude === null) {
        return { valid: false, error: 'Latitude is required' };
    }
    if (longitude === undefined || longitude === null) {
        return { valid: false, error: 'Longitude is required' };
    }

    const lat = parseFloat(latitude);
    const lon = parseFloat(longitude);

    if (isNaN(lat) || lat < -90 || lat > 90) {
        return { valid: false, error: 'Latitude must be between -90 and 90' };
    }
    if (isNaN(lon) || lon < -180 || lon > 180) {
        return { valid: false, error: 'Longitude must be between -180 and 180' };
    }

    return { valid: true, latitude: lat, longitude: lon };
}

// Call Open-Meteo Historical Weather API
function callOpenMeteoApi(latitude, longitude, date) {
    return new Promise((resolve, reject) => {
        const params = new URLSearchParams({
            latitude: latitude.toString(),
            longitude: longitude.toString(),
            start_date: date,
            end_date: date,
            daily: 'temperature_2m_max,temperature_2m_min,precipitation_sum,windspeed_10m_max,weathercode',
            timezone: 'auto'
        });

        const path = `/v1/archive?${params.toString()}`;

        const options = {
            hostname: OPEN_METEO_HOST,
            port: 443,
            path: path,
            method: 'GET',
            timeout: API_TIMEOUT_MS
        };

        const req = https.request(options, (res) => {
            let data = '';

            res.on('data', (chunk) => {
                data += chunk;
            });

            res.on('end', () => {
                try {
                    const json = JSON.parse(data);

                    if (res.statusCode !== 200) {
                        reject(new Error(json.reason || `API returned status ${res.statusCode}`));
                        return;
                    }

                    resolve(json);
                } catch (e) {
                    reject(new Error('Failed to parse API response'));
                }
            });
        });

        req.on('error', (e) => {
            reject(new Error(`API request failed: ${e.message}`));
        });

        req.on('timeout', () => {
            req.destroy();
            reject(new Error('API request timed out'));
        });

        req.end();
    });
}

// Build conditions summary string
function buildConditionsSummary(weatherData) {
    const daily = weatherData.daily;

    if (!daily || !daily.time || daily.time.length === 0) {
        return null;
    }

    const weatherCode = daily.weathercode ? daily.weathercode[0] : null;
    const tempMaxC = daily.temperature_2m_max ? daily.temperature_2m_max[0] : null;
    const tempMinC = daily.temperature_2m_min ? daily.temperature_2m_min[0] : null;
    const precipMm = daily.precipitation_sum ? daily.precipitation_sum[0] : 0;
    const windKmh = daily.windspeed_10m_max ? daily.windspeed_10m_max[0] : 0;

    // Check if we have valid data
    if (tempMaxC === null || tempMinC === null) {
        return null;
    }

    const weatherDescription = getWeatherDescription(weatherCode);
    const tempMaxF = celsiusToFahrenheit(tempMaxC);
    const tempMinF = celsiusToFahrenheit(tempMinC);
    const windMph = kmhToMph(windKmh || 0);
    const precipIn = mmToInches(precipMm || 0);

    // Build response object
    const details = {
        weatherCode: weatherCode,
        weatherDescription: weatherDescription,
        temperatureMaxC: Math.round(tempMaxC),
        temperatureMinC: Math.round(tempMinC),
        temperatureMaxF: tempMaxF,
        temperatureMinF: tempMinF,
        precipitationMm: Math.round((precipMm || 0) * 10) / 10,
        precipitationIn: parseFloat(precipIn),
        windSpeedKmh: Math.round(windKmh || 0),
        windSpeedMph: windMph
    };

    // Build summary string (compact format to fit in ~100 chars)
    const summary = `${weatherDescription}, High: ${tempMaxF}°F (${Math.round(tempMaxC)}°C), Low: ${tempMinF}°F (${Math.round(tempMinC)}°C), Wind: ${windMph} mph, Precip: ${precipIn} in`;

    return {
        conditions: summary,
        details: details
    };
}

// Main function handler
module.exports = async function (context, req) {
    context.log('WeatherLookup function processing request');

    // CORS headers
    const headers = {
        'Content-Type': 'application/json',
        'Access-Control-Allow-Origin': '*',
        'Access-Control-Allow-Methods': 'POST, OPTIONS',
        'Access-Control-Allow-Headers': 'Content-Type, x-functions-key'
    };

    // Handle OPTIONS preflight
    if (req.method === 'OPTIONS') {
        context.res = {
            status: 204,
            headers: headers,
            body: ''
        };
        return;
    }

    try {
        // Parse request body
        const body = req.body || {};
        const { latitude, longitude, date } = body;

        // Check required fields
        if (latitude === undefined || longitude === undefined || !date) {
            context.res = {
                status: 400,
                headers: headers,
                body: JSON.stringify({
                    success: false,
                    error: 'latitude, longitude, and date are required'
                })
            };
            return;
        }

        // Validate coordinates
        const coordValidation = validateCoordinates(latitude, longitude);
        if (!coordValidation.valid) {
            context.res = {
                status: 400,
                headers: headers,
                body: JSON.stringify({
                    success: false,
                    error: coordValidation.error
                })
            };
            return;
        }

        // Validate date
        const dateValidation = parseDate(date);
        if (!dateValidation.valid) {
            context.res = {
                status: 400,
                headers: headers,
                body: JSON.stringify({
                    success: false,
                    error: dateValidation.error
                })
            };
            return;
        }

        context.log(`Fetching weather for lat: ${coordValidation.latitude}, lon: ${coordValidation.longitude}, date: ${dateValidation.date}`);

        // Call Open-Meteo API
        const weatherData = await callOpenMeteoApi(
            coordValidation.latitude,
            coordValidation.longitude,
            dateValidation.date
        );

        // Parse and format response
        const result = buildConditionsSummary(weatherData);

        if (!result) {
            context.res = {
                status: 200,
                headers: headers,
                body: JSON.stringify({
                    success: false,
                    conditions: null,
                    error: 'Weather data not available for the specified date'
                })
            };
            return;
        }

        // Success response
        context.res = {
            status: 200,
            headers: headers,
            body: JSON.stringify({
                success: true,
                conditions: result.conditions,
                details: result.details
            })
        };

    } catch (error) {
        context.log.error(`WeatherLookup error: ${error.message}`);

        context.res = {
            status: 200,
            headers: headers,
            body: JSON.stringify({
                success: false,
                conditions: null,
                error: error.message || 'An unexpected error occurred'
            })
        };
    }
};
