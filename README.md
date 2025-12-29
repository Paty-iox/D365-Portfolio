# Dynamics 365 & Power Platform Portfolio

A collection of end-to-end solutions demonstrating Dynamics 365, Power Platform, and Azure integration capabilities.

---

## Projects

### [Apex Claims](./ApexClaims/)
**Insurance Claims Management System**

An insurance claims processing solution with fraud detection, geolocation services, and weather data integration.

| Component | Technology |
|-----------|------------|
| Custom Entities | Claims, Policies, Incidents |
| Plugins | C# (.NET 4.6.2) - Geocoding, Weather lookup |
| PCF Control | React/TypeScript - Fraud Risk visualization |
| Azure Functions | Node.js - Fraud scoring, Geocoding, Weather API |
| Web Resources | JavaScript forms, Azure Maps integration |
| Portal | Power Pages - Customer self-service |

**Key Features:**
- Real-time fraud risk scoring with configurable thresholds
- Automatic incident location geocoding via Azure Maps
- Weather conditions capture at time of incident
- Interactive risk visualization component
- Customer portal for claim submission and tracking

---

### [Feedback Demo](./FeedbackDemo/)
**AI-Powered Customer Feedback Analytics**

A feedback processing platform using Azure AI services for sentiment analysis, language translation, and automated response generation.

| Component | Technology |
|-----------|------------|
| Custom Entities | Customer Feedback, Activity Log |
| Azure Functions | Node.js - AI processing pipeline |
| Logic Apps | Dataverse updates, Email/Teams notifications |
| Azure AI | Cognitive Services, Translator, OpenAI |
| Copilot | Feedback submission and status lookup bot |
| Cloud Flows | Service Bus integration, Teams alerts |

**Key Features:**
- Multilingual support with automatic translation
- Sentiment analysis with priority escalation
- GPT-powered auto-response generation
- Daily analytics reports via email and Teams
- Copilot bot for customer self-service

---

### [Power BI Reports](./PBI/)
**Business Intelligence Dashboards**

Power BI reports for business analytics and performance monitoring.

| Report | Description |
|--------|-------------|
| Commerce Growth & Performance | Sales trends, regional analysis, KPI tracking |

---

## Repository Structure

```
D365-Portfolio/
├── ApexClaims/           # Insurance claims solution
│   ├── Code/
│   │   ├── AzureFunctions/
│   │   ├── Plugins/
│   │   ├── PCF/
│   │   └── WebResources/
│   ├── Portal/
│   ├── Solutions/
│   └── Documentation/
│
├── FeedbackDemo/         # Customer feedback solution
│   ├── Code/
│   │   ├── Functions/
│   │   ├── LogicApps/
│   │   └── Infrastructure/
│   ├── Solutions/
│   └── Documentation/
│
└── PBI/                  # Power BI reports
    └── Demo Commerce Growth and Performance/
```

## Technologies Used

**Microsoft Power Platform**
- Dynamics 365 Customer Engagement
- Power Apps (Model-driven)
- Power Automate (Cloud Flows)
- Power Pages
- Copilot Studio
- Power BI

**Azure Services**
- Azure Functions (Node.js, Consumption Plan)
- Azure Logic Apps
- Azure Cognitive Services (Text Analytics, Translator)
- Azure OpenAI Service
- Azure Service Bus
- Azure Maps

**Development**
- C# / .NET Framework 4.6.2 (Plugins)
- TypeScript / React (PCF Controls)
- JavaScript (Web Resources, Azure Functions)
- Power Platform CLI (pac)

## Author

**Patrick Y** - Dynamics 365 Consultant

- Solutions Architecture
- Power Platform Development
- Azure Integration
