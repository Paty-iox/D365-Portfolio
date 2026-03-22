"""
Test Plan: Re-Ranker Quality Improvement

Compares retrieval with and without FlashRank re-ranking.
For each question, runs two passes:
  A) Baseline: hybrid retriever with k=10, no re-ranker
  B) Re-ranked: hybrid retriever with k=30 (over-fetch), FlashRank trims to top 10

Measures: source diversity, reasoning quality, answer length, refusals.

Run:
    python test_reranker.py
"""

import json
import time
import re
from langchain_chroma import Chroma
from langchain.prompts import ChatPromptTemplate
from langchain.schema.runnable import RunnableLambda, RunnableParallel
from langchain.schema.output_parser import StrOutputParser
from langchain.retrievers import EnsembleRetriever, ContextualCompressionRetriever
from langchain_community.retrievers import BM25Retriever
from langchain.schema import Document
from config import (
    get_embeddings, get_llm, CHROMA_DIR,
    RERANKER_MODEL, get_reranker,
)

# Same enhanced prompt used in the app
RAG_PROMPT = ChatPromptTemplate.from_template("""
You are a senior Microsoft Dynamics 365, Power Platform, and Azure technical architect.
Answer using the provided context. For every design choice or recommendation:
- Explain WHY, not just what
- Note relevant trade-offs or alternatives
- Flag constraints or limitations from the documentation
Synthesize information across multiple context chunks into a cohesive answer.
If parts of the question aren't covered by the context, answer what you can
and clearly note which parts lack documentation.
Do not fabricate specific features or settings not mentioned in the context.

Context:
{context}

Question: {question}

Answer:""")


def format_docs(docs):
    """Combine retrieved documents into a single string for the prompt."""
    filtered = [
        doc for doc in docs
        if not doc.metadata.get("source", "").endswith("TOC.md")
        and "/includes/" not in doc.metadata.get("source", "")
    ]
    return "\n\n---\n\n".join(doc.page_content for doc in filtered)


def get_source_list(docs):
    """Extract unique source file paths from retrieved docs."""
    filtered = [
        doc for doc in docs
        if not doc.metadata.get("source", "").endswith("TOC.md")
        and "/includes/" not in doc.metadata.get("source", "")
    ]
    seen = set()
    sources = []
    for doc in filtered:
        src = doc.metadata.get("source", "unknown")
        if src not in seen:
            seen.add(src)
            sources.append(src)
    return sources


def check_reasoning(answer):
    """Check for architectural reasoning indicators in the answer."""
    lower = answer.lower()
    has_justification = any(w in lower for w in [
        "because", "since", "the reason", "due to", "this is why",
        "this ensures", "this allows", "the benefit",
    ])
    has_tradeoff = any(w in lower for w in [
        "trade-off", "tradeoff", "alternatively", "however",
        "limitation", "on the other hand", "downside", "versus",
    ])
    has_risk = any(w in lower for w in [
        "risk", "consider", "constraint", "caveat", "caution",
        "challenge", "concern", "careful",
    ])
    score = sum([has_justification, has_tradeoff, has_risk])
    return {
        "has_justification": has_justification,
        "has_tradeoff": has_tradeoff,
        "has_risk": has_risk,
        "reasoning_score": score,
    }


# ─── Test Questions ───────────────────────────────────────────
QUESTIONS = [
    "How should immutable compliance evidence be stored for a D365 Field Service implementation?",
    "Compare Power Automate vs Logic Apps vs Azure Functions for ERP integration patterns",
    "Design the offline mobile strategy for Field Service technicians in low-connectivity sites",
    "What is the recommended approach for multi-region data residency in Power Platform?",
    "How do Customer Insights prediction models integrate with Dynamics 365 Sales for lead prioritization?",
]


def build_hybrid_retriever(vectorstore, k):
    """Build a hybrid retriever with the given k."""
    vector_retriever = vectorstore.as_retriever(
        search_type="similarity",
        search_kwargs={"k": k * 2},
    )

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
            source = meta.get("source", "")
            if source.endswith("TOC.md") or "/includes/" in source:
                continue
            bm25_docs.append(Document(page_content=doc, metadata=meta))

    bm25_retriever = BM25Retriever.from_documents(bm25_docs, k=k)
    hybrid = EnsembleRetriever(
        retrievers=[vector_retriever, bm25_retriever],
        weights=[0.5, 0.5],
    )
    return hybrid, bm25_docs


def main():
    print("=" * 60)
    print("RE-RANKER A/B COMPARISON TEST")
    print("=" * 60)
    print()

    # Load vector store
    print("Loading vector store...")
    embeddings = get_embeddings()
    vectorstore = Chroma(
        persist_directory=CHROMA_DIR,
        embedding_function=embeddings,
    )

    # Build retrievers
    print("Building baseline retriever (k=10)...")
    baseline_retriever, bm25_docs = build_hybrid_retriever(vectorstore, k=10)
    print(f"  Indexed {len(bm25_docs)} chunks")

    print("Building over-fetch retriever (k=30)...")
    overfetch_retriever, _ = build_hybrid_retriever(vectorstore, k=30)

    # Build re-ranked retriever
    print("Setting up FlashRank re-ranker...")
    compressor = get_reranker()
    if compressor is None:
        print("ERROR: Re-ranker not available. Check config.")
        return

    reranked_retriever = ContextualCompressionRetriever(
        base_compressor=compressor,
        base_retriever=overfetch_retriever,
    )

    # Build LLM and chains
    llm = get_llm()

    baseline_chain = (
        RunnableParallel(
            context=RunnableLambda(lambda x: x["question"]) | baseline_retriever | format_docs,
            question=RunnableLambda(lambda x: x["question"]),
        )
        | RAG_PROMPT
        | llm
        | StrOutputParser()
    )

    reranked_chain = (
        RunnableParallel(
            context=RunnableLambda(lambda x: x["question"]) | reranked_retriever | format_docs,
            question=RunnableLambda(lambda x: x["question"]),
        )
        | RAG_PROMPT
        | llm
        | StrOutputParser()
    )

    # Run tests
    results = []
    baseline_wins = 0
    rerank_wins = 0
    ties = 0

    for i, question in enumerate(QUESTIONS, 1):
        print(f"\n{'─' * 50}")
        print(f"Q{i}: {question[:80]}...")
        print(f"{'─' * 50}")

        # Baseline run
        print("  [A] Baseline (k=10, no re-rank)...")
        start = time.time()
        baseline_answer = baseline_chain.invoke({"question": question})
        baseline_time = time.time() - start
        baseline_docs = baseline_retriever.invoke(question)
        baseline_sources = get_source_list(baseline_docs)
        baseline_reasoning = check_reasoning(baseline_answer)
        print(f"      {len(baseline_answer)} chars, {baseline_time:.1f}s, "
              f"reasoning: {baseline_reasoning['reasoning_score']}/3, "
              f"sources: {len(baseline_sources)}")

        # Re-ranked run
        print("  [B] Re-ranked (k=30 → top 10)...")
        start = time.time()
        reranked_answer = reranked_chain.invoke({"question": question})
        reranked_time = time.time() - start
        reranked_docs = reranked_retriever.invoke(question)
        reranked_sources = get_source_list(reranked_docs)
        reranked_reasoning = check_reasoning(reranked_answer)
        print(f"      {len(reranked_answer)} chars, {reranked_time:.1f}s, "
              f"reasoning: {reranked_reasoning['reasoning_score']}/3, "
              f"sources: {len(reranked_sources)}")

        # Compare
        is_refusal_baseline = len(baseline_answer) < 100 or "i don't know" in baseline_answer.lower()
        is_refusal_reranked = len(reranked_answer) < 100 or "i don't know" in reranked_answer.lower()

        # Determine winner - based on reasoning quality (source count is
        # intentionally NOT a factor since fewer, more relevant sources is a win)
        b_score = baseline_reasoning["reasoning_score"]
        r_score = reranked_reasoning["reasoning_score"]
        # Tie-break on answer length (more detailed = better)
        if b_score == r_score:
            b_score += len(baseline_answer) / 10000
            r_score += len(reranked_answer) / 10000
        if r_score > b_score:
            winner = "reranked"
            rerank_wins += 1
        elif b_score > r_score:
            winner = "baseline"
            baseline_wins += 1
        else:
            winner = "tie"
            ties += 1

        print(f"  Winner: {winner.upper()}")

        results.append({
            "id": i,
            "question": question,
            "baseline": {
                "answer_length": len(baseline_answer),
                "time_seconds": round(baseline_time, 1),
                "reasoning": baseline_reasoning,
                "source_count": len(baseline_sources),
                "is_refusal": is_refusal_baseline,
                "answer": baseline_answer,
                "sources": baseline_sources,
            },
            "reranked": {
                "answer_length": len(reranked_answer),
                "time_seconds": round(reranked_time, 1),
                "reasoning": reranked_reasoning,
                "source_count": len(reranked_sources),
                "is_refusal": is_refusal_reranked,
                "answer": reranked_answer,
                "sources": reranked_sources,
            },
            "winner": winner,
        })

    # Summary
    print(f"\n{'=' * 60}")
    print("RESULTS SUMMARY")
    print(f"{'=' * 60}")
    print(f"Re-ranker wins:  {rerank_wins}/5")
    print(f"Baseline wins:   {baseline_wins}/5")
    print(f"Ties:            {ties}/5")

    # Check pass criteria
    reranked_refusals = sum(1 for r in results if r["reranked"]["is_refusal"])
    reranked_reasoning_wins = sum(
        1 for r in results
        if r["reranked"]["reasoning"]["reasoning_score"] >= r["baseline"]["reasoning"]["reasoning_score"]
    )
    # Check answer quality - re-ranked should not produce shorter answers
    reranked_quality_wins = sum(
        1 for r in results
        if r["reranked"]["answer_length"] >= r["baseline"]["answer_length"] * 0.7  # within 30%
    )

    print(f"\nPASS CRITERIA:")
    print(f"  Reasoning match/beat baseline: {reranked_reasoning_wins}/5 (need >= 4) {'PASS' if reranked_reasoning_wins >= 4 else 'FAIL'}")
    print(f"  No refusals: {5 - reranked_refusals}/5 (need 5) {'PASS' if reranked_refusals == 0 else 'FAIL'}")
    print(f"  Answer quality maintained: {reranked_quality_wins}/5 (need >= 4) {'PASS' if reranked_quality_wins >= 4 else 'FAIL'}")

    overall = (reranked_reasoning_wins >= 4 and reranked_refusals == 0 and reranked_quality_wins >= 4)
    print(f"\n  OVERALL: {'PASS' if overall else 'FAIL'}")

    # Save results
    output = {
        "summary": {
            "rerank_wins": rerank_wins,
            "baseline_wins": baseline_wins,
            "ties": ties,
            "reasoning_match_or_beat": reranked_reasoning_wins,
            "no_refusals": reranked_refusals == 0,
            "quality_maintained": reranked_quality_wins,
            "overall_pass": overall,
        },
        "results": results,
    }
    with open("test_results_reranker.json", "w") as f:
        json.dump(output, f, indent=2)
    print(f"\nResults saved to test_results_reranker.json")


if __name__ == "__main__":
    main()
