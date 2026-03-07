# Dynamics 365 & Power Platform Portfolio

[![Tests](https://github.com/Paty-iox/D365-Portfolio/actions/workflows/tests.yml/badge.svg)](https://github.com/Paty-iox/D365-Portfolio/actions/workflows/tests.yml)
[![Build Plugins](https://github.com/Paty-iox/D365-Portfolio/actions/workflows/build-plugins.yml/badge.svg)](https://github.com/Paty-iox/D365-Portfolio/actions/workflows/build-plugins.yml)

Power Platform projects with Dataverse plugins, Azure Functions, PCF controls, and AI integrations.

> All projects are original demos, not derived from client or employer work.

---

## Projects

### [Apex Claims](./ApexClaims/)
[![Demo Video](https://img.shields.io/badge/Demo-YouTube-red?logo=youtube)](https://youtu.be/v14AGGMQdQw)

Claims processing with fraud scoring, geocoding, and weather lookup.

| Component | Technology |
|-----------|------------|
| Plugins | C# .NET 4.6.2 |
| PCF Control | React/TypeScript |
| Azure Functions | Node.js |
| Portal | Power Pages |

---

### [Feedback Demo](./FeedbackDemo/)
[![Demo Video](https://img.shields.io/badge/Demo-YouTube-red?logo=youtube)](https://youtu.be/3y7FADlBmLs)

Feedback processing with sentiment analysis, translation, and auto-responses.

| Component | Technology |
|-----------|------------|
| Azure Functions | Node.js |
| Logic Apps | Dataverse/Email/Teams |
| Azure AI | Cognitive Services, OpenAI |
| Copilot | Feedback bot |

---

### [Contoso Vendor Risk](./ContosoDemo/)

Vendor risk management with multi-factor scoring, compliance tracking, and ERP integration via virtual tables.

| Component | Technology |
|-----------|------------|
| Plugins | C# .NET 4.6.2 |
| Azure Functions | .NET 8 Isolated (OData v4) |
| Cloud Flows | Power Automate (4 flows, 3 Copilot skills) |
| Desktop Flow | Power Automate Desktop (RPA) |
| UI | Model-driven App, Canvas App |
| Infrastructure | Azure SQL, ARM Template |

---

### [Power BI Reports](./PBI/)
[![Demo Video](https://img.shields.io/badge/Demo-YouTube-red?logo=youtube)](https://youtu.be/X2voYGrieos)

Commerce performance dashboard using [UK E-Commerce dataset](https://www.kaggle.com/datasets/atharvaarya25/e-commerce-analysis-uk).

---

## Repository Structure

```
D365-Portfolio/
├── ApexClaims/
│   ├── Code/
│   │   ├── AzureFunctions/
│   │   ├── PCF/
│   │   ├── Plugins/
│   │   └── WebResources/
│   ├── Portal/
│   └── Solutions/
├── ContosoDemo/
│   ├── Code/
│   │   ├── Contoso.VendorRisk.Plugins/
│   │   └── ContosoErpODataApi/
│   ├── Documentation/
│   ├── Infrastructure/
│   └── Solutions/
├── FeedbackDemo/
│   ├── Code/
│   │   ├── Functions/
│   │   └── LogicApps/
│   └── Solutions/
├── PBI/
├── docs/adr/
└── .github/workflows/
```

## Stack

**Power Platform:** D365 CE, Model-driven Apps, Canvas Apps, Power Automate, Power Automate Desktop (RPA), Power Pages, Copilot Studio, Power BI

**Azure:** Functions, Logic Apps, SQL Database, Cognitive Services, OpenAI, Service Bus, Maps, ARM Templates

**Dev:** C# .NET 4.6.2, .NET 8 Isolated, TypeScript/React, JavaScript, OData v4, pac CLI

## Author

**Patrick Y**
