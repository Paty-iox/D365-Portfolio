"""
Independent RAG QA Test - 25 Hard Architect-Level Questions

Creates hard questions, runs them through the hybrid RAG pipeline,
and collects raw answers WITHOUT scoring or judging.

Results saved to test_results_independent.json
"""

import time
import json
from langchain_chroma import Chroma
from langchain.prompts import ChatPromptTemplate
from langchain.schema.runnable import RunnablePassthrough
from langchain.schema.output_parser import StrOutputParser
from langchain.schema import Document
from langchain.retrievers import EnsembleRetriever
from langchain_community.retrievers import BM25Retriever
from config import get_embeddings, get_llm, CHROMA_DIR


# ── 25 HARD ARCHITECT-LEVEL QUESTIONS ─────────────────────────────────

QUESTIONS = [
    # --- power-platform (5) ---
    {
        "id": 1,
        "topic": "power-platform",
        "question": (
            "How should an enterprise architect design a Center of Excellence (CoE) "
            "governance model using the CoE Starter Kit, including the Power Platform "
            "admin module, audit log connectors, environment request workflows, and "
            "maker assessment automation to enforce compliance without blocking citizen "
            "developer innovation?"
        ),
    },
    {
        "id": 2,
        "topic": "power-platform",
        "question": (
            "What are the best practices for configuring tenant-level and environment-level "
            "DLP policies when an organization has HTTP and custom connectors that must "
            "be blocked in production but allowed in sandbox environments, and how do "
            "DLP policy conflicts resolve when multiple policies apply to the same "
            "environment?"
        ),
    },
    {
        "id": 3,
        "topic": "power-platform",
        "question": (
            "How should solution layering and patch solutions be architected for a "
            "multi-vendor Dynamics 365 implementation where independent ISV solutions "
            "share common Dataverse tables, and what are the risks and mitigation "
            "strategies for solution upgrade failures due to dependency chains and "
            "missing components?"
        ),
    },
    {
        "id": 4,
        "topic": "power-platform",
        "question": (
            "What is the recommended environment strategy for Power Platform ALM, "
            "including how to use managed environments, environment groups, pipeline "
            "stages with Power Platform pipelines, and what role do environment "
            "variables and connection references play in automated deployments across "
            "dev/test/production?"
        ),
    },
    {
        "id": 5,
        "topic": "power-platform",
        "question": (
            "How does the Power Platform service protection API limit architecture "
            "work, including the per-user, per-web-server, and per-organization "
            "throttling tiers, the 429 retry-after response pattern, and what "
            "architectural strategies should be used to design resilient integrations "
            "that handle Dataverse API throttling gracefully?"
        ),
    },
    # --- customer-service (4) ---
    {
        "id": 6,
        "topic": "customer-service",
        "question": (
            "How does unified routing in Dynamics 365 Customer Service work end-to-end, "
            "including intake rules, classification rulesets with machine learning-based "
            "skill identification, route-to-queue decision logic, assignment rules with "
            "capacity profiles, and overflow handling for peak load scenarios?"
        ),
    },
    {
        "id": 7,
        "topic": "customer-service",
        "question": (
            "What is the architecture of SLA management in Dynamics 365 Customer Service "
            "including how SLA KPIs are configured with applicable-when, success, and "
            "warning conditions, how pause and resume conditions work with business "
            "hours calendars, and how SLA timer controls display countdown on case forms?"
        ),
    },
    {
        "id": 8,
        "topic": "customer-service",
        "question": (
            "How should an architect design an omnichannel deployment for Dynamics 365 "
            "Customer Service that supports live chat, asynchronous messaging channels "
            "(WhatsApp, Facebook, SMS), and voice, including agent presence management, "
            "session handling, conversation capacity configuration, and channel-specific "
            "workstream configuration?"
        ),
    },
    {
        "id": 9,
        "topic": "customer-service",
        "question": (
            "How does the Customer Service embedded analytics architecture work, "
            "including the Omnichannel real-time analytics dashboard, historical "
            "analytics for supervisors, topic clustering with AI-driven case "
            "categorization, and how do you configure custom metrics and KPIs using "
            "Dataverse data model extensions?"
        ),
    },
    # --- sales (4) ---
    {
        "id": 10,
        "topic": "sales",
        "question": (
            "How does the Dynamics 365 Sales predictive lead scoring model work, "
            "including what entity attributes and behavioral signals it uses, how the "
            "model is trained on historical win/loss data, how scores are recalculated, "
            "and how a solution architect should configure grade thresholds and "
            "integrate scores into lead qualification business process flows?"
        ),
    },
    {
        "id": 11,
        "topic": "sales",
        "question": (
            "What is the architecture of sales pipeline management in Dynamics 365 Sales, "
            "including how opportunity stages map to business process flows, how weighted "
            "revenue is calculated across pipeline stages, the deal manager workspace "
            "capabilities, and how pipeline hygiene rules can be enforced using "
            "Power Automate and business rules?"
        ),
    },
    {
        "id": 12,
        "topic": "sales",
        "question": (
            "How does the Dynamics 365 Sales forecasting engine handle hierarchical "
            "forecast rollups across territory, product line, and organizational "
            "hierarchies, including the difference between system-calculated and "
            "manually adjusted forecasts, snapshot comparison for variance analysis, "
            "and how predictive forecasting leverages historical pipeline data?"
        ),
    },
    {
        "id": 13,
        "topic": "sales",
        "question": (
            "What is the architecture of the Dynamics 365 Sales relationship analytics "
            "and who-knows-whom feature, including how Exchange activity data and "
            "LinkedIn connections are aggregated to calculate relationship health scores, "
            "and what privacy, consent, and admin configuration steps are required to "
            "enable cross-system activity collection?"
        ),
    },
    # --- customer-insights (4) ---
    {
        "id": 14,
        "topic": "customer-insights",
        "question": (
            "How does the data unification process work in Dynamics 365 Customer Insights, "
            "including the step-by-step flow from source data mapping, through "
            "deduplication rules with fuzzy matching, match rule configuration across "
            "entities, merge policy conflict resolution, and how the unified customer "
            "profile entity is generated and refreshed?"
        ),
    },
    {
        "id": 15,
        "topic": "customer-insights",
        "question": (
            "How does the Customer Lifetime Value (CLV) prediction model in Customer "
            "Insights work, including what transaction history and customer attributes "
            "it requires as input, how the prediction window is configured, what the "
            "model output includes (score buckets, contributing factors), and how CLV "
            "segments can drive downstream marketing orchestration?"
        ),
    },
    {
        "id": 16,
        "topic": "customer-insights",
        "question": (
            "What are the different types of segments available in Customer Insights "
            "(static, dynamic, expansion, compound), how are segment membership rules "
            "defined using attribute conditions versus behavioral measures, and how "
            "does segment refresh scheduling interact with data source refresh cycles "
            "to ensure segment accuracy?"
        ),
    },
    {
        "id": 17,
        "topic": "customer-insights",
        "question": (
            "How does Customer Insights handle prediction model configuration for "
            "subscription churn and transaction churn, including the required data "
            "prerequisites (subscription history, transaction logs, customer activity), "
            "how model performance is evaluated with training metrics, and how "
            "churn scores are operationalized through segments and alerts?"
        ),
    },
    # --- business-central (4) ---
    {
        "id": 18,
        "topic": "business-central",
        "question": (
            "How do dimensions work in Business Central for financial analysis, "
            "including the configuration of global dimensions versus shortcut dimensions, "
            "default dimension rules (Code Mandatory, Same Code, No Code), dimension "
            "combinations blocking, and how dimension set entries are stored and used "
            "in ledger entries for multi-dimensional reporting?"
        ),
    },
    {
        "id": 19,
        "topic": "business-central",
        "question": (
            "What is the architecture of posting groups in Business Central, including "
            "how general business posting groups, general product posting groups, and "
            "the general posting setup matrix determine which G/L accounts are used "
            "during transaction posting, and how do customer, vendor, inventory, and "
            "bank posting groups layer on top of this?"
        ),
    },
    {
        "id": 20,
        "topic": "business-central",
        "question": (
            "How should an AL extension developer design a multi-tenant extension for "
            "Business Central SaaS that handles per-tenant configuration using isolated "
            "storage, respects the app isolation levels (full vs UI), manages upgrade "
            "codeunits for schema changes, and follows the lifecycle events for "
            "install, upgrade, and uninstall?"
        ),
    },
    {
        "id": 21,
        "topic": "business-central",
        "question": (
            "What are the architectural options for data migration into Business Central "
            "online, including the use of configuration packages (RapidStart), Excel-based "
            "data import via Edit in Excel, custom API pages for programmatic migration, "
            "and data migration wizards for legacy ERP systems, and what are the row "
            "limits and performance considerations for each approach?"
        ),
    },
    # --- azure (4) ---
    {
        "id": 22,
        "topic": "azure",
        "question": (
            "How should an architect configure Azure Entra ID (formerly Azure AD) "
            "Conditional Access policies for a Dynamics 365 deployment, including "
            "MFA enforcement, named location-based exclusions, device compliance "
            "requirements, session controls with continuous access evaluation, and "
            "integration with Privileged Identity Management for just-in-time admin "
            "role activation?"
        ),
    },
    {
        "id": 23,
        "topic": "azure",
        "question": (
            "What is the recommended architecture for using Azure Key Vault to manage "
            "secrets, certificates, and encryption keys for Dynamics 365 and Power "
            "Platform integrations, including how to use managed identities for "
            "keyless authentication, Key Vault references in Azure App Configuration, "
            "and customer-managed keys for Dataverse encryption at rest?"
        ),
    },
    {
        "id": 24,
        "topic": "azure",
        "question": (
            "How does Azure Cosmos DB partitioning and consistency model architecture "
            "work, including the choice between strong, bounded staleness, session, "
            "consistent prefix, and eventual consistency levels, how logical and physical "
            "partition key selection impacts throughput and query performance, and what "
            "are the RU cost considerations for cross-partition queries?"
        ),
    },
    {
        "id": 25,
        "topic": "azure",
        "question": (
            "What is the architecture of Azure AI Services (formerly Cognitive Services) "
            "for document processing, including how Azure AI Document Intelligence "
            "extracts structured data from invoices and receipts using prebuilt models, "
            "how custom models can be trained with labeled datasets, and how the "
            "extracted data can be fed into Power Automate flows for downstream "
            "business process automation?"
        ),
    },
    # --- CROSS-TOPIC (3) ---
    {
        "id": 26,
        "topic": "cross-topic",
        "question": (
            "How should a solution architect design a Dynamics 365 Customer Service "
            "implementation that uses Azure Entra ID for agent authentication, "
            "Power Automate for automated case escalation workflows, Customer Insights "
            "unified profiles displayed on case forms for 360-degree customer views, "
            "and Power BI embedded dashboards for supervisor real-time queue monitoring?"
        ),
    },
    {
        "id": 27,
        "topic": "cross-topic",
        "question": (
            "What is the recommended architecture for integrating Business Central "
            "with Dynamics 365 Sales using dual-write or virtual tables, including "
            "how customer and product master data synchronization works, how sales "
            "orders flow from Sales to Business Central for fulfillment, and what "
            "conflict resolution strategies apply when both systems update shared "
            "records concurrently?"
        ),
    },
    {
        "id": 28,
        "topic": "cross-topic",
        "question": (
            "How do you architect an end-to-end solution where Azure Cosmos DB serves "
            "as a high-throughput event store for IoT telemetry, Azure Functions process "
            "the events and write relevant alerts to Dataverse via the Web API, "
            "Power Automate triggers case creation in Customer Service based on alert "
            "severity, and Customer Insights ingests the telemetry data for predictive "
            "maintenance scoring?"
        ),
    },
]


# ── RAG SETUP ─────────────────────────────────────────────────────────

RAG_PROMPT = ChatPromptTemplate.from_template("""
You are a helpful assistant that answers questions based on the provided context.
If the context doesn't contain enough information to answer, say so honestly.
Do not make up information that isn't in the context.
Give a detailed, thorough answer.

Context:
{context}

Question: {question}

Answer:""")


def format_docs(docs):
    return "\n\n---\n\n".join(doc.page_content for doc in docs)


def setup_rag():
    """Initialize the hybrid RAG pipeline (vector + BM25)."""
    print("Initializing RAG pipeline...")
    embeddings = get_embeddings()
    vectorstore = Chroma(
        persist_directory=CHROMA_DIR,
        embedding_function=embeddings,
    )

    k = 10

    # Vector retriever
    vector_retriever = vectorstore.as_retriever(
        search_type="similarity",
        search_kwargs={"k": k},
    )

    # BM25 keyword retriever - load all chunks in batches of 5000
    print("Building keyword index for hybrid search...")
    bm25_docs = []
    collection = vectorstore._collection
    total = collection.count()
    batch_size = 5000
    for offset in range(0, total, batch_size):
        batch = collection.get(
            include=["documents", "metadatas"],
            limit=batch_size,
            offset=offset,
        )
        for doc, meta in zip(batch["documents"], batch["metadatas"]):
            bm25_docs.append(Document(page_content=doc, metadata=meta))
        print(f"   Loaded {len(bm25_docs)}/{total} chunks...")

    bm25_retriever = BM25Retriever.from_documents(bm25_docs, k=k)

    # Combine with EnsembleRetriever (50/50)
    retriever = EnsembleRetriever(
        retrievers=[vector_retriever, bm25_retriever],
        weights=[0.5, 0.5],
    )
    print(f"Hybrid retriever ready ({len(bm25_docs)} chunks indexed)")

    llm = get_llm()
    rag_chain = (
        {"context": retriever | format_docs, "question": RunnablePassthrough()}
        | RAG_PROMPT
        | llm
        | StrOutputParser()
    )
    return rag_chain


# ── MAIN ──────────────────────────────────────────────────────────────

def main():
    rag_chain = setup_rag()

    results = []
    total = len(QUESTIONS)

    print(f"\nRunning {total} questions...\n")
    print("=" * 70)

    for q in QUESTIONS:
        qid = q["id"]
        topic = q["topic"]
        question = q["question"]

        print(f"\n[{qid}/{total}] ({topic})")
        print(f"Q: {question}")

        start = time.time()
        try:
            answer = rag_chain.invoke(question)
        except Exception as e:
            answer = f"ERROR: {str(e)}"
        elapsed = time.time() - start

        print(f"A: {answer}")
        print(f"   ({elapsed:.1f}s)")
        print("-" * 70)

        results.append({
            "id": qid,
            "topic": topic,
            "question": question,
            "answer": answer,
            "time_seconds": round(elapsed, 1),
        })

    # Save results
    output = {"questions_and_answers": results}
    with open("test_results_independent.json", "w") as f:
        json.dump(output, f, indent=2, ensure_ascii=False)

    print(f"\nDone. Results saved to test_results_independent.json")
    total_time = sum(r["time_seconds"] for r in results)
    print(f"Total time: {total_time:.0f}s")


if __name__ == "__main__":
    main()
