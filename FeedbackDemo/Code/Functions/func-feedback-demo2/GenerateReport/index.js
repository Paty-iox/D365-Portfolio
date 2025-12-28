// Generates HTML report and Teams message from stats and trends data
// Called by the daily analytics Logic App

// Escape HTML to prevent XSS
function escapeHtml(str) {
    if (!str) return '';
    return String(str)
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;')
        .replace(/'/g, '&#39;');
}

module.exports = async function (context, req) {
    context.log('Generate Report');

    const stats = req.body?.stats;
    const trends = req.body?.trends;

    if (!stats || !trends) {
        context.res = { status: 400, body: { error: 'stats and trends required' } };
        return;
    }

    const reportDate = new Date().toLocaleDateString('en-US', {
        weekday: 'long', year: 'numeric', month: 'long', day: 'numeric'
    }).replace(',', '');

    const footerDate = new Date().toLocaleDateString('en-US', {
        year: 'numeric', month: 'long', day: 'numeric'
    }).replace(',', '');

    // HTML report for email
    const htmlReport = `
<!DOCTYPE html>
<html>
<head>
    <style>
        * { margin: 0; padding: 0; box-sizing: border-box; }
        body {
            font-family: 'Georgia', 'Times New Roman', serif;
            color: #1a1a1a;
            line-height: 1.6;
            background: #ffffff;
        }
        .container {
            max-width: 700px;
            margin: 0 auto;
            padding: 40px;
        }
        .header {
            border-bottom: 3px solid #1a1a1a;
            padding-bottom: 20px;
            margin-bottom: 30px;
        }
        .header-label {
            font-family: 'Arial', sans-serif;
            font-size: 11px;
            text-transform: uppercase;
            letter-spacing: 2px;
            color: #666;
            margin-bottom: 5px;
        }
        .header h1 {
            font-size: 28px;
            font-weight: normal;
            color: #1a1a1a;
            margin-bottom: 5px;
        }
        .header-date {
            font-family: 'Arial', sans-serif;
            font-size: 13px;
            color: #666;
        }
        .executive-summary {
            background: #f8f8f8;
            padding: 25px;
            margin-bottom: 35px;
            border-left: 4px solid #1a1a1a;
        }
        .executive-summary h2 {
            font-family: 'Arial', sans-serif;
            font-size: 12px;
            text-transform: uppercase;
            letter-spacing: 1.5px;
            color: #666;
            margin-bottom: 15px;
        }
        .executive-summary p {
            font-size: 15px;
            color: #333;
        }
        .metrics-grid {
            display: grid;
            grid-template-columns: repeat(4, 1fr);
            gap: 20px;
            margin-bottom: 35px;
        }
        .metric {
            text-align: center;
            padding: 15px 10px;
            border: 1px solid #e0e0e0;
        }
        .metric-value {
            font-size: 32px;
            font-weight: normal;
            color: #1a1a1a;
            display: block;
        }
        .metric-label {
            font-family: 'Arial', sans-serif;
            font-size: 10px;
            text-transform: uppercase;
            letter-spacing: 1px;
            color: #666;
            margin-top: 5px;
        }
        .metric-change {
            font-family: 'Arial', sans-serif;
            font-size: 11px;
            margin-top: 5px;
        }
        .metric-change.positive { color: #2e7d32; }
        .metric-change.negative { color: #c62828; }
        .section {
            margin-bottom: 35px;
        }
        .section h2 {
            font-family: 'Arial', sans-serif;
            font-size: 12px;
            text-transform: uppercase;
            letter-spacing: 1.5px;
            color: #1a1a1a;
            border-bottom: 1px solid #e0e0e0;
            padding-bottom: 10px;
            margin-bottom: 20px;
        }
        .sentiment-container {
            margin: 20px 0;
        }
        .sentiment-bar {
            display: flex;
            height: 8px;
            background: #e0e0e0;
            margin-bottom: 15px;
        }
        .sentiment-positive { background: #1a1a1a; }
        .sentiment-neutral { background: #999; }
        .sentiment-negative { background: #c62828; }
        .sentiment-legend {
            font-family: 'Arial', sans-serif;
            font-size: 12px;
            color: #666;
        }
        .sentiment-legend span {
            margin-right: 20px;
        }
        .alert {
            padding: 15px 20px;
            margin-bottom: 12px;
            border-left: 3px solid #c62828;
            background: #fafafa;
        }
        .alert-title {
            font-family: 'Arial', sans-serif;
            font-size: 12px;
            font-weight: bold;
            text-transform: uppercase;
            letter-spacing: 0.5px;
            color: #c62828;
            margin-bottom: 5px;
        }
        .alert-message {
            font-size: 14px;
            color: #333;
        }
        .insight {
            padding: 15px 0;
        }
        .insight-title {
            font-family: 'Arial', sans-serif;
            font-size: 11px;
            font-weight: bold;
            text-transform: uppercase;
            letter-spacing: 0.5px;
            color: #666;
            margin-bottom: 5px;
        }
        .insight-message {
            font-size: 14px;
            color: #333;
        }
        .key-phrases {
            display: flex;
            flex-wrap: wrap;
            gap: 8px;
        }
        .key-phrase {
            font-family: 'Arial', sans-serif;
            font-size: 12px;
            padding: 6px 14px;
            background: #f5f5f5;
            color: #333;
        }
        .key-phrase-count {
            color: #999;
            margin-left: 5px;
        }
        .case-table {
            width: 100%;
            border-collapse: collapse;
            font-family: 'Arial', sans-serif;
            font-size: 13px;
        }
        .case-table th {
            text-align: left;
            padding: 10px;
            border-bottom: 2px solid #1a1a1a;
            font-size: 10px;
            text-transform: uppercase;
            letter-spacing: 1px;
            color: #666;
            font-weight: normal;
        }
        .case-table td {
            padding: 12px 10px;
            color: #333;
        }
        .case-id {
            font-weight: bold;
            color: #c62828;
        }
        .footer {
            margin-top: 40px;
            padding-top: 20px;
            border-top: 1px solid #e0e0e0;
            font-family: 'Arial', sans-serif;
            font-size: 11px;
            color: #999;
            text-align: center;
        }
    </style>
</head>
<body>
    <div class="container">
        <div class="header">
            <div class="header-label">Customer Feedback Analytics</div>
            <h1>Daily Performance Report</h1>
            <div class="header-date">${reportDate}</div>
        </div>

        <div class="executive-summary">
            <h2>Executive Summary</h2>
            <p>
                During the reporting period, the organization received <strong>${stats.totalFeedback} customer feedback submissions</strong>,
                representing a ${Math.abs(trends.volumeChange.percentChange)}% ${trends.volumeChange.trend === 'increasing' ? 'increase' : 'decrease'}
                compared to the previous period. Overall sentiment remains ${stats.avgSentimentScore >= 0.7 ? 'positive' : stats.avgSentimentScore >= 0.4 ? 'moderate' : 'concerning'}
                with a ${(stats.avgSentimentScore * 100).toFixed(0)}% favorability score.
                ${stats.byPriority.critical > 0 ? `There are ${stats.byPriority.critical} critical cases requiring immediate attention.` : 'No critical cases are pending.'}
            </p>
        </div>

        <div class="metrics-grid">
            <div class="metric">
                <span class="metric-value">${stats.totalFeedback}</span>
                <div class="metric-label">Total Feedback</div>
                <div class="metric-change ${trends.volumeChange.trend === 'increasing' ? 'positive' : 'negative'}">
                    ${trends.volumeChange.trend === 'increasing' ? '+' : ''}${trends.volumeChange.percentChange}%
                </div>
            </div>
            <div class="metric">
                <span class="metric-value">${stats.bySentiment.positive}</span>
                <div class="metric-label">Positive</div>
                <div class="metric-change positive">${(stats.bySentiment.positive/stats.totalFeedback*100).toFixed(0)}%</div>
            </div>
            <div class="metric">
                <span class="metric-value">${stats.bySentiment.negative}</span>
                <div class="metric-label">Negative</div>
                <div class="metric-change negative">${(stats.bySentiment.negative/stats.totalFeedback*100).toFixed(0)}%</div>
            </div>
            <div class="metric">
                <span class="metric-value">${stats.byPriority.critical}</span>
                <div class="metric-label">Critical</div>
                <div class="metric-change ${stats.byPriority.critical > 0 ? 'negative' : 'positive'}">Pending</div>
            </div>
        </div>

        <div class="section">
            <h2>Sentiment Distribution</h2>
            <div class="sentiment-container">
                <div class="sentiment-bar">
                    <div class="sentiment-positive" style="width: ${(stats.bySentiment.positive/stats.totalFeedback*100).toFixed(0)}%"></div>
                    <div class="sentiment-neutral" style="width: ${(stats.bySentiment.neutral/stats.totalFeedback*100).toFixed(0)}%"></div>
                    <div class="sentiment-negative" style="width: ${(stats.bySentiment.negative/stats.totalFeedback*100).toFixed(0)}%"></div>
                </div>
                <div class="sentiment-legend">
                    <span>Positive: ${stats.bySentiment.positive} (${(stats.bySentiment.positive/stats.totalFeedback*100).toFixed(0)}%)</span>
                    <span>Neutral: ${stats.bySentiment.neutral} (${(stats.bySentiment.neutral/stats.totalFeedback*100).toFixed(0)}%)</span>
                    <span>Negative: ${stats.bySentiment.negative} (${(stats.bySentiment.negative/stats.totalFeedback*100).toFixed(0)}%)</span>
                </div>
            </div>
        </div>

        ${trends.alerts.length > 0 ? `
        <div class="section">
            <h2>Items Requiring Attention</h2>
            ${trends.alerts.map(a => `
                <div class="alert">
                    <div class="alert-title">${escapeHtml(a.title)}</div>
                    <div class="alert-message">${escapeHtml(a.message)}</div>
                </div>
            `).join('')}
        </div>
        ` : ''}

        <div class="section">
            <h2>Key Insights</h2>
            ${trends.insights.map(i => `
                <div class="insight">
                    <div class="insight-title">${escapeHtml(i.title)}</div>
                    <div class="insight-message">${escapeHtml(i.message)}</div>
                </div>
            `).join('')}
        </div>

        <div class="section">
            <h2>Frequently Mentioned Topics</h2>
            <div class="key-phrases">
                ${stats.topKeyPhrases.map(kp => `
                    <span class="key-phrase">${escapeHtml(kp.phrase)}<span class="key-phrase-count">(${kp.count})</span></span>
                `).join('')}
            </div>
        </div>

        ${stats.criticalCases.length > 0 ? `
        <div class="section">
            <h2>Critical Cases</h2>
            <table class="case-table">
                <thead>
                    <tr>
                        <th>Reference</th>
                        <th>Customer</th>
                        <th>Issue</th>
                        <th>Wait Time</th>
                    </tr>
                </thead>
                <tbody>
                    ${stats.criticalCases.map(c => `
                        <tr>
                            <td><span class="case-id">${escapeHtml(c.id)}</span></td>
                            <td>${escapeHtml(c.customer)}</td>
                            <td>${escapeHtml(c.issue)}</td>
                            <td>${escapeHtml(c.waitTime)}</td>
                        </tr>
                    `).join('')}
                </tbody>
            </table>
        </div>
        ` : ''}

        <div class="footer">
            This report was automatically generated by the Customer Feedback Analytics System<br>
            Report Date: ${footerDate}
        </div>
    </div>
</body>
</html>`;

    // Plain text version for Teams
    const teamsSummary = `DAILY FEEDBACK REPORT
${reportDate}
────────────────────────────────────

EXECUTIVE SUMMARY

During the reporting period, the organization received ${stats.totalFeedback} customer feedback submissions, representing a ${Math.abs(trends.volumeChange.percentChange)}% ${trends.volumeChange.trend === 'increasing' ? 'increase' : 'decrease'} compared to the previous period.

KEY METRICS
- Total Submissions: ${stats.totalFeedback}
- Positive: ${stats.bySentiment.positive} (${(stats.bySentiment.positive/stats.totalFeedback*100).toFixed(0)}%)
- Neutral: ${stats.bySentiment.neutral} (${(stats.bySentiment.neutral/stats.totalFeedback*100).toFixed(0)}%)
- Negative: ${stats.bySentiment.negative} (${(stats.bySentiment.negative/stats.totalFeedback*100).toFixed(0)}%)
- Critical Cases Pending: ${stats.byPriority.critical}

${trends.alerts.length > 0 ? `ITEMS REQUIRING ATTENTION
${trends.alerts.map(a => `• ${a.title}: ${a.message}`).join('\n')}

` : ''}KEY INSIGHT
${trends.insights[0]?.message || 'No significant insights identified during this period.'}

RECOMMENDED ACTION
${trends.recommendations[0] || 'Continue standard monitoring procedures.'}

────────────────────────────────────

Full detailed report has been distributed via email.

Report Date: ${footerDate}`;

    context.res = {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
        body: {
            htmlReport: htmlReport,
            teamsSummary: teamsSummary,
            executiveSummary: {
                totalFeedback: stats.totalFeedback,
                sentimentScore: stats.avgSentimentScore,
                criticalCases: stats.byPriority.critical,
                trend: trends.volumeChange.trend,
                alertCount: trends.alerts.length
            },
            generatedAt: new Date().toISOString()
        }
    };
};
