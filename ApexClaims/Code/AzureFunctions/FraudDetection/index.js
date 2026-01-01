const { v4: uuidv4 } = require('uuid');

module.exports = async function (context, req) {
    const correlationId = req.headers['x-correlation-id'] || uuidv4();
    context.log(`FraudDetection triggered - correlationId: ${correlationId}`);

    // Validate required fields
    const requiredFields = ['claimId', 'policyId', 'claimType', 'amount', 'location', 'incidentDate', 'description'];
    const missingFields = requiredFields.filter(field => !req.body || req.body[field] === undefined || req.body[field] === null);

    if (missingFields.length > 0) {
        context.log(`Validation failed - correlationId: ${correlationId}, missing: ${missingFields.join(', ')}`);
        context.res = {
            status: 400,
            headers: { 'Content-Type': 'application/json' },
            body: {
                error: 'Missing required fields',
                missingFields: missingFields,
                message: `The following fields are required: ${missingFields.join(', ')}`
            }
        };
        return;
    }

    const { claimId, policyId, claimType, amount, location, incidentDate, description } = req.body;

    context.log(`Processing - correlationId: ${correlationId}, claimType: ${claimType}`);

    // Simulate processing delay (2-3 seconds)
    const delay = 2000 + Math.random() * 1000;
    await new Promise(resolve => setTimeout(resolve, delay));

    // Calculate fraud score
    let riskScore = 15; // Base score
    const riskFactors = [];

    // Parse dates
    const incidentDateObj = new Date(incidentDate);
    const now = new Date();
    const dayOfWeek = incidentDateObj.getDay();
    const daysSinceIncident = Math.floor((now - incidentDateObj) / (1000 * 60 * 60 * 24));

    // 1. Amount-based risk
    if (amount > 50000) {
        riskScore += 25;
        riskFactors.push('High claim amount');
    } else if (amount > 20000) {
        riskScore += 15;
        riskFactors.push('Elevated claim amount');
    } else if (amount > 10000) {
        riskScore += 5;
    }

    // 2. Time-based risk
    if (dayOfWeek === 0 || dayOfWeek === 6) {
        riskScore += 10;
        riskFactors.push('Weekend incident');
    }

    if (daysSinceIncident <= 1 && daysSinceIncident >= 0) {
        riskScore += 8;
        riskFactors.push('Rapid claim submission');
    }

    if (daysSinceIncident > 30) {
        riskScore += 12;
        riskFactors.push('Delayed reporting');
    }

    // 3. Location-based risk
    const locationLower = (location || '').toLowerCase();
    if (claimType === 'Auto' && (locationLower.includes('highway') || locationLower.includes('interstate'))) {
        riskScore += 5;
    }

    if ((location || '').length < 15) {
        riskScore += 10;
        riskFactors.push('Vague location details');
    }

    // 4. Description-based risk
    const descriptionLower = (description || '').toLowerCase();

    if ((description || '').length < 50) {
        riskScore += 15;
        riskFactors.push('Minimal incident description');
    }

    if (descriptionLower.includes('total loss') || descriptionLower.includes('totaled')) {
        riskScore += 10;
        riskFactors.push('Total loss claim');
    }

    if (descriptionLower.includes('witness') || descriptionLower.includes('police report')) {
        riskScore -= 10;
    }

    // 5. Claim type modifiers
    if (claimType === 'Commercial') {
        riskScore += 5;
    }

    if (claimType === 'Auto' && amount > 30000) {
        riskScore += 8;
        riskFactors.push('High-value auto claim');
    }

    // 6. Random variance (-5 to +10)
    const variance = Math.floor(Math.random() * 16) - 5;
    riskScore += variance;

    // Cap score between 0 and 100
    riskScore = Math.max(0, Math.min(100, riskScore));

    // Determine recommendation
    let recommendation;
    if (riskScore <= 30) {
        recommendation = 'Proceed';
    } else if (riskScore <= 60) {
        recommendation = 'Review';
    } else {
        recommendation = 'Investigate';
    }

    // Generate response
    const assessmentId = uuidv4();
    const response = {
        riskScore: riskScore,
        riskFactors: riskFactors,
        recommendation: recommendation,
        assessmentId: assessmentId,
        correlationId: correlationId,
        timestamp: new Date().toISOString()
    };

    context.log(`Complete - correlationId: ${correlationId}, score: ${riskScore}, recommendation: ${recommendation}`);

    context.res = {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
        body: response
    };
};
