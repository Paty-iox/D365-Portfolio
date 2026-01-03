# Customer Feedback Analytics

Feedback processing with sentiment analysis, translation, and auto-responses.

Stats/trends endpoints use stub data. Service Bus namespace/topic are demo defaults.

## Azure Functions (Node.js)

| Function | Trigger |
|----------|---------|
| ProcessFeedback | Service Bus Queue |
| FetchFeedbackStats | HTTP |
| AnalyzeTrends | HTTP |
| GenerateReport | HTTP |


## Logic Apps

| Logic App | Trigger |
|-----------|---------|
| logic-feedback-processor | Service Bus Topic |
| logic-daily-analytics | Recurrence (8 AM) |

## D365 Components

| Component | Type |
|-----------|------|
| CustomerFeedback | Entity |
| FeedbackActivityLog | Entity |
| Copilot Bot | Feedback submission/status |

## Configuration

| Setting | Purpose |
|---------|---------|
| COGNITIVE_ENDPOINT | Text Analytics |
| COGNITIVE_KEY | Text Analytics key |
| TRANSLATOR_KEY | Translator key |
| OPENAI_ENDPOINT | Azure OpenAI |
| OPENAI_KEY | OpenAI key |
| OPENAI_DEPLOYMENT | GPT model name |

## Deployment

```bash
# Infrastructure
az deployment group create --resource-group rg-feedback-demo --template-file Code/Infrastructure/arm-template.json

# Functions
cd Code/Functions/func-feedback-demo2 && func azure functionapp publish func-feedback-demo2
```

Service Bus entities: queue `feedback-incoming`, topic `feedback-analyzed`

## Documentation

- [Video](https://youtu.be/3y7FADlBmLs)
- [Architecture](./Documentation/Customer%20Feedback%20Solution%20Architecture.PNG)
- [FDD](./Documentation/DEMO_Customer_Feedback_Analytics_FDD_v1.0.pdf)
