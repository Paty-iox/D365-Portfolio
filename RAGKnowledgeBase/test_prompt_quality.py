"""
Test Plan 1: Enhanced Standard Prompt Quality

Verifies the new system prompt produces reasoning-based answers
(WHY, trade-offs, risks) rather than mapping-based answers (use X for Y).

Run:
    python test_prompt_quality.py
"""

import json
import time
import re
from langchain_chroma import Chroma
from langchain.prompts import ChatPromptTemplate
from langchain.schema.runnable import RunnableLambda, RunnableParallel
from langchain.schema.output_parser import StrOutputParser
from langchain.retrievers import EnsembleRetriever
from langchain_community.retrievers import BM25Retriever
from langchain.schema import Document
from config import get_embeddings, get_llm, CHROMA_DIR

# The enhanced standard prompt (must match app.py / query.py)
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

# Questions designed to expose mapping-vs-reasoning quality
QUESTIONS = [
    "Should service request data live in Dataverse or Azure SQL for a global field service company?",
    "When should you use Power Automate vs Logic Apps for ERP integration?",
    "How should you design offline capability for a Power Apps mobile app used by field technicians?",
    "What is the recommended environment strategy for a multi-region Power Platform deployment?",
    "How should immutable compliance evidence be stored - Dataverse, Blob Storage, or ADLS?",
]

# Reasoning indicators
JUSTIFICATION_WORDS = ["because", "since", "the reason", "due to", "this is why", "this ensures"]
TRADEOFF_WORDS = ["trade-off", "tradeoff", "alternatively", "however", "limitation", "on the other hand", "whereas", "downside", "compared to"]
RISK_WORDS = ["risk", "consider", "constraint", "caveat", "downside", "failure", "challenge", "careful", "be aware"]

REFUSAL_PHRASES = ["i don't know", "i don't have", "insufficient context", "cannot answer", "no information"]


def format_docs(docs):
    filtered = [
        doc for doc in docs
        if not doc.metadata.get("source", "").endswith("TOC.md")
        and "/includes/" not in doc.metadata.get("source", "")
    ]
    return "\n\n---\n\n".join(doc.page_content for doc in filtered)


def check_reasoning(answer):
    """Check if the answer contains reasoning indicators."""
    lower = answer.lower()

    has_justification = any(w in lower for w in JUSTIFICATION_WORDS)
    has_tradeoff = any(w in lower for w in TRADEOFF_WORDS)
    has_risk = any(w in lower for w in RISK_WORDS)
    is_refusal = any(p in lower for p in REFUSAL_PHRASES)

    reasoning_score = sum([has_justification, has_tradeoff, has_risk])

    passed = (
        reasoning_score >= 2
        and len(answer) >= 1500
        and not is_refusal
    )

    return {
        "has_justification": has_justification,
        "has_tradeoff": has_tradeoff,
        "has_risk": has_risk,
        "reasoning_score": reasoning_score,
        "answer_length": len(answer),
        "is_refusal": is_refusal,
        "passed": passed,
    }


def main():
    print("=" * 60)
    print("TEST PLAN 1: Enhanced Standard Prompt Quality")
    print("=" * 60)

    # Setup retriever
    print("\nSetting up hybrid retriever...")
    embeddings = get_embeddings()
    vectorstore = Chroma(persist_directory=CHROMA_DIR, embedding_function=embeddings)

    vector_retriever = vectorstore.as_retriever(
        search_type="similarity", search_kwargs={"k": 20}
    )

    bm25_docs = []
    collection = vectorstore._collection
    total = collection.count()
    batch_size = 5000
    for offset in range(0, total, batch_size):
        batch = collection.get(include=["documents", "metadatas"], limit=batch_size, offset=offset)
        for doc, meta in zip(batch["documents"], batch["metadatas"]):
            source = meta.get("source", "")
            if source.endswith("TOC.md") or "/includes/" in source:
                continue
            bm25_docs.append(Document(page_content=doc, metadata=meta))

    bm25_retriever = BM25Retriever.from_documents(bm25_docs, k=10)
    retriever = EnsembleRetriever(
        retrievers=[vector_retriever, bm25_retriever], weights=[0.5, 0.5]
    )
    print(f"Retriever ready ({total:,} chunks)")

    # Build chain
    llm = get_llm()
    rag_chain = (
        RunnableParallel(
            context=RunnableLambda(lambda x: x["question"]) | retriever | format_docs,
            question=RunnableLambda(lambda x: x["question"]),
        )
        | RAG_PROMPT
        | llm
        | StrOutputParser()
    )

    # Run tests
    results = []
    passed_count = 0

    for i, question in enumerate(QUESTIONS, 1):
        print(f"\n--- Q{i} ---")
        print(f"Q: {question[:80]}...")

        start = time.time()
        answer = rag_chain.invoke({"question": question})
        elapsed = time.time() - start

        check = check_reasoning(answer)
        check["question"] = question
        check["answer"] = answer
        check["time_seconds"] = round(elapsed, 1)

        status = "PASS" if check["passed"] else "FAIL"
        if check["passed"]:
            passed_count += 1

        print(f"   {status} | reasoning={check['reasoning_score']}/3 | "
              f"len={check['answer_length']} | {elapsed:.1f}s")
        print(f"   justification={check['has_justification']} "
              f"tradeoff={check['has_tradeoff']} risk={check['has_risk']}")

        results.append(check)

    # Summary
    print(f"\n{'=' * 60}")
    print(f"RESULT: {passed_count}/{len(QUESTIONS)} passed (need 4/5)")
    overall_pass = passed_count >= 4
    print(f"OVERALL: {'PASS' if overall_pass else 'FAIL'}")
    print(f"{'=' * 60}")

    # Save results
    output = {
        "test": "prompt_quality",
        "passed": passed_count,
        "total": len(QUESTIONS),
        "overall_pass": overall_pass,
        "results": results,
    }
    with open("test_results_prompt.json", "w") as f:
        json.dump(output, f, indent=2, ensure_ascii=False)
    print(f"\nResults saved to test_results_prompt.json")


if __name__ == "__main__":
    main()
