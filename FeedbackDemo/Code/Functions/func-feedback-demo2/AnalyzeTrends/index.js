// Compares current period stats against previous period
// Generates alerts for spikes and insights for the report

module.exports = async function (context, req) {
    context.log('Analyze Trends');

    const currentStats = req.body?.currentStats;

    if (!currentStats) {
        context.res = { status: 400, body: { error: 'currentStats required' } };
        return;
    }

    // Previous period baseline - in production this would come from Dataverse
    const previousPeriod = {
        totalFeedback: 42,
        bySentiment: { positive: 25, neutral: 10, negative: 7 },
        avgSentimentScore: 0.68
    };

    const trends = {
        volumeChange: {
            current: currentStats.totalFeedback,
            previous: previousPeriod.totalFeedback,
            change: currentStats.totalFeedback - previousPeriod.totalFeedback,
            percentChange: ((currentStats.totalFeedback - previousPeriod.totalFeedback) / previousPeriod.totalFeedback * 100).toFixed(1),
            trend: currentStats.totalFeedback > previousPeriod.totalFeedback ? 'increasing' : 'decreasing'
        },
        sentimentChange: {
            current: currentStats.avgSentimentScore,
            previous: previousPeriod.avgSentimentScore,
            change: (currentStats.avgSentimentScore - previousPeriod.avgSentimentScore).toFixed(2),
            trend: currentStats.avgSentimentScore > previousPeriod.avgSentimentScore ? 'improving' : 'declining'
        },
        alerts: [],
        insights: []
    };

    // Check for negative feedback spike
    if (currentStats.bySentiment.negative > previousPeriod.bySentiment.negative * 1.5) {
        trends.alerts.push({
            type: 'warning',
            title: 'Negative Feedback Spike',
            message: `Negative feedback increased by ${((currentStats.bySentiment.negative / previousPeriod.bySentiment.negative - 1) * 100).toFixed(0)}% compared to previous period`
        });
    }

    // Flag if too many critical cases
    if (currentStats.byPriority.critical > 2) {
        trends.alerts.push({
            type: 'critical',
            title: 'Critical Cases Pending',
            message: `${currentStats.byPriority.critical} critical cases require immediate attention`
        });
    }

    // Find top category
    const topCategory = Object.entries(currentStats.byCategory)
        .sort((a, b) => b[1] - a[1])[0];

    trends.insights.push({
        type: 'info',
        title: 'Top Feedback Category',
        message: `"${topCategory[0]}" received the most feedback with ${topCategory[1]} submissions`
    });

    // Check multilingual activity
    if (currentStats.byLanguage.english < currentStats.totalFeedback * 0.9) {
        const nonEnglish = currentStats.totalFeedback - currentStats.byLanguage.english;
        trends.insights.push({
            type: 'info',
            title: 'Multilingual Customers',
            message: `${nonEnglish} feedbacks (${(nonEnglish/currentStats.totalFeedback*100).toFixed(0)}%) were from non-English speaking customers`
        });
    }

    trends.insights.push({
        type: 'success',
        title: 'Sentiment Overview',
        message: `Overall sentiment is ${trends.sentimentChange.trend} with ${(currentStats.avgSentimentScore * 100).toFixed(0)}% positive score`
    });

    // Recommendations
    trends.recommendations = [];

    if (currentStats.byPriority.critical > 0) {
        trends.recommendations.push('Prioritize resolution of critical cases within 24 hours');
    }

    if (currentStats.bySentiment.negative > currentStats.bySentiment.positive * 0.3) {
        trends.recommendations.push('Review negative feedback patterns and identify root causes');
    }

    trends.recommendations.push('Continue monitoring top key phrases for emerging issues');

    trends.analyzedAt = new Date().toISOString();

    context.res = {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
        body: trends
    };
};
