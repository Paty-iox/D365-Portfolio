// Returns feedback statistics for the dashboard
// Currently using demo data - swap out with Dataverse queries for production

module.exports = async function (context, req) {
    context.log('Fetch Feedback Stats');

    const startDate = req.body?.startDate || getYesterday();
    const endDate = req.body?.endDate || getToday();

    context.log('Date range:', startDate, 'to', endDate);

    // Demo data - replace with actual Dataverse query
    const stats = {
        dateRange: { start: startDate, end: endDate },
        totalFeedback: 47,
        bySentiment: {
            positive: 28,
            neutral: 12,
            negative: 7
        },
        byCategory: {
            product: 18,
            service: 15,
            support: 10,
            other: 4
        },
        byPriority: {
            low: 25,
            medium: 12,
            high: 7,
            critical: 3
        },
        byLanguage: {
            english: 38,
            spanish: 5,
            german: 3,
            french: 1
        },
        avgSentimentScore: 0.72,
        avgResponseTime: "2.3 hours",
        topKeyPhrases: [
            { phrase: "customer service", count: 12 },
            { phrase: "product quality", count: 9 },
            { phrase: "delivery time", count: 7 },
            { phrase: "easy to use", count: 6 },
            { phrase: "great experience", count: 5 }
        ],
        criticalCases: [
            { id: "FB-001", customer: "John Smith", issue: "Product defect", waitTime: "48 hours" },
            { id: "FB-002", customer: "Jane Doe", issue: "Billing error", waitTime: "24 hours" },
            { id: "FB-003", customer: "Bob Wilson", issue: "Service outage", waitTime: "12 hours" }
        ],
        generatedAt: new Date().toISOString()
    };

    context.res = {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
        body: stats
    };
};

function getYesterday() {
    const d = new Date();
    d.setDate(d.getDate() - 1);
    return d.toISOString().split('T')[0];
}

function getToday() {
    return new Date().toISOString().split('T')[0];
}
