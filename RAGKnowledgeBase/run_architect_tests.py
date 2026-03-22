"""
Architect-level RAG test: 25 hard questions across Power Platform, Dynamics 365, and Azure topics.
Saves results to test_results_independent.json.
"""

import os
import sys
import json
import time

os.chdir("/Users/p/Projects/rag-knowledge-base")
sys.path.insert(0, ".")

from langchain_chroma import Chroma
from langchain.chains import RetrievalQA
from config import get_embeddings, get_llm, CHROMA_DIR

QUESTIONS = [
    # --- Power Platform (5) ---
    {
        "id": 1,
        "topic": "Power Platform",
        "question": "How does the Power Platform Center of Excellence (CoE) Starter Kit manage environment lifecycle, and what governance controls does it provide for DLP policy enforcement across tenant-level and environment-level scopes?",
    },
    {
        "id": 2,
        "topic": "Power Apps",
        "question": "What are the architectural differences between canvas apps and model-driven apps in Power Apps regarding data binding, delegation limits, and offline capability, and when should each be chosen for enterprise deployments?",
    },
    {
        "id": 3,
        "topic": "Power Automate",
        "question": "Explain how Power Automate managed environments work with solution-aware cloud flows, including how ALM pipelines handle environment variables and connection references during deployment across dev, test, and production stages.",
    },
    {
        "id": 4,
        "topic": "Power BI",
        "question": "Describe the differences between DirectQuery, Import mode, and Composite models in Power BI, including performance implications, row-level security enforcement, and the constraints each imposes on DAX measure design.",
    },
    {
        "id": 5,
        "topic": "Power Platform",
        "question": "How does the Dataverse capacity-based licensing model work, and what are the implications of exceeding API entitlements for automated flows versus interactive Power Apps users?",
    },

    # --- Power Apps deep-dive (2) ---
    {
        "id": 6,
        "topic": "Power Apps",
        "question": "What is the role of the Power Apps Component Framework (PCF) in extending model-driven and canvas apps, and what are the security sandbox restrictions that apply to code components running in the browser versus server-side rendering?",
    },
    {
        "id": 7,
        "topic": "Power Apps",
        "question": "How do Power Apps portals (now Power Pages) handle authentication federation with Azure AD B2C, and what are the limitations around row-level filtering using table permissions and web roles?",
    },

    # --- Power Automate deep-dive (2) ---
    {
        "id": 8,
        "topic": "Power Automate",
        "question": "Compare the concurrency and throttling behaviors of Power Automate cloud flows using the Standard versus Premium connectors, and explain how the retry policy and run-after configuration affect error-handling architecture.",
    },
    {
        "id": 9,
        "topic": "Power Automate",
        "question": "What are the architectural constraints of using Power Automate desktop (RPA) unattended mode in a VDI environment, and how does machine group load balancing interact with Windows credential management for attended versus unattended runs?",
    },

    # --- Power BI deep-dive (2) ---
    {
        "id": 10,
        "topic": "Power BI",
        "question": "Explain how Power BI Premium Gen2 capacity autoscaling works, what metrics trigger scale-out, and how dataset refresh parallelism is managed when multiple workspaces share a single Premium capacity.",
    },
    {
        "id": 11,
        "topic": "Power BI",
        "question": "How does Power BI's XMLA endpoint enable third-party ALM tooling integration, and what are the restrictions on write operations for datasets in Import versus DirectQuery storage mode?",
    },

    # --- Dynamics 365 Customer Service (2) ---
    {
        "id": 12,
        "topic": "Dynamics 365 Customer Service",
        "question": "How does the Unified Routing engine in Dynamics 365 Customer Service distribute work items using skill-based and capacity-based routing rules, and how does the classification model interact with assignment rules and overflow actions?",
    },
    {
        "id": 13,
        "topic": "Dynamics 365 Customer Service",
        "question": "What is the architecture of the Dynamics 365 Customer Service omnichannel layer, and how does the communication fabric handle session persistence, co-browse escalation, and agent presence synchronization across channels?",
    },

    # --- Dynamics 365 Sales (2) ---
    {
        "id": 14,
        "topic": "Dynamics 365 Sales",
        "question": "Explain how the Dynamics 365 Sales predictive lead scoring model is trained and retrained, what Dataverse tables it depends on, and how scores are surfaced in the sales accelerator workspace.",
    },
    {
        "id": 15,
        "topic": "Dynamics 365 Sales",
        "question": "How does the Dynamics 365 Sales sequence feature enforce cadence across email, phone, and LinkedIn tasks, and what happens to sequence enrollment when a lead is qualified or an opportunity stage changes?",
    },

    # --- Customer Insights (2) ---
    {
        "id": 16,
        "topic": "Customer Insights",
        "question": "Describe the Customer Insights – Data unified customer profile pipeline: how does it ingest from multiple data sources, resolve entity matches using the match and merge phase, and expose unified profiles to downstream applications via API or export?",
    },
    {
        "id": 17,
        "topic": "Customer Insights",
        "question": "How does Customer Insights – Journeys (formerly Marketing) orchestrate real-time triggers from Dataverse events, and what are the segment evaluation latency guarantees versus batch segment refresh for audience targeting?",
    },

    # --- Business Central (2) ---
    {
        "id": 18,
        "topic": "Business Central",
        "question": "Explain the AL extension dependency graph in Business Central, how runtime packages differ from source-based extensions, and the implications of breaking changes in a base application update for ISV solutions in AppSource.",
    },
    {
        "id": 19,
        "topic": "Business Central",
        "question": "How does Business Central's telemetry framework emit Application Insights signals for query performance, report execution, and extension errors, and what KQL queries are recommended for detecting slow queries in a SaaS tenant?",
    },

    # --- Azure: Entra ID (2) ---
    {
        "id": 20,
        "topic": "Azure Entra ID",
        "question": "How does Microsoft Entra ID Conditional Access evaluate risk signals from Identity Protection (user risk + sign-in risk) in combination with device compliance and location policies, and in what order are the grant controls evaluated when multiple policies apply?",
    },
    {
        "id": 21,
        "topic": "Azure Entra ID",
        "question": "Explain the Entra ID Workload Identity Federation mechanism: how does it replace client secrets/certificates for service principals accessing Azure resources from GitHub Actions or Kubernetes, and what trust model is established between the external IdP and Entra ID?",
    },

    # --- Azure: Cosmos DB (1) ---
    {
        "id": 22,
        "topic": "Azure Cosmos DB",
        "question": "How does Cosmos DB for NoSQL handle multi-region writes with conflict resolution policies, and what are the consistency level trade-offs (session vs. bounded staleness vs. eventual) for globally distributed applications with strict RPO requirements?",
    },

    # --- Azure: Key Vault (1) ---
    {
        "id": 23,
        "topic": "Azure Key Vault",
        "question": "What are the architectural differences between Azure Key Vault and Azure Managed HSM for cryptographic key custody, and how should bring-your-own-key (BYOK) scenarios be designed for Dynamics 365 Customer Managed Keys integration?",
    },

    # --- Azure AI Services (1) ---
    {
        "id": 24,
        "topic": "Azure AI Services",
        "question": "How does Azure AI Search (formerly Cognitive Search) integrate with Azure OpenAI Service in a RAG pattern using the 'on your data' API, and what are the chunking strategy trade-offs between fixed-size, sentence-boundary, and semantic chunking for retrieval precision?",
    },

    # --- Cross-topic questions (5) ---
    {
        "id": 25,
        "topic": "Cross-topic: Power Platform + Entra ID",
        "question": "How should a Power Platform tenant be configured to enforce Zero Trust principles using Entra ID Conditional Access, tenant isolation policies, customer-managed keys in Azure Key Vault, and Power Platform DLP policies together?",
    },
    {
        "id": 26,  # Note: listed as question 25 set but index 26 for clarity; we keep 25 total
        "topic": "Cross-topic: Dynamics 365 + Azure AI",
        "question": "How can Azure AI Services (specifically Azure OpenAI and Azure AI Search) be integrated with Dynamics 365 Customer Service to build a copilot that retrieves knowledge articles and case history using RAG, and what authentication and data residency constraints apply?",
    },
    {
        "id": 27,
        "topic": "Cross-topic: Power BI + Cosmos DB + Synapse",
        "question": "What is the recommended architecture for exposing Cosmos DB operational data in Power BI Premium with near-real-time latency, and how does Azure Synapse Link for Cosmos DB compare to using DirectQuery via the Cosmos DB connector?",
    },
    {
        "id": 28,
        "topic": "Cross-topic: Business Central + Power Platform",
        "question": "How does the Business Central connector in Power Automate handle OData pagination, API rate limits, and authentication delegation when building automated financial document workflows that span Business Central and SharePoint?",
    },
    {
        "id": 29,
        "topic": "Cross-topic: Customer Insights + Dynamics 365 Sales + Power BI",
        "question": "Describe an end-to-end architecture where Customer Insights – Data unified profiles feed predictive churn scores into Dynamics 365 Sales sequences, and Power BI Premium reports on segment-level engagement, including how data refresh cadences and API quotas must be coordinated.",
    },
]

# Keep exactly 25 questions (trim the last 4 which are extras due to the cross-topic group starting at id 25)
QUESTIONS = QUESTIONS[:25]


def run_tests():
    print("Loading embeddings and LLM...")
    embeddings = get_embeddings()
    llm = get_llm()

    print("Connecting to ChromaDB...")
    vectorstore = Chroma(persist_directory=CHROMA_DIR, embedding_function=embeddings)

    retriever = vectorstore.as_retriever(search_kwargs={"k": 10})

    qa_chain = RetrievalQA.from_chain_type(
        llm=llm,
        chain_type="stuff",
        retriever=retriever,
        return_source_documents=True,
    )

    results = []

    for i, q in enumerate(QUESTIONS, 1):
        print(f"\n[{i}/25] Topic: {q['topic']}")
        print(f"  Q: {q['question'][:100]}...")
        start = time.time()
        try:
            result = qa_chain.invoke({"query": q["question"]})
            elapsed = round(time.time() - start, 2)
            answer = result["result"]
            sources = list({doc.metadata.get("source", "") for doc in result["source_documents"]})
            status = "ok"
        except Exception as e:
            elapsed = round(time.time() - start, 2)
            answer = f"ERROR: {e}"
            sources = []
            status = "error"

        record = {
            "id": q["id"],
            "topic": q["topic"],
            "question": q["question"],
            "answer": answer,
            "sources": sources,
            "response_time_seconds": elapsed,
            "status": status,
        }
        results.append(record)
        print(f"  -> {elapsed}s | {len(sources)} sources | status={status}")
        print(f"  -> Answer preview: {answer[:120]}...")

    output_path = "/Users/p/Projects/rag-knowledge-base/test_results_independent.json"
    with open(output_path, "w") as f:
        json.dump(results, f, indent=2)

    print(f"\n\nDone! Results saved to {output_path}")
    print(f"Total questions: {len(results)}")
    ok = sum(1 for r in results if r["status"] == "ok")
    errors = sum(1 for r in results if r["status"] == "error")
    avg_time = round(sum(r["response_time_seconds"] for r in results) / len(results), 2)
    print(f"  OK: {ok} | Errors: {errors} | Avg response time: {avg_time}s")
    return results


if __name__ == "__main__":
    run_tests()
