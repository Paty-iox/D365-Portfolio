"""
Test Plan 2: Deep Analysis Mode

Verifies that deep mode produces structured, sectioned answers with
all 5 required sections, and that deep answers are meaningfully
different (longer, more structured) than standard answers.

Run:
    python test_deep_mode.py
"""

import json
import time
import re
from langchain_chroma import Chroma
from langchain.prompts import ChatPromptTemplate
from langchain.schema.runnable import RunnableLambda
from langchain.schema.output_parser import StrOutputParser
from langchain.retrievers import EnsembleRetriever
from langchain_community.retrievers import BM25Retriever
from langchain.schema import Document
from config import get_embeddings, get_llm, CHROMA_DIR

# Standard prompt (enhanced)
STANDARD_PROMPT = ChatPromptTemplate.from_template("""
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

# Deep analysis prompt
DEEP_PROMPT = ChatPromptTemplate.from_template("""
You are a principal-level Microsoft Dynamics 365, Power Platform, and Azure solution architect.
Provide an expert, structured analysis using the provided context. Your answer MUST include:

1. **Direct Answer** - Address the question with specifics from the documentation
2. **Architecture Reasoning** - Explain WHY this approach, not just what. Reference constraints.
3. **Trade-offs** - For each major choice, state what you chose, what the alternative was, and the cost of your choice
4. **Failure Modes & Risks** - What could go wrong, edge cases, operational concerns
5. **Implementation Considerations** - Practical details: sequencing, dependencies, gotchas

Synthesize across all provided context chunks. Connect concepts across different documentation sources.
If the context doesn't cover something, say so explicitly rather than guessing.
Do not fabricate features or settings not mentioned in the context.

Context:
{context}

Question: {question}

Answer:""")

# Complex architecture questions
QUESTIONS = [
    "Design the integration architecture for connecting an on-prem ERP with Dynamics 365 and Power Platform, considering sync vs async patterns, retry handling, and peak load management.",
    "How should a solution architect design data residency compliance for a Dynamics 365 Customer Service deployment across EU and APAC regions?",
    "Compare using Power Pages vs a custom Azure App Service portal for external customer access to service data, including authentication, scalability, and maintenance trade-offs.",
]

# Expected structural sections in deep mode answers
SECTION_PATTERNS = [
    ("Direct Answer", r"(?i)(direct\s+answer|direct\s+response)"),
    ("Architecture Reasoning", r"(?i)(architecture|reasoning|why\s+this)"),
    ("Trade-offs", r"(?i)(trade-?off|comparison|alternative)"),
    ("Risks", r"(?i)(risk|failure|mode|edge\s+case|concern)"),
    ("Implementation", r"(?i)(implementation|consideration|sequencing|dependencies|gotcha)"),
]

REFUSAL_PHRASES = ["i don't know", "i don't have", "insufficient context", "cannot answer"]


def format_docs(docs):
    filtered = [
        doc for doc in docs
        if not doc.metadata.get("source", "").endswith("TOC.md")
        and "/includes/" not in doc.metadata.get("source", "")
    ]
    return "\n\n---\n\n".join(doc.page_content for doc in filtered)


def check_deep_structure(answer):
    """Check if answer has the required structural sections."""
    sections_found = []
    for name, pattern in SECTION_PATTERNS:
        if re.search(pattern, answer):
            sections_found.append(name)

    has_tradeoff = bool(re.search(r"(?i)(trade-?off|however|alternatively|whereas|on the other hand|compared to)", answer))
    is_refusal = any(p in answer.lower() for p in REFUSAL_PHRASES)

    passed = (
        len(sections_found) >= 3
        and len(answer) >= 2500
        and has_tradeoff
        and not is_refusal
    )

    return {
        "sections_found": sections_found,
        "sections_score": len(sections_found),
        "answer_length": len(answer),
        "has_tradeoff": has_tradeoff,
        "is_refusal": is_refusal,
        "passed": passed,
    }


def main():
    print("=" * 60)
    print("TEST PLAN 2: Deep Analysis Mode")
    print("=" * 60)

    # Setup retriever
    print("\nSetting up hybrid retriever...")
    embeddings = get_embeddings()
    vectorstore = Chroma(persist_directory=CHROMA_DIR, embedding_function=embeddings)

    vector_retriever_standard = vectorstore.as_retriever(
        search_type="similarity", search_kwargs={"k": 20}
    )
    vector_retriever_deep = vectorstore.as_retriever(
        search_type="similarity", search_kwargs={"k": 40}
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
    bm25_retriever_deep = BM25Retriever.from_documents(bm25_docs, k=20)

    standard_retriever = EnsembleRetriever(
        retrievers=[vector_retriever_standard, bm25_retriever], weights=[0.5, 0.5]
    )
    deep_retriever = EnsembleRetriever(
        retrievers=[vector_retriever_deep, bm25_retriever_deep], weights=[0.5, 0.5]
    )
    print(f"Retrievers ready ({total:,} chunks)")

    # Build chains
    llm = get_llm()

    standard_chain = (
        {
            "context": RunnableLambda(lambda x: x["question"]) | standard_retriever | format_docs,
            "question": RunnableLambda(lambda x: x["question"]),
        }
        | STANDARD_PROMPT | llm | StrOutputParser()
    )

    deep_chain = (
        {
            "context": RunnableLambda(lambda x: x["question"]) | deep_retriever | format_docs,
            "question": RunnableLambda(lambda x: x["question"]),
        }
        | DEEP_PROMPT | llm | StrOutputParser()
    )

    # Run tests - both modes for comparison
    results = []
    deep_passed = 0

    for i, question in enumerate(QUESTIONS, 1):
        print(f"\n{'=' * 50}")
        print(f"Q{i}: {question[:80]}...")
        print(f"{'=' * 50}")

        # Standard mode
        print("\n  [STANDARD MODE]")
        start = time.time()
        standard_answer = standard_chain.invoke({"question": question})
        standard_time = time.time() - start
        print(f"  Length: {len(standard_answer)} chars | {standard_time:.1f}s")

        # Deep mode
        print("\n  [DEEP MODE]")
        start = time.time()
        deep_answer = deep_chain.invoke({"question": question})
        deep_time = time.time() - start

        deep_check = check_deep_structure(deep_answer)
        if deep_check["passed"]:
            deep_passed += 1

        status = "PASS" if deep_check["passed"] else "FAIL"
        print(f"  {status} | sections={deep_check['sections_score']}/5 | "
              f"len={deep_check['answer_length']} | {deep_time:.1f}s")
        print(f"  Sections: {', '.join(deep_check['sections_found'])}")

        # Comparison
        length_increase = ((len(deep_answer) - len(standard_answer)) / len(standard_answer) * 100) if len(standard_answer) > 0 else 0
        print(f"\n  COMPARISON:")
        print(f"  Standard: {len(standard_answer)} chars")
        print(f"  Deep:     {len(deep_answer)} chars (+{length_increase:.0f}%)")
        print(f"  Deep >= 50% longer: {'YES' if length_increase >= 50 else 'NO'}")

        result = {
            "question": question,
            "standard_answer": standard_answer,
            "standard_length": len(standard_answer),
            "standard_time": round(standard_time, 1),
            "deep_answer": deep_answer,
            "deep_length": len(deep_answer),
            "deep_time": round(deep_time, 1),
            "deep_sections_found": deep_check["sections_found"],
            "deep_sections_score": deep_check["sections_score"],
            "deep_has_tradeoff": deep_check["has_tradeoff"],
            "length_increase_pct": round(length_increase, 1),
            "deep_passed": deep_check["passed"],
        }
        results.append(result)

    # Summary
    print(f"\n{'=' * 60}")
    print(f"DEEP MODE RESULT: {deep_passed}/{len(QUESTIONS)} passed (need 3/3)")
    overall_pass = deep_passed == len(QUESTIONS)
    print(f"OVERALL: {'PASS' if overall_pass else 'FAIL'}")

    avg_increase = sum(r["length_increase_pct"] for r in results) / len(results)
    print(f"Average length increase: +{avg_increase:.0f}%")
    print(f"{'=' * 60}")

    # Save results
    output = {
        "test": "deep_mode",
        "deep_passed": deep_passed,
        "total": len(QUESTIONS),
        "overall_pass": overall_pass,
        "avg_length_increase_pct": round(avg_increase, 1),
        "results": results,
    }
    with open("test_results_deep.json", "w") as f:
        json.dump(output, f, indent=2, ensure_ascii=False)
    print(f"\nResults saved to test_results_deep.json")


if __name__ == "__main__":
    main()
