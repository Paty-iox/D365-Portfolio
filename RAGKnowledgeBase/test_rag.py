"""
Automated RAG Quality Test Suite

Tests the RAG pipeline across all document categories at three difficulty levels:
- Easy: Basic factual questions with clear answers in the docs
- Medium: Questions requiring synthesis across a section
- Hard: Questions requiring cross-topic reasoning or nuanced understanding

Each question includes an expected answer keyword/phrase. The test checks:
1. Did the RAG return a substantive answer (not "I don't know")?
2. Does the answer contain the expected keywords?

Usage:
    python test_rag.py                # run all tests
    python test_rag.py --topic azure  # run tests for one topic
    python test_rag.py --verbose      # show full answers
"""

import sys
import time
import json
from datetime import datetime
from langchain_chroma import Chroma
from langchain.prompts import ChatPromptTemplate
from langchain.schema.runnable import RunnablePassthrough
from langchain.schema.output_parser import StrOutputParser
from langchain.schema import Document
from langchain.retrievers import EnsembleRetriever
from langchain_community.retrievers import BM25Retriever
from config import get_embeddings, get_llm, CHROMA_DIR, PROVIDER


# ── TEST QUESTIONS ────────────────────────────────────────────────────
# Format: (question, [expected_keywords], difficulty)
# Keywords are case-insensitive. At least ONE keyword must appear for a pass.

TEST_QUESTIONS = {
    "power-platform": [
        # Easy
        (
            "What is Microsoft Dataverse?",
            ["dataverse", "data platform", "data storage", "database"],
            "easy",
        ),
        (
            "What is a solution in Power Platform?",
            ["solution", "components", "transport", "ALM", "package"],
            "easy",
        ),
        # Medium
        (
            "What are the different types of environments in Power Platform and when should you use each?",
            ["sandbox", "production", "developer", "trial"],
            "medium",
        ),
        (
            "How does Power Platform ALM work with Azure DevOps?",
            ["pipeline", "devops", "deploy", "CI/CD", "build"],
            "medium",
        ),
        # Hard
        (
            "What security considerations should an architect address when designing a multi-tenant Power Platform solution?",
            ["security", "tenant", "DLP", "policy", "access", "role"],
            "hard",
        ),
    ],
    "power-apps": [
        # Easy
        (
            "What is a canvas app in Power Apps?",
            ["canvas", "drag", "drop", "layout", "screen", "pixel"],
            "easy",
        ),
        (
            "What is a model-driven app?",
            ["model-driven", "data model", "table", "Dataverse", "component"],
            "easy",
        ),
        # Medium
        (
            "How do you connect a canvas app to an external data source?",
            ["connector", "connection", "data source", "API", "custom connector"],
            "medium",
        ),
        (
            "What are business rules in model-driven apps and how do they work?",
            ["business rule", "condition", "action", "field", "validation"],
            "medium",
        ),
        # Hard
        (
            "When should you choose a canvas app versus a model-driven app versus a custom page, and what are the trade-offs?",
            ["canvas", "model-driven", "custom page", "flexibility", "complexity"],
            "hard",
        ),
    ],
    "power-automate": [
        # Easy
        (
            "What is a cloud flow in Power Automate?",
            ["cloud flow", "trigger", "action", "automate", "workflow"],
            "easy",
        ),
        (
            "What is the difference between an instant flow and an automated flow?",
            ["instant", "automated", "trigger", "manual", "button"],
            "easy",
        ),
        # Medium
        (
            "How do you handle errors and retries in Power Automate flows?",
            ["error", "retry", "configure run after", "try", "catch", "scope"],
            "medium",
        ),
        # Hard
        (
            "What are the best practices for building enterprise-scale Power Automate solutions with proper governance?",
            ["governance", "DLP", "environment", "monitoring", "naming convention", "enterprise"],
            "hard",
        ),
    ],
    "power-bi": [
        # Easy
        (
            "What is a DAX measure in Power BI?",
            ["DAX", "measure", "calculation", "formula"],
            "easy",
        ),
        # Medium
        (
            "What is the difference between Import mode and DirectQuery in Power BI?",
            ["import", "DirectQuery", "memory", "real-time", "performance"],
            "medium",
        ),
        (
            "How do you implement row-level security in Power BI?",
            ["row-level security", "RLS", "role", "filter", "DAX"],
            "medium",
        ),
        # Hard
        (
            "What are best practices for optimizing a large Power BI data model for performance?",
            ["star schema", "relationship", "cardinality", "aggregate", "partition", "optimization"],
            "hard",
        ),
    ],
    "customer-service": [
        # Easy
        (
            "What is Dynamics 365 Customer Service?",
            ["customer service", "case", "support", "agent", "service"],
            "easy",
        ),
        (
            "What is a case in Dynamics 365 Customer Service?",
            ["case", "issue", "customer", "track", "resolve"],
            "easy",
        ),
        # Medium
        (
            "How do you configure queues in Dynamics 365 Customer Service?",
            ["queue", "route", "assign", "create", "configure"],
            "medium",
        ),
        (
            "What is Omnichannel for Customer Service and what channels does it support?",
            ["omnichannel", "chat", "voice", "channel", "messaging", "digital"],
            "medium",
        ),
        # Hard
        (
            "How do you design a unified routing strategy that uses both queues and intelligent skill-based routing?",
            ["unified routing", "skill", "queue", "capacity", "workstream"],
            "hard",
        ),
    ],
    "sales": [
        # Easy
        (
            "What is an opportunity in Dynamics 365 Sales?",
            ["opportunity", "deal", "revenue", "lead", "sales"],
            "easy",
        ),
        # Medium
        (
            "How does the lead qualification process work in Dynamics 365 Sales?",
            ["lead", "qualify", "opportunity", "contact", "account", "disqualify"],
            "medium",
        ),
        (
            "What is the sales accelerator in Dynamics 365 Sales?",
            ["sales accelerator", "sequence", "prioritize", "worklist"],
            "medium",
        ),
        # Hard
        (
            "How would you configure a multi-stage sales pipeline with automated lead scoring in Dynamics 365?",
            ["pipeline", "stage", "scoring", "business process flow", "qualify"],
            "hard",
        ),
    ],
    "customer-insights": [
        # Easy
        (
            "What is Dynamics 365 Customer Insights?",
            ["customer insights", "data", "profile", "unified", "customer data platform", "CDP"],
            "easy",
        ),
        # Medium
        (
            "How does data unification work in Customer Insights?",
            ["unify", "match", "merge", "deduplicate", "map", "source"],
            "medium",
        ),
        # Hard
        (
            "How do you create and use segments and measures in Customer Insights for targeted marketing?",
            ["segment", "measure", "filter", "audience", "attribute"],
            "hard",
        ),
    ],
    "business-central": [
        # Easy
        (
            "What is Microsoft Dynamics 365 Business Central?",
            ["business central", "ERP", "small", "medium", "finance", "accounting"],
            "easy",
        ),
        (
            "What are dimensions in Business Central?",
            ["dimension", "analysis", "tag", "categorize", "reporting"],
            "easy",
        ),
        # Medium
        (
            "How does the approval workflow work in Business Central?",
            ["approval", "workflow", "request", "approve", "reject"],
            "medium",
        ),
        # Hard
        (
            "How do you extend Business Central with AL extensions and what are the best practices?",
            ["AL", "extension", "app", "codeunit", "page", "table"],
            "hard",
        ),
    ],
    "azure": [
        # Easy
        (
            "What is Azure Logic Apps?",
            ["logic apps", "workflow", "integration", "connector", "automate"],
            "easy",
        ),
        (
            "What is Azure Key Vault used for?",
            ["key vault", "secret", "key", "certificate", "encrypt"],
            "easy",
        ),
        # Medium
        (
            "How does Azure API Management work and what are its main components?",
            ["API management", "gateway", "developer portal", "policy", "product"],
            "medium",
        ),
        (
            "What is the difference between Azure Service Bus queues and Event Grid?",
            ["service bus", "event grid", "queue", "event", "message", "pub/sub"],
            "medium",
        ),
        # Hard
        (
            "How would you design a secure integration between Power Platform and Azure services using managed identities and private endpoints?",
            ["managed identity", "private endpoint", "virtual network", "secure", "authentication"],
            "hard",
        ),
        (
            "What monitoring and observability strategy should you implement for a Power Platform solution that uses Azure backend services?",
            ["monitor", "application insights", "log analytics", "alert", "diagnostic"],
            "hard",
        ),
    ],
}


# ── RAG SETUP ─────────────────────────────────────────────────────────

RAG_PROMPT = ChatPromptTemplate.from_template("""
You are a helpful assistant that answers questions based on the provided context.
If the context doesn't contain enough information to answer, say "INSUFFICIENT CONTEXT".
Do not make up information that isn't in the context.
Give a detailed, specific answer.

Context:
{context}

Question: {question}

Answer:""")


def format_docs(docs):
    return "\n\n---\n\n".join(doc.page_content for doc in docs)


def setup_rag():
    """Initialize the RAG pipeline with hybrid search (vector + keyword)."""
    embeddings = get_embeddings()
    vectorstore = Chroma(
        persist_directory=CHROMA_DIR,
        embedding_function=embeddings,
    )

    k = 10

    # Vector retriever - semantic similarity
    vector_retriever = vectorstore.as_retriever(
        search_type="similarity",
        search_kwargs={"k": k},
    )

    # BM25 keyword retriever - exact term matching
    print("   Building keyword index for hybrid search...")
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

    # Combine both retrievers (50/50 weight)
    retriever = EnsembleRetriever(
        retrievers=[vector_retriever, bm25_retriever],
        weights=[0.5, 0.5],
    )
    print(f"   ✅ Hybrid retriever ready ({len(bm25_docs)} chunks)")

    llm = get_llm()
    rag_chain = (
        {"context": retriever | format_docs, "question": RunnablePassthrough()}
        | RAG_PROMPT
        | llm
        | StrOutputParser()
    )
    return rag_chain, retriever


# ── TEST RUNNER ───────────────────────────────────────────────────────

def check_answer(answer, expected_keywords):
    """Check if the answer contains expected keywords and isn't a refusal."""
    answer_lower = answer.lower()

    # Check if it's a refusal/no-answer
    refusal_phrases = [
        "insufficient context",
        "i don't have",
        "i cannot answer",
        "not in the context",
        "context does not contain",
        "context doesn't contain",
        "no information",
        "not mentioned",
    ]
    is_refusal = any(phrase in answer_lower for phrase in refusal_phrases)

    # Check keyword hits
    keyword_hits = [kw for kw in expected_keywords if kw.lower() in answer_lower]
    keyword_score = len(keyword_hits) / len(expected_keywords)

    # Pass if: not a refusal AND at least one keyword hit
    passed = (not is_refusal) and (len(keyword_hits) > 0)

    return {
        "passed": passed,
        "is_refusal": is_refusal,
        "keyword_hits": keyword_hits,
        "keyword_misses": [kw for kw in expected_keywords if kw.lower() not in answer_lower],
        "keyword_score": keyword_score,
    }


def run_tests(topics=None, verbose=False):
    """Run the test suite."""
    print("=" * 60)
    print("🧪 RAG Quality Test Suite")
    print(f"   Provider: {PROVIDER}")
    print(f"   Date: {datetime.now().strftime('%Y-%m-%d %H:%M')}")
    print("=" * 60)

    # Setup
    print("\n⏳ Initializing RAG pipeline...")
    rag_chain, retriever = setup_rag()

    # Filter topics
    if topics:
        test_set = {k: v for k, v in TEST_QUESTIONS.items() if k in topics}
    else:
        test_set = TEST_QUESTIONS

    # Results tracking
    all_results = []
    topic_scores = {}
    difficulty_scores = {"easy": [], "medium": [], "hard": []}

    total_questions = sum(len(qs) for qs in test_set.values())
    current = 0

    for topic, questions in test_set.items():
        print(f"\n{'─'*60}")
        print(f"📂 Topic: {topic.upper()}")
        print(f"{'─'*60}")

        topic_results = []

        for question, expected_keywords, difficulty in questions:
            current += 1
            diff_icon = {"easy": "🟢", "medium": "🟡", "hard": "🔴"}[difficulty]
            print(f"\n  [{current}/{total_questions}] {diff_icon} ({difficulty}) {question}")

            # Query RAG
            start = time.time()
            try:
                answer = rag_chain.invoke(question)
            except Exception as e:
                answer = f"ERROR: {str(e)}"
            elapsed = time.time() - start

            # Evaluate
            result = check_answer(answer, expected_keywords)
            result.update({
                "topic": topic,
                "difficulty": difficulty,
                "question": question,
                "answer": answer,
                "time": elapsed,
            })

            status = "✅ PASS" if result["passed"] else "❌ FAIL"
            print(f"     {status}  ({elapsed:.1f}s)  keywords: {len(result['keyword_hits'])}/{len(expected_keywords)}")

            if result["is_refusal"]:
                print(f"     ⚠️  Refusal - model said it couldn't answer")
            if result["keyword_misses"] and not result["passed"]:
                print(f"     Missing: {', '.join(result['keyword_misses'])}")

            if verbose:
                # Truncate for readability
                display = answer[:300] + "..." if len(answer) > 300 else answer
                print(f"     💬 {display}")

            topic_results.append(result)
            all_results.append(result)
            difficulty_scores[difficulty].append(result["passed"])

        # Topic summary
        passed = sum(1 for r in topic_results if r["passed"])
        total = len(topic_results)
        pct = (passed / total * 100) if total else 0
        topic_scores[topic] = pct
        print(f"\n  📊 {topic}: {passed}/{total} passed ({pct:.0f}%)")

    # ── FINAL REPORT ──────────────────────────────────────────────────
    print("\n" + "=" * 60)
    print("📋 FINAL REPORT")
    print("=" * 60)

    total_passed = sum(1 for r in all_results if r["passed"])
    total = len(all_results)
    overall_pct = (total_passed / total * 100) if total else 0

    print(f"\n  Overall: {total_passed}/{total} ({overall_pct:.0f}%)")
    print()

    # By difficulty
    print("  By Difficulty:")
    for diff in ["easy", "medium", "hard"]:
        scores = difficulty_scores[diff]
        if scores:
            p = sum(scores)
            t = len(scores)
            icon = {"easy": "🟢", "medium": "🟡", "hard": "🔴"}[diff]
            print(f"    {icon} {diff.capitalize():8s}: {p}/{t} ({p/t*100:.0f}%)")

    # By topic
    print("\n  By Topic:")
    for topic, pct in sorted(topic_scores.items(), key=lambda x: x[1], reverse=True):
        bar_len = int(pct / 5)
        bar = "█" * bar_len + "░" * (20 - bar_len)
        print(f"    {topic:20s} {bar} {pct:.0f}%")

    # Failures summary
    failures = [r for r in all_results if not r["passed"]]
    if failures:
        print(f"\n  ❌ Failed Questions ({len(failures)}):")
        for r in failures:
            reason = "refusal" if r["is_refusal"] else f"missing keywords"
            print(f"    - [{r['topic']}] ({r['difficulty']}) {r['question'][:60]}... → {reason}")

    # Save results to JSON
    report = {
        "date": datetime.now().isoformat(),
        "provider": PROVIDER,
        "total_chunks": "N/A",
        "overall_score": overall_pct,
        "by_difficulty": {
            diff: (sum(scores) / len(scores) * 100) if scores else 0
            for diff, scores in difficulty_scores.items()
        },
        "by_topic": topic_scores,
        "results": [
            {
                "topic": r["topic"],
                "difficulty": r["difficulty"],
                "question": r["question"],
                "passed": r["passed"],
                "keyword_hits": r["keyword_hits"],
                "keyword_misses": r["keyword_misses"],
                "is_refusal": r["is_refusal"],
                "time": r["time"],
            }
            for r in all_results
        ],
    }
    with open("test_results.json", "w") as f:
        json.dump(report, f, indent=2)
    print(f"\n  💾 Detailed results saved to test_results.json")

    print("\n" + "=" * 60)
    avg_time = sum(r["time"] for r in all_results) / len(all_results) if all_results else 0
    print(f"  ⏱️  Average response time: {avg_time:.1f}s")
    print(f"  🏆 Overall Score: {overall_pct:.0f}/100")
    print("=" * 60)

    return overall_pct


if __name__ == "__main__":
    topics = None
    verbose = "--verbose" in sys.argv or "-v" in sys.argv

    for arg in sys.argv[1:]:
        if arg.startswith("--topic="):
            topics = [arg.split("=")[1]]
        elif arg not in ("--verbose", "-v"):
            topics = [arg]

    run_tests(topics=topics, verbose=verbose)
