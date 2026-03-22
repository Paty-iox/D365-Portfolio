"""
Deep conversation memory tests (Phase 3).

Tests 6 advanced scenarios:
1. Deep Coreference Chain (5 turns) - pronoun resolution across many turns
2. Topic Switch + Return (4 turns) - switch topics and come back
3. Ambiguous Pronouns (3 turns) - "it" when two topics are active
4. Memory Window Boundary (7 turns) - verify old context drops off
5. Memory Clear Mid-Conversation (3 turns) - clear resets context
6. Conversation Quality Over Time (4 turns) - answers build, not repeat

Reuses the hybrid retriever across all tests (only built once).
Saves results to test_results_memory_deep.json.
"""

import json
import time
from datetime import datetime

from langchain_chroma import Chroma
from langchain.schema.runnable import RunnableLambda
from langchain.schema.output_parser import StrOutputParser
from langchain.memory import ConversationBufferWindowMemory

from config import get_embeddings, get_llm, CHROMA_DIR, PROVIDER, MEMORY_WINDOW
from query import create_hybrid_retriever, format_docs, RAG_PROMPT


def ask(rag_chain, memory, question):
    """Ask a question using the RAG chain with memory, return answer + history length."""
    chat_history = memory.load_memory_variables({}).get("chat_history", "")
    history_length = len(chat_history)

    answer = rag_chain.invoke({
        "question": question,
        "chat_history": chat_history,
    })

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


def check_keywords(answer, keywords):
    """Check if answer contains any of the expected keywords. Returns (passed, matches)."""
    answer_lower = answer.lower()
    matches = [kw for kw in keywords if kw.lower() in answer_lower]
    return len(matches) >= 1, matches


def check_keywords_absent(answer, keywords):
    """Check that answer does NOT contain certain keywords. Returns (passed, found)."""
    answer_lower = answer.lower()
    found = [kw for kw in keywords if kw.lower() in answer_lower]
    return len(found) == 0, found


def make_turn_result(turn_id, question, answer, history_length, passed, explanation, evidence=""):
    """Create a standardized turn result dict."""
    return {
        "turn": turn_id,
        "question": question,
        "answer": answer,
        "history_length_chars": history_length,
        "passed": passed,
        "explanation": explanation,
        "evidence": evidence,
    }


# =====================================================================
# TEST 1: Deep Coreference Chain (5 turns)
# =====================================================================
def test_deep_coreference(chain, retriever, llm):
    print("\n" + "=" * 60)
    print("TEST 1: Deep Coreference Chain (5 turns)")
    print("=" * 60)

    memory = ConversationBufferWindowMemory(
        k=MEMORY_WINDOW, memory_key="chat_history", return_messages=False,
    )
    turns = []

    # Turn 1
    q = "What are the environment types in Power Platform?"
    print(f"\n  T1: {q}")
    a, h = ask(chain, memory, q)
    print(f"  A: {a[:150]}...")
    ok, m = check_keywords(a, ["environment", "sandbox", "production", "developer", "default", "trial"])
    turns.append(make_turn_result("T1", q, a, h, ok,
        "First question - should list environment types",
        f"Found keywords: {m}"))

    # Turn 2 - "Which one is best for testing?"
    q = "Which one is best for testing?"
    print(f"\n  T2: {q}")
    a, h = ask(chain, memory, q)
    print(f"  A: {a[:150]}...")
    ok, m = check_keywords(a, ["sandbox", "trial", "test", "environment"])
    turns.append(make_turn_result("T2", q, a, h, ok,
        "'one' should resolve to environment types; should recommend sandbox/trial for testing",
        f"Resolved 'one' to environment types. Found: {m}"))

    # Turn 3 - "How do you create it?"
    q = "How do you create it?"
    print(f"\n  T3: {q}")
    a, h = ask(chain, memory, q)
    print(f"  A: {a[:150]}...")
    ok, m = check_keywords(a, ["environment", "create", "admin", "power platform", "sandbox", "provision"])
    turns.append(make_turn_result("T3", q, a, h, ok,
        "'it' should resolve to the testing environment type from T2",
        f"Resolved 'it' to environment. Found: {m}"))

    # Turn 4 - "Can you move solutions into it?"
    q = "Can you move solutions into it?"
    print(f"\n  T4: {q}")
    a, h = ask(chain, memory, q)
    print(f"  A: {a[:150]}...")
    ok, m = check_keywords(a, ["solution", "import", "export", "environment", "deploy", "managed", "move"])
    turns.append(make_turn_result("T4", q, a, h, ok,
        "'it' should still track the environment context; should discuss solution deployment",
        f"Maintained environment context. Found: {m}"))

    # Turn 5 - "What about DLP policies for those?"
    q = "What about DLP policies for those?"
    print(f"\n  T5: {q}")
    a, h = ask(chain, memory, q)
    print(f"  A: {a[:150]}...")
    ok, m = check_keywords(a, ["dlp", "data loss prevention", "policy", "environment", "connector"])
    turns.append(make_turn_result("T5", q, a, h, ok,
        "'those' should resolve to environments; should discuss DLP policies",
        f"Resolved 'those' to environments. Found: {m}"))

    return turns


# =====================================================================
# TEST 2: Topic Switch + Return (4 turns)
# =====================================================================
def test_topic_switch_return(chain, retriever, llm):
    print("\n" + "=" * 60)
    print("TEST 2: Topic Switch + Return (4 turns)")
    print("=" * 60)

    memory = ConversationBufferWindowMemory(
        k=MEMORY_WINDOW, memory_key="chat_history", return_messages=False,
    )
    turns = []

    # Turn 1 - Lead scoring
    q = "How does lead scoring work in Dynamics 365 Sales?"
    print(f"\n  T1: {q}")
    a, h = ask(chain, memory, q)
    print(f"  A: {a[:150]}...")
    ok, m = check_keywords(a, ["lead", "score", "scoring", "sales", "qualify"])
    turns.append(make_turn_result("T1", q, a, h, ok,
        "Should explain lead scoring in Dynamics 365 Sales",
        f"Found: {m}"))

    # Turn 2 - Topic switch to Customer Insights
    q = "What about Customer Insights prediction models?"
    print(f"\n  T2: {q}")
    a, h = ask(chain, memory, q)
    print(f"  A: {a[:150]}...")
    ok, m = check_keywords(a, ["prediction", "model", "customer insights", "churn", "lifetime value", "sentiment"])
    turns.append(make_turn_result("T2", q, a, h, ok,
        "Topic switch: should now discuss Customer Insights predictions, not lead scoring",
        f"Switched topic. Found: {m}"))

    # Turn 3 - Explicit return to lead scoring
    q = "Going back to lead scoring, how does the model retrain?"
    print(f"\n  T3: {q}")
    a, h = ask(chain, memory, q)
    print(f"  A: {a[:150]}...")
    ok, m = check_keywords(a, ["lead", "score", "retrain", "model", "sales", "predictive"])
    turns.append(make_turn_result("T3", q, a, h, ok,
        "Explicit return: should switch back to lead scoring topic",
        f"Returned to lead scoring. Found: {m}"))

    # Turn 4 - Should stay on lead scoring
    q = "How do scores get used in business process flows?"
    print(f"\n  T4: {q}")
    a, h = ask(chain, memory, q)
    print(f"  A: {a[:150]}...")
    ok, m = check_keywords(a, ["score", "lead", "business process flow", "qualify", "sales", "stage"])
    turns.append(make_turn_result("T4", q, a, h, ok,
        "Should stay on lead scoring context, discuss BPF integration",
        f"Stayed on lead scoring. Found: {m}"))

    return turns


# =====================================================================
# TEST 3: Ambiguous Pronouns (3 turns)
# =====================================================================
def test_ambiguous_pronouns(chain, retriever, llm):
    print("\n" + "=" * 60)
    print("TEST 3: Ambiguous Pronouns (3 turns)")
    print("=" * 60)

    memory = ConversationBufferWindowMemory(
        k=MEMORY_WINDOW, memory_key="chat_history", return_messages=False,
    )
    turns = []

    # Turn 1 - Introduce two topics
    q = "Compare SLAs in Customer Service with forecasting in Sales"
    print(f"\n  T1: {q}")
    a, h = ask(chain, memory, q)
    print(f"  A: {a[:150]}...")
    ok_sla, m_sla = check_keywords(a, ["sla", "service level"])
    ok_fc, m_fc = check_keywords(a, ["forecast", "sales"])
    ok = ok_sla and ok_fc
    turns.append(make_turn_result("T1", q, a, h, ok,
        "Should discuss both SLAs and forecasting",
        f"SLA keywords: {m_sla}, Forecast keywords: {m_fc}"))

    # Turn 2 - Ambiguous "it"
    q = "How is it configured?"
    print(f"\n  T2: {q}")
    a, h = ask(chain, memory, q)
    print(f"  A: {a[:150]}...")
    # Check which topic was picked
    picked_sla, m_sla2 = check_keywords(a, ["sla", "service level", "customer service"])
    picked_fc, m_fc2 = check_keywords(a, ["forecast", "sales forecast", "pipeline"])
    # At least one topic should be addressed (ambiguous, either is valid)
    ok = picked_sla or picked_fc
    picked_topic = "SLA" if picked_sla else ("forecasting" if picked_fc else "neither")
    turns.append(make_turn_result("T2", q, a, h, ok,
        f"Ambiguous 'it' - system chose: {picked_topic}. Either is acceptable.",
        f"SLA refs: {m_sla2}, Forecast refs: {m_fc2}"))

    # Turn 3 - "the other one"
    q = "What data does the other one need?"
    print(f"\n  T3: {q}")
    a, h = ask(chain, memory, q)
    print(f"  A: {a[:150]}...")
    # Should reference whichever was NOT picked in T2
    if picked_topic == "SLA":
        ok, m = check_keywords(a, ["forecast", "sales", "pipeline", "revenue", "opportunity"])
        expected = "forecasting (since SLA was discussed in T2)"
    elif picked_topic == "forecasting":
        ok, m = check_keywords(a, ["sla", "service level", "case", "entitlement", "customer service"])
        expected = "SLA (since forecasting was discussed in T2)"
    else:
        ok = False
        m = []
        expected = "unknown (T2 didn't clearly pick either)"
    turns.append(make_turn_result("T3", q, a, h, ok,
        f"'the other one' should resolve to {expected}",
        f"Found: {m}"))

    return turns


# =====================================================================
# TEST 4: Memory Window Boundary (7 turns)
# =====================================================================
def test_memory_window_boundary(chain, retriever, llm):
    print("\n" + "=" * 60)
    print(f"TEST 4: Memory Window Boundary (k={MEMORY_WINDOW}, 7 turns)")
    print("=" * 60)

    memory = ConversationBufferWindowMemory(
        k=MEMORY_WINDOW, memory_key="chat_history", return_messages=False,
    )
    turns = []
    history_lengths = []

    questions = [
        ("T4.1", "What is Dataverse?",
         ["dataverse", "data", "table", "platform"],
         "Should explain Dataverse basics"),
        ("T4.2", "How do custom tables work?",
         ["table", "custom", "column", "field", "entity"],
         "Should discuss custom tables in Dataverse context"),
        ("T4.3", "What about relationships between tables?",
         ["relationship", "table", "one-to-many", "many-to-many", "lookup", "foreign"],
         "Should discuss table relationships"),
        ("T4.4", "How do business rules work?",
         ["business rule", "condition", "action", "validation", "field"],
         "Should explain business rules"),
        ("T4.5", "What are canvas apps?",
         ["canvas", "app", "power apps", "screen", "design"],
         "Should explain canvas apps"),
        ("T4.6", "How do they connect to data?",
         ["connect", "data", "source", "connector", "canvas", "dataverse"],
         "'they' should resolve to canvas apps and discuss data connections"),
    ]

    for turn_id, q, keywords, explanation in questions:
        print(f"\n  {turn_id}: {q}")
        a, h = ask(chain, memory, q)
        print(f"  A: {a[:120]}...")
        print(f"  History chars: {h}")
        ok, m = check_keywords(a, keywords)
        history_lengths.append(h)
        turns.append(make_turn_result(turn_id, q, a, h, ok, explanation, f"Found: {m}"))

    # Turn 7 - Try to reference the FIRST topic (should be outside window)
    q = "Going back to our first topic about Dataverse, what were the key points we discussed?"
    print(f"\n  T4.7: {q}")
    a, h = ask(chain, memory, q)
    print(f"  A: {a[:150]}...")
    print(f"  History chars: {h}")

    # Check the raw memory to see if early turns are gone
    chat_history = memory.load_memory_variables({}).get("chat_history", "")
    early_context_present = "what is dataverse" in chat_history.lower()

    # The answer might still discuss Dataverse (from retrieval), but the history
    # should NOT contain the early turns if k=5 window is working
    if MEMORY_WINDOW < 7:
        # With k=5, turns 1-2 should have been pushed out
        window_ok = not early_context_present
        explanation = (
            f"With k={MEMORY_WINDOW}, the first Dataverse question (turn 1) should be outside "
            f"the memory window. Early context in history: {early_context_present}"
        )
    else:
        window_ok = True
        explanation = f"k={MEMORY_WINDOW} is large enough to retain all turns"

    turns.append(make_turn_result("T4.7", q, a, h, window_ok, explanation,
        f"Early context in memory: {early_context_present}, history chars: {h}"))

    # Also track history growth for analysis
    turns.append({
        "turn": "T4_analysis",
        "question": "N/A",
        "answer": "N/A",
        "history_length_chars": 0,
        "passed": True,
        "explanation": "History length progression across turns",
        "evidence": f"History chars per turn: {history_lengths + [h]}",
    })

    return turns


# =====================================================================
# TEST 5: Memory Clear Mid-Conversation (3 turns)
# =====================================================================
def test_memory_clear(chain, retriever, llm):
    print("\n" + "=" * 60)
    print("TEST 5: Memory Clear Mid-Conversation (3 turns)")
    print("=" * 60)

    memory = ConversationBufferWindowMemory(
        k=MEMORY_WINDOW, memory_key="chat_history", return_messages=False,
    )
    turns = []

    # Turn 1
    q = "Explain Azure Key Vault secret management"
    print(f"\n  T1: {q}")
    a, h = ask(chain, memory, q)
    print(f"  A: {a[:150]}...")
    ok, m = check_keywords(a, ["key vault", "secret", "azure", "certificate", "key"])
    turns.append(make_turn_result("T1", q, a, h, ok,
        "Should explain Azure Key Vault secret management",
        f"Found: {m}"))

    # Clear memory
    print("\n  --- CLEARING MEMORY ---")
    memory.clear()

    # Verify memory is empty
    chat_history_after_clear = memory.load_memory_variables({}).get("chat_history", "")
    print(f"  Memory after clear: '{chat_history_after_clear}' (length: {len(chat_history_after_clear)})")

    # Turn 2 (after clear)
    q = "Can you elaborate on what we just discussed?"
    print(f"\n  T2 (after clear): {q}")
    a, h = ask(chain, memory, q)
    print(f"  A: {a[:150]}...")

    # Should NOT know about Key Vault
    memory_empty = h == 0
    no_keyvault, found_kv = check_keywords_absent(a, ["key vault", "secret management"])
    # The answer should be generic or say it doesn't know what was discussed
    ok = memory_empty
    turns.append(make_turn_result("T2_after_clear", q, a, h, ok,
        f"After clear, should not know prior context. Memory empty: {memory_empty}. "
        f"Key Vault refs absent: {no_keyvault} (found: {found_kv})",
        f"History was {h} chars. Key Vault leak: {found_kv}"))

    # Turn 3 - Ask a fresh question to confirm memory works again
    q = "What is Power Automate?"
    print(f"\n  T3: {q}")
    a, h = ask(chain, memory, q)
    print(f"  A: {a[:150]}...")
    ok, m = check_keywords(a, ["power automate", "flow", "automation", "workflow", "trigger"])
    turns.append(make_turn_result("T3", q, a, h, ok,
        "Fresh question after clear - memory should work normally again",
        f"Found: {m}"))

    return turns


# =====================================================================
# TEST 6: Conversation Quality Over Time (4 turns)
# =====================================================================
def test_conversation_quality(chain, retriever, llm):
    print("\n" + "=" * 60)
    print("TEST 6: Conversation Quality Over Time (4 turns)")
    print("=" * 60)

    memory = ConversationBufferWindowMemory(
        k=MEMORY_WINDOW, memory_key="chat_history", return_messages=False,
    )
    turns = []
    previous_answers = []

    questions_and_checks = [
        ("T1", "What is the Customer Insights data unification process?",
         ["unification", "data", "customer insights", "match", "merge", "map"],
         "Should provide overview of data unification"),
        ("T2", "Tell me more about the matching step",
         ["match", "rule", "criteria", "duplicate", "record", "pair"],
         "Should go deeper into matching, building on T1"),
        ("T3", "How do merge policies work in that context?",
         ["merge", "policy", "record", "winner", "conflict", "unif"],
         "Should explain merge policies within data unification context"),
        ("T4", "What happens when there are conflicting records?",
         ["conflict", "record", "merge", "duplicate", "resolution", "rule", "priority"],
         "Should be most specific - discuss conflict resolution in merge"),
    ]

    for turn_id, q, keywords, explanation in questions_and_checks:
        print(f"\n  {turn_id}: {q}")
        a, h = ask(chain, memory, q)
        print(f"  A: {a[:150]}...")
        ok, m = check_keywords(a, keywords)
        previous_answers.append(a)
        turns.append(make_turn_result(turn_id, q, a, h, ok, explanation, f"Found: {m}"))

    # Quality analysis: check that later answers don't just repeat earlier ones
    # Simple heuristic: measure overlap between consecutive answer pairs
    print("\n  Quality Analysis:")
    overlap_scores = []
    for i in range(1, len(previous_answers)):
        prev_words = set(previous_answers[i - 1].lower().split())
        curr_words = set(previous_answers[i].lower().split())
        if len(prev_words | curr_words) > 0:
            overlap = len(prev_words & curr_words) / len(prev_words | curr_words)
        else:
            overlap = 0
        overlap_scores.append(overlap)
        print(f"    T{i} -> T{i+1} word overlap: {overlap:.2%}")

    # If overlap is excessively high (>80%), answers are repeating too much
    excessive_overlap = any(o > 0.80 for o in overlap_scores)
    quality_passed = not excessive_overlap
    turns.append({
        "turn": "quality_analysis",
        "question": "N/A",
        "answer": "N/A",
        "history_length_chars": 0,
        "passed": quality_passed,
        "explanation": f"Consecutive answer overlap should be <80%. Scores: {[f'{o:.2%}' for o in overlap_scores]}",
        "evidence": f"Overlap scores: {overlap_scores}. Excessive: {excessive_overlap}",
    })

    return turns


# =====================================================================
# MAIN
# =====================================================================
def run_tests():
    start_time = time.time()
    print(f"Provider: {PROVIDER}")
    print(f"Memory window: {MEMORY_WINDOW}")
    print()

    # --- Shared setup (retriever built once) ---
    print("Loading vector store...")
    embeddings = get_embeddings()
    vectorstore = Chroma(
        persist_directory=CHROMA_DIR,
        embedding_function=embeddings,
    )

    print("Building hybrid retriever (this takes 2-3 minutes)...")
    retriever = create_hybrid_retriever(vectorstore, k=10)

    llm = get_llm()
    chain = build_chain(retriever, llm)

    setup_time = time.time() - start_time
    print(f"\nSetup completed in {setup_time:.1f}s")

    # --- Run all tests ---
    results = {
        "test_run": datetime.now().isoformat(),
        "provider": PROVIDER,
        "memory_window": MEMORY_WINDOW,
        "setup_time_seconds": round(setup_time, 1),
        "tests": {},
        "summary": {},
    }

    test_functions = [
        ("test_1_deep_coreference", test_deep_coreference),
        ("test_2_topic_switch_return", test_topic_switch_return),
        ("test_3_ambiguous_pronouns", test_ambiguous_pronouns),
        ("test_4_memory_window_boundary", test_memory_window_boundary),
        ("test_5_memory_clear", test_memory_clear),
        ("test_6_conversation_quality", test_conversation_quality),
    ]

    total_passed = 0
    total_failed = 0
    failures = []

    for test_name, test_fn in test_functions:
        test_start = time.time()
        turns = test_fn(chain, retriever, llm)
        test_duration = time.time() - test_start

        test_passed = sum(1 for t in turns if t["passed"])
        test_failed = sum(1 for t in turns if not t["passed"])
        total_passed += test_passed
        total_failed += test_failed

        for t in turns:
            if not t["passed"]:
                failures.append(f"{test_name}/{t['turn']}: {t['explanation']}")

        results["tests"][test_name] = {
            "turns": turns,
            "duration_seconds": round(test_duration, 1),
            "passed": test_passed,
            "failed": test_failed,
            "all_passed": test_failed == 0,
        }

    total_time = time.time() - start_time

    results["summary"] = {
        "total_turns": total_passed + total_failed,
        "total_passed": total_passed,
        "total_failed": total_failed,
        "all_passed": total_failed == 0,
        "total_time_seconds": round(total_time, 1),
        "failures": failures,
    }

    # Save results
    output_file = "test_results_memory_deep.json"
    with open(output_file, "w") as f:
        json.dump(results, f, indent=2)

    # Print summary
    print("\n" + "=" * 60)
    print("DEEP MEMORY TEST SUMMARY")
    print("=" * 60)
    for test_name, test_data in results["tests"].items():
        status = "PASS" if test_data["all_passed"] else "FAIL"
        print(f"\n  {test_name}: {status} ({test_data['passed']}/{test_data['passed'] + test_data['failed']} turns)")
        for t in test_data["turns"]:
            if not t["passed"]:
                print(f"    FAIL {t['turn']}: {t['explanation'][:90]}")

    print(f"\n  OVERALL: {'PASS' if results['summary']['all_passed'] else 'FAIL'}")
    print(f"  Passed: {total_passed}/{total_passed + total_failed}")
    print(f"  Total time: {total_time:.1f}s")

    if failures:
        print(f"\n  Failures ({len(failures)}):")
        for f_msg in failures:
            print(f"    - {f_msg}")

    print(f"\n  Results saved to {output_file}")
    return results


if __name__ == "__main__":
    run_tests()
