"""
Step 2: Query your documents using RAG with conversation memory.

This script:
1. Takes your question
2. Searches the vector store using HYBRID search (vector + keyword)
3. Includes recent conversation history so follow-up questions work
4. Sends the chunks + history + question to the LLM
5. Returns a grounded answer with sources

Hybrid search combines:
- Vector similarity (semantic meaning - "car" matches "automobile")
- BM25 keyword matching (exact terms - "AL extension" matches "AL extension")

Conversation memory keeps the last N turns (configurable in config.py)
so you can ask follow-up questions like "tell me more about that" or
"how does that compare to Power Apps?" without repeating context.

Run after ingesting documents:
    python query.py
"""

from langchain_chroma import Chroma
from langchain.prompts import ChatPromptTemplate
from langchain.schema.runnable import RunnablePassthrough, RunnableLambda
from langchain.schema.output_parser import StrOutputParser
from langchain.retrievers import EnsembleRetriever, ContextualCompressionRetriever
from langchain_community.retrievers import BM25Retriever
from langchain.memory import ConversationBufferWindowMemory
from config import (
    get_embeddings, get_llm, CHROMA_DIR, PROVIDER, MEMORY_WINDOW,
    RERANKER_ENABLED, RERANKER_CANDIDATES, RERANKER_TOP_K, get_reranker,
)

# The prompt template tells the LLM how to use the retrieved context.
# This is where you control the behavior of your RAG system.
# {chat_history} holds recent conversation turns so the LLM can
# understand follow-up questions like "tell me more about that".
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
Use the conversation history to understand follow-up questions - if the user
says "tell me more" or "how does that work", refer back to the prior topic.

Context:
{context}

Conversation history:
{chat_history}

Question: {question}

Answer:""")


def format_docs(docs):
    """Combine retrieved documents into a single string for the prompt."""
    # Filter out noise files (TOC, includes) that sneak through vector search
    filtered = [
        doc for doc in docs
        if not doc.metadata.get("source", "").endswith("TOC.md")
        and "/includes/" not in doc.metadata.get("source", "")
    ]
    return "\n\n---\n\n".join(doc.page_content for doc in filtered)


def create_hybrid_retriever(vectorstore, k=10):
    """
    Create a hybrid retriever that combines vector search + keyword search.

    Why hybrid? Vector search is great at understanding meaning ("car" ≈ "automobile")
    but can miss exact technical terms. BM25 keyword search catches those exact matches.
    EnsembleRetriever merges results from both, giving the best of both worlds.

    Weights: 0.5 vector + 0.5 keyword (equal weight to both strategies)
    """
    # Vector retriever - semantic similarity
    # We fetch extra candidates (k*2) because some will be noise, then
    # the EnsembleRetriever merges and re-ranks the combined results.
    vector_retriever = vectorstore.as_retriever(
        search_type="similarity",
        search_kwargs={"k": k * 2},
    )

    # Keyword retriever - BM25 (exact term matching)
    # We load all documents from ChromaDB in batches to build the BM25 index
    print("📚 Building keyword index for hybrid search...")

    from langchain.schema import Document
    bm25_docs = []
    collection = vectorstore._collection
    total = collection.count()
    batch_size = 5000
    skipped = 0
    for offset in range(0, total, batch_size):
        batch = collection.get(
            include=["documents", "metadatas"],
            limit=batch_size,
            offset=offset,
        )
        for doc, meta in zip(batch["documents"], batch["metadatas"]):
            # Skip noise files that hurt retrieval quality
            source = meta.get("source", "")
            if source.endswith("TOC.md") or "/includes/" in source:
                skipped += 1
                continue
            bm25_docs.append(Document(page_content=doc, metadata=meta))
        print(f"   Loaded {len(bm25_docs)}/{total} chunks...")
    if skipped:
        print(f"   Skipped {skipped} noise chunks (TOC.md, includes/)")

    bm25_retriever = BM25Retriever.from_documents(bm25_docs, k=k)

    # Combine both retrievers
    # weights control how much each contributes (must sum to 1.0)
    hybrid_retriever = EnsembleRetriever(
        retrievers=[vector_retriever, bm25_retriever],
        weights=[0.5, 0.5],
    )

    print(f"   ✅ Hybrid retriever ready ({len(bm25_docs)} chunks indexed)")
    return hybrid_retriever


def create_reranked_retriever(base_retriever):
    """
    Wrap a retriever with FlashRank re-ranking.

    How it works:
    1. The base_retriever fetches MANY candidates (e.g., 30 chunks)
    2. FlashRank scores each chunk against the question using a cross-encoder
    3. Only the top_n most relevant chunks survive (e.g., top 10)

    This is like a hiring process: the retriever is the resume screen
    (cast a wide net), and the re-ranker is the interview (pick the best).

    Returns a ContextualCompressionRetriever that behaves exactly like
    a normal retriever - you call .invoke(question) and get documents back.
    """
    compressor = get_reranker()
    if compressor is None:
        return base_retriever

    print(f"   🔄 Re-ranker enabled: {RERANKER_CANDIDATES} candidates → top {RERANKER_TOP_K}")

    return ContextualCompressionRetriever(
        base_compressor=compressor,
        base_retriever=base_retriever,
    )


def main():
    print(f"🔧 Using provider: {PROVIDER}")
    print()

    # 1. LOAD THE VECTOR STORE
    # Connect to the same ChromaDB we created in ingest.py
    embeddings = get_embeddings()
    vectorstore = Chroma(
        persist_directory=CHROMA_DIR,
        embedding_function=embeddings,
    )

    # 2. CREATE HYBRID RETRIEVER
    # Combines vector similarity + BM25 keyword matching
    # Over-fetch candidates when re-ranking is enabled, otherwise use standard k
    fetch_k = RERANKER_CANDIDATES if RERANKER_ENABLED else 10
    retriever = create_hybrid_retriever(vectorstore, k=fetch_k)

    # Wrap with re-ranker if enabled (scores and trims to top results)
    retriever = create_reranked_retriever(retriever)

    # 3. CREATE THE LLM
    llm = get_llm()

    # 4. SET UP CONVERSATION MEMORY
    # ConversationBufferWindowMemory keeps the last k turns of conversation.
    # This lets the LLM understand follow-up questions like "tell me more"
    # without needing the user to repeat the full context each time.
    # The window auto-drops the oldest turns when it exceeds k.
    memory = ConversationBufferWindowMemory(
        k=MEMORY_WINDOW,
        memory_key="chat_history",  # must match the prompt template variable
        return_messages=False,       # return as a formatted string, not message objects
    )

    # 5. BUILD THE RAG CHAIN (using LCEL - LangChain Expression Language)
    #
    # The chain now handles three inputs:
    #   - context: retrieved document chunks (from hybrid search)
    #   - chat_history: recent conversation turns (from memory)
    #   - question: the user's current question
    #
    # The retriever searches using ONLY the current question (not history)
    # to keep retrieval focused and avoid noise from old topics.
    rag_chain = (
        {
            "context": RunnableLambda(lambda x: x["question"]) | retriever | format_docs,
            "chat_history": RunnableLambda(lambda x: x["chat_history"]),
            "question": RunnableLambda(lambda x: x["question"]),
        }
        | RAG_PROMPT
        | llm
        | StrOutputParser()
    )

    # 6. INTERACTIVE QUERY LOOP
    print()
    print("=" * 50)
    print("📚 RAG Knowledge Base (with conversation memory)")
    print("Ask questions about your documents!")
    print("Follow-up questions work - the system remembers context.")
    print()
    print("Commands:")
    print("  'quit'    - exit")
    print("  'sources' - toggle source chunk display")
    print("  'clear'   - reset conversation history")
    print("=" * 50)

    show_sources = False

    while True:
        print()
        question = input("❓ Your question: ").strip()

        if not question:
            continue
        if question.lower() == "quit":
            print("Goodbye!")
            break
        if question.lower() == "sources":
            show_sources = not show_sources
            print(f"   Source display: {'ON' if show_sources else 'OFF'}")
            continue
        if question.lower() == "clear":
            memory.clear()
            print("   🗑️  Conversation history cleared. Starting fresh!")
            continue

        # Load conversation history from memory
        chat_history = memory.load_memory_variables({}).get("chat_history", "")

        # Get the answer - pass both the question and history
        print("\n🤔 Thinking...\n")
        answer = rag_chain.invoke({
            "question": question,
            "chat_history": chat_history,
        })
        print(f"💡 {answer}")

        # Save this turn to memory (so the next question has context)
        memory.save_context(
            {"input": question},
            {"output": answer},
        )

        # Optionally show which chunks were retrieved
        if show_sources:
            print("\n📄 Sources used:")
            docs = retriever.invoke(question)
            for i, doc in enumerate(docs, 1):
                print(f"\n--- Source {i} ---")
                print(doc.page_content[:200] + "...")


if __name__ == "__main__":
    main()
