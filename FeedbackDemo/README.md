# Customer Feedback Analytics

**AI-Powered Feedback Processing Platform for Dynamics 365**

## Business Problem

Organizations receive customer feedback in multiple languages through various channels. Manual processing is slow, sentiment assessment is inconsistent, and response times suffer. Management lacks visibility into feedback trends and emerging issues.

## Solution

An automated feedback processing platform that:

- **Analyzes Sentiment** - AI-powered classification of positive, neutral, and negative feedback
- **Supports Multiple Languages** - Automatic detection and translation to English
- **Generates Responses** - GPT-powered draft responses for agent review
- **Escalates Priorities** - Automatic routing based on sentiment severity
- **Reports Trends** - Daily analytics via email and Teams

## Components

### Azure Functions (Node.js)

| Function | Trigger | Description |
|----------|---------|-------------|
| ProcessFeedback | Service Bus Queue | 5-stage AI pipeline: language detection, translation, sentiment, NER, auto-response |
| FetchFeedbackStats | HTTP | Aggregates feedback data for reporting period |
| AnalyzeTrends | HTTP | Compares current vs previous period, generates alerts |
| GenerateReport | HTTP | Creates HTML email and Teams summary |

**AI Processing Pipeline:**
1. Language Detection (Azure Text Analytics)
2. Translation to English (Azure Translator) - if non-English
3. Sentiment Analysis with confidence scores
4. Named Entity Recognition (products, people, orgs)
5. Auto-Response Generation (Azure OpenAI GPT-3.5)

### Logic Apps

| Logic App | Trigger | Description |
|-----------|---------|-------------|
| logic-feedback-processor | Service Bus Topic | Updates Dataverse record, sends thank-you email |
| logic-daily-analytics | Recurrence (8 AM) | Runs analytics pipeline, distributes reports |

### D365 Solution Components

| Component | Description |
|-----------|-------------|
| CustomerFeedback Entity | Stores feedback with AI enrichment fields |
| FeedbackActivityLog Entity | Audit trail for feedback actions |
| Model-Driven App | Agent interface for feedback management |
| Copilot Bot | Customer self-service for submission and status |
| Cloud Flows | Service Bus integration, Teams alerts |

### Copilot Bot Skills

| Skill | Description |
|-------|-------------|
| Create Feedback | Submit new feedback via conversation |
| Check Status | Look up feedback by email address |

## Data Model

**Customer Feedback Entity (`new_customerfeedback`)**

| Field | Type | Description |
|-------|------|-------------|
| FeedbackID | Auto-number | Reference (FB-00001) |
| FeedbackText | Multiline | Original submission |
| SentimentCategory | Option Set | Positive/Neutral/Negative |
| SentimentScore | Decimal | AI confidence (0-1) |
| DetectedLanguage | Text | ISO 639-1 code |
| TranslatedText | Multiline | English translation |
| AutoResponse | Multiline | GPT-generated draft |
| Priority | Option Set | Low/Medium/High/Critical |
| Entities | Multiline | Extracted entities (JSON) |

## Folder Structure

```
FeedbackDemo/
├── Code/
│   ├── Functions/
│   │   └── func-feedback-demo2/
│   │       ├── ProcessFeedback/
│   │       ├── FetchFeedbackStats/
│   │       ├── AnalyzeTrends/
│   │       └── GenerateReport/
│   ├── LogicApps/
│   │   ├── logic-feedback-processor.json
│   │   └── logic-daily-analytics.json
│   └── Infrastructure/
│       └── arm-template.json
├── Solutions/
│   └── DEMOSOLUTION_1_0_0_1_managed.zip
└── Documentation/
    └── DEMO_Customer_Feedback_Analytics_FDD_v1.0.pdf
```

## Setup

### Prerequisites

- Dynamics 365 environment with Dataverse
- Azure subscription with:
  - Azure Functions (Consumption Plan)
  - Azure Cognitive Services (Text Analytics)
  - Azure Translator
  - Azure OpenAI Service (requires access approval)
  - Azure Service Bus (Standard tier)
  - Azure Logic Apps
- Office 365 (for email/Teams integration)

### Deployment

1. **Deploy Azure Infrastructure**
   ```bash
   az deployment group create \
     --resource-group rg-feedback-demo \
     --template-file Code/Infrastructure/arm-template.json
   ```

2. **Configure Cognitive Services**
   - Retrieve API keys from Azure Portal
   - Update Function App settings

3. **Deploy Functions**
   ```bash
   cd Code/Functions/func-feedback-demo2
   func azure functionapp publish func-feedback-demo2
   ```

4. **Create Service Bus Entities**
   - Queue: `feedback-incoming`
   - Topic: `feedback-analyzed`
   - Subscriptions: `all-feedback`, `negative-feedback`

5. **Deploy Logic Apps**
   - Import definitions from `Code/LogicApps/`
   - Authorize OAuth connections

6. **Import D365 Solution**
   - Import `DEMOSOLUTION_1_0_0_1_managed.zip`
   - Configure connection references

### Configuration

| Setting | Description |
|---------|-------------|
| COGNITIVE_ENDPOINT | Text Analytics endpoint |
| COGNITIVE_KEY | Text Analytics API key |
| TRANSLATOR_KEY | Translator API key |
| OPENAI_ENDPOINT | Azure OpenAI endpoint |
| OPENAI_KEY | Azure OpenAI API key |
| OPENAI_DEPLOYMENT | GPT model deployment name |

## Documentation

- [Solution Architecture Diagram](./Documentation/Customer%20Feedback%20Solution%20Architecture.PNG)
- [Functional Design Document](./Documentation/DEMO_Customer_Feedback_Analytics_FDD_v1.0.pdf)
