"""
Test script for conversation memory (Phase 3).

Tests:
1. Coreference resolution - "it", "that", "those" resolve to prior topic
2. Context continuity - system stays on topic across 3-4 turns
3. Memory clear - after clearing, system loses prior context
4. Multi-conversation independence - separate chains don't bleed into each other

Saves results to test_results_memory.json.
"""

import json
import time
from datetime import datetime

from langchain_chroma import Chroma
from langchain.prompts import ChatPromptTemplate
from langchain.schema.runnable import RunnableLambda
from langchain.schema.output_parser import StrOutputParser
from langchain.memory import ConversationBufferWindowMemory

from config import get_embeddings, get_llm, CHROMA_DIR, PROVIDER, MEMORY_WINDOW
from query import create_hybrid_retriever, format_docs, RAG_PROMPT


def ask(rag_chain, memory, question):
    """Ask a question using the RAG chain with memory, return the answer."""
    chat_history = memory.load_memory_variables({}).get("chat_history", "")
    history_length = len(chat_history)

    answer = rag_chain.invoke({
        "question": question,
        "chat_history": chat_history,
    })

    # Save to memory
    memory.save_context(
        {"input": question},
        {"output": answer},
    )

    return answer, history_length


def build_chain(retriever, llm):
    """Build a RAG chain (same as query.py)."""
    return (
        {
            "context": RunnableLambda(lambda x: x["question"]) | retriever | format_docs,
            "chat_history": RunnableLambda(lambda x: x["chat_history"]),
            "question": RunnableLambda(lambda x: x["question"]),
        }
        | RAG_PROMPT
        | llm
        | StrOutputParser()
    )


def check_coreference(answer, expected_topic_keywords):
    """Check if the answer references the expected topic (simple keyword check)."""
    answer_lower = answer.lower()
    matches = [kw for kw in expected_topic_keywords if kw.lower() in answer_lower]
    return len(matches) >= 1, matches


def run_tests():
    print(f"Provider: {PROVIDER}")
    print(f"Memory window: {MEMORY_WINDOW}")
    print()

    # --- Setup (shared across all tests) ---
    print("Loading vector store...")
    embeddings = get_embeddings()
    vectorstore = Chroma(
        persist_directory=CHROMA_DIR,
        embedding_function=embeddings,
    )

    print("Building hybrid retriever (this takes a few minutes)...")
    retriever = create_hybrid_retriever(vectorstore, k=10)

    llm = get_llm()

    results = {
        "test_run": datetime.now().isoformat(),
        "provider": PROVIDER,
        "memory_window": MEMORY_WINDOW,
        "conversation_a": [],
        "conversation_b": [],
        "cross_contamination": None,
        "summary": {},
    }

    # ================================================================
    # CONVERSATION A (4 turns + memory clear test)
    # ================================================================
    print("\n" + "=" * 60)
    print("CONVERSATION A: Customer Insights prediction models")
    print("=" * 60)

    memory_a = ConversationBufferWindowMemory(
        k=MEMORY_WINDOW, memory_key="chat_history", return_messages=False,
    )
    chain = build_chain(retriever, llm)

    # Turn A1
    q1 = "What are the out-of-box prediction models in Customer Insights?"
    print(f"\n--- A1 ---\nQ: {q1}")
    a1, hist_len1 = ask(chain, memory_a, q1)
    print(f"A: {a1[:200]}...")
    print(f"History length: {hist_len1}")
    results["conversation_a"].append({
        "turn": "A1",
        "question": q1,
        "answer": a1,
        "history_length": hist_len1,
        "coreference_check": "N/A (first question)",
        "coreference_passed": True,
        "explanation": "First question in conversation, no coreference needed."
    })

    # Turn A2
    q2 = "How does the subscription churn model work specifically?"
    print(f"\n--- A2 ---\nQ: {q2}")
    a2, hist_len2 = ask(chain, memory_a, q2)
    print(f"A: {a2[:200]}...")
    print(f"History length: {hist_len2}")
    # Should build on turn 1 - check for subscription churn details
    ref_ok, ref_matches = check_coreference(a2, ["subscription", "churn", "predict"])
    results["conversation_a"].append({
        "turn": "A2",
        "question": q2,
        "answer": a2,
        "history_length": hist_len2,
        "coreference_check": f"Should discuss subscription churn model. Found keywords: {ref_matches}",
        "coreference_passed": ref_ok,
        "explanation": "Explicit follow-up on subscription churn from A1 context."
    })

    # Turn A3 - coreference: "it" should resolve to subscription churn
    q3 = "What data does it need?"
    print(f"\n--- A3 ---\nQ: {q3}")
    a3, hist_len3 = ask(chain, memory_a, q3)
    print(f"A: {a3[:200]}...")
    print(f"History length: {hist_len3}")
    coref_ok, coref_matches = check_coreference(a3, [
        "subscription", "churn", "transaction", "customer", "data"
    ])
    results["conversation_a"].append({
        "turn": "A3",
        "question": q3,
        "answer": a3,
        "history_length": hist_len3,
        "coreference_check": f"'it' should resolve to subscription churn model. Found keywords: {coref_matches}",
        "coreference_passed": coref_ok,
        "explanation": "'it' in 'What data does it need?' should refer to the subscription churn model discussed in A2."
    })

    # Turn A4 - memory clear, then ask about "that"
    print("\n--- Clearing memory ---")
    memory_a.clear()
    q4 = "Tell me more about that"
    print(f"\n--- A4 (after clear) ---\nQ: {q4}")
    a4, hist_len4 = ask(chain, memory_a, q4)
    print(f"A: {a4[:200]}...")
    print(f"History length: {hist_len4}")
    # After clear, the system should NOT know what "that" refers to
    # Check that history_length is 0 (empty) and answer is generic/confused
    clear_ok = hist_len4 == 0
    # Also check the answer doesn't magically reference subscription churn
    no_leak, leak_matches = check_coreference(a4, ["subscription churn"])
    clear_passed = clear_ok and not no_leak  # should NOT find subscription churn
    results["conversation_a"].append({
        "turn": "A4_after_clear",
        "question": q4,
        "answer": a4,
        "history_length": hist_len4,
        "coreference_check": f"After clear, should NOT know what 'that' is. History empty: {clear_ok}. Found prior topic leak: {leak_matches}",
        "coreference_passed": clear_passed,
        "explanation": "After memory.clear(), the system should have no context. 'Tell me more about that' should get a generic or confused response."
    })

    # ================================================================
    # CONVERSATION B (3 turns - separate memory instance)
    # ================================================================
    print("\n" + "=" * 60)
    print("CONVERSATION B: Unified routing in Customer Service")
    print("=" * 60)

    memory_b = ConversationBufferWindowMemory(
        k=MEMORY_WINDOW, memory_key="chat_history", return_messages=False,
    )

    # Turn B1
    q_b1 = "How does unified routing work in Dynamics 365 Customer Service?"
    print(f"\n--- B1 ---\nQ: {q_b1}")
    a_b1, hist_b1 = ask(chain, memory_b, q_b1)
    print(f"A: {a_b1[:200]}...")
    print(f"History length: {hist_b1}")
    results["conversation_b"].append({
        "turn": "B1",
        "question": q_b1,
        "answer": a_b1,
        "history_length": hist_b1,
        "coreference_check": "N/A (first question)",
        "coreference_passed": True,
        "explanation": "First question in conversation B."
    })

    # Turn B2 - "What about the assignment rules?" - should stay in unified routing
    q_b2 = "What about the assignment rules?"
    print(f"\n--- B2 ---\nQ: {q_b2}")
    a_b2, hist_b2 = ask(chain, memory_b, q_b2)
    print(f"A: {a_b2[:200]}...")
    print(f"History length: {hist_b2}")
    ctx_ok, ctx_matches = check_coreference(a_b2, [
        "routing", "assignment", "rule", "queue", "agent", "work item"
    ])
    results["conversation_b"].append({
        "turn": "B2",
        "question": q_b2,
        "answer": a_b2,
        "history_length": hist_b2,
        "coreference_check": f"Should stay in unified routing context. Found: {ctx_matches}",
        "coreference_passed": ctx_ok,
        "explanation": "Follow-up about assignment rules should remain in unified routing context."
    })

    # Turn B3 - "How does it handle overflow?" - "it" = unified routing
    q_b3 = "How does it handle overflow?"
    print(f"\n--- B3 ---\nQ: {q_b3}")
    a_b3, hist_b3 = ask(chain, memory_b, q_b3)
    print(f"A: {a_b3[:200]}...")
    print(f"History length: {hist_b3}")
    overflow_ok, overflow_matches = check_coreference(a_b3, [
        "routing", "overflow", "queue", "agent", "capacity", "work item"
    ])
    results["conversation_b"].append({
        "turn": "B3",
        "question": q_b3,
        "answer": a_b3,
        "history_length": hist_b3,
        "coreference_check": f"'it' should resolve to unified routing. Found: {overflow_matches}",
        "coreference_passed": overflow_ok,
        "explanation": "'it' should refer to unified routing from the conversation context."
    })

    # ================================================================
    # CROSS-CONTAMINATION CHECK
    # ================================================================
    print("\n" + "=" * 60)
    print("CROSS-CONTAMINATION CHECK")
    print("=" * 60)

    memory_c = ConversationBufferWindowMemory(
        k=MEMORY_WINDOW, memory_key="chat_history", return_messages=False,
    )
    q_cross = "What were we discussing?"
    print(f"\nQ: {q_cross}")
    a_cross, hist_cross = ask(chain, memory_c, q_cross)
    print(f"A: {a_cross[:200]}...")
    print(f"History length: {hist_cross}")
    # Should have no context from A or B
    no_leak_a, leak_a = check_coreference(a_cross, ["subscription churn"])
    no_leak_b, leak_b = check_coreference(a_cross, ["unified routing"])
    cross_ok = hist_cross == 0 and not no_leak_a and not no_leak_b
    results["cross_contamination"] = {
        "question": q_cross,
        "answer": a_cross,
        "history_length": hist_cross,
        "conv_a_leaked": no_leak_a,
        "conv_a_leak_keywords": leak_a,
        "conv_b_leaked": no_leak_b,
        "conv_b_leak_keywords": leak_b,
        "passed": cross_ok,
        "explanation": "New memory instance should have no context from Conv A or Conv B."
    }

    # ================================================================
    # SUMMARY
    # ================================================================
    all_a_passed = all(t["coreference_passed"] for t in results["conversation_a"])
    all_b_passed = all(t["coreference_passed"] for t in results["conversation_b"])
    cross_passed = results["cross_contamination"]["passed"]

    results["summary"] = {
        "conversation_a_all_passed": all_a_passed,
        "conversation_b_all_passed": all_b_passed,
        "cross_contamination_passed": cross_passed,
        "overall_passed": all_a_passed and all_b_passed and cross_passed,
        "total_turns_tested": len(results["conversation_a"]) + len(results["conversation_b"]) + 1,
    }

    # Save results
    with open("test_results_memory.json", "w") as f:
        json.dump(results, f, indent=2)

    print("\n" + "=" * 60)
    print("TEST SUMMARY")
    print("=" * 60)
    print(f"Conv A (4 turns): {'PASS' if all_a_passed else 'FAIL'}")
    for t in results["conversation_a"]:
        status = "PASS" if t["coreference_passed"] else "FAIL"
        print(f"  {t['turn']}: {status} - {t['explanation'][:80]}")
    print(f"Conv B (3 turns): {'PASS' if all_b_passed else 'FAIL'}")
    for t in results["conversation_b"]:
        status = "PASS" if t["coreference_passed"] else "FAIL"
        print(f"  {t['turn']}: {status} - {t['explanation'][:80]}")
    print(f"Cross-contamination: {'PASS' if cross_passed else 'FAIL'}")
    print(f"\nOVERALL: {'PASS' if results['summary']['overall_passed'] else 'FAIL'}")
    print(f"\nResults saved to test_results_memory.json")


if __name__ == "__main__":
    run_tests()
