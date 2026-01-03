const { v4: uuidv4 } = require('uuid');

module.exports = async function (context, req) {
    const correlationId = req.headers['x-correlation-id'] || uuidv4();

    const requiredFields = ['claimId', 'policyId', 'claimType', 'amount', 'location', 'incidentDate', 'description'];
    const missingFields = requiredFields.filter(field => !req.body || req.body[field] === undefined || req.body[field] === null);

    if (missingFields.length > 0) {
        context.res = { status: 400, headers: { 'Content-Type': 'application/json' }, body: { error: 'Missing required fields', missingFields } };
        return;
    }

    const { claimType, amount, location, incidentDate, description } = req.body;

    let riskScore = 15;
    const riskFactors = [];

    const incidentDateObj = new Date(incidentDate);
    const referenceDate = req.body.submissionDate ? new Date(req.body.submissionDate) : new Date();
    const dayOfWeek = incidentDateObj.getDay();
    const daysSinceIncident = Math.floor((referenceDate - incidentDateObj) / (1000 * 60 * 60 * 24));

    if (amount > 50000) { riskScore += 25; riskFactors.push('High claim amount'); }
    else if (amount > 20000) { riskScore += 15; riskFactors.push('Elevated claim amount'); }
    else if (amount > 10000) { riskScore += 5; }

    if (dayOfWeek === 0 || dayOfWeek === 6) { riskScore += 10; riskFactors.push('Weekend incident'); }
    if (daysSinceIncident <= 1 && daysSinceIncident >= 0) { riskScore += 8; riskFactors.push('Rapid claim submission'); }
    if (daysSinceIncident > 30) { riskScore += 12; riskFactors.push('Delayed reporting'); }

    const locationLower = (location || '').toLowerCase();
    if (claimType === 'Auto' && (locationLower.includes('highway') || locationLower.includes('interstate'))) { riskScore += 5; }
    if ((location || '').length < 15) { riskScore += 10; riskFactors.push('Vague location details'); }

    const descriptionLower = (description || '').toLowerCase();
    if ((description || '').length < 50) { riskScore += 15; riskFactors.push('Minimal incident description'); }
    if (descriptionLower.includes('total loss') || descriptionLower.includes('totaled')) { riskScore += 10; riskFactors.push('Total loss claim'); }
    if (descriptionLower.includes('witness') || descriptionLower.includes('police report')) { riskScore -= 10; }

    if (claimType === 'Commercial') { riskScore += 5; }
    if (claimType === 'Auto' && amount > 30000) { riskScore += 8; riskFactors.push('High-value auto claim'); }

    riskScore = Math.max(0, Math.min(100, riskScore));

    let recommendation = riskScore <= 30 ? 'Proceed' : riskScore <= 60 ? 'Review' : 'Investigate';

    context.res = {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
        body: {
            riskScore,
            riskFactors,
            recommendation,
            assessmentId: uuidv4(),
            correlationId,
            timestamp: new Date().toISOString()
        }
    };
};
